using Microsoft.Extensions.Logging;
using MenuApi.API;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using Bolgeler.Config;
using Bolgeler.Hud;
using Bolgeler.Menu;
using Bolgeler.Services;

namespace Bolgeler;

[PluginMetadata(
    Id = "Bolgeler",
    Version = "1.1.0",
    Name = "Bölgeler (CS2 Zones)",
    Author = "surfevo",
    Description = "CS2 Bölgeler Eklentisi: Mouse ile alan çizimi, harita başı kayıt, GodMode, can/zırh yenileme, hasar engelleme, Custom Panorama HUD ve CenterHTML desteği."
)]
public class BolgelerPlugin(ISwiftlyCore core) : BasePlugin(core)
{
    private PluginConfig _config = new();
    private ZoneStore? _store;
    private AimTracer? _tracer;
    private ZoneRenderer? _renderer;
    private PushService? _pushService;
    private ChatInputService? _chatInput;
    private DrawingService? _drawing;
    private HudService? _hud;
    private ZoneEffectService? _effects;
    private ZoneMenuManager? _menu;
    private IMenuApi? _menuApi;

    private readonly List<Guid> _commandGuids = new();
    private readonly Dictionary<int, float> _lastCmdTime = new();
    private Guid _chatCommandHookGuid;
    private Guid _roundStartHookGuid;
    private int _tickCounter;
    private bool _mapReady;

    public override void Load(bool hotReload)
    {
        try
        {
            _config = ZoneStore.LoadConfig(Core);

            _store = new ZoneStore(Core);
            _tracer = new AimTracer(Core);
            _renderer = new ZoneRenderer(Core) { BeamWidth = _config.BeamWidth };
            _pushService = new PushService(Core);
            _chatInput = new ChatInputService(Core);
            _drawing = new DrawingService(Core, _tracer, _renderer, _config);
            _hud = new HudService(Core, _config);
            _effects = new ZoneEffectService(Core, _store, _pushService, _config);
            _menu = new ZoneMenuManager(Core, _store, _renderer, _drawing, _chatInput, _hud, _config)
            {
                MenuApi = _menuApi
            };

            _pushService.Load();
            _chatInput.Load();
            _effects.Load();

            // Komutlari kaydet
            RegisterCommands();
            _chatCommandHookGuid = Core.Command.HookClientChat(OnChatFallback);

            // Eventleri bagla
            Core.Event.OnTick += OnTick;
            Core.Event.OnMapLoad += OnMapLoad;
            Core.Event.OnClientKeyStateChanged += OnClientKeyStateChanged;
            Core.Event.OnCustomHudClicked += OnCustomHudClicked;
            Core.Event.OnClientDisconnected += OnClientDisconnected;
            _roundStartHookGuid = Core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);

            // Sunucu ilk acilirken (cold boot) GlobalVars.MapName henuz hazir olmayabilir ve dogrudan erisimde
            // istisna firlatip eklentinin otomatik yuklenmesini engelleyebilir. Bu yuzden guvenli sekilde kontrol et.
            var currentMap = TryGetCurrentMap();
            if (!string.IsNullOrWhiteSpace(currentMap) && currentMap != "unknown")
            {
                Core.Scheduler.NextTick(() =>
                {
                    _store?.LoadMap(currentMap);
                    _renderer?.Forget(0.5f);
                    _mapReady = true;
                });
            }
            else if (hotReload)
            {
                Core.Scheduler.NextTick(() =>
                {
                    var retryMap = TryGetCurrentMap();
                    if (!string.IsNullOrWhiteSpace(retryMap) && retryMap != "unknown")
                    {
                        _store?.LoadMap(retryMap);
                        _renderer?.Forget(0.5f);
                        _mapReady = true;
                    }
                });
            }

            Core.Logger.LogInformation($"[Bolgeler] Eklenti yüklendi. Komutlar: !{_config.Command}, sw_{_config.Command}, {_config.Command}");
        }
        catch (Exception ex)
        {
            Core.Logger.LogError($"[Bolgeler] Yukleme sirasinda hata: {ex}");
        }
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        if (interfaceManager.HasSharedInterface("MenuApi"))
        {
            try
            {
                _menuApi = interfaceManager.GetSharedInterface<IMenuApi>("MenuApi");
                if (_menu != null)
                {
                    _menu.MenuApi = _menuApi;
                }
                Core.Logger.LogInformation("[Bolgeler] SwiftlyS2-MenuApi arayüzü bağlandı.");
            }
            catch (Exception ex)
            {
                Core.Logger.LogError(ex, "[Bolgeler] SwiftlyS2-MenuApi bağlanırken hata oluştu.");
            }
        }
        else
        {
            _menuApi = null;
            if (_menu != null) _menu.MenuApi = null;
        }
    }

    public override void Unload()
    {
        Core.Event.OnTick -= OnTick;
        Core.Event.OnMapLoad -= OnMapLoad;
        Core.Event.OnClientKeyStateChanged -= OnClientKeyStateChanged;
        Core.Event.OnCustomHudClicked -= OnCustomHudClicked;
        Core.Event.OnClientDisconnected -= OnClientDisconnected;

        if (_roundStartHookGuid != Guid.Empty)
        {
            try { Core.GameEvent.Unhook(_roundStartHookGuid); } catch { }
            _roundStartHookGuid = Guid.Empty;
        }

        foreach (var guid in _commandGuids)
        {
            try { Core.Command.UnregisterCommand(guid); } catch { }
        }
        _commandGuids.Clear();

        if (_chatCommandHookGuid != Guid.Empty)
        {
            try { Core.Command.UnhookClientChat(_chatCommandHookGuid); } catch { }
            _chatCommandHookGuid = Guid.Empty;
        }

        _menu?.CloseAllMenus();
        _drawing?.CancelAll();
        _effects?.Unload();
        _chatInput?.Unload();
        _pushService?.Unload();
        _renderer?.DestroyAll();
        _hud?.Custom.Destroy();
        _mapReady = false;

        Core.Logger.LogInformation("[Bolgeler] Eklenti kapatıldı.");
    }

    private string? TryGetCurrentMap()
    {
        try
        {
            var raw = Core.Engine.GlobalVars.MapName;
            return string.IsNullOrWhiteSpace(raw) ? null : ZoneStore.Sanitize(raw);
        }
        catch
        {
            // Sunucu acilisinda harita henuz yuklenmemisse GlobalVars firlatir
            return null;
        }
    }

    private void RegisterCommands()
    {
        var baseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            _config.Command,
            "bolgeler",
            "bolge",
            "zones",
            "zone"
        };
        if (_config.Aliases != null)
        {
            foreach (var a in _config.Aliases) baseNames.Add(a);
        }

        foreach (var name in baseNames)
        {
            // 1. Standart SwiftlyS2 komutu (Konsolda 'sw_' öneki alır: sw_bolgeler, sw_bolge)
            var g1 = Core.Command.RegisterCommand(name, OnCommand, registerRaw: false, helpText: "Bölgeler menüsünü açar.");
            _commandGuids.Add(g1);

            // 2. Raw komut (Konsolda doğrudan 'bolgeler', 'bolge' ve sohbette '!bolgeler', '/bolgeler')
            var g2 = Core.Command.RegisterCommand(name, OnCommand, registerRaw: true, helpText: "Bölgeler menüsünü açar.");
            _commandGuids.Add(g2);
        }
    }

    private void OnCommand(ICommandContext ctx)
    {
        var player = ctx.Sender;
        if (player == null || !player.IsValid)
        {
            ctx.Reply($"[Bolgeler] Eklenti aktif! Aktif harita: {_store?.CurrentMap}, Kayıtlı bölge sayısı: {_store?.Zones.Count}. Bölge menüsü oyun içinden '!bolgeler' veya 'sw_bolgeler' yazılarak açılır.");
            return;
        }

        ExecutePlayerCommand(player, ctx.Args);
    }

    private HookResult OnChatFallback(int playerId, string text, bool teamonly)
    {
        if (_chatInput != null && _chatInput.TryConsumeChat(playerId, text))
        {
            return HookResult.Stop;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2)
        {
            trimmed = trimmed[1..^1].Trim();
        }

        if (trimmed.Length < 2 || (trimmed[0] != '!' && trimmed[0] != '/'))
        {
            return HookResult.Continue;
        }

        var parts = trimmed[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return HookResult.Continue;

        var cmd = parts[0];
        bool match = cmd.Equals(_config.Command, StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("bolgeler", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("bolge", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("zones", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("zone", StringComparison.OrdinalIgnoreCase)
            || (_config.Aliases != null && _config.Aliases.Any(a => a.Equals(cmd, StringComparison.OrdinalIgnoreCase)));

        if (!match) return HookResult.Continue;

        var player = Core.PlayerManager.GetPlayer(playerId);
        if (player == null || !player.IsValid) return HookResult.Continue;

        var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();
        ExecutePlayerCommand(player, args);
        return trimmed[0] == '/' ? HookResult.Stop : HookResult.Continue;
    }

    private void ExecutePlayerCommand(IPlayer player, string[] args)
    {
        var now = ZoneStore.SafeCurrentTime(Core);
        if (_lastCmdTime.TryGetValue(player.Slot, out var lastTime) && MathF.Abs(now - lastTime) < 0.2f)
        {
            return;
        }
        _lastCmdTime[player.Slot] = now;

        // Eger harita henuz yuklenmediyse simdi yuklemeyi dene
        if (_store != null && (_store.CurrentMap == "unknown" || string.IsNullOrWhiteSpace(_store.CurrentMap)))
        {
            var map = TryGetCurrentMap();
            if (!string.IsNullOrWhiteSpace(map) && map != "unknown")
            {
                _store.LoadMap(map);
                _mapReady = true;
            }
        }

        if (!HasPermission(player))
        {
            player.SendChat($"{_config.ChatPrefix} [Red]Bu komutu kullanmak için yetkiniz yok.[Default]");
            return;
        }

        if (args.Length > 0)
        {
            var sub = args[0].ToLowerInvariant();
            if (sub == "hud")
            {
                var cur = _hud!.GetMode(player.Slot);
                var next = cur == HudMode.Custom ? HudMode.CenterHtml : HudMode.Custom;
                _hud.SetMode(player, next);
                player.SendChat($"{_config.ChatPrefix} HUD Modunuz: [Lime]{(next == HudMode.Custom ? "Panorama HUD" : "CenterHTML")}[Default]");
                return;
            }
            if (sub == "ciz" || sub == "ekle" || sub == "olustur" || sub == "draw")
            {
                _menu!.CloseMenu(player);
                var name = args.Length > 1 ? string.Join(" ", args[1..]) : _store!.NextFreeName();
                _drawing!.Start(player, name, zone =>
                {
                    _store!.Add(zone);
                    _menu.OpenZoneDetailMenu(player, zone.Id);
                }, () => _menu.OpenMainMenu(player));
                return;
            }
            if (sub == "liste" || sub == "list")
            {
                if (_store!.Zones.Count == 0)
                {
                    player.SendChat($"{_config.ChatPrefix} Bu haritada henüz kayıtlı bölge yok. [Lime]!bolgeler ciz[Default] veya menüden oluşturabilirsiniz.");
                }
                else
                {
                    player.SendChat($"{_config.ChatPrefix} Kayıtlı Bölgeler ({_store.Zones.Count}): " +
                        string.Join(", ", _store.Zones.Select(z => $"[Lime]{z.Name}[Default]")));
                }
                return;
            }
            if ((sub == "sil" || sub == "delete") && args.Length > 1)
            {
                var targetName = string.Join(" ", args[1..]);
                var zone = _store!.Zones.FirstOrDefault(z => z.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                if (zone == null)
                {
                    player.SendChat($"{_config.ChatPrefix} [Red]'{targetName}' adında bir bölge bulunamadı.[Default]");
                }
                else
                {
                    _store.Remove(zone.Id);
                    player.SendChat($"{_config.ChatPrefix} [Red]'{zone.Name}' silindi.[Default]");
                }
                return;
            }
        }

        if (_drawing != null && _drawing.IsDrawing(player.Slot))
        {
            _drawing.Cancel(player.Slot);
            return;
        }

        if (_menu!.IsMenuOpen(player.Slot))
        {
            _menu.CloseMenu(player);
            player.SendChat($"{_config.ChatPrefix} Bölge menüsü kapatıldı.");
        }
        else
        {
            _menu.OpenMainMenu(player);
        }
    }

    private bool HasPermission(IPlayer player)
    {
        if (!_config.RequirePermission) return true;
        if (_config.Permissions == null || _config.Permissions.Count == 0) return true;
        foreach (var node in _config.Permissions)
        {
            if (string.IsNullOrWhiteSpace(node) || Core.Permission.PlayerHasPermission(player.SteamID, node))
            {
                return true;
            }
        }
        return false;
    }

    private void OnMapLoad(IOnMapLoadEvent @event)
    {
        _mapReady = false;
        _menu?.CloseAllMenus();
        _drawing?.CancelAll();
        _renderer?.Forget(1.0f);
        _effects?.OnRoundStart();
        _hud?.OnMapChanged();

        var mapName = @event.MapName;
        Core.Scheduler.NextTick(() =>
        {
            _store?.LoadMap(mapName);
            _renderer?.Forget(1.0f);
            _mapReady = true;
        });
    }

    private HookResult OnRoundStart(EventRoundStart @event)
    {
        // CS2 her round baslangicinda dunyadaki env_beam ve custom_hud_layout nesnelerini sifirlar.
        // Eski beam referanslarini ve entity transmit bloklarini temizle.
        _renderer?.Forget(0.35f);
        _effects?.OnRoundStart();
        _hud?.Custom.Forget();

        if (_store != null && _store.CurrentMap == "unknown")
        {
            var map = TryGetCurrentMap();
            if (!string.IsNullOrWhiteSpace(map) && map != "unknown")
            {
                _store.LoadMap(map);
            }
        }
        _mapReady = true;
        return HookResult.Continue;
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        var slot = @event.PlayerId;
        _lastCmdTime.Remove(slot);
        _drawing?.Cancel(slot);
        _menu?.OnPlayerDisconnected(slot);
        _renderer?.OnPlayerDisconnected(slot);
        _hud?.OnDisconnect(slot);
        _chatInput?.CancelInput(slot);
    }

    private void OnClientKeyStateChanged(IOnClientKeyStateChangedEvent @event)
    {
        if (!@event.Pressed) return;

        var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player == null || !player.IsValid) return;

        if (_drawing != null && _drawing.IsDrawing(player.Slot))
        {
            _drawing.OnKeyPressed(player, @event.Key);
            return;
        }

        if (_menu != null && _menu.IsMenuOpen(player.Slot))
        {
            _menu.OnKeyPressed(player, @event.Key);
        }
    }

    private void OnCustomHudClicked(IOnCustomHudClickedEvent @event)
    {
        if (_hud == null || !_hud.Custom.IsOurs(@event.CustomHudLayout)) return;

        var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player == null || !player.IsValid) return;

        _menu?.OnCustomHudButtonClicked(player, @event.ButtonId);
    }

    private void OnTick()
    {
        if (!_mapReady)
        {
            var map = TryGetCurrentMap();
            if (string.IsNullOrWhiteSpace(map) || map == "unknown")
            {
                return;
            }
            _store?.LoadMap(map);
            _renderer?.Forget(0.5f);
            _mapReady = true;
        }

        _drawing?.OnTick();
        _hud?.Tick();

        var allPlayers = Core.PlayerManager.GetAllPlayers().Where(p => p != null && p.IsValid).ToList();
        var playerCount = allPlayers.Count(p => !p.IsFakeClient);

        // Her oyuncunun HUD'unu guncelle
        foreach (var player in allPlayers)
        {
            if (_menu != null && _menu.IsMenuOpen(player.Slot))
            {
                _menu.PollPlayerInput(player);
            }

            var frame = _menu?.BuildFrameForPlayer(player);
            if (frame == null || frame.IsEmpty)
            {
                // Menu veya cizim modu acik degilse, bolge ici bilgi HUD'unu kontrol et
                if (_config.ShowZoneInfoHud && _effects != null)
                {
                    var info = _effects.GetZoneInfoView(player.Slot);
                    if (info != null)
                    {
                        frame = new HudFrame { Info = info };
                    }
                }
            }

            if (frame != null && !frame.IsEmpty)
            {
                _hud?.Render(player, frame);
            }
            else
            {
                _hud?.Clear(player);
            }
        }

        // Bolge sinirlarini senkronize et (her 4 tickte bir)
        _tickCounter++;
        if (_tickCounter % 4 == 0 && _renderer != null && _store != null && _menu != null)
        {
            _renderer.Sync(_store.Zones, _menu.AdminViewers, _menu.HighlightedZones, playerCount);
        }
    }
}
