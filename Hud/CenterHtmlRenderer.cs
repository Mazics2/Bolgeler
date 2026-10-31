using System.Net;
using System.Text;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using Bolgeler.Services;

namespace Bolgeler.Hud;

/// <summary>
/// CS2'nin yerlesik Panorama CenterHTML panelini kullanarak modern ve renkli HUD cizer.
/// Hicbir workshop addon gerektirmez, her istemcide dogrudan gorunur.
/// </summary>
public sealed class CenterHtmlRenderer
{
    private const int DurationMs = 2000;
    private const float ResendSeconds = 0.25f;

    private readonly ISwiftlyCore _core;
    private readonly Dictionary<int, (string Html, float Time)> _last = new();

    public CenterHtmlRenderer(ISwiftlyCore core)
    {
        _core = core;
    }

    public void Render(IPlayer player, HudFrame frame)
    {
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
        if (_last.Remove(player.Slot))
        {
            try
            {
                player.SendCenterHTML(" ", 1);
            }
            catch { }
        }
    }

    public void OnDisconnect(int slot) => _last.Remove(slot);

    public void ResetAll() => _last.Clear();

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string Build(HudFrame f)
    {
        var sb = new StringBuilder();
        if (f.Menu != null)
        {
            var m = f.Menu;
            sb.Append($"<font class='fontSize-m' color='#38BDF8'><b>✦ {E(m.Title)} ✦</b></font>");
            if (m.PageCount > 1)
            {
                sb.Append($" <font class='fontSize-s' color='#94A3B8'>[{m.Page + 1}/{m.PageCount}]</font>");
            }
            sb.Append("<br>");

            if (!string.IsNullOrEmpty(m.Subtitle))
            {
                sb.Append($"<font class='fontSize-xs' color='#CBD5E1'>{E(m.Subtitle)}</font><br>");
            }

            foreach (var row in m.Rows)
            {
                var labelColor = row.Selected ? "#FACC15" : row.Style switch
                {
                    RowStyle.Danger => "#F87171",
                    RowStyle.Accent => "#38BDF8",
                    RowStyle.Muted => "#64748B",
                    _ => "#F8FAFC",
                };

                sb.Append("<font class='fontSize-sm'>");
                sb.Append(row.Selected
                    ? "<font color='#FACC15'><b>➤ </b></font>"
                    : "<font color='#475569'>• </font>");

                sb.Append(row.Selected
                    ? $"<font color='{labelColor}'><b>{E(row.Label)}</b></font>"
                    : $"<font color='{labelColor}'>{E(row.Label)}</font>");

                if (!string.IsNullOrEmpty(row.Value))
                {
                    var valueColor = row.Style switch
                    {
                        RowStyle.On => "#4ADE80",
                        RowStyle.Off => "#F87171",
                        _ => "#E2E8F0",
                    };
                    var v = row.HasArrows ? $"◀ {E(row.Value)} ▶" : $"[{E(row.Value)}]";
                    sb.Append($" <font color='{valueColor}'><b>{v}</b></font>");
                }
                sb.Append("</font><br>");
            }

            if (!string.IsNullOrEmpty(m.Prompt))
            {
                sb.Append($"<font class='fontSize-s' color='#FBBF24'><b>💬 {E(m.Prompt)}</b></font><br>");
            }

            sb.Append($"<font class='fontSize-xs' color='#94A3B8'>{E(m.Hint)}</font>");
            return sb.ToString();
        }

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
