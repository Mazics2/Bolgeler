using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.Natives;

namespace Bolgeler.Services;

/// <summary>
/// Giris engelli bolgelerden oyuncuyu disari iter. Duzeltme GameHooks.Pawn.PostThink icinde
/// yapilir; boylece o tick'in hareket simulasyonu teleport'u ezemez (tus basili olsa bile).
/// </summary>
public class PushService
{
    private readonly ISwiftlyCore _core;
    private readonly Dictionary<int, (Vector Pos, Vector Vel)> _pending = new();

    public PushService(ISwiftlyCore core)
    {
        _core = core;
    }

    public void Load()
    {
        try
        {
            _core.GameHooks.Pawn.PostThink.Post += OnPostThink;
        }
        catch { }
    }

    public void Unload()
    {
        try
        {
            _core.GameHooks.Pawn.PostThink.Post -= OnPostThink;
        }
        catch { }
        _pending.Clear();
    }

    public void Request(int slot, Vector pos, Vector velocity) => _pending[slot] = (pos, velocity);

    private void OnPostThink(ref PostThinkPawnPostContext ctx)
    {
        if (_pending.Count == 0) return;
        try
        {
            var player = ctx.Params.Player;
            if (player == null || !player.IsValid || !player.IsAlive || !_pending.Remove(player.Slot, out var req)) return;
            player.Teleport(req.Pos, null, req.Vel);
        }
        catch { }
    }
}
