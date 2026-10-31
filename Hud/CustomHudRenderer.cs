using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using Bolgeler.Services;

namespace Bolgeler.Hud;

/// <summary>
/// Gercek Panorama custom HUD. Bir adet <c>custom_hud_layout</c> entity'si olusturur ve
/// panorama/layout/custom_game/bolgeler.xml layout'unu tum oyunculara yukler. Her oyuncunun
/// gordugu icerik per-player dialog variable ve CSS class override'lari ile ayarlanir;
/// sadece degisen degerler gonderilir.
/// </summary>
public sealed class CustomHudRenderer
{
    public const string Var = "v";
    public const int MaxRows = 8;

    private readonly ISwiftlyCore _core;
    private readonly string _layoutPath;
    private CCSCustomHudLayout? _hud;

    private readonly Dictionary<int, Dictionary<string, string>> _texts = new();
    private readonly Dictionary<int, Dictionary<string, bool>> _classes = new();
    private readonly Dictionary<int, bool> _capture = new();
    private float _nextEnsure;

    public CustomHudRenderer(ISwiftlyCore core, string layoutPath)
    {
        _core = core;
        _layoutPath = layoutPath;
    }

    public CCSCustomHudLayout? Entity => _hud;

    public bool IsOurs(CCSCustomHudLayout? layout)
    {
        try
        {
            return layout != null && _hud != null && _hud.IsValid && layout.Index == _hud.Index;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Entity yoksa (harita degisimi, round restart) yeniden olusturur.</summary>
    public bool EnsureEntity()
    {
        try
        {
            if (_hud != null && _hud.IsValid) return true;
        }
        catch { }

        var now = ZoneStore.SafeCurrentTime(_core);
        if (now < _nextEnsure) return false;
        _nextEnsure = now + 1f;

        try
        {
            var hud = _core.EntitySystem.CreateEntity<CCSCustomHudLayout>();
            if (hud == null) return false;
            hud.StrLayout = _layoutPath;
            hud.StrLayoutUpdated();
            hud.DispatchSpawn();
            _hud = hud;

            // Yeni entity'de hicbir override yok - tum cache'i sifirla ki her sey tekrar gonderilsin.
            _texts.Clear();
            _classes.Clear();
            _capture.Clear();
            _core.Logger.LogInformation($"[Bolgeler] Custom HUD olusturuldu ({_layoutPath}).");
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError($"[Bolgeler] Custom HUD olusturulamadi: {ex.Message}");
            _hud = null;
            return false;
        }
    }

    public void Forget()
    {
        _hud = null;
        _texts.Clear();
        _classes.Clear();
        _capture.Clear();
    }

    public void Destroy()
    {
        try
        {
            if (_hud != null && _hud.IsValid) _hud.Despawn();
        }
        catch { }
        Forget();
    }

    public void Render(IPlayer player, HudFrame frame)
    {
        if (!EnsureEntity()) return;
        int id = player.PlayerID;

        // ---------------- Menu ----------------
        var menu = frame.Menu;
        SetClass(id, "bz_menu", "bz_open", menu != null);
        if (menu != null)
        {
            SetText(id, "bz_title", menu.Title);
            SetText(id, "bz_subtitle", menu.Subtitle);
            SetText(id, "bz_page", menu.PageCount > 1 ? $"{menu.Page + 1} / {menu.PageCount}" : "");
            SetClass(id, "bz_prev", "bz_disabled", menu.Page <= 0);
            SetClass(id, "bz_next", "bz_disabled", menu.Page >= menu.PageCount - 1);
            SetClass(id, "bz_back", "bz_disabled", !menu.CanGoBack);
            SetText(id, "bz_hint", menu.Hint);
            SetText(id, "bz_prompt", menu.Prompt ?? "");
            SetClass(id, "bz_prompt", "bz_open", !string.IsNullOrEmpty(menu.Prompt));

            for (int i = 0; i < MaxRows; i++)
            {
                var row = i < menu.Rows.Count ? menu.Rows[i] : null;
                var rowId = $"bz_row{i}";
                SetClass(id, rowId, "bz_hidden", row == null);
                if (row == null) continue;

                SetText(id, rowId + "_label", row.Label);
                SetText(id, rowId + "_value", row.Value ?? "");
                SetClass(id, rowId, "bz_selected", row.Selected);
                SetClass(id, rowId, "bz_arrows", row.HasArrows);
                SetClass(id, rowId, "bz_on", row.Style == RowStyle.On);
                SetClass(id, rowId, "bz_off", row.Style == RowStyle.Off);
                SetClass(id, rowId, "bz_danger", row.Style == RowStyle.Danger);
                SetClass(id, rowId, "bz_accent", row.Style == RowStyle.Accent);
                SetClass(id, rowId, "bz_muted", row.Style == RowStyle.Muted);
            }
        }
        SetCapture(id, menu != null && menu.CursorEnabled);

        // ---------------- Cizim paneli ----------------
        var draw = frame.Draw;
        SetClass(id, "bz_draw", "bz_open", draw != null);
        if (draw != null)
        {
            SetText(id, "bz_draw_title", draw.Title);
            SetText(id, "bz_draw_info", draw.Info);
            SetText(id, "bz_draw_keys", draw.Keys);
            for (int s = 1; s <= 3; s++)
            {
                SetClass(id, $"bz_step{s}", "bz_done", draw.Step > s);
                SetClass(id, $"bz_step{s}", "bz_active", draw.Step == s);
            }
        }

        // ---------------- Bolge bilgi paneli ----------------
        var info = frame.Info;
        SetClass(id, "bz_info", "bz_open", info != null);
        if (info != null)
        {
            SetText(id, "bz_info_title", info.Title);
            SetText(id, "bz_info_effects", info.Effects);
        }
    }

    public void Clear(IPlayer player)
    {
        if (_hud == null) return;
        int id = player.PlayerID;
        SetClass(id, "bz_menu", "bz_open", false);
        SetClass(id, "bz_draw", "bz_open", false);
        SetClass(id, "bz_info", "bz_open", false);
        SetCapture(id, false);
    }

    public void OnDisconnect(int playerId)
    {
        // Ayni slota gelecek yeni oyuncu eski override'lari gormesin.
        try
        {
            if (_hud != null && _hud.IsValid)
            {
                if (_classes.TryGetValue(playerId, out var cls))
                {
                    foreach (var key in cls.Keys)
                    {
                        var sep = key.IndexOf('|');
                        _hud.SetHasClassForPlayer(playerId, key[..sep], key[(sep + 1)..], EHudPanelClassStatus_t.k_eHudPanelClassStatus_Undefined);
                    }
                }
                if (_texts.TryGetValue(playerId, out var txt))
                {
                    foreach (var panel in txt.Keys) _hud.RemoveDialogVariableStringForPlayer(playerId, panel, Var);
                }
                if (_capture.TryGetValue(playerId, out var cap) && cap) _hud.SetInputCaptureEnabledForPlayer(playerId, false);
            }
        }
        catch { }

        _texts.Remove(playerId);
        _classes.Remove(playerId);
        _capture.Remove(playerId);
    }

    private void SetText(int playerId, string panelId, string value)
    {
        if (!_texts.TryGetValue(playerId, out var map))
        {
            map = new Dictionary<string, string>();
            _texts[playerId] = map;
        }
        if (map.TryGetValue(panelId, out var old) && old == value) return;
        map[panelId] = value;
        _hud!.SetDialogVariableStringForPlayer(playerId, panelId, Var, value);
    }

    private void SetClass(int playerId, string panelId, string className, bool has)
    {
        if (!_classes.TryGetValue(playerId, out var map))
        {
            map = new Dictionary<string, bool>();
            _classes[playerId] = map;
        }
        var key = panelId + "|" + className;
        if (map.TryGetValue(key, out var old) && old == has) return;
        map[key] = has;
        _hud!.SetHasClassForPlayer(playerId, panelId, className,
            has ? EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass : EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass);
    }

    private void SetCapture(int playerId, bool enabled)
    {
        if (_hud == null) return;
        if (_capture.TryGetValue(playerId, out var old) && old == enabled) return;
        _capture[playerId] = enabled;
        _hud.SetInputCaptureEnabledForPlayer(playerId, enabled);
    }
}

