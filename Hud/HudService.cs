using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace Bolgeler.Hud;

/// <summary>Menü dışı (çizim modu ve bölge içi bilgi) HUD gönderimini yönetir.</summary>
public sealed class HudService
{
    private readonly HashSet<int> _visible = new();

    public CenterHtmlRenderer CenterHtml { get; }

    public HudService(ISwiftlyCore core)
    {
        CenterHtml = new CenterHtmlRenderer(core);
    }

    public void Render(IPlayer player, HudFrame frame)
    {
        if (frame.IsEmpty)
        {
            Clear(player);
            return;
        }

        _visible.Add(player.Slot);
        CenterHtml.Render(player, frame);
    }

    public void Clear(IPlayer player)
    {
        if (!_visible.Remove(player.Slot))
            return;

        CenterHtml.Clear(player);
    }

    public void OnDisconnect(int slot)
    {
        _visible.Remove(slot);
        CenterHtml.OnDisconnect(slot);
    }

    /// <summary>Harita değiştiğinde önbelleği sıfırlar.</summary>
    public void OnMapChanged()
    {
        _visible.Clear();
        CenterHtml.ResetAll();
    }
}
