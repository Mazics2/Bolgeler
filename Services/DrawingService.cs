using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using Bolgeler.Config;
using Bolgeler.Hud;
using Bolgeler.Models;

namespace Bolgeler.Services;

public sealed class DrawState
{
    public string ZoneName { get; set; } = "";
    public ZoneData? ExistingZone { get; set; }
    public Vector? Corner1 { get; set; }
    public Vector CurrentAim { get; set; }
    public float GridSize { get; set; } = 8f;
    public float Height { get; set; } = 128f;
    public bool LastMouse1 { get; set; }
    public bool LastMouse2 { get; set; }
    public GameButtonFlags LastButtons { get; set; }
    public float LastActionTime { get; set; }
    public float LastKeyTime { get; set; }
    public Action<ZoneData>? OnCompleted { get; set; }
    public Action? OnCancelled { get; set; }
}

/// <summary>
/// Mouse / nisangah ile interaktif 3D bolge cizimi.
/// Mouse1 (veya E) ile 1. koseyi koyar, canli onizleme cikar, 2. koseyi tiklayinca bolge olusur.
/// R ile grid degistirilir, F ile yukseklik artirilir, Mouse2 veya ESC ile iptal edilir.
/// </summary>
public class DrawingService
{
    private const float Debounce = 0.15f;

    private readonly ISwiftlyCore _core;
    private readonly AimTracer _tracer;
    private readonly ZoneRenderer _renderer;
    private readonly PluginConfig _config;

    private readonly Dictionary<int, DrawState> _sessions = new();

    public DrawingService(ISwiftlyCore core, AimTracer tracer, ZoneRenderer renderer, PluginConfig config)
    {
        _core = core;
        _tracer = tracer;
        _renderer = renderer;
        _config = config;
    }

    public bool IsDrawing(int slot) => _sessions.ContainsKey(slot);

    public IEnumerable<int> ActiveSlots => _sessions.Keys;

    public DrawState? GetState(int slot) => _sessions.TryGetValue(slot, out var s) ? s : null;

    public void Start(IPlayer player, string name, Action<ZoneData> onCompleted, Action? onCancelled = null, ZoneData? existingZone = null)
    {
        var now = ZoneStore.SafeCurrentTime(_core);
        _sessions[player.Slot] = new DrawState
        {
            ZoneName = name,
            ExistingZone = existingZone,
            GridSize = _config.DefaultGridSize,
            Height = existingZone != null ? MathF.Max(64f, existingZone.Box.Size.Z) : _config.DefaultHeight,
            OnCompleted = onCompleted,
            OnCancelled = onCancelled,
            LastMouse1 = (player.PressedButtons & GameButtonFlags.Mouse1) != 0,
            LastMouse2 = (player.PressedButtons & GameButtonFlags.Mouse2) != 0,
            LastButtons = player.PressedButtons,
            LastActionTime = now,
            LastKeyTime = now
        };
        player.SendChat($"{_config.ChatPrefix} [Lime]Bölge çizim modu başladı![Default] 1. köşeye bakıp [Yellow]Sol Tık (Ateş)[Default] veya [Yellow][E][Default] basın. İptal: [Red]Sağ Tık[Default]");
    }

    public void Cancel(int slot)
    {
        if (_sessions.Remove(slot, out var session))
        {
            _renderer.ClearPreview(slot);
            session.OnCancelled?.Invoke();
        }
    }

    public void CancelAll()
    {
        foreach (var slot in _sessions.Keys.ToList())
        {
            if (_sessions.Remove(slot))
            {
                _renderer.ClearPreview(slot);
            }
        }
    }

    public void OnTick()
    {
        if (_sessions.Count == 0) return;

        var now = ZoneStore.SafeCurrentTime(_core);

        foreach (var (slot, state) in _sessions.ToList())
        {
            var player = _core.PlayerManager.GetPlayer(slot);
            if (player == null || !player.IsValid || !player.IsAlive)
            {
                Cancel(slot);
                continue;
            }

            var pawn = player.PlayerPawn;
            if (pawn == null || !pawn.IsValid) continue;

            if (_tracer.TryGetAimPoint(pawn, out var rawAim))
            {
                state.CurrentAim = AimTracer.Snap(rawAim, state.GridSize);
            }

            var aim = state.CurrentAim;

            // Live preview render
            if (state.Corner1.HasValue)
            {
                var box = ZoneBox.FromCorners(state.Corner1.Value, aim, state.Height);
                _renderer.DrawPreview(slot, box, new Color(0, 255, 120, 230));
            }
            else
            {
                // 1. kose imleci: Nisan alinan noktada yuksekligi gosteren isaret kutusu
                var p1 = new Vector(aim.X - 8f, aim.Y - 8f, aim.Z);
                var p2 = new Vector(aim.X + 8f, aim.Y + 8f, aim.Z + MathF.Min(state.Height, 64f));
                _renderer.DrawPreview(slot, new ZoneBox(p1, p2), new Color(255, 220, 0, 220));
            }

            // Input handling (Mouse1, Mouse2, E, R, F)
            var buttons = player.PressedButtons;
            var rising = buttons & ~state.LastButtons;
            state.LastButtons = buttons;

            var m1 = (buttons & GameButtonFlags.Mouse1) != 0;
            var m2 = (buttons & GameButtonFlags.Mouse2) != 0;

            var m1Rising = m1 && !state.LastMouse1;
            var m2Rising = m2 && !state.LastMouse2;

            state.LastMouse1 = m1;
            state.LastMouse2 = m2;

            if (m2Rising)
            {
                player.SendChat($"{_config.ChatPrefix} [Red]Çizim iptal edildi.[Default]");
                Cancel(slot);
                continue;
            }

            bool eRising = (rising & GameButtonFlags.E) != 0;
            if ((m1Rising || eRising) && now - state.LastActionTime >= Debounce)
            {
                state.LastActionTime = now;
                state.LastKeyTime = now;
                HandleClick(player, state, aim);
                continue;
            }

            if ((rising & GameButtonFlags.R) != 0) OnKeyPressed(player, KeyKind.R);
            else if ((rising & GameButtonFlags.F) != 0) OnKeyPressed(player, KeyKind.F);
        }
    }

    public void OnKeyPressed(IPlayer player, KeyKind key)
    {
        if (!_sessions.TryGetValue(player.Slot, out var state)) return;

        var now = ZoneStore.SafeCurrentTime(_core);
        if (now - state.LastKeyTime < Debounce) return;
        state.LastKeyTime = now;

        switch (key)
        {
            case KeyKind.E:
                if (now - state.LastActionTime >= Debounce)
                {
                    state.LastActionTime = now;
                    HandleClick(player, state, state.CurrentAim);
                }
                break;

            case KeyKind.R:
                // Grid boyutu dongusu: 0 -> 8 -> 16 -> 32 -> 64 -> 0
                state.GridSize = state.GridSize switch
                {
                    0f => 8f,
                    8f => 16f,
                    16f => 32f,
                    32f => 64f,
                    _ => 0f
                };
                player.SendChat($"{_config.ChatPrefix} Izgara (Grid): [Lime]{(state.GridSize == 0f ? "KAPALI" : $"{state.GridSize} birim")}[Default]");
                break;

            case KeyKind.F:
                // Yukseklik dongusu: 64 -> 96 -> 128 -> 192 -> 256 -> 384 -> 512 -> 64
                state.Height = state.Height switch
                {
                    64f => 96f,
                    96f => 128f,
                    128f => 192f,
                    192f => 256f,
                    256f => 384f,
                    384f => 512f,
                    _ => 64f
                };
                player.SendChat($"{_config.ChatPrefix} Bölge Yüksekliği: [Lime]{state.Height:F0} birim[Default]");
                break;

            case KeyKind.Esc:
                player.SendChat($"{_config.ChatPrefix} [Red]Çizim iptal edildi.[Default]");
                Cancel(player.Slot);
                break;
        }
    }

    private void HandleClick(IPlayer player, DrawState state, Vector aim)
    {
        if (!state.Corner1.HasValue)
        {
            state.Corner1 = aim;
            player.SendChat($"{_config.ChatPrefix} [Yellow]1. Köşe belirlendi![Default] ({aim.X:F0}, {aim.Y:F0}, {aim.Z:F0}) — Şimdi 2. köşeye bakıp [Lime]Sol Tık[Default] veya [Lime][E][Default] basın.");
        }
        else
        {
            var p1 = state.Corner1.Value;
            var p2 = aim;
            var box = ZoneBox.FromCorners(p1, p2, state.Height);

            ZoneData zone;
            if (state.ExistingZone != null)
            {
                zone = state.ExistingZone;
                zone.Box = box;
            }
            else
            {
                zone = new ZoneData
                {
                    Name = state.ZoneName,
                    Mins = box.MinsArray(),
                    Maxs = box.MaxsArray(),
                    Enabled = true,
                    GodMode = true,
                    NoDamageOut = true,
                    Heal = true
                };
            }

            _sessions.Remove(player.Slot);
            _renderer.ClearPreview(player.Slot);

            player.SendChat($"{_config.ChatPrefix} [Lime]'{zone.Name}' ({box.Size.X:F0}x{box.Size.Y:F0}x{box.Size.Z:F0}) başarıyla kaydedildi![Default]");
            state.OnCompleted?.Invoke(zone);
        }
    }

    public HudDrawView? GetDrawView(int slot)
    {
        if (!_sessions.TryGetValue(slot, out var state)) return null;

        bool step1 = !state.Corner1.HasValue;
        var aim = state.CurrentAim;

        string infoText;
        if (step1)
        {
            infoText = $"1. Köşeyi seçmek için zemine bakıp SOL TIK veya [E] bas.\nNişangah: ({aim.X:F0}, {aim.Y:F0}, {aim.Z:F0}) | Izgara: {(state.GridSize == 0 ? "Kapalı" : $"{state.GridSize:F0}")} | Yükseklik: {state.Height:F0}";
        }
        else
        {
            var previewBox = ZoneBox.FromCorners(state.Corner1!.Value, aim, state.Height);
            var sz = previewBox.Size;
            infoText = $"2. Köşeyi seçmek için karşı köşeye bakıp SOL TIK veya [E] bas.\nBoyut: {sz.X:F0} x {sz.Y:F0} x {sz.Z:F0} | Izgara: {(state.GridSize == 0 ? "Kapalı" : $"{state.GridSize:F0}")}";
        }

        return new HudDrawView
        {
            Title = $"BÖLGE ÇİZİMİ: {state.ZoneName}",
            Step = step1 ? 1 : 2,
            Info = infoText,
            Keys = "[Sol Tık / E] Köşe Seç  |  [Sağ Tık] İptal  |  [R] Izgara  |  [F] Yükseklik"
        };
    }
}
