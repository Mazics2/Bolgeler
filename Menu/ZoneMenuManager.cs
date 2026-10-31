using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using Bolgeler.Config;
using Bolgeler.Hud;
using Bolgeler.Models;

namespace Bolgeler.Menu;

public class ZoneMenuManager
{
    private static readonly string[] ColorNames = { "Mavi (#00C8FF)", "Yeşil (#00FF66)", "Kırmızı (#FF3333)", "Sarı (#FFD700)", "Mor (#BF00FF)", "Turuncu (#FF8000)", "Beyaz (#FFFFFF)" };
    private static readonly string[] ColorHexes = { "#00C8FF", "#00FF66", "#FF3333", "#FFD700", "#BF00FF", "#FF8000", "#FFFFFF" };
    private static readonly string[] TeamOptions = { "Herkes", "T Takımı", "CT Takımı" };

    private readonly ISwiftlyCore _core;
    private readonly Services.ZoneStore _store;
    private readonly Services.ZoneRenderer _renderer;
    private readonly Services.DrawingService _drawing;
    private readonly Services.ChatInputService _chatInput;
    private readonly HudService _hud;
    private readonly PluginConfig _config;

    private readonly Dictionary<int, MenuSession> _sessions = new();
    private readonly HashSet<int> _persistentAdminViewers = new();
    private readonly Dictionary<int, string> _editingZoneId = new();

    public ZoneMenuManager(
        ISwiftlyCore core,
        Services.ZoneStore store,
        Services.ZoneRenderer renderer,
        Services.DrawingService drawing,
        Services.ChatInputService chatInput,
        HudService hud,
        PluginConfig config)
    {
        _core = core;
        _store = store;
        _renderer = renderer;
        _drawing = drawing;
        _chatInput = chatInput;
        _hud = hud;
        _config = config;
    }

    public IReadOnlySet<int> AdminViewers
    {
        get
        {
            var set = new HashSet<int>(_persistentAdminViewers);
            foreach (var s in _sessions.Keys) set.Add(s);
            foreach (var s in _drawing.ActiveSlots) set.Add(s);
            return set;
        }
    }

    public IReadOnlySet<string> HighlightedZones => _editingZoneId.Values.ToHashSet();

    public bool IsMenuOpen(int slot) => _sessions.ContainsKey(slot);

    public void OpenMainMenu(IPlayer player)
    {
        _sessions[player.Slot] = new MenuSession(BuildMainMenu)
        {
            LastButtons = player.PressedButtons,
            LastKeyTime = Services.ZoneStore.SafeCurrentTime(_core)
        };
        SetPlayerFrozen(player, true);
        player.SendChat($"{_config.ChatPrefix} Bölge menüsü açıldı! [Lime]W/S[Default]: Gezin | [Lime]A/D[Default]: Değer | [Lime]E[Default]: Seç | [Lime]R[Default]: Geri/Kapat");
    }

    public void CloseMenu(IPlayer player)
    {
        if (_sessions.Remove(player.Slot))
        {
            _editingZoneId.Remove(player.Slot);
            SetPlayerFrozen(player, false);
            _hud.Clear(player);
        }
    }

    public void CloseAllMenus()
    {
        foreach (var slot in _sessions.Keys.ToList())
        {
            var p = _core.PlayerManager.GetPlayer(slot);
            if (p != null && p.IsValid)
            {
                CloseMenu(p);
            }
            else
            {
                _sessions.Remove(slot);
                _editingZoneId.Remove(slot);
            }
        }
    }

    private void SetPlayerFrozen(IPlayer player, bool freeze)
    {
        if (!_config.FreezeWhileMenuOpen) return;
        try
        {
            var pawn = player.PlayerPawn;
            if (pawn == null || !pawn.IsValid) return;

            var targetMoveType = freeze ? MoveType_t.MOVETYPE_NONE : MoveType_t.MOVETYPE_WALK;
            if (pawn.MoveType != targetMoveType)
            {
                pawn.MoveType = targetMoveType;
                pawn.ActualMoveType = targetMoveType;
                pawn.MoveTypeUpdated();
            }

            if (freeze)
            {
                pawn.AbsVelocity.X = 0f;
                pawn.AbsVelocity.Y = 0f;
                pawn.AbsVelocity.Z = 0f;
            }
        }
        catch { }
    }

    public void OnPlayerDisconnected(int slot)
    {
        _sessions.Remove(slot);
        _persistentAdminViewers.Remove(slot);
        _editingZoneId.Remove(slot);
    }

    // ------------------------------------------------------------------
    // Key Navigation (W, S, A, D, E, R, Esc, Tab)
    // ------------------------------------------------------------------

    public void PollPlayerInput(IPlayer player)
    {
        if (!_sessions.TryGetValue(player.Slot, out var session)) return;

        if (_config.FreezeWhileMenuOpen)
        {
            SetPlayerFrozen(player, true);
        }

        var buttons = player.PressedButtons;
        var rising = buttons & ~session.LastButtons;
        session.LastButtons = buttons;

        if (rising == 0) return;

        if ((rising & GameButtonFlags.W) != 0) OnKeyPressed(player, KeyKind.W);
        else if ((rising & GameButtonFlags.S) != 0) OnKeyPressed(player, KeyKind.S);
        else if ((rising & GameButtonFlags.A) != 0) OnKeyPressed(player, KeyKind.A);
        else if ((rising & GameButtonFlags.D) != 0) OnKeyPressed(player, KeyKind.D);
        else if ((rising & GameButtonFlags.E) != 0) OnKeyPressed(player, KeyKind.E);
        else if ((rising & GameButtonFlags.R) != 0) OnKeyPressed(player, KeyKind.R);
        else if ((rising & GameButtonFlags.Tab) != 0) OnKeyPressed(player, KeyKind.Tab);
    }

    public void OnKeyPressed(IPlayer player, KeyKind key)
    {
        if (!_sessions.TryGetValue(player.Slot, out var session)) return;

        var now = Services.ZoneStore.SafeCurrentTime(_core);
        if (now - session.LastKeyTime < 0.12f) return;
        session.LastKeyTime = now;

        var page = session.Current(player);
        var total = page.Items.Count;
        if (total == 0) return;

        switch (key)
        {
            case KeyKind.W:
                session.Cursor = (session.Cursor - 1 + total) % total;
                break;

            case KeyKind.S:
                session.Cursor = (session.Cursor + 1) % total;
                break;

            case KeyKind.A:
                page.Items[session.Cursor].Adjust(player, -1);
                break;

            case KeyKind.D:
                page.Items[session.Cursor].Adjust(player, +1);
                break;

            case KeyKind.E:
                page.Items[session.Cursor].Select(player);
                break;

            case KeyKind.Tab:
                if (_config.EnableWorkshopCustomHud)
                {
                    session.CursorCapture = !session.CursorCapture;
                }
                break;

            case KeyKind.R:
            case KeyKind.Esc:
                if (!session.Back())
                {
                    CloseMenu(player);
                }
                break;
        }
    }

    // ------------------------------------------------------------------
    // Panorama Custom HUD Button Click Handler
    // ------------------------------------------------------------------

    public void OnCustomHudButtonClicked(IPlayer player, string buttonId)
    {
        if (!_sessions.TryGetValue(player.Slot, out var session)) return;

        var page = session.Current(player);
        int rpp = _config.RowsPerPage;
        int curPage = session.Cursor / rpp;
        int maxPage = Math.Max(0, (page.Items.Count - 1) / rpp);

        if (buttonId.StartsWith("bz_row"))
        {
            var parts = buttonId.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[1].Replace("row", ""), out var rowIndex))
            {
                int itemIndex = curPage * rpp + rowIndex;
                if (itemIndex < page.Items.Count)
                {
                    session.Cursor = itemIndex;
                    if (parts.Length > 2 && parts[2] == "left")
                    {
                        page.Items[itemIndex].Adjust(player, -1);
                    }
                    else if (parts.Length > 2 && parts[2] == "right")
                    {
                        page.Items[itemIndex].Adjust(player, +1);
                    }
                    else
                    {
                        page.Items[itemIndex].Select(player);
                    }
                }
            }
        }
        else if (buttonId == "bz_prev")
        {
            if (curPage > 0) session.Cursor = (curPage - 1) * rpp;
        }
        else if (buttonId == "bz_next")
        {
            if (curPage < maxPage) session.Cursor = (curPage + 1) * rpp;
        }
        else if (buttonId == "bz_back")
        {
            if (!session.Back()) CloseMenu(player);
        }
        else if (buttonId == "bz_close")
        {
            CloseMenu(player);
        }
        else if (buttonId == "bz_cursor")
        {
            session.CursorCapture = !session.CursorCapture;
        }
    }

    // ------------------------------------------------------------------
    // HUD Render Frame Builder
    // ------------------------------------------------------------------

    public HudFrame BuildFrameForPlayer(IPlayer player)
    {
        // 1. Cizim modu aktif mi?
        var drawView = _drawing.GetDrawView(player.Slot);
        if (drawView != null)
        {
            return new HudFrame { Draw = drawView };
        }

        // 2. Menu acik mi?
        HudMenuView? menuView = null;
        if (_sessions.TryGetValue(player.Slot, out var session))
        {
            var page = session.Current(player);
            int total = page.Items.Count;
            int rpp = _config.RowsPerPage;
            int pageCount = Math.Max(1, (int)MathF.Ceiling((float)total / rpp));
            session.Cursor = Math.Clamp(session.Cursor, 0, Math.Max(0, total - 1));
            int curPage = session.Cursor / rpp;

            var rows = new List<HudRow>();
            int start = curPage * rpp;
            int end = Math.Min(total, start + rpp);

            for (int i = start; i < end; i++)
            {
                var item = page.Items[i];
                rows.Add(new HudRow
                {
                    Label = item.Label,
                    Value = item.Value,
                    Selected = (i == session.Cursor),
                    HasArrows = item.HasArrows,
                    Style = item.Style
                });
            }

            menuView = new HudMenuView
            {
                Title = page.Title,
                Subtitle = page.Subtitle,
                Rows = rows,
                Page = curPage,
                PageCount = pageCount,
                CanGoBack = session.CanGoBack,
                CursorEnabled = session.CursorCapture,
                Hint = session.CursorCapture
                    ? "Mouse ile butonlara tıkla | [TAB] Fareyi Gizle"
                    : "[W/S] Gezin  [A/D] Değer  [E] Seç  [R] Geri/Kapat",
                Prompt = _chatInput.IsWaiting(player.Slot) ? "Chat'e yeni ismi yaz (veya '!iptal')..." : null
            };
        }

        return new HudFrame
        {
            Menu = menuView
        };
    }

    // ------------------------------------------------------------------
    // Pages / Menus
    // ------------------------------------------------------------------

    private MenuPage BuildMainMenu(IPlayer player)
    {
        _editingZoneId.Remove(player.Slot);

        var items = new List<MenuItem>
        {
            new ActionItem("➕ Yeni Bölge Çiz (Mouse İle)", p =>
            {
                CloseMenu(p);
                var defaultName = _store.NextFreeName();
                _drawing.Start(p, defaultName, zone =>
                {
                    _store.Add(zone);
                    OpenZoneDetailMenu(p, zone.Id);
                }, () => OpenMainMenu(p));
            }, null, RowStyle.Accent),

            new ActionItem($"📁 Bölgeleri Yönet ({_store.Zones.Count})", p =>
            {
                _sessions[p.Slot].Push(BuildZoneListMenu);
            }),

            new ToggleItem("👁️ Kalıcı Sınır Görünümü",
                () => _persistentAdminViewers.Contains(player.Slot),
                (p, val) =>
                {
                    if (val) _persistentAdminViewers.Add(p.Slot); else _persistentAdminViewers.Remove(p.Slot);
                }),

            new ChoiceItem("🎨 HUD Modu",
                new[] { "Panorama HUD", "Center HTML" },
                () => _hud.GetMode(player.Slot) == HudMode.Custom ? 0 : 1,
                (p, idx) => _hud.SetMode(p, idx == 0 ? HudMode.Custom : HudMode.CenterHtml)),

            new ActionItem("🔄 Harita Bölgelerini Yenile", p =>
            {
                _store.LoadMap(_store.CurrentMap);
                p.SendChat($"{_config.ChatPrefix} Harita bölgeleri yeniden yüklendi.");
            }),

            new ActionItem("❌ Menüyü Kapat", p => CloseMenu(p), null, RowStyle.Danger)
        };

        return new MenuPage
        {
            Title = "BÖLGE YÖNETİMİ",
            Subtitle = $"Harita: {_store.CurrentMap} • Toplam Bölge: {_store.Zones.Count}",
            Items = items
        };
    }

    private MenuPage BuildZoneListMenu(IPlayer player)
    {
        var items = new List<MenuItem>();

        if (_store.Zones.Count == 0)
        {
            items.Add(new ActionItem("Kayıtlı bölge bulunmuyor.", _ => { }, null, RowStyle.Muted));
        }
        else
        {
            foreach (var zone in _store.Zones)
            {
                var z = zone;
                var style = !z.Enabled ? RowStyle.Muted : (z.BlockEntry ? RowStyle.Danger : RowStyle.Normal);
                items.Add(new ActionItem(
                    z.Name,
                    p => OpenZoneDetailMenu(p, z.Id),
                    () => z.Enabled ? (z.BlockEntry ? "ENGEL" : "AKTİF") : "PASİF",
                    style
                ));
            }
        }

        return new MenuPage
        {
            Title = "KAYITLI BÖLGELER",
            Subtitle = $"{_store.Zones.Count} bölge listeleniyor",
            Items = items
        };
    }

    public void OpenZoneDetailMenu(IPlayer player, string zoneId)
    {
        if (!_sessions.ContainsKey(player.Slot))
        {
            _sessions[player.Slot] = new MenuSession(BuildMainMenu)
            {
                LastButtons = player.PressedButtons,
                LastKeyTime = Services.ZoneStore.SafeCurrentTime(_core)
            };
            SetPlayerFrozen(player, true);
        }

        _editingZoneId[player.Slot] = zoneId;
        _sessions[player.Slot].Push(p => BuildZoneDetailMenu(p, zoneId));
    }

    private MenuPage BuildZoneDetailMenu(IPlayer player, string zoneId)
    {
        var zone = _store.Get(zoneId);
        if (zone == null)
        {
            return new MenuPage
            {
                Title = "BÖLGE BULUNAMADI",
                Items = new List<MenuItem> { new ActionItem("Geri Dön", p => _sessions[p.Slot].Back()) }
            };
        }

        _editingZoneId[player.Slot] = zone.Id;

        int currentColorIndex = Math.Max(0, Array.IndexOf(ColorHexes, zone.Color));

        var items = new List<MenuItem>
        {
            new ActionItem("✏️ İsim:", p =>
            {
                _chatInput.RequestInput(p, $"{_config.ChatPrefix} Bölgenin yeni ismini chat'e yazın (İptal için '!iptal'):", newName =>
                {
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        zone.Name = newName.Trim();
                        _store.Save();
                        p.SendChat($"{_config.ChatPrefix} Bölge adı [Lime]'{zone.Name}'[Default] olarak güncellendi.");
                    }
                });
            }, () => zone.Name, RowStyle.Accent),

            new ToggleItem("🟢 Aktif", () => zone.Enabled, (p, v) => { zone.Enabled = v; _store.Save(); }),

            new ToggleItem("🛡️ Ölümsüzlük (GodMode)", () => zone.GodMode, (p, v) => { zone.GodMode = v; _store.Save(); }),

            new ToggleItem("🚫 Dışarıya Hasar Kapalı", () => zone.NoDamageOut, (p, v) => { zone.NoDamageOut = v; _store.Save(); }),

            new ToggleItem("🛡️ Dışarıdan Hasar Kapalı", () => zone.NoDamageIn, (p, v) => { zone.NoDamageIn = v; _store.Save(); }),

            new ToggleItem("🕊️ Tüm Saldırı Hasarı Kapalı", () => zone.NoDamageDealt, (p, v) => { zone.NoDamageDealt = v; _store.Save(); }),

            new ToggleItem("❤️ Can Yenileme", () => zone.Heal, (p, v) => { zone.Heal = v; _store.Save(); }),

            new NumberItem("   └ Can/sn", () => zone.HealPerSecond, (p, v) => { zone.HealPerSecond = v; _store.Save(); }, 5f, 1f, 100f, v => $"{v:0} hp/s"),

            new NumberItem("   └ Max Can", () => zone.HealMax, (p, v) => { zone.HealMax = (int)v; _store.Save(); }, 10f, 50f, 500f, v => $"{v:0} hp"),

            new ToggleItem("🛡️ Zırh Doldurma", () => zone.Armor, (p, v) => { zone.Armor = v; _store.Save(); }),

            new NumberItem("   └ Zırh/sn", () => zone.ArmorPerSecond, (p, v) => { zone.ArmorPerSecond = v; _store.Save(); }, 5f, 1f, 100f, v => $"{v:0} ap/s"),

            new NumberItem("   └ Max Zırh", () => zone.ArmorMax, (p, v) => { zone.ArmorMax = (int)v; _store.Save(); }, 10f, 50f, 500f, v => $"{v:0} ap"),

            new ToggleItem("🪂 Düşme Hasarını Kapat", () => zone.NoFallDamage, (p, v) => { zone.NoFallDamage = v; _store.Save(); }),

            new ToggleItem("🌙 Özel Yerçekimi", () => zone.Gravity, (p, v) => { zone.Gravity = v; _store.Save(); }),

            new NumberItem("   └ Yerçekimi Çarpanı", () => zone.GravityScale, (p, v) => { zone.GravityScale = MathF.Round(v * 10f) / 10f; _store.Save(); }, 0.1f, 0.1f, 2.5f, v => $"{v:0.0}x"),

            new ToggleItem("⛔ Giriş Engeli (Pushback)", () => zone.BlockEntry, (p, v) => { zone.BlockEntry = v; _store.Save(); }),

            new NumberItem("   └ Min Oyuncu Şartı", () => zone.BlockBelowPlayers, (p, v) => { zone.BlockBelowPlayers = (int)v; _store.Save(); }, 1f, 0f, 64f, v => v == 0 ? "Her Zaman" : $"<{v:0} Kişi"),

            new ChoiceItem("👥 Takım", TeamOptions, () => (int)zone.Team, (p, idx) => { zone.Team = (ZoneTeam)idx; _store.Save(); }),

            new ChoiceItem("🎨 Çizgi Rengi", ColorNames, () => currentColorIndex, (p, idx) => { zone.Color = ColorHexes[idx]; _store.Save(); }),

            new ToggleItem("🌐 Sınırları Herkese Göster", () => zone.ShowBorder, (p, v) => { zone.ShowBorder = v; _store.Save(); }),

            new ToggleItem("💬 Giriş/Çıkış Mesajları", () => zone.Messages, (p, v) => { zone.Messages = v; _store.Save(); }),

            new ActionItem("📐 Konum & Boyut Düzenle", p =>
            {
                _sessions[p.Slot].Push(pl => BuildTransformMenu(pl, zone.Id));
            }, null, RowStyle.Accent),

            new ActionItem("🎯 Köşeleri Yeniden Çiz (Mouse İle)", p =>
            {
                CloseMenu(p);
                _drawing.Start(p, zone.Name, updated =>
                {
                    _store.Save();
                    OpenZoneDetailMenu(p, updated.Id);
                }, () => OpenZoneDetailMenu(p, zone.Id), existingZone: zone);
            }, null, RowStyle.Accent),

            new ActionItem("⚡ Bölgeye Işınlan", p =>
            {
                var c = zone.Box.Center;
                p.Teleport(new Vector(c.X, c.Y, zone.Box.Mins.Z + 10f), null, new Vector(0, 0, 0));
                p.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] merkezine ışınlandınız.");
            }),

            new ActionItem("🗑️ BÖLGEYİ SİL", p =>
            {
                _store.Remove(zone.Id);
                p.SendChat($"{_config.ChatPrefix} [Red]'{zone.Name}' silindi.[Default]");
                _sessions[p.Slot].Back();
            }, null, RowStyle.Danger)
        };

        return new MenuPage
        {
            Title = $"BÖLGE: {zone.Name}",
            Subtitle = $"Boyut: {zone.Box.Size.X:F0}x{zone.Box.Size.Y:F0}x{zone.Box.Size.Z:F0}",
            Items = items
        };
    }

    private MenuPage BuildTransformMenu(IPlayer player, string zoneId)
    {
        var zone = _store.Get(zoneId);
        if (zone == null)
        {
            return new MenuPage { Title = "Bölge bulunamadı" };
        }

        float step = 16f;
        var box = zone.Box;

        var items = new List<MenuItem>
        {
            new NumberItem("Kaydır X (Sağ/Sol)", () => zone.Box.Center.X, (p, v) =>
            {
                zone.Box = zone.Box.Offset(v - zone.Box.Center.X, 0, 0);
                _store.Save();
            }, step, -16384f, 16384f, v => $"{v:0} px"),

            new NumberItem("Kaydır Y (İleri/Geri)", () => zone.Box.Center.Y, (p, v) =>
            {
                zone.Box = zone.Box.Offset(0, v - zone.Box.Center.Y, 0);
                _store.Save();
            }, step, -16384f, 16384f, v => $"{v:0} px"),

            new NumberItem("Kaydır Z (Yukarı/Aşağı)", () => zone.Box.Center.Z, (p, v) =>
            {
                zone.Box = zone.Box.Offset(0, 0, v - zone.Box.Center.Z);
                _store.Save();
            }, step, -16384f, 16384f, v => $"{v:0} px"),

            new NumberItem("Genişlik (X)", () => zone.Box.Size.X, (p, v) =>
            {
                zone.Box = zone.Box.WithSize(v, zone.Box.Size.Y, zone.Box.Size.Z);
                _store.Save();
            }, step, 16f, 4096f, v => $"{v:0} px"),

            new NumberItem("Uzunluk (Y)", () => zone.Box.Size.Y, (p, v) =>
            {
                zone.Box = zone.Box.WithSize(zone.Box.Size.X, v, zone.Box.Size.Z);
                _store.Save();
            }, step, 16f, 4096f, v => $"{v:0} px"),

            new NumberItem("Yükseklik (Z)", () => zone.Box.Size.Z, (p, v) =>
            {
                zone.Box = zone.Box.WithSize(zone.Box.Size.X, zone.Box.Size.Y, v);
                _store.Save();
            }, step, 16f, 4096f, v => $"{v:0} px")
        };

        return new MenuPage
        {
            Title = $"{zone.Name} - KONUM & BOYUT",
            Subtitle = "A/D ile değeri 16px değiştir",
            Items = items
        };
    }
}

