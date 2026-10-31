using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using Bolgeler.Models;

namespace Bolgeler.Services;

/// <summary>
/// Bolgelerin kenar cizgilerini yonetir.
/// - ShowBorder acik bolgeler herkese gorunur.
/// - Diger bolgeler sadece "izleyici" adminlere (gorunum acik / editor acik) gorunur;
///   diger oyunculara entity transmit'i engellenir.
/// - Cizim sirasindaki canli onizleme kutulari da burada tutulur.
/// </summary>
public class ZoneRenderer
{
    private readonly ISwiftlyCore _core;
    private readonly Dictionary<string, BeamBox> _zoneBoxes = new();
    private readonly Dictionary<string, uint[]> _lastIndexes = new();
    private readonly Dictionary<int, BeamBox> _previews = new();
    private readonly HashSet<(int Slot, uint Index)> _blocked = new();
    private float _readyTime;

    public float BeamWidth { get; set; } = 1.5f;

    public ZoneRenderer(ISwiftlyCore core)
    {
        _core = core;
    }

    /// <summary>
    /// Periyodik senkron. <paramref name="viewers"/>: kenarlari her zaman goren oyuncu slotlari.
    /// <paramref name="highlighted"/>: bir adminin su an duzenledigi bolge id'leri.
    /// </summary>
    public void Sync(IReadOnlyList<ZoneData> zones, IReadOnlySet<int> viewers, IReadOnlySet<string> highlighted, int playerCount)
    {
        var now = ZoneStore.SafeCurrentTime(_core);
        if (now < _readyTime) return;

        var wanted = new HashSet<string>();
        var hiddenIndexes = new List<uint>();
        var publicIndexes = new List<uint>();

        foreach (var zone in zones)
        {
            bool visibleToAll = zone.Enabled && zone.ShowBorder;
            bool draw = visibleToAll || viewers.Count > 0;
            if (!draw) continue;

            wanted.Add(zone.Id);
            if (!_zoneBoxes.TryGetValue(zone.Id, out var box))
            {
                box = new BeamBox(_core);
                _zoneBoxes[zone.Id] = box;
            }

            var color = ResolveColor(zone, highlighted.Contains(zone.Id), playerCount);
            var width = highlighted.Contains(zone.Id) ? BeamWidth * 2f : BeamWidth;

            if (!box.IsAlive)
            {
                // Round restart vb. ile silinmis; eski indexlerin engelini kaldir (baska entity'ye gecmis olabilir).
                if (_lastIndexes.TryGetValue(zone.Id, out var old)) UnblockIndexes(old);
            }

            box.Draw(zone.Box, color, width);
            var idx = box.EntityIndexes.ToArray();
            _lastIndexes[zone.Id] = idx;
            (visibleToAll ? publicIndexes : hiddenIndexes).AddRange(idx);
        }

        foreach (var id in _zoneBoxes.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            RemoveZoneBox(id);
        }

        ApplyTransmit(hiddenIndexes, publicIndexes, viewers);
    }

    private static Color ResolveColor(ZoneData zone, bool highlighted, int playerCount)
    {
        if (highlighted) return new Color(255, 255, 255, 255);
        if (!zone.Enabled) return new Color(120, 120, 120, 160);
        if (zone.BlockEntry && (zone.BlockBelowPlayers <= 0 || playerCount < zone.BlockBelowPlayers))
            return new Color(255, 60, 60, 230);
        try
        {
            var c = Color.FromHex(zone.Color);
            return new Color(c.R, c.G, c.B, (byte)220);
        }
        catch
        {
            return new Color(0, 200, 255, 220);
        }
    }

    private void ApplyTransmit(List<uint> hidden, List<uint> pub, IReadOnlySet<int> viewers)
    {
        foreach (var player in _core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient) continue;
            bool isViewer = viewers.Contains(player.Slot);

            foreach (var idx in hidden)
            {
                SetBlocked(player.Slot, idx, !isViewer);
            }
            foreach (var idx in pub)
            {
                SetBlocked(player.Slot, idx, false);
            }
        }
    }

    private void SetBlocked(int slot, uint idx, bool block)
    {
        var key = (slot, idx);
        bool isBlocked = _blocked.Contains(key);
        if (isBlocked == block) return;

        var player = _core.PlayerManager.GetPlayer(slot);
        if (player == null || !player.IsValid) return;

        try
        {
            player.ShouldBlockTransmitEntity((int)idx, block);
            if (block) _blocked.Add(key); else _blocked.Remove(key);
        }
        catch { }
    }

    private void UnblockIndexes(IEnumerable<uint> indexes)
    {
        var set = indexes.ToHashSet();
        foreach (var key in _blocked.Where(k => set.Contains(k.Index)).ToList())
        {
            try
            {
                var player = _core.PlayerManager.GetPlayer(key.Slot);
                if (player != null && player.IsValid)
                {
                    player.ShouldBlockTransmitEntity((int)key.Index, false);
                }
            }
            catch { }
            _blocked.Remove(key);
        }
    }

    private void RemoveZoneBox(string id)
    {
        if (_zoneBoxes.Remove(id, out var box))
        {
            UnblockIndexes(box.EntityIndexes.ToArray());
            box.Destroy();
        }
        if (_lastIndexes.Remove(id, out var old)) UnblockIndexes(old);
    }

    // ------------------------------------------------------------------
    // Onizleme (cizim modu)
    // ------------------------------------------------------------------

    public void DrawPreview(int slot, ZoneBox box, Color color)
    {
        if (!_previews.TryGetValue(slot, out var preview))
        {
            preview = new BeamBox(_core);
            _previews[slot] = preview;
        }
        preview.Draw(box, color, BeamWidth * 1.5f);
    }

    public void ClearPreview(int slot)
    {
        if (_previews.Remove(slot, out var preview))
        {
            preview.Destroy();
        }
    }

    public void OnPlayerDisconnected(int slot)
    {
        ClearPreview(slot);
        _blocked.RemoveWhere(k => k.Slot == slot);
    }

    /// <summary>Harita degisiminde veya round baslangicinda eski beam referanslarini sifirlar.</summary>
    public void Forget(float delaySeconds = 0.5f)
    {
        UnblockIndexes(_blocked.Select(k => k.Index).ToList());
        _zoneBoxes.Clear();
        _lastIndexes.Clear();
        _previews.Clear();
        _blocked.Clear();
        _readyTime = ZoneStore.SafeCurrentTime(_core) + delaySeconds;
    }

    public void DestroyAll()
    {
        foreach (var id in _zoneBoxes.Keys.ToList()) RemoveZoneBox(id);
        foreach (var slot in _previews.Keys.ToList()) ClearPreview(slot);
        _blocked.Clear();
    }
}

