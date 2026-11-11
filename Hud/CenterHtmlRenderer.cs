using System.Net;
using System.Text;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using Bolgeler.Services;

namespace Bolgeler.Hud;

/// <summary>
/// Menü dışı HUD durumlarını (çizim modu adımları ve bölge içi bilgi paneli) CenterHTML üzerinden gösterir.
/// Herhangi bir Core.Menu oturumu açıkken araya girmez.
/// </summary>
public sealed class CenterHtmlRenderer
{
    private const int DurationMs = 2500;
    private const float ResendSeconds = 1.5f;

    private readonly ISwiftlyCore _core;
    private readonly Dictionary<int, (string Html, float Time)> _last = new();

    public CenterHtmlRenderer(ISwiftlyCore core)
    {
        _core = core;
    }

    public void Render(IPlayer player, HudFrame frame)
    {
        if (_core.Menu.GetSession(player) != null)
        {
            _last.Remove(player.Slot);
            return;
        }

        var html = Build(frame);
        var now = ZoneStore.SafeCurrentTime(_core);
        if (_last.TryGetValue(player.Slot, out var last) && last.Html == html && now - last.Time < ResendSeconds)
        {
            return;
        }

        _last[player.Slot] = (html, now);
        try
        {
            player.SendCenterHTML(html, DurationMs);
        }
        catch { }
    }

    public void Clear(IPlayer player)
    {
        if (!_last.Remove(player.Slot))
            return;

        if (_core.Menu.GetSession(player) != null)
            return;

        try
        {
            player.SendCenterHTML(" ", 1);
        }
        catch { }
    }

    public void OnDisconnect(int slot) => _last.Remove(slot);

    public void ResetAll() => _last.Clear();

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string Build(HudFrame f)
    {
        var sb = new StringBuilder();
        if (f.Draw != null)
        {
            var d = f.Draw;
            sb.Append($"<font class='fontSize-m' color='#4ADE80'><b>📐 {E(d.Title)}</b></font><br>");
            sb.Append($"<font class='fontSize-sm' color='#F8FAFC'>{E(d.Info).Replace("\n", "<br>")}</font><br>");
            sb.Append($"<font class='fontSize-xs' color='#FACC15'><b>{E(d.Keys).Replace("\n", "<br>")}</b></font>");
            return sb.ToString();
        }

        if (f.Info != null)
        {
            var i = f.Info;
            sb.Append($"<font class='fontSize-m' color='{E(i.Color)}'><b>{E(i.Title)}</b></font><br>");
            sb.Append($"<font class='fontSize-sm' color='#E2E8F0'>{E(i.Effects)}</font>");
            return sb.ToString();
        }

        return " ";
    }
}
