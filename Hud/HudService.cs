using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using Bolgeler.Config;

namespace Bolgeler.Hud;

/// <summary>Oyuncu basina HUD modunu secip ilgili renderer'a yonlendirir.</summary>
public sealed class HudService
{
    private readonly ISwiftlyCore _core;
    private readonly PluginConfig _config;
    private readonly Dictionary<int, HudMode> _modes = new();
    private readonly HashSet<int> _visible = new();

    public CustomHudRenderer Custom { get; }
    public CenterHtmlRenderer CenterHtml { get; }

    public HudService(ISwiftlyCore core, PluginConfig config)
    {
        _core = core;
        _config = config;
        Custom = new CustomHudRenderer(core, config.HudLayoutPath);
        CenterHtml = new CenterHtmlRenderer(core);
    }

    public HudMode GetMode(int slot)
    {
        if (!_config.EnableWorkshopCustomHud)
        {
            return HudMode.CenterHtml;
        }
        return _modes.TryGetValue(slot, out var m) ? m : _config.DefaultHudMode;
    }

    public void SetMode(IPlayer player, HudMode mode)
    {
        if (mode == HudMode.Custom && !_config.EnableWorkshopCustomHud)
        {
            player.SendChat($"{_config.ChatPrefix} [Yellow]Not:[Default] Workshop Custom HUD (bolgeler.xml) aktif değil (config.json -> EnableWorkshopCustomHud: false). Panorama CenterHTML HUD kullanılıyor.");
            mode = HudMode.CenterHtml;
        }

        var old = GetMode(player.Slot);
        if (old == mode) return;
        Clear(player);
        _modes[player.Slot] = mode;
    }

    public void Render(IPlayer player, HudFrame frame)
    {
        if (frame.IsEmpty)
        {
            Clear(player);
            return;
        }

        _visible.Add(player.Slot);
        if (GetMode(player.Slot) == HudMode.Custom)
        {
            Custom.Render(player, frame);
        }
        else
        {
            CenterHtml.Render(player, frame);
        }
    }

    public void Clear(IPlayer player)
    {
        if (!_visible.Remove(player.Slot)) return;
        if (_config.EnableWorkshopCustomHud)
        {
            Custom.Clear(player);
        }
        CenterHtml.Clear(player);
    }

    public void OnDisconnect(int slot)
    {
        _visible.Remove(slot);
        _modes.Remove(slot);
        Custom.OnDisconnect(slot);
        CenterHtml.OnDisconnect(slot);
    }

    /// <summary>Harita degisti: entity'ler gitti, herkesin HUD'u sifirdan gonderilecek.</summary>
    public void OnMapChanged()
    {
        _visible.Clear();
        Custom.Forget();
        CenterHtml.ResetAll();
    }

    /// <summary>Workshop Custom HUD aktifse entity hazir tutulur.</summary>
    public void Tick()
    {
        if (_config.EnableWorkshopCustomHud)
        {
            Custom.EnsureEntity();
        }
    }
}
