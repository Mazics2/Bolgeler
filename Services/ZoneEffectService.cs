using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using Bolgeler.Config;
using Bolgeler.Hud;
using Bolgeler.Models;

namespace Bolgeler.Services;

public class ZoneEffectService
{
    private readonly ISwiftlyCore _core;
    private readonly ZoneStore _store;
    private readonly PushService _pushService;
    private readonly PluginConfig _config;

    private readonly Dictionary<int, HashSet<string>> _playerZones = new();
    private readonly Dictionary<int, float> _healCarry = new();
    private readonly Dictionary<int, float> _armorCarry = new();
    private readonly Dictionary<int, float> _lastHealTime = new();
    private readonly Dictionary<int, float> _lastArmorTime = new();
    private readonly Dictionary<int, float> _lastBlockMsgTime = new();
    private readonly HashSet<int> _gravityModifiedSlots = new();

    public ZoneEffectService(ISwiftlyCore core, ZoneStore store, PushService pushService, PluginConfig config)
    {
        _core = core;
        _store = store;
        _pushService = pushService;
        _config = config;
    }

    public void Load()
    {
        _core.Event.OnTick += OnTick;
        try
        {
            _core.GameHooks.Entities.TakeDamage.Pre += OnEntityTakeDamage;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError($"[Bolgeler] TakeDamage hook baglanamadi: {ex.Message}");
        }
        _core.Event.OnClientDisconnected += OnClientDisconnected;
    }

    public void Unload()
    {
        _core.Event.OnTick -= OnTick;
        try
        {
            _core.GameHooks.Entities.TakeDamage.Pre -= OnEntityTakeDamage;
        }
        catch { }
        _core.Event.OnClientDisconnected -= OnClientDisconnected;
        _playerZones.Clear();
        _healCarry.Clear();
        _armorCarry.Clear();
        _lastHealTime.Clear();
        _lastArmorTime.Clear();
        _lastBlockMsgTime.Clear();
        _gravityModifiedSlots.Clear();
    }

    public void OnRoundStart()
    {
        _playerZones.Clear();
        _healCarry.Clear();
        _armorCarry.Clear();
        _lastHealTime.Clear();
        _lastArmorTime.Clear();
        _lastBlockMsgTime.Clear();
        _gravityModifiedSlots.Clear();
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        var slot = @event.PlayerId;
        _playerZones.Remove(slot);
        _healCarry.Remove(slot);
        _armorCarry.Remove(slot);
        _lastHealTime.Remove(slot);
        _lastArmorTime.Remove(slot);
        _lastBlockMsgTime.Remove(slot);
        _gravityModifiedSlots.Remove(slot);
    }

    public ZoneData? GetPrimaryZoneForPlayer(int slot)
    {
        if (!_playerZones.TryGetValue(slot, out var ids) || ids.Count == 0) return null;
        var firstId = ids.First();
        return _store.Get(firstId);
    }

    public HudInfoView? GetZoneInfoView(int slot)
    {
        var zone = GetPrimaryZoneForPlayer(slot);
        if (zone == null || !zone.Enabled) return null;

        var fx = new List<string>();
        if (zone.GodMode) fx.Add("Ölümsüzlük");
        if (zone.NoDamageOut) fx.Add("Dışarı Hasar Kapalı");
        if (zone.NoDamageIn) fx.Add("İçeri Hasar Kapalı");
        if (zone.Heal) fx.Add($"+{zone.HealPerSecond:0} Can/sn");
        if (zone.Armor) fx.Add($"+{zone.ArmorPerSecond:0} Zırh/sn");
        if (zone.Gravity) fx.Add($"Yerçekimi: {zone.GravityScale:0.0}x");

        return new HudInfoView
        {
            Title = $"📍 {zone.Name.ToUpperInvariant()}",
            Effects = fx.Count > 0 ? string.Join(" • ", fx) : "Özel Bölge",
            Color = zone.Color
        };
    }

    private void OnTick()
    {
        var zones = _store.Zones;
        var allPlayers = _core.PlayerManager.GetAllPlayers().Where(p => p != null && p.IsValid).ToList();
        if (zones.Count == 0 && _gravityModifiedSlots.Count == 0) return;

        var playerCount = allPlayers.Count(p => !p.IsFakeClient);
        var now = ZoneStore.SafeCurrentTime(_core);

        foreach (var player in allPlayers)
        {
            var pawn = player.PlayerPawn;
            var slot = player.Slot;
            if (pawn == null || !pawn.IsValid || !player.IsAlive || !pawn.AbsOrigin.HasValue)
            {
                _playerZones.Remove(slot);
                _gravityModifiedSlots.Remove(slot);
                continue;
            }

            var pos = pawn.AbsOrigin.Value;
            var currentZones = new HashSet<string>();

            if (!_playerZones.TryGetValue(slot, out var previousZones))
            {
                previousZones = new HashSet<string>();
                _playerZones[slot] = previousZones;
            }

            bool inGravityZone = false;

            foreach (var zone in zones)
            {
                if (!zone.Enabled) continue;
                if (!MatchesTeam(player, zone)) continue;

                var box = zone.Box;
                if (!box.ContainsPlayer(pos)) continue;

                // --- Giris engeli (Pushback / BlockingZones) ---
                bool isBlocked = zone.BlockEntry && (zone.BlockBelowPlayers <= 0 || playerCount < zone.BlockBelowPlayers);
                if (isBlocked)
                {
                    var dir = box.OutwardDirection(pos);
                    var target = box.GetPushOutTarget(pos, 22f);
                    var vel = new Vector(dir.X * 280f, dir.Y * 280f, 35f);
                    _pushService.Request(slot, target, vel);

                    if (_config.ChatEnterExitMessages)
                    {
                        if (!_lastBlockMsgTime.TryGetValue(slot, out var lastMsg) || now - lastMsg >= 2.0f)
                        {
                            _lastBlockMsgTime[slot] = now;
                            player.SendChat($"{_config.ChatPrefix} {_config.MessageBlocked.Replace("{zone}", zone.Name)}");
                        }
                    }
                    continue;
                }

                currentZones.Add(zone.Id);

                // --- Iyilestirme (Heal) ---
                if (zone.Heal && pawn.Health < zone.HealMax)
                {
                    ApplyHeal(pawn, slot, zone.HealPerSecond, zone.HealMax, now);
                }

                // --- Zirh doldurma (Armor) ---
                if (zone.Armor && pawn.ArmorValue < zone.ArmorMax)
                {
                    ApplyArmor(pawn, slot, zone.ArmorPerSecond, zone.ArmorMax, now);
                }

                // --- Yercekimi (Gravity) ---
                if (zone.Gravity)
                {
                    inGravityZone = true;
                    _gravityModifiedSlots.Add(slot);
                    if (MathF.Abs(pawn.GravityScale - zone.GravityScale) > 0.01f)
                    {
                        pawn.GravityScale = zone.GravityScale;
                        pawn.GravityScaleUpdated();
                    }
                }
            }

            // Yercekimi bolgesinden cikildiysa veya bolge silindiyse/kapatildiysa yercekimini 1.0f'e dondur
            if (!inGravityZone && _gravityModifiedSlots.Remove(slot))
            {
                if (MathF.Abs(pawn.GravityScale - 1.0f) > 0.01f)
                {
                    pawn.GravityScale = 1.0f;
                    pawn.GravityScaleUpdated();
                }
            }

            // Giris / Cikis bildirimleri
            if (_config.ChatEnterExitMessages)
            {
                foreach (var id in currentZones)
                {
                    if (!previousZones.Contains(id))
                    {
                        var z = _store.Get(id);
                        if (z != null && z.Messages)
                        {
                            player.SendChat($"{_config.ChatPrefix} {_config.MessageEnter.Replace("{zone}", z.Name)}");
                        }
                    }
                }

                foreach (var id in previousZones)
                {
                    if (!currentZones.Contains(id))
                    {
                        var z = _store.Get(id);
                        if (z != null && z.Messages)
                        {
                            player.SendChat($"{_config.ChatPrefix} {_config.MessageExit.Replace("{zone}", z.Name)}");
                        }
                    }
                }
            }

            _playerZones[slot] = currentZones;
        }
    }

    private void ApplyHeal(CCSPlayerPawn pawn, int slot, float rate, int max, float now)
    {
        if (!_lastHealTime.TryGetValue(slot, out var last))
        {
            _lastHealTime[slot] = now;
            _healCarry[slot] = 0f;
            return;
        }

        var delta = Math.Clamp(now - last, 0f, 0.5f);
        _lastHealTime[slot] = now;

        var carry = _healCarry.TryGetValue(slot, out var c) ? c : 0f;
        var total = rate * delta + carry;
        var add = (int)MathF.Floor(total);
        _healCarry[slot] = total - add;

        if (add > 0)
        {
            var next = Math.Min(max, pawn.Health + add);
            if (next != pawn.Health)
            {
                pawn.Health = next;
                pawn.HealthUpdated();
            }
        }
    }

    private void ApplyArmor(CCSPlayerPawn pawn, int slot, float rate, int max, float now)
    {
        if (!_lastArmorTime.TryGetValue(slot, out var last))
        {
            _lastArmorTime[slot] = now;
            _armorCarry[slot] = 0f;
            return;
        }

        var delta = Math.Clamp(now - last, 0f, 0.5f);
        _lastArmorTime[slot] = now;

        var carry = _armorCarry.TryGetValue(slot, out var c) ? c : 0f;
        var total = rate * delta + carry;
        var add = (int)MathF.Floor(total);
        _armorCarry[slot] = total - add;

        if (add > 0)
        {
            var next = Math.Min(max, pawn.ArmorValue + add);
            if (next != pawn.ArmorValue)
            {
                pawn.ArmorValue = next;
                pawn.ArmorValueUpdated();
            }
        }
    }

    private void OnEntityTakeDamage(ref TakeDamageEntityPreContext ctx)
    {
        if (_store.Zones.Count == 0) return;

        try
        {
            var victim = ResolvePlayer(ctx.Params.Entity);
            ref var info = ref ctx.Params.Info;
            var attacker = ResolvePlayer(info.Attacker.Value);

            // Dusme hasari kontrolu
            if (victim != null && (info.DamageType & DamageTypes_t.DMG_FALL) != 0)
            {
                if (IsPlayerInZoneWith(victim, z => z.NoFallDamage || z.GodMode))
                {
                    ctx.SetHookResult(HookResult.Stop);
                    return;
                }
            }

            // Kurban Olumsuzluk bolgesindeyse
            if (victim != null && IsPlayerInZoneWith(victim, z => z.GodMode))
            {
                ctx.SetHookResult(HookResult.Stop);
                return;
            }

            // Saldirgan bolge icinden bolge disina hasar veremez (NoDamageOut)
            if (attacker != null)
            {
                var attackerZones = GetActiveZonesForPlayer(attacker).ToList();

                if (attackerZones.Any(z => z.NoDamageDealt))
                {
                    ctx.SetHookResult(HookResult.Stop);
                    return;
                }

                if (attackerZones.Any(z => z.NoDamageOut))
                {
                    // Eger kurban saldirganla ayni guvenli bolgede degilse hasari durdur
                    var victimZones = victim != null ? GetActiveZonesForPlayer(victim).Select(z => z.Id).ToHashSet() : new HashSet<string>();
                    bool sameSafeZone = attackerZones.Any(z => z.NoDamageOut && victimZones.Contains(z.Id));
                    if (!sameSafeZone)
                    {
                        ctx.SetHookResult(HookResult.Stop);
                        return;
                    }
                }
            }

            // Kurban NoDamageIn bolgesindeyse ve saldirgan bolge disindaysa
            if (victim != null)
            {
                var victimZones = GetActiveZonesForPlayer(victim).ToList();
                if (victimZones.Any(z => z.NoDamageIn))
                {
                    var attackerZones = attacker != null ? GetActiveZonesForPlayer(attacker).Select(z => z.Id).ToHashSet() : new HashSet<string>();
                    bool sameSafeZone = victimZones.Any(z => z.NoDamageIn && attackerZones.Contains(z.Id));
                    if (!sameSafeZone)
                    {
                        ctx.SetHookResult(HookResult.Stop);
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogError($"[Bolgeler] Hasar kontrolunde hata: {ex.Message}");
        }
    }

    private static bool MatchesTeam(IPlayer player, ZoneData zone)
    {
        if (zone.Team == ZoneTeam.Herkes) return true;
        byte teamNum = player.Controller != null && player.Controller.IsValid ? player.Controller.TeamNum : (byte)0;
        if (zone.Team == ZoneTeam.T) return teamNum == 2;
        if (zone.Team == ZoneTeam.CT) return teamNum == 3;
        return true;
    }

    private IEnumerable<ZoneData> GetActiveZonesForPlayer(IPlayer player)
    {
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid || !pawn.AbsOrigin.HasValue) yield break;
        var pos = pawn.AbsOrigin.Value;

        foreach (var z in _store.Zones)
        {
            if (z.Enabled && MatchesTeam(player, z) && z.Box.ContainsPlayer(pos))
            {
                yield return z;
            }
        }
    }

    private bool IsPlayerInZoneWith(IPlayer player, Func<ZoneData, bool> predicate)
    {
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid || !pawn.AbsOrigin.HasValue) return false;
        var pos = pawn.AbsOrigin.Value;

        foreach (var z in _store.Zones)
        {
            if (z.Enabled && MatchesTeam(player, z) && z.Box.ContainsPlayer(pos) && predicate(z))
            {
                return true;
            }
        }
        return false;
    }

    private IPlayer? ResolvePlayer(CEntityInstance? entity)
    {
        if (entity == null || !entity.IsValid) return null;
        var targetIdx = entity.Index;

        foreach (var p in _core.PlayerManager.GetAllPlayers())
        {
            if (p == null || !p.IsValid) continue;
            try
            {
                if (p.PlayerPawn != null && p.PlayerPawn.Index == targetIdx) return p;
                if (p.Controller != null && p.Controller.Index == targetIdx) return p;
            }
            catch { }
        }
        return null;
    }
}
