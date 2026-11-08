using MenuApi.API;
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

    public IMenuApi? MenuApi { get; set; }

    private readonly Dictionary<int, MenuSession> _sessions = new();
    private readonly HashSet<int> _persistentAdminViewers = new();
    private readonly Dictionary<int, string> _editingZoneId = new();
    private readonly Dictionary<int, float> _transformSteps = new();
    private static readonly float[] StepSizes = { 4f, 16f, 32f, 64f, 128f };
    private static readonly string[] StepLabels = { "4 px (Hassas)", "16 px (Normal)", "32 px (Orta)", "64 px (Geniş)", "128 px (Hızlı)" };

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
            foreach (var s in _editingZoneId.Keys) set.Add(s);
            return set;
        }
    }

    public IReadOnlySet<string> HighlightedZones => _editingZoneId.Values.ToHashSet();

    public bool IsMenuOpen(int slot)
    {
        if (_sessions.ContainsKey(slot)) return true;
        if (MenuApi != null)
        {
            var p = _core.PlayerManager.GetPlayer(slot);
            if (p != null && p.IsValid && MenuApi.HasActiveMenu(p))
            {
                var active = MenuApi.GetActiveMenu(p);
                if (active != null && active.Id.StartsWith("bolgeler.", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    public void OpenMainMenu(IPlayer player)
    {
        if (MenuApi != null)
        {
            OpenMenuApiFromSession(player, BuildMainMenu);
            return;
        }

        _sessions[player.Slot] = new MenuSession(BuildMainMenu)
        {
            LastButtons = player.PressedButtons,
            LastKeyTime = Services.ZoneStore.SafeCurrentTime(_core)
        };
        SetPlayerFrozen(player, true);
        player.SendChat($"{_config.ChatPrefix} Bölge menüsü açıldı! [Lime]W/S[Default]: Gezin | [Lime]A/D[Default]: Değer | [Lime]E[Default]: Seç | [Lime]R[Default]: Geri/Kapat");
    }

    private void OpenMenuApiFromSession(IPlayer player, MenuFactory rootFactory, MenuFactory? initialPageFactory = null)
    {
        var session = new MenuSession(rootFactory)
        {
            LastButtons = player.PressedButtons,
            LastKeyTime = Services.ZoneStore.SafeCurrentTime(_core)
        };
        if (initialPageFactory != null)
        {
            session.Push(initialPageFactory);
        }
        _sessions[player.Slot] = session;
        RenderMenuApiPage(player, 0);
    }

    private void RenderMenuApiPage(IPlayer player, int pageIndex = 0)
    {
        if (MenuApi == null) return;
        if (!_sessions.TryGetValue(player.Slot, out var session)) return;

        var page = session.Current(player);
        var builder = MenuApi.CreateBuilder($"bolgeler.page.{player.Slot}", page.Title)
            .SetTheme(MenuTheme.Blue)
            .SetItemsPerPage(7)
            .SetTimeout(TimeSpan.FromSeconds(60))
            .EnableSounds(true)
            .EnableSlotSelection(true)
            .EnableChatSelection(true);

        if (!string.IsNullOrWhiteSpace(page.Subtitle))
        {
            builder.SetSubtitle(page.Subtitle);
        }

        for (int i = 0; i < page.Items.Count; i++)
        {
            var item = page.Items[i];
            int idx = i;
            string valStr = string.IsNullOrWhiteSpace(item.Value) ? "" : $" [{item.Value}]";
            string text = $"{item.Label}{valStr}";
            string? color = item.Style switch
            {
                RowStyle.Accent => "#38BDF8",
                RowStyle.Danger => "#FF5555",
                RowStyle.Muted => "#94A3B8",
                _ => null
            };

            if (item.HasArrows)
            {
                builder.AddItem($"{text} (+)", ctx =>
                {
                    if (!_sessions.ContainsKey(ctx.Player.Slot)) return;
                    item.Adjust(ctx.Player, +1);
                    if (_sessions.ContainsKey(ctx.Player.Slot))
                        RenderMenuApiPage(ctx.Player, ctx.PageIndex);
                }, color ?? "#FACC15", enabled: true, closeOnSelect: false);
            }
            else
            {
                builder.AddItem(text, ctx =>
                {
                    if (!_sessions.ContainsKey(ctx.Player.Slot)) return;
                    int beforeDepth = session.CanGoBack ? 1 : 0;
                    item.Select(ctx.Player);
                    if (_sessions.ContainsKey(ctx.Player.Slot) && !_chatInput.IsWaiting(ctx.Player.Slot) && !_drawing.IsDrawing(ctx.Player.Slot))
                    {
                        RenderMenuApiPage(ctx.Player, 0);
                    }
                }, color ?? "#E2E8F0", enabled: item.Style != RowStyle.Muted, closeOnSelect: false);
            }
        }

        if (session.CanGoBack)
        {
            builder.AddItem("⬅️ Geri Dön", ctx =>
            {
                if (_sessions.TryGetValue(ctx.Player.Slot, out var s))
                {
                    if (s.Back())
                        RenderMenuApiPage(ctx.Player, 0);
                    else
                        CloseMenu(ctx.Player);
                }
            }, "#FFB000", enabled: true, closeOnSelect: false);
        }

        builder.Open(player, pageIndex);
    }

    public void CloseMenu(IPlayer player)
    {
        if (MenuApi != null && MenuApi.HasActiveMenu(player))
        {
            MenuApi.CloseMenu(player);
        }
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

            // Admin Noclip modundaysa noclip durumunu bozma
            if (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP || pawn.ActualMoveType == MoveType_t.MOVETYPE_NOCLIP)
                return;

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
        if (MenuApi != null) return;
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
        if (MenuApi != null) return;
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

        // 2. Menu acik mi? (MenuApi aktifse kendi CenterHTML/HUD cizimini yapar)
        HudMenuView? menuView = null;
        if (MenuApi == null && _sessions.TryGetValue(player.Slot, out var session))
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
        int activeCount = _store.Zones.Count(z => z.Enabled);

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

            new ActionItem("📦 Konumuma Kutu Bölge Oluştur (256x256)", p =>
            {
                var pawn = p.PlayerPawn;
                if (pawn == null || !pawn.IsValid || !pawn.AbsOrigin.HasValue)
                {
                    p.SendChat($"{_config.ChatPrefix} [Red]Geçerli bir oyuncu konumunuz yok.[Default]");
                    return;
                }
                var pos = pawn.AbsOrigin.Value;
                var zone = new ZoneData
                {
                    Name = _store.NextFreeName(),
                    Box = new ZoneBox(
                        new Vector(pos.X - 128f, pos.Y - 128f, pos.Z),
                        new Vector(pos.X + 128f, pos.Y + 128f, pos.Z + 128f))
                };
                _store.Add(zone);
                p.SendChat($"{_config.ChatPrefix} Konumunuza [Lime]'{zone.Name}'[Default] kutu bölgesi oluşturuldu!");
                OpenZoneDetailMenu(p, zone.Id);
            }, () => "Hızlı", RowStyle.Accent),

            new ActionItem($"📁 Bölgeleri Yönet ({activeCount}/{_store.Zones.Count})", p =>
            {
                _sessions[p.Slot].Push(BuildZoneListMenu);
            }),

            new ActionItem("⚡ Toplu Bölge İşlemleri", p =>
            {
                _sessions[p.Slot].Push(BuildBulkActionsMenu);
            }),

            new ToggleItem("👁️ Kalıcı Sınır Görünümü",
                () => _persistentAdminViewers.Contains(player.Slot),
                (p, val) =>
                {
                    if (val) _persistentAdminViewers.Add(p.Slot); else _persistentAdminViewers.Remove(p.Slot);
                }),

            new ToggleItem("🔒 Menü Açıkken Hareketi Kilitle",
                () => _config.FreezeWhileMenuOpen,
                (p, val) =>
                {
                    _config.FreezeWhileMenuOpen = val;
                    if (!val)
                    {
                        var pawn = p.PlayerPawn;
                        if (pawn != null && pawn.IsValid && pawn.MoveType == MoveType_t.MOVETYPE_NONE)
                        {
                            pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                            pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                            pawn.MoveTypeUpdated();
                        }
                    }
                    else
                    {
                        SetPlayerFrozen(p, true);
                    }
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
            Subtitle = $"Harita: {_store.CurrentMap} • Aktif: {activeCount}/{_store.Zones.Count}",
            Items = items
        };
    }

    private MenuPage BuildBulkActionsMenu(IPlayer player)
    {
        var items = new List<MenuItem>
        {
            new ActionItem("🟢 Tüm Bölgeleri Aktif Et", p =>
            {
                foreach (var z in _store.Zones) z.Enabled = true;
                _store.Save();
                p.SendChat($"{_config.ChatPrefix} Tüm bölgeler ({_store.Zones.Count}) [Lime]aktif edildi[Default].");
            }, null, RowStyle.Accent),

            new ActionItem("🔴 Tüm Bölgeleri Pasif Yap", p =>
            {
                foreach (var z in _store.Zones) z.Enabled = false;
                _store.Save();
                p.SendChat($"{_config.ChatPrefix} Tüm bölgeler ({_store.Zones.Count}) [Yellow]pasif yapıldı[Default].");
            }),

            new ActionItem("🌐 Tüm Bölgelerin Sınırlarını Herkese Aç", p =>
            {
                foreach (var z in _store.Zones) z.ShowBorder = true;
                _store.Save();
                p.SendChat($"{_config.ChatPrefix} Tüm bölgelerin sınır görünümü [Lime]herkese açıldı[Default].");
            }),

            new ActionItem("🙈 Tüm Bölgelerin Sınırlarını Gizle", p =>
            {
                foreach (var z in _store.Zones) z.ShowBorder = false;
                _store.Save();
                p.SendChat($"{_config.ChatPrefix} Tüm bölgelerin sınır görünümü [Yellow]gizlendi[Default].");
            }),

            new ActionItem("⬅️ Ana Menüye Dön", p => _sessions[p.Slot].Back())
        };

        return new MenuPage
        {
            Title = "TOPLU BÖLGE İŞLEMLERİ",
            Subtitle = $"Harita: {_store.CurrentMap} • {_store.Zones.Count} Bölge",
            Items = items
        };
    }

    private MenuPage BuildZonePresetsMenu(IPlayer player, string zoneId)
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

        var items = new List<MenuItem>
        {
            new ActionItem("🛡️ Güvenli Bölge (SafeZone + Şifa)", p =>
            {
                zone.GodMode = true;
                zone.NoDamageOut = true;
                zone.NoDamageIn = true;
                zone.NoDamageDealt = false;
                zone.NoFallDamage = true;
                zone.Heal = true;
                zone.HealPerSecond = 15f;
                zone.HealMax = 100;
                zone.Armor = true;
                zone.ArmorPerSecond = 15f;
                zone.ArmorMax = 100;
                zone.BlockEntry = false;
                zone.Color = "#00FF66";
                _store.Save();
                p.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesine [Lime]Güvenli Bölge (SafeZone)[Default] şablonu uygulandı!");
                _sessions[p.Slot].Back();
            }, null, RowStyle.Accent),

            new ActionItem("⛔ Yasaklı Bölge / Rush Engeli (Pushback)", p =>
            {
                zone.GodMode = false;
                zone.NoDamageOut = false;
                zone.NoDamageIn = false;
                zone.NoDamageDealt = false;
                zone.Heal = false;
                zone.Armor = false;
                zone.BlockEntry = true;
                zone.ShowBorder = true;
                zone.Color = "#FF3333";
                _store.Save();
                p.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesine [Red]Yasaklı Bölge (Pushback)[Default] şablonu uygulandı!");
                _sessions[p.Slot].Back();
            }, null, RowStyle.Danger),

            new ActionItem("⚔️ Arena / Düello Bölgesi (Dış Müdahale Kapalı)", p =>
            {
                zone.GodMode = false;
                zone.NoDamageOut = true;
                zone.NoDamageIn = true;
                zone.NoDamageDealt = false;
                zone.NoFallDamage = true;
                zone.Heal = false;
                zone.BlockEntry = false;
                zone.ShowBorder = true;
                zone.Color = "#FFD700";
                _store.Save();
                p.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesine [Yellow]Arena / Düello[Default] şablonu uygulandı!");
                _sessions[p.Slot].Back();
            }),

            new ActionItem("🌙 Ay Yerçekimi & Surf Bölgesi (0.4x Gravity)", p =>
            {
                zone.Gravity = true;
                zone.GravityScale = 0.4f;
                zone.NoFallDamage = true;
                zone.Color = "#BF00FF";
                _store.Save();
                p.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesine [Purple]Ay Yerçekimi (0.4x)[Default] şablonu uygulandı!");
                _sessions[p.Slot].Back();
            }),

            new ActionItem("🏥 Hızlı Revir İstasyonu (25 HP/s + 25 AP/s)", p =>
            {
                zone.GodMode = false;
                zone.Heal = true;
                zone.HealPerSecond = 25f;
                zone.HealMax = 100;
                zone.Armor = true;
                zone.ArmorPerSecond = 25f;
                zone.ArmorMax = 100;
                zone.ShowBorder = true;
                zone.Color = "#00C8FF";
                _store.Save();
                p.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesine [Lime]Revir İstasyonu[Default] şablonu uygulandı!");
                _sessions[p.Slot].Back();
            }),

            new ActionItem("⬅️ Bölge Detayına Dön", p => _sessions[p.Slot].Back())
        };

        return new MenuPage
        {
            Title = $"ŞABLON SEÇ: {zone.Name}",
            Subtitle = "Tek tıkla hazır bölge yapılandırması uygula",
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
            if (MenuApi == null)
                SetPlayerFrozen(player, true);
        }

        _editingZoneId[player.Slot] = zoneId;
        _sessions[player.Slot].Push(p => BuildZoneDetailMenu(p, zoneId));
        if (MenuApi != null)
        {
            RenderMenuApiPage(player, 0);
        }
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
                if (MenuApi != null) MenuApi.CloseMenu(p);
                _chatInput.RequestInput(p, $"{_config.ChatPrefix} Bölgenin yeni ismini chat'e yazın (İptal için '!iptal'):", newName =>
                {
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        zone.Name = newName.Trim();
                        _store.Save();
                        p.SendChat($"{_config.ChatPrefix} Bölge adı [Lime]'{zone.Name}'[Default] olarak güncellendi.");
                    }
                    if (MenuApi != null) OpenZoneDetailMenu(p, zone.Id);
                });
            }, () => zone.Name, RowStyle.Accent),

            new ToggleItem("🟢 Aktif", () => zone.Enabled, (p, v) => { zone.Enabled = v; _store.Save(); }),

            new ActionItem("⚡ Hazır Bölge Şablonu Uygula", p =>
            {
                _sessions[p.Slot].Push(pl => BuildZonePresetsMenu(pl, zone.Id));
            }, () => "5 Şablon", RowStyle.Accent),

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

            new ActionItem("📍 Bölgeyi Konumuma Taşı", p =>
            {
                var pawn = p.PlayerPawn;
                if (pawn == null || !pawn.IsValid || !pawn.AbsOrigin.HasValue) return;
                var pos = pawn.AbsOrigin.Value;
                var size = zone.Box.Size;
                zone.Box = new ZoneBox(
                    new Vector(pos.X - size.X * 0.5f, pos.Y - size.Y * 0.5f, pos.Z),
                    new Vector(pos.X + size.X * 0.5f, pos.Y + size.Y * 0.5f, pos.Z + size.Z));
                _store.Save();
                p.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesi mevcut konumunuza taşındı.");
            }),

            new ActionItem("📋 Bölgeyi Kopyala (Çoğalt)", p =>
            {
                var copy = new ZoneData
                {
                    Name = $"{zone.Name} (Kopya)",
                    Mins = zone.Mins.ToArray(),
                    Maxs = zone.Maxs.ToArray(),
                    Enabled = zone.Enabled,
                    Color = zone.Color,
                    ShowBorder = zone.ShowBorder,
                    Team = zone.Team,
                    GodMode = zone.GodMode,
                    NoDamageOut = zone.NoDamageOut,
                    NoDamageIn = zone.NoDamageIn,
                    NoDamageDealt = zone.NoDamageDealt,
                    NoFallDamage = zone.NoFallDamage,
                    Heal = zone.Heal,
                    HealPerSecond = zone.HealPerSecond,
                    HealMax = zone.HealMax,
                    Armor = zone.Armor,
                    ArmorPerSecond = zone.ArmorPerSecond,
                    ArmorMax = zone.ArmorMax,
                    Gravity = zone.Gravity,
                    GravityScale = zone.GravityScale,
                    BlockEntry = zone.BlockEntry,
                    BlockBelowPlayers = zone.BlockBelowPlayers,
                    Messages = zone.Messages
                };
                copy.Box = copy.Box.Offset(32f, 32f, 0f);
                _store.Add(copy);
                p.SendChat($"{_config.ChatPrefix} [Lime]'{copy.Name}'[Default] oluşturuldu.");
                OpenZoneDetailMenu(p, copy.Id);
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

        float step = _transformSteps.TryGetValue(player.Slot, out var s) ? s : 16f;
        int stepIdx = Math.Max(0, Array.IndexOf(StepSizes, step));

        var items = new List<MenuItem>
        {
            new ChoiceItem("📏 Hassasiyet (Adım)", StepLabels, () => stepIdx, (p, idx) =>
            {
                _transformSteps[p.Slot] = StepSizes[idx];
            }),

            new NumberItem("Kaydır X (Sağ/Sol)", () => zone.Box.Center.X, (p, v) =>
            {
                float curStep = _transformSteps.TryGetValue(p.Slot, out var st) ? st : 16f;
                float delta = MathF.Sign(v - zone.Box.Center.X) * curStep;
                zone.Box = zone.Box.Offset(delta, 0, 0);
                _store.Save();
            }, step, -16384f, 16384f, v => $"{v:0} px"),

            new NumberItem("Kaydır Y (İleri/Geri)", () => zone.Box.Center.Y, (p, v) =>
            {
                float curStep = _transformSteps.TryGetValue(p.Slot, out var st) ? st : 16f;
                float delta = MathF.Sign(v - zone.Box.Center.Y) * curStep;
                zone.Box = zone.Box.Offset(0, delta, 0);
                _store.Save();
            }, step, -16384f, 16384f, v => $"{v:0} px"),

            new NumberItem("Kaydır Z (Yukarı/Aşağı)", () => zone.Box.Center.Z, (p, v) =>
            {
                float curStep = _transformSteps.TryGetValue(p.Slot, out var st) ? st : 16f;
                float delta = MathF.Sign(v - zone.Box.Center.Z) * curStep;
                zone.Box = zone.Box.Offset(0, 0, delta);
                _store.Save();
            }, step, -16384f, 16384f, v => $"{v:0} px"),

            new NumberItem("Genişlik (X)", () => zone.Box.Size.X, (p, v) =>
            {
                float curStep = _transformSteps.TryGetValue(p.Slot, out var st) ? st : 16f;
                float next = Math.Clamp(zone.Box.Size.X + MathF.Sign(v - zone.Box.Size.X) * curStep, 16f, 8192f);
                zone.Box = zone.Box.WithSize(next, zone.Box.Size.Y, zone.Box.Size.Z);
                _store.Save();
            }, step, 16f, 8192f, v => $"{v:0} px"),

            new NumberItem("Uzunluk (Y)", () => zone.Box.Size.Y, (p, v) =>
            {
                float curStep = _transformSteps.TryGetValue(p.Slot, out var st) ? st : 16f;
                float next = Math.Clamp(zone.Box.Size.Y + MathF.Sign(v - zone.Box.Size.Y) * curStep, 16f, 8192f);
                zone.Box = zone.Box.WithSize(zone.Box.Size.X, next, zone.Box.Size.Z);
                _store.Save();
            }, step, 16f, 8192f, v => $"{v:0} px"),

            new NumberItem("Yükseklik (Z)", () => zone.Box.Size.Z, (p, v) =>
            {
                float curStep = _transformSteps.TryGetValue(p.Slot, out var st) ? st : 16f;
                float next = Math.Clamp(zone.Box.Size.Z + MathF.Sign(v - zone.Box.Size.Z) * curStep, 16f, 8192f);
                zone.Box = zone.Box.WithSize(zone.Box.Size.X, zone.Box.Size.Y, next);
                _store.Save();
            }, step, 16f, 8192f, v => $"{v:0} px"),

            new ActionItem("📍 Bölgeyi Konumuma Hizala", p =>
            {
                var pawn = p.PlayerPawn;
                if (pawn == null || !pawn.IsValid || !pawn.AbsOrigin.HasValue) return;
                var pos = pawn.AbsOrigin.Value;
                var sz = zone.Box.Size;
                zone.Box = new ZoneBox(
                    new Vector(pos.X - sz.X * 0.5f, pos.Y - sz.Y * 0.5f, pos.Z),
                    new Vector(pos.X + sz.X * 0.5f, pos.Y + sz.Y * 0.5f, pos.Z + sz.Z));
                _store.Save();
            }, null, RowStyle.Accent)
        };

        return new MenuPage
        {
            Title = $"{zone.Name} - KONUM & BOYUT",
            Subtitle = $"A/D ile değeri {step:0}px değiştir",
            Items = items
        };
    }
}

