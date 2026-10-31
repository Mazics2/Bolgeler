using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;
using Bolgeler.Models;

namespace Bolgeler.Services;

/// <summary>
/// 12 adet env_beam ile cizilen tel kafes kutu. Yerinde guncellenebilir (onizleme her tick
/// yeniden spawn etmeden hareket eder).
/// </summary>
public sealed class BeamBox
{
    private readonly ISwiftlyCore _core;
    private readonly CBeam?[] _beams = new CBeam?[12];

    public Color Color { get; private set; }
    public float Width { get; private set; }

    public BeamBox(ISwiftlyCore core)
    {
        _core = core;
    }

    public bool IsAlive
    {
        get
        {
            try
            {
                foreach (var b in _beams)
                {
                    if (b == null || !b.IsValid) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public IEnumerable<uint> EntityIndexes
    {
        get
        {
            foreach (var b in _beams)
            {
                uint idx = 0;
                try
                {
                    if (b != null && b.IsValid) idx = b.Index;
                }
                catch { }
                if (idx != 0) yield return idx;
            }
        }
    }

    public void Draw(ZoneBox box, Color color, float width)
    {
        var corners = box.Corners();
        bool styleChanged = color.R != Color.R || color.G != Color.G || color.B != Color.B || color.A != Color.A || MathF.Abs(width - Width) > 0.01f;
        Color = color;
        Width = width;

        for (int i = 0; i < 12; i++)
        {
            var (a, b) = ZoneBox.Edges[i];
            var beam = _beams[i];
            bool valid;
            try { valid = beam != null && beam.IsValid; } catch { valid = false; }

            if (!valid)
            {
                _beams[i] = Create(corners[a], corners[b], color, width);
                continue;
            }

            try
            {
                beam!.Teleport(corners[a], new QAngle(0f, 0f, 0f), new Vector(0f, 0f, 0f));
                beam.EndPos = corners[b];
                beam.EndPosUpdated();
                if (styleChanged)
                {
                    ApplyStyle(beam, color, width);
                }
            }
            catch (Exception ex)
            {
                _core.Logger.LogDebug($"[Bolgeler] beam guncellenemedi: {ex.Message}");
            }
        }
    }

    public void Destroy()
    {
        for (int i = 0; i < 12; i++)
        {
            try
            {
                var b = _beams[i];
                if (b != null && b.IsValid) b.Despawn();
            }
            catch { }
            _beams[i] = null;
        }
    }

    private CBeam? Create(Vector start, Vector end, Color color, float width)
    {
        try
        {
            var beam = _core.EntitySystem.CreateEntity<CBeam>();
            if (beam == null) return null;

            // Spawn ONCE yapilmali; render/collision bilesenleri spawn sirasinda olusur.
            beam.DispatchSpawn();
            beam.Teleport(start, new QAngle(0f, 0f, 0f), new Vector(0f, 0f, 0f));
            beam.EndPos = end;
            beam.EndPosUpdated();
            ApplyStyle(beam, color, width);
            return beam;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError($"[Bolgeler] beam olusturulamadi: {ex.Message}");
            return null;
        }
    }

    private static void ApplyStyle(CBeam beam, Color color, float width)
    {
        beam.Width = width;
        beam.WidthUpdated();
        beam.EndWidth = width;
        beam.EndWidthUpdated();
        beam.Render = color;
        beam.RenderUpdated();
    }
}

