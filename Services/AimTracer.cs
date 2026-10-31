using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Trace;

namespace Bolgeler.Services;

/// <summary>
/// Nisangahin tam baktigi noktayi bulur. Mermi izine (bullet_impact) degil, goz acisindan
/// atilan geometrik isina dayanir; silah sekmesinden etkilenmez, bicakla da calisir.
/// </summary>
public class AimTracer
{
    private readonly ISwiftlyCore _core;

    public AimTracer(ISwiftlyCore core)
    {
        _core = core;
    }

    public bool TryGetAimPoint(CCSPlayerPawn pawn, out Vector point)
    {
        point = default;
        if (pawn == null || !pawn.IsValid) return false;

        Vector start;
        if (!TryGetEyePosition(pawn, out start))
        {
            return false;
        }

        var angles = pawn.EyeAngles;

        // Oyuncunun kendi bedenini (pawn) ve trigger/beam gibi gorunmez nesneleri yoksayarak
        // dogrudan dunya yuzeylerine (zemin, duvar, prop, clip) isin at.
        TraceParams? traceParams = TraceParams.Builder()
            .WithLineRay()
            .WithObjectQuery(RnQueryObjectSet.All)
            .WithInteraction(
                MaskTrace.WorldGeometry | MaskTrace.StaticLevel | MaskTrace.CsgoOpaque |
                MaskTrace.PhysicsProp | MaskTrace.PlayerClip | MaskTrace.Window | MaskTrace.Solid,
                MaskTrace.Empty,
                MaskTrace.Empty)
            .WithCollisionGroup(CollisionGroup.Player)
            .HitSolid(true)
            .HitTrigger(false)
            .IgnoreEntity(pawn)
            .IgnoreOwner(pawn)
            .Build();

        var result = _core.Trace.TraceShapeAngle(in start, in angles, 8192f, in traceParams);
        if (!result.DidHit)
        {
            // Yedek trace: ozel/nodraw yuzeylerde genel sorgu (yine oyuncunun kendisini yoksayarak)
            TraceParams? fallbackParams = TraceParams.Builder()
                .WithLineRay()
                .WithObjectQuery(RnQueryObjectSet.All)
                .HitSolid(true)
                .HitTrigger(false)
                .IgnoreEntity(pawn)
                .IgnoreOwner(pawn)
                .Build();

            result = _core.Trace.TraceShapeAngle(in start, in angles, 8192f, in fallbackParams);
            if (!result.DidHit)
            {
                return false;
            }
        }

        // Eger trace baslangic noktasina cok yakinsa (0-2 birim, kendi hitbox'i vb.) ileriye dogru kucuk ofsetle tekrar dene
        if (result.Distance < 1.5f)
        {
            angles.ToDirectionVectors(out var forward, out _, out _);
            var nudgedStart = new Vector(
                start.X + forward.X * 20f,
                start.Y + forward.Y * 20f,
                start.Z + forward.Z * 20f);

            var retry = _core.Trace.TraceShapeAngle(in nudgedStart, in angles, 8192f, in traceParams);
            if (retry.DidHit)
            {
                point = retry.HitPoint;
                return true;
            }
        }

        point = result.HitPoint;
        return true;
    }

    private static bool TryGetEyePosition(CCSPlayerPawn pawn, out Vector eyePos)
    {
        eyePos = default;
        try
        {
            if (pawn.AbsOrigin.HasValue)
            {
                var origin = pawn.AbsOrigin.Value;
                float viewZ = 64f;
                try
                {
                    var zOffset = pawn.ViewOffset.Z.Value;
                    if (zOffset > 16f && zOffset < 96f)
                    {
                        viewZ = zOffset;
                    }
                }
                catch { }

                // Eger EyePosition gecerli bir dunya koordinatiysa ve oyuncunun konumuna yakinsa onu kullan
                if (pawn.EyePosition.HasValue)
                {
                    var ep = pawn.EyePosition.Value;
                    float dx = MathF.Abs(ep.X - origin.X);
                    float dy = MathF.Abs(ep.Y - origin.Y);
                    float dz = ep.Z - origin.Z;
                    if (dx < 32f && dy < 32f && dz >= 16f && dz <= 96f)
                    {
                        eyePos = ep;
                        return true;
                    }
                }

                eyePos = new Vector(origin.X, origin.Y, origin.Z + viewZ);
                return true;
            }

            if (pawn.EyePosition.HasValue)
            {
                eyePos = pawn.EyePosition.Value;
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Yatay (X, Y) koordinatlarini izgaraya (grid) hizalar; Z (zemin yuksekligi) ise
    /// zeminin altina gomulmemesi veya havada asili kalmamasi icin oldugu gibi korunur.
    /// </summary>
    public static Vector Snap(Vector v, float grid)
    {
        if (grid <= 0.5f) return v;
        return new Vector(
            MathF.Round(v.X / grid) * grid,
            MathF.Round(v.Y / grid) * grid,
            MathF.Round(v.Z * 2f) / 2f);
    }
}
