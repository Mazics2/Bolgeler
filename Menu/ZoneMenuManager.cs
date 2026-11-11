using System.Text.RegularExpressions;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using Bolgeler.Config;
using Bolgeler.Models;

namespace Bolgeler.Menu;

public class ZoneMenuManager
{
    private const string MenuTag = "bolgeler.menu";

    private sealed record ColorPreset(string Label, string Hex, string SwatchToken);

    private static readonly ColorPreset[] ColorPresets =
    [
        new("Mavi (#00C8FF)", "#00C8FF", "cyan"),
        new("Yeşil (#00FF66)", "#00FF66", "green"),
        new("Kırmızı (#FF3333)", "#FF3333", "red"),
        new("Sarı (#FFD700)", "#FFD700", "yellow"),
        new("Mor (#BF00FF)", "#BF00FF", "purple"),
        new("Turuncu (#FF8000)", "#FF8000", "orange"),
        new("Beyaz (#FFFFFF)", "#FFFFFF", "white")
    ];

    private static readonly float[] StepSizes = [4f, 16f, 32f, 64f, 128f];
    private static readonly Regex HtmlTagRegex = new("<[^>]*>", RegexOptions.Compiled);

    private sealed class SwatchColorSelector : SelectorComponent<ColorPreset>
    {
        public SwatchColorSelector(
            string text,
            IReadOnlyList<ColorPreset> choices,
            int defaultIndex,
            Action<MenuValueChangedContext<ColorPreset>> onItemChanged,
            string? id = null)
            : base(text, choices, defaultIndex, onItemChanged, id)
        {
            Formatter = c => c.Label;
        }

        public override MenuNode Render(IMenuComponentRenderContext context)
        {
            var node = base.Render(context);
            if (Choices.Count == 0)
                return node;

            int idx = Math.Clamp(GetValue(context.Session), 0, Choices.Count - 1);
            string token = Choices[idx].SwatchToken;
            if (string.IsNullOrEmpty(token))
                return node;

            var raw = new MenuRawNode("customhud.panorama", $"swatch={token}");
            return node is MenuLineNode line
                ? line with { Children = [.. line.Children, raw] }
                : MenuLineNode.Of(node, raw);
        }
    }

    private sealed class DangerSubmenuComponent : SubmenuComponent
    {
        public DangerSubmenuComponent(string text, Func<IMenuSession, IMenu?> factory, string? id = null)
            : base(text, factory, id)
        {
            FocusedColor = "#F87171";
        }

        public override MenuNode Render(IMenuComponentRenderContext context)
        {
            var node = base.Render(context);
            var raw = new MenuRawNode("customhud.panorama", "danger");
            return node is MenuLineNode line
                ? line with { Children = [.. line.Children, raw] }
                : MenuLineNode.Of(node, raw);
        }
    }

    private sealed class DangerButtonComponent : ButtonComponent
    {
        public DangerButtonComponent(string text, Func<MenuActionContext, ValueTask> onClicked, string? id = null)
            : base(text, onClicked, id)
        {
            FocusedColor = "#F87171";
        }

        public DangerButtonComponent(string text, Action<MenuActionContext> onClicked, string? id = null)
            : this(text, ctx => { onClicked(ctx); return ValueTask.CompletedTask; }, id)
        {
        }

        public override MenuNode Render(IMenuComponentRenderContext context)
        {
            var node = base.Render(context);
            var raw = new MenuRawNode("customhud.panorama", "danger");
            return node is MenuLineNode line
                ? line with { Children = [.. line.Children, raw] }
                : MenuLineNode.Of(node, raw);
        }
    }

    private sealed class PageState
    {
        public int? Focus { get; set; }
        public object? FocusKey { get; set; }
        public int RowsAt { get; set; }
        public List<IMenuComponent> Rows { get; } = [];
    }

    private readonly ISwiftlyCore _core;
    private readonly Services.ZoneStore _store;
    private readonly Services.DrawingService _drawing;
    private readonly PluginConfig _config;

    private readonly Dictionary<int, List<IMenu>> _ownedMenus = new();
    private readonly Dictionary<int, long> _lastToggleTick = new();
    private readonly HashSet<int> _persistentAdminViewers = new();
    private readonly Dictionary<int, string> _editingZoneId = new();
    private readonly Dictionary<int, float> _transformSteps = new();
    private readonly HashSet<int> _frozenSlots = new();

    public ZoneMenuManager(
        ISwiftlyCore core,
        Services.ZoneStore store,
        Services.DrawingService drawing,
        PluginConfig config)
    {
        _core = core;
        _store = store;
        _drawing = drawing;
        _config = config;
    }

    public IReadOnlySet<int> AdminViewers
    {
        get
        {
            var set = new HashSet<int>(_persistentAdminViewers);
            foreach (var s in _drawing.ActiveSlots) set.Add(s);
            foreach (var s in _editingZoneId.Keys) set.Add(s);
            foreach (var p in _core.PlayerManager.GetAllPlayers())
            {
                if (p != null && p.IsValid && !p.IsFakeClient && IsMenuOpen(p))
                    set.Add(p.Slot);
            }
            return set;
        }
    }

    public IReadOnlySet<string> HighlightedZones => _editingZoneId.Values.ToHashSet();

    public bool IsMenuOpen(int slot)
    {
        var p = _core.PlayerManager.GetPlayer(slot);
        return p != null && p.IsValid && IsMenuOpen(p);
    }

    private bool IsMenuOpen(IPlayer player)
    {
        return _core.Menu.GetSession(player) is { IsOpen: true, Menu.Tag: string tag } && tag == MenuTag;
    }

    public void ToggleMenu(IPlayer player)
    {
        if (player == null || !player.IsValid || player.IsFakeClient)
            return;

        long now = Environment.TickCount64;
        if (_lastToggleTick.TryGetValue(player.Slot, out long prev) && now - prev < 150)
            return;
        _lastToggleTick[player.Slot] = now;

        if (IsMenuOpen(player))
        {
            CloseMenu(player);
            return;
        }

        OpenMainMenu(player);
    }

    public void OpenMainMenu(IPlayer player)
    {
        if (player == null || !player.IsValid || player.IsFakeClient)
            return;

        CloseMenu(player);
        var main = BuildMainMenu(player.Slot);
        main.Open(player);
    }

    public void OpenZoneDetailMenu(IPlayer player, string zoneId)
    {
        if (player == null || !player.IsValid || player.IsFakeClient)
            return;

        var zone = _store.Get(zoneId);
        if (zone == null)
        {
            OpenMainMenu(player);
            return;
        }

        CloseMenu(player);
        var main = BuildMainMenu(player.Slot);
        var list = BuildZoneListMenu(player.Slot, main);
        list.Parent = main;
        var detail = BuildZoneDetailMenu(player.Slot, zone, list);
        detail.Parent = list;
        detail.Open(player);
    }

    public void CloseMenu(IPlayer player)
    {
        if (player == null || !player.IsValid)
            return;

        if (IsMenuOpen(player))
        {
            _core.Menu.CloseFor(player);
        }

        _editingZoneId.Remove(player.Slot);
        if (_frozenSlots.Remove(player.Slot))
        {
            SetPlayerFrozen(player, false);
        }
        DisposeSlotMenus(player.Slot);
    }

    public void CloseAllMenus()
    {
        foreach (var p in _core.PlayerManager.GetAllPlayers())
        {
            if (p != null && p.IsValid)
            {
                if (IsMenuOpen(p))
                {
                    try { _core.Menu.CloseFor(p); } catch { }
                }
                if (_frozenSlots.Remove(p.Slot))
                {
                    SetPlayerFrozen(p, false);
                }
            }
        }

        _frozenSlots.Clear();
        _editingZoneId.Clear();

        foreach (int slot in _ownedMenus.Keys.ToList())
        {
            DisposeSlotMenus(slot);
        }
        _ownedMenus.Clear();
    }

    public void OnPlayerDisconnected(int slot)
    {
        _lastToggleTick.Remove(slot);
        _persistentAdminViewers.Remove(slot);
        _editingZoneId.Remove(slot);
        _transformSteps.Remove(slot);
        _frozenSlots.Remove(slot);
        DisposeSlotMenus(slot);
    }

    public void SyncTickState()
    {
        foreach (int slot in _editingZoneId.Keys.ToList())
        {
            if (!IsMenuOpen(slot))
                _editingZoneId.Remove(slot);
        }

        if (_config.FreezeWhileMenuOpen)
        {
            foreach (var player in _core.PlayerManager.GetAllPlayers())
            {
                if (player == null || !player.IsValid || player.IsFakeClient)
                    continue;

                if (IsMenuOpen(player))
                {
                    SetPlayerFrozen(player, true);
                    _frozenSlots.Add(player.Slot);
                }
                else if (_frozenSlots.Remove(player.Slot))
                {
                    SetPlayerFrozen(player, false);
                }
            }
        }
        else if (_frozenSlots.Count > 0)
        {
            foreach (int slot in _frozenSlots.ToList())
            {
                var p = _core.PlayerManager.GetPlayer(slot);
                if (p != null && p.IsValid)
                    SetPlayerFrozen(p, false);
                _frozenSlots.Remove(slot);
            }
        }
    }

    private IMenu Track(int slot, IMenu menu)
    {
        menu.Tag = MenuTag;
        if (!_ownedMenus.TryGetValue(slot, out var list))
        {
            list = new List<IMenu>();
            _ownedMenus[slot] = list;
        }
        list.Add(menu);
        return menu;
    }

    private void DisposeSlotMenus(int slot)
    {
        if (!_ownedMenus.Remove(slot, out var list))
            return;

        foreach (var menu in list)
        {
            try { menu.Dispose(); } catch { }
        }
        list.Clear();
    }

    private void SetPlayerFrozen(IPlayer player, bool freeze)
    {
        try
        {
            var pawn = player.PlayerPawn;
            if (pawn == null || !pawn.IsValid) return;

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

    private float GetStep(int slot) => _transformSteps.TryGetValue(slot, out var s) ? s : 16f;

    private static string SafePreview(string? text, int maxLen = 28)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clean = HtmlTagRegex.Replace(text, " ");
        clean = clean.Replace("<", "").Replace(">", "").Replace("\r", " ").Replace("\n", " ").Trim();
        while (clean.Contains("  ", StringComparison.Ordinal))
            clean = clean.Replace("  ", " ", StringComparison.Ordinal);

        if (clean.Length == 0) return "(boş)";
        return clean.Length > maxLen ? clean[..maxLen] + "..." : clean;
    }

    private static void CloseTree(IMenuSession session)
    {
        var menu = session.Menu;
        var parent = menu.Parent;
        menu.Parent = null;
        session.Close();
        menu.Parent = parent;
    }

    private static void ReplaceRows(IMenu menu, PageState page, IReadOnlyList<IMenuComponent> rows)
    {
        foreach (var row in page.Rows)
            menu.Remove(row);
        page.Rows.Clear();

        for (int i = 0; i < rows.Count; i++)
        {
            menu.Insert(MenuRegion.Body, page.RowsAt + i, rows[i]);
            page.Rows.Add(rows[i]);
        }
    }

    private static void RestoreFocus(IMenuSession session, PageState page)
    {
        if (page.FocusKey is { } key && FocusWhere(session, c => c is MenuComponentBase { Tag: { } tag } && tag.Equals(key)))
            return;

        if (page.Focus is { } focus)
        {
            for (int i = focus; i > 0; i--)
            {
                if (session.FocusedIndex == i || session.SetFocus(i))
                    return;
            }
        }
    }

    private static bool FocusWhere(IMenuSession session, Func<IMenuComponent, bool> match)
    {
        int index = 0;
        foreach (var component in session.Menu.GetComponents(MenuRegion.Body))
        {
            if (!component.IsFocusable || !component.IsVisible(session))
                continue;

            if (match(component))
            {
                if (session.FocusedIndex != index)
                    session.SetFocus(index);
                return true;
            }

            index++;
        }

        return false;
    }

    // ============================== 1. ANA MENÜ ==============================

    private IMenu BuildMainMenu(int ownerSlot)
    {
        IMenu? mainMenu = null;

        mainMenu = Track(ownerSlot, _core.Menu.CreateMenu("bolgeler.main")
            .WithTitle("BÖLGE YÖNETİMİ")
            .AddHeader(new TextComponent
            {
                TextProvider = _ => $"Harita: {_store.CurrentMap} • Aktif: {_store.Zones.Count(z => z.Enabled)}/{_store.Zones.Count}"
            })
            .Add(new ButtonComponent("Yeni Bölge Çiz (Mouse İle)", ctx =>
            {
                var p = ctx.Player;
                CloseTree(ctx.Session);
                if (_frozenSlots.Remove(p.Slot))
                    SetPlayerFrozen(p, false);

                _core.Scheduler.NextTick(() =>
                {
                    if (!p.IsValid) return;
                    var defaultName = _store.NextFreeName();
                    _drawing.Start(p, defaultName, zone =>
                    {
                        _store.Add(zone);
                        OpenZoneDetailMenu(p, zone.Id);
                    }, () => OpenMainMenu(p));
                });
                return ValueTask.CompletedTask;
            }))
            .Add(new ButtonComponent("Konumuma Kutu Bölge Oluştur (256x256)", ctx =>
            {
                var p = ctx.Player;
                var pawn = p.PlayerPawn;
                if (pawn == null || !pawn.IsValid || !pawn.AbsOrigin.HasValue)
                {
                    p.SendChat($"{_config.ChatPrefix} [Red]Geçerli bir oyuncu konumunuz yok.[Default]");
                    return ValueTask.CompletedTask;
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

                var list = BuildZoneListMenu(ownerSlot, mainMenu);
                list.Parent = mainMenu;
                var detail = BuildZoneDetailMenu(ownerSlot, zone, list);
                detail.Parent = list;
                detail.Open(p);
                return ValueTask.CompletedTask;
            }))
            .Add(new SubmenuComponent("", ctx =>
            {
                var sub = BuildZoneListMenu(ownerSlot, mainMenu);
                sub.Parent = ctx.Menu;
                return sub;
            })
            {
                TextProvider = _ => $"Bölgeleri Yönet\t{_store.Zones.Count(z => z.Enabled)}/{_store.Zones.Count}"
            })
            .Add(new SubmenuComponent("Toplu Bölge İşlemleri", ctx =>
            {
                var sub = BuildBulkActionsMenu(ownerSlot);
                sub.Parent = ctx.Menu;
                return sub;
            }))
            .Add(new ToggleComponent("Kalıcı Sınır Görünümü", _persistentAdminViewers.Contains(ownerSlot), change =>
            {
                if (change.NewValue) _persistentAdminViewers.Add(change.Player.Slot);
                else _persistentAdminViewers.Remove(change.Player.Slot);
            })
            {
                DefaultValueProvider = _ => _persistentAdminViewers.Contains(ownerSlot)
            })
            .Add(new ToggleComponent("Menü Açıkken Hareketi Kilitle", _config.FreezeWhileMenuOpen, change =>
            {
                _config.FreezeWhileMenuOpen = change.NewValue;
                if (!change.NewValue)
                {
                    _frozenSlots.Remove(change.Player.Slot);
                    SetPlayerFrozen(change.Player, false);
                }
                else
                {
                    _frozenSlots.Add(change.Player.Slot);
                    SetPlayerFrozen(change.Player, true);
                }
            })
            {
                DefaultValueProvider = _ => _config.FreezeWhileMenuOpen
            })
            .Add(new ButtonComponent("Harita Bölgelerini Yenile", ctx =>
            {
                _store.LoadMap(_store.CurrentMap);
                ctx.Player.SendChat($"{_config.ChatPrefix} Harita bölgeleri yeniden yüklendi.");
                ctx.Session.Invalidate();
                return ValueTask.CompletedTask;
            }))
            .Build());

        mainMenu.Opened += session =>
        {
            _editingZoneId.Remove(session.Player.Slot);
        };

        return mainMenu;
    }

    // ============================== 2. TOPLU İŞLEMLER ==============================

    private IMenu BuildBulkActionsMenu(int ownerSlot)
    {
        var menu = _core.Menu.CreateMenu("bolgeler.bulk")
            .WithTitle("TOPLU BÖLGE İŞLEMLERİ")
            .AddHeader(new TextComponent
            {
                TextProvider = _ => $"Harita: {_store.CurrentMap} • {_store.Zones.Count} Bölge"
            })
            .Add(new ButtonComponent("Tüm Bölgeleri Aktif Et", ctx =>
            {
                foreach (var z in _store.Zones) z.Enabled = true;
                _store.Save();
                ctx.Player.SendChat($"{_config.ChatPrefix} Tüm bölgeler ({_store.Zones.Count}) [Lime]aktif edildi[Default].");
                return ValueTask.CompletedTask;
            }))
            .Add(new ButtonComponent("Tüm Bölgeleri Pasif Yap", ctx =>
            {
                foreach (var z in _store.Zones) z.Enabled = false;
                _store.Save();
                ctx.Player.SendChat($"{_config.ChatPrefix} Tüm bölgeler ({_store.Zones.Count}) [Yellow]pasif yapıldı[Default].");
                return ValueTask.CompletedTask;
            }))
            .Add(new ButtonComponent("Tüm Bölgelerin Sınırlarını Herkese Aç", ctx =>
            {
                foreach (var z in _store.Zones) z.ShowBorder = true;
                _store.Save();
                ctx.Player.SendChat($"{_config.ChatPrefix} Tüm bölgelerin sınır görünümü [Lime]herkese açıldı[Default].");
                return ValueTask.CompletedTask;
            }))
            .Add(new ButtonComponent("Tüm Bölgelerin Sınırlarını Gizle", ctx =>
            {
                foreach (var z in _store.Zones) z.ShowBorder = false;
                _store.Save();
                ctx.Player.SendChat($"{_config.ChatPrefix} Tüm bölgelerin sınır görünümü [Yellow]gizlendi[Default].");
                return ValueTask.CompletedTask;
            }))
            .Build();

        return Track(ownerSlot, menu);
    }

    // ============================== 3. BÖLGE LİSTESİ ==============================

    private IMenu BuildZoneListMenu(int ownerSlot, IMenu? mainMenu)
    {
        var page = new PageState { RowsAt = 0 };
        IMenu? listMenu = null;

        var builder = _core.Menu.CreateMenu("bolgeler.list")
            .WithTitle("KAYITLI BÖLGELER")
            .AddHeader(new TextComponent
            {
                TextProvider = _ => $"{_store.Zones.Count} bölge listeleniyor • Harita: {_store.CurrentMap}"
            });

        listMenu = Track(ownerSlot, builder.Build());

        void Populate()
        {
            var rows = new List<IMenuComponent>();
            foreach (var zone in _store.Zones.ToList())
            {
                var z = zone;
                rows.Add(new SubmenuComponent("", ctx =>
                {
                    var detail = BuildZoneDetailMenu(ownerSlot, z, listMenu);
                    detail.Parent = ctx.Menu;
                    return detail;
                })
                {
                    Tag = z.Id,
                    TextProvider = _ =>
                    {
                        string status = z.Enabled ? (z.BlockEntry ? "ENGEL" : "AKTİF") : "PASİF";
                        return $"{SafePreview(z.Name, 22)}\t{status}";
                    }
                });
            }

            if (rows.Count == 0)
            {
                rows.Add(new TextComponent("Kayıtlı bölge bulunmuyor."));
            }

            ReplaceRows(listMenu, page, rows);
        }

        listMenu.Opened += session =>
        {
            _editingZoneId.Remove(session.Player.Slot);
            Populate();
            RestoreFocus(session, page);
        };

        listMenu.FocusChanged += session =>
        {
            page.Focus = session.FocusedIndex;
            page.FocusKey = (session.FocusedComponent as MenuComponentBase)?.Tag;
        };

        return listMenu;
    }

    // ============================== 4. BÖLGE DETAYI ==============================

    private IMenu BuildZoneDetailMenu(int ownerSlot, ZoneData zone, IMenu? listMenu)
    {
        IMenu? detailMenu = null;
        bool syncingName = false;
        InputComponent? nameInput = null;

        nameInput = new InputComponent(
            "Bölge İsmi",
            defaultValue: SafePreview(zone.Name, 22),
            validator: v => !string.IsNullOrWhiteSpace(v),
            onChanged: change =>
            {
                if (syncingName) return;
                syncingName = true;
                zone.Name = change.NewValue.Trim();
                _store.Save();
                nameInput?.SetValue(change.Session, SafePreview(zone.Name, 22));
                syncingName = false;
                change.Session.Invalidate();
                change.Player.SendChat($"{_config.ChatPrefix} Bölge adı [Lime]'{zone.Name}'[Default] olarak güncellendi.");
            })
        {
            MaxLength = 48,
            EmptyText = "İsim",
            InvalidText = "Bölge adı boş olamaz",
            DefaultValueProvider = _ => SafePreview(zone.Name, 22),
            HintMessage = "[gold]Bölgenin yeni ismini sohbete yazın[default]"
        };

        int FindColorIdx(string hex)
        {
            int idx = Array.FindIndex(ColorPresets, c => c.Hex.Equals(hex, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 ? idx : 0;
        }

        detailMenu = Track(ownerSlot, _core.Menu.CreateMenu("bolgeler.detail")
            .WithTitle(SafePreview($"BÖLGE: {zone.Name}", 30))
            .AddHeader(new TextComponent
            {
                TextProvider = _ => $"{SafePreview(zone.Name, 20)} • Boyut: {zone.Box.Size.X:F0}x{zone.Box.Size.Y:F0}x{zone.Box.Size.Z:F0}"
            })
            .Add(nameInput)
            .Add(new ToggleComponent("Aktif", zone.Enabled, change =>
            {
                zone.Enabled = change.NewValue;
                _store.Save();
                change.Session.Invalidate();
            })
            {
                DefaultValueProvider = _ => zone.Enabled
            })
            .Add(new SubmenuComponent("Hazır Bölge Şablonu Uygula\t5 Şablon", ctx =>
            {
                var sub = BuildZonePresetsMenu(ownerSlot, zone);
                sub.Parent = ctx.Menu;
                return sub;
            }))
            .Add(new ToggleComponent("Ölümsüzlük (GodMode)", zone.GodMode, change =>
            {
                zone.GodMode = change.NewValue;
                _store.Save();
            })
            {
                DefaultValueProvider = _ => zone.GodMode
            })
            .Add(new ToggleComponent("Dışarıya Hasar Kapalı", zone.NoDamageOut, change =>
            {
                zone.NoDamageOut = change.NewValue;
                _store.Save();
            })
            {
                DefaultValueProvider = _ => zone.NoDamageOut
            })
            .Add(new ToggleComponent("Dışarıdan Hasar Kapalı", zone.NoDamageIn, change =>
            {
                zone.NoDamageIn = change.NewValue;
                _store.Save();
            })
            {
                DefaultValueProvider = _ => zone.NoDamageIn
            })
            .Add(new ToggleComponent("Tüm Saldırı Hasarı Kapalı", zone.NoDamageDealt, change =>
            {
                zone.NoDamageDealt = change.NewValue;
                _store.Save();
            })
            {
                DefaultValueProvider = _ => zone.NoDamageDealt
            })
            .Add(new ToggleComponent("Can Yenileme", zone.Heal, change =>
            {
                zone.Heal = change.NewValue;
                _store.Save();
                change.Session.Invalidate();
            })
            {
                DefaultValueProvider = _ => zone.Heal
            })
            .Add(new SliderComponent(
                "Can/sn",
                1f,
                100f,
                Math.Clamp(zone.HealPerSecond, 1f, 100f),
                5f,
                change =>
                {
                    zone.HealPerSecond = MathF.Round(change.NewValue);
                    _store.Save();
                })
            {
                TotalBars = 10,
                ValueFormat = "0 hp/s",
                VisibleWhen = _ => zone.Heal,
                DefaultValueProvider = _ => Math.Clamp(zone.HealPerSecond, 1f, 100f)
            })
            .Add(new SliderComponent(
                "Max Can",
                50f,
                500f,
                Math.Clamp(zone.HealMax, 50, 500),
                10f,
                change =>
                {
                    zone.HealMax = (int)MathF.Round(change.NewValue);
                    _store.Save();
                })
            {
                TotalBars = 10,
                ValueFormat = "0 hp",
                VisibleWhen = _ => zone.Heal,
                DefaultValueProvider = _ => Math.Clamp(zone.HealMax, 50, 500)
            })
            .Add(new ToggleComponent("Zırh Doldurma", zone.Armor, change =>
            {
                zone.Armor = change.NewValue;
                _store.Save();
                change.Session.Invalidate();
            })
            {
                DefaultValueProvider = _ => zone.Armor
            })
            .Add(new SliderComponent(
                "Zırh/sn",
                1f,
                100f,
                Math.Clamp(zone.ArmorPerSecond, 1f, 100f),
                5f,
                change =>
                {
                    zone.ArmorPerSecond = MathF.Round(change.NewValue);
                    _store.Save();
                })
            {
                TotalBars = 10,
                ValueFormat = "0 ap/s",
                VisibleWhen = _ => zone.Armor,
                DefaultValueProvider = _ => Math.Clamp(zone.ArmorPerSecond, 1f, 100f)
            })
            .Add(new SliderComponent(
                "Max Zırh",
                50f,
                500f,
                Math.Clamp(zone.ArmorMax, 50, 500),
                10f,
                change =>
                {
                    zone.ArmorMax = (int)MathF.Round(change.NewValue);
                    _store.Save();
                })
            {
                TotalBars = 10,
                ValueFormat = "0 ap",
                VisibleWhen = _ => zone.Armor,
                DefaultValueProvider = _ => Math.Clamp(zone.ArmorMax, 50, 500)
            })
            .Add(new ToggleComponent("Düşme Hasarını Kapat", zone.NoFallDamage, change =>
            {
                zone.NoFallDamage = change.NewValue;
                _store.Save();
            })
            {
                DefaultValueProvider = _ => zone.NoFallDamage
            })
            .Add(new ToggleComponent("Özel Yerçekimi", zone.Gravity, change =>
            {
                zone.Gravity = change.NewValue;
                _store.Save();
                change.Session.Invalidate();
            })
            {
                DefaultValueProvider = _ => zone.Gravity
            })
            .Add(new SliderComponent(
                "Yerçekimi Çarpanı",
                0.1f,
                2.5f,
                Math.Clamp(zone.GravityScale, 0.1f, 2.5f),
                0.1f,
                change =>
                {
                    zone.GravityScale = MathF.Round(change.NewValue * 10f) / 10f;
                    _store.Save();
                })
            {
                TotalBars = 10,
                ValueFormat = "0.0x",
                VisibleWhen = _ => zone.Gravity,
                DefaultValueProvider = _ => Math.Clamp(zone.GravityScale, 0.1f, 2.5f)
            })
            .Add(new ToggleComponent("Giriş Engeli (Pushback)", zone.BlockEntry, change =>
            {
                zone.BlockEntry = change.NewValue;
                _store.Save();
                change.Session.Invalidate();
            })
            {
                DefaultValueProvider = _ => zone.BlockEntry
            })
            .Add(new SliderComponent(
                "Min Oyuncu Şartı (0 = Daima)",
                0f,
                64f,
                Math.Clamp(zone.BlockBelowPlayers, 0, 64),
                1f,
                change =>
                {
                    zone.BlockBelowPlayers = (int)MathF.Round(change.NewValue);
                    _store.Save();
                })
            {
                TotalBars = 10,
                ValueFormat = "0",
                VisibleWhen = _ => zone.BlockEntry,
                DefaultValueProvider = _ => Math.Clamp(zone.BlockBelowPlayers, 0, 64)
            })
            .Add(new SelectorComponent<ZoneTeam>(
                "Takım",
                [ZoneTeam.Herkes, ZoneTeam.T, ZoneTeam.CT],
                Math.Clamp((int)zone.Team, 0, 2),
                change =>
                {
                    zone.Team = change.NewValue;
                    _store.Save();
                })
            {
                DefaultValueProvider = _ => Math.Clamp((int)zone.Team, 0, 2),
                Formatter = t => t switch
                {
                    ZoneTeam.T => "T Takımı",
                    ZoneTeam.CT => "CT Takımı",
                    _ => "Herkes"
                }
            })
            .Add(new SwatchColorSelector(
                "Çizgi Rengi",
                ColorPresets,
                FindColorIdx(zone.Color),
                change =>
                {
                    zone.Color = change.NewValue.Hex;
                    _store.Save();
                })
            {
                DefaultValueProvider = _ => FindColorIdx(zone.Color)
            })
            .Add(new ToggleComponent("Sınırları Herkese Göster", zone.ShowBorder, change =>
            {
                zone.ShowBorder = change.NewValue;
                _store.Save();
            })
            {
                DefaultValueProvider = _ => zone.ShowBorder
            })
            .Add(new ToggleComponent("Giriş/Çıkış Mesajları", zone.Messages, change =>
            {
                zone.Messages = change.NewValue;
                _store.Save();
            })
            {
                DefaultValueProvider = _ => zone.Messages
            })
            .Add(new SubmenuComponent("Konum & Boyut Düzenle", ctx =>
            {
                var sub = BuildTransformMenu(ownerSlot, zone);
                sub.Parent = ctx.Menu;
                return sub;
            }))
            .Add(new ButtonComponent("Köşeleri Yeniden Çiz (Mouse İle)", ctx =>
            {
                var p = ctx.Player;
                CloseTree(ctx.Session);
                if (_frozenSlots.Remove(p.Slot))
                    SetPlayerFrozen(p, false);

                _core.Scheduler.NextTick(() =>
                {
                    if (!p.IsValid) return;
                    _drawing.Start(p, zone.Name, updated =>
                    {
                        _store.Save();
                        OpenZoneDetailMenu(p, updated.Id);
                    }, () => OpenZoneDetailMenu(p, zone.Id), existingZone: zone);
                });
                return ValueTask.CompletedTask;
            }))
            .Add(new ButtonComponent("Bölgeye Işınlan", ctx =>
            {
                var c = zone.Box.Center;
                ctx.Player.Teleport(new Vector(c.X, c.Y, zone.Box.Mins.Z + 10f), null, new Vector(0, 0, 0));
                ctx.Player.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] merkezine ışınlandınız.");
                return ValueTask.CompletedTask;
            }))
            .Add(new ButtonComponent("Bölgeyi Konumuma Taşı", ctx =>
            {
                var p = ctx.Player;
                var pawn = p.PlayerPawn;
                if (pawn == null || !pawn.IsValid || !pawn.AbsOrigin.HasValue)
                    return ValueTask.CompletedTask;

                var pos = pawn.AbsOrigin.Value;
                var size = zone.Box.Size;
                zone.Box = new ZoneBox(
                    new Vector(pos.X - size.X * 0.5f, pos.Y - size.Y * 0.5f, pos.Z),
                    new Vector(pos.X + size.X * 0.5f, pos.Y + size.Y * 0.5f, pos.Z + size.Z));
                _store.Save();
                ctx.Session.Invalidate();
                p.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesi mevcut konumunuza taşındı.");
                return ValueTask.CompletedTask;
            }))
            .Add(new ButtonComponent("Bölgeyi Kopyala (Çoğalt)", ctx =>
            {
                var p = ctx.Player;
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

                var copyDetail = BuildZoneDetailMenu(ownerSlot, copy, listMenu);
                copyDetail.Parent = listMenu ?? ctx.Menu;
                copyDetail.Open(p);
                return ValueTask.CompletedTask;
            }))
            .Add(new DangerSubmenuComponent("Bölgeyi Sil", ctx =>
            {
                var confirm = BuildConfirmDeleteMenu(ownerSlot, zone, listMenu);
                confirm.Parent = ctx.Menu;
                return confirm;
            }))
            .Build());

        detailMenu.Opened += session =>
        {
            _editingZoneId[session.Player.Slot] = zone.Id;
        };

        return detailMenu;
    }

    private IMenu BuildConfirmDeleteMenu(int ownerSlot, ZoneData zone, IMenu? listMenu)
    {
        IMenu? confirmMenu = null;

        confirmMenu = Track(ownerSlot, _core.Menu.CreateMenu("bolgeler.confirm_delete")
            .WithTitle(SafePreview($"SİL: {zone.Name}", 30))
            .AddHeader(new TextComponent($"'{SafePreview(zone.Name, 24)}' bölgesi kalıcı olarak silinecek."))
            .Add(new DangerButtonComponent("Evet, Kalıcı Olarak Sil", ctx =>
            {
                _store.Remove(zone.Id);
                _editingZoneId.Remove(ctx.Player.Slot);
                ctx.Player.SendChat($"{_config.ChatPrefix} [Red]'{zone.Name}' silindi.[Default]");
                if (confirmMenu != null && listMenu != null)
                {
                    confirmMenu.Parent = listMenu;
                }
                ctx.Session.Close();
            }))
            .Add(new ButtonComponent("İptal", ctx =>
            {
                ctx.Session.Close();
                return ValueTask.CompletedTask;
            }))
            .Build());

        return confirmMenu;
    }

    // ============================== 5. HAZIR ŞABLONLAR ==============================

    private IMenu BuildZonePresetsMenu(int ownerSlot, ZoneData zone)
    {
        var menu = _core.Menu.CreateMenu("bolgeler.presets")
            .WithTitle(SafePreview($"ŞABLON SEÇ: {zone.Name}", 32))
            .AddHeader(new TextComponent("Tek tıkla hazır bölge yapılandırması uygula"))
            .Add(new ButtonComponent("Güvenli Bölge (SafeZone + Şifa)", ctx =>
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
                ctx.Player.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesine [Lime]Güvenli Bölge (SafeZone)[Default] şablonu uygulandı!");
                ctx.Session.Close();
                return ValueTask.CompletedTask;
            }))
            .Add(new DangerButtonComponent("Yasaklı Bölge / Rush Engeli (Pushback)", ctx =>
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
                ctx.Player.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesine [Red]Yasaklı Bölge (Pushback)[Default] şablonu uygulandı!");
                ctx.Session.Close();
            }))
            .Add(new ButtonComponent("Arena / Düello Bölgesi (Dış Müdahale Kapalı)", ctx =>
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
                ctx.Player.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesine [Yellow]Arena / Düello[Default] şablonu uygulandı!");
                ctx.Session.Close();
                return ValueTask.CompletedTask;
            }))
            .Add(new ButtonComponent("Ay Yerçekimi & Surf Bölgesi (0.4x Gravity)", ctx =>
            {
                zone.Gravity = true;
                zone.GravityScale = 0.4f;
                zone.NoFallDamage = true;
                zone.Color = "#BF00FF";
                _store.Save();
                ctx.Player.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesine [Purple]Ay Yerçekimi (0.4x)[Default] şablonu uygulandı!");
                ctx.Session.Close();
                return ValueTask.CompletedTask;
            }))
            .Add(new ButtonComponent("Hızlı Revir İstasyonu (25 HP/s + 25 AP/s)", ctx =>
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
                ctx.Player.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesine [Lime]Revir İstasyonu[Default] şablonu uygulandı!");
                ctx.Session.Close();
                return ValueTask.CompletedTask;
            }))
            .Build();

        menu.Opened += session =>
        {
            _editingZoneId[session.Player.Slot] = zone.Id;
        };

        return Track(ownerSlot, menu);
    }

    // ============================== 6. KONUM & BOYUT ==============================

    private IMenu BuildTransformMenu(int ownerSlot, ZoneData zone)
    {
        float initialStep = GetStep(ownerSlot);
        int stepIdx = Math.Max(0, Array.IndexOf(StepSizes, initialStep));

        ButtonComponent MakeNudge(Func<float, string> label, Action<float> apply)
        {
            return new ButtonComponent("", ctx =>
            {
                apply(GetStep(ctx.Player.Slot));
                _store.Save();
                ctx.Session.Invalidate();
                return ValueTask.CompletedTask;
            })
            {
                TextProvider = _ => label(GetStep(ownerSlot))
            };
        }

        var menu = _core.Menu.CreateMenu("bolgeler.transform")
            .WithTitle(SafePreview($"{zone.Name} - KONUM & BOYUT", 32))
            .AddHeader(new TextComponent
            {
                TextProvider = _ => $"Adım: {GetStep(ownerSlot):0} px • Boyut: {zone.Box.Size.X:0}x{zone.Box.Size.Y:0}x{zone.Box.Size.Z:0}"
            })
            .Add(new SelectorComponent<float>(
                "Hassasiyet (Adım)",
                StepSizes,
                stepIdx,
                change =>
                {
                    _transformSteps[change.Player.Slot] = change.NewValue;
                    change.Session.Invalidate();
                })
            {
                DefaultValueProvider = _ => Math.Max(0, Array.IndexOf(StepSizes, GetStep(ownerSlot))),
                Formatter = s => s switch
                {
                    4f => "4 px (Hassas)",
                    16f => "16 px (Normal)",
                    32f => "32 px (Orta)",
                    64f => "64 px (Geniş)",
                    128f => "128 px (Hızlı)",
                    _ => $"{s:0} px"
                }
            })
            .Add(MakeNudge(s => $"Kaydır +X (+{s:0} px)", s => zone.Box = zone.Box.Offset(s, 0, 0)))
            .Add(MakeNudge(s => $"Kaydır -X (-{s:0} px)", s => zone.Box = zone.Box.Offset(-s, 0, 0)))
            .Add(MakeNudge(s => $"Kaydır +Y (+{s:0} px)", s => zone.Box = zone.Box.Offset(0, s, 0)))
            .Add(MakeNudge(s => $"Kaydır -Y (-{s:0} px)", s => zone.Box = zone.Box.Offset(0, -s, 0)))
            .Add(MakeNudge(s => $"Kaydır +Z Yukarı (+{s:0} px)", s => zone.Box = zone.Box.Offset(0, 0, s)))
            .Add(MakeNudge(s => $"Kaydır -Z Aşağı (-{s:0} px)", s => zone.Box = zone.Box.Offset(0, 0, -s)))
            .Add(MakeNudge(s => $"Genişlik +X (+{s:0} px)\t{zone.Box.Size.X:0}", s => zone.Box = zone.Box.WithSize(Math.Clamp(zone.Box.Size.X + s, 16f, 8192f), zone.Box.Size.Y, zone.Box.Size.Z)))
            .Add(MakeNudge(s => $"Genişlik -X (-{s:0} px)\t{zone.Box.Size.X:0}", s => zone.Box = zone.Box.WithSize(Math.Clamp(zone.Box.Size.X - s, 16f, 8192f), zone.Box.Size.Y, zone.Box.Size.Z)))
            .Add(MakeNudge(s => $"Uzunluk +Y (+{s:0} px)\t{zone.Box.Size.Y:0}", s => zone.Box = zone.Box.WithSize(zone.Box.Size.X, Math.Clamp(zone.Box.Size.Y + s, 16f, 8192f), zone.Box.Size.Z)))
            .Add(MakeNudge(s => $"Uzunluk -Y (-{s:0} px)\t{zone.Box.Size.Y:0}", s => zone.Box = zone.Box.WithSize(zone.Box.Size.X, Math.Clamp(zone.Box.Size.Y - s, 16f, 8192f), zone.Box.Size.Z)))
            .Add(MakeNudge(s => $"Yükseklik +Z (+{s:0} px)\t{zone.Box.Size.Z:0}", s => zone.Box = zone.Box.WithSize(zone.Box.Size.X, zone.Box.Size.Y, Math.Clamp(zone.Box.Size.Z + s, 16f, 8192f))))
            .Add(MakeNudge(s => $"Yükseklik -Z (-{s:0} px)\t{zone.Box.Size.Z:0}", s => zone.Box = zone.Box.WithSize(zone.Box.Size.X, zone.Box.Size.Y, Math.Clamp(zone.Box.Size.Z - s, 16f, 8192f))))
            .Add(new ButtonComponent("Bölgeyi Konumuma Hizala", ctx =>
            {
                var pawn = ctx.Player.PlayerPawn;
                if (pawn == null || !pawn.IsValid || !pawn.AbsOrigin.HasValue)
                    return ValueTask.CompletedTask;

                var pos = pawn.AbsOrigin.Value;
                var sz = zone.Box.Size;
                zone.Box = new ZoneBox(
                    new Vector(pos.X - sz.X * 0.5f, pos.Y - sz.Y * 0.5f, pos.Z),
                    new Vector(pos.X + sz.X * 0.5f, pos.Y + sz.Y * 0.5f, pos.Z + sz.Z));
                _store.Save();
                ctx.Session.Invalidate();
                ctx.Player.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}'[Default] bölgesi mevcut konumunuza hizalandı.");
                return ValueTask.CompletedTask;
            }))
            .Build();

        menu.Opened += session =>
        {
            _editingZoneId[session.Player.Slot] = zone.Id;
        };

        return Track(ownerSlot, menu);
    }
}
