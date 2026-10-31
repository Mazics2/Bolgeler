using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using Bolgeler.Config;
using Bolgeler.Models;

namespace Bolgeler.Services;

/// <summary>
/// Harita bazli JSON kayit. Dosyalar: configs/plugins/Bolgeler/maps/&lt;harita&gt;.json
/// Harita degisince eski bolgeler kapanir, yeni haritanin bolgeleri yuklenir.
/// </summary>
public class ZoneStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly ISwiftlyCore _core;
    private readonly List<ZoneData> _zones = new();
    private readonly string _basePath;

    public string CurrentMap { get; private set; } = "unknown";
    public IReadOnlyList<ZoneData> Zones => _zones;

    /// <summary>Bolge listesi her degistiginde (yukleme, ekleme, silme, duzenleme).</summary>
    public event Action? Changed;

    public ZoneStore(ISwiftlyCore core)
    {
        _core = core;
        _basePath = ResolveBasePath(core);
    }

    private string MapsFolder => Path.Combine(_basePath, "maps");
    private string MapFile => Path.Combine(MapsFolder, CurrentMap + ".json");

    public static string ResolveBasePath(ISwiftlyCore core)
    {
        try
        {
            var basePath = core.Configuration.BasePath;
            if (!string.IsNullOrWhiteSpace(basePath))
            {
                return basePath;
            }
        }
        catch
        {
            // Sunucu ilk acilirken BasePath henuz hazir olmayabilir
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(core.PluginPath))
            {
                return Path.Combine(core.PluginPath, "Bolgeler");
            }
        }
        catch { }

        return Path.Combine(AppContext.BaseDirectory, "Bolgeler");
    }

    public static float SafeCurrentTime(ISwiftlyCore core)
    {
        try
        {
            return core.Engine.GlobalVars.CurrentTime;
        }
        catch
        {
            return (float)(Environment.TickCount64 / 1000.0);
        }
    }

    public void LoadMap(string? mapName)
    {
        var sanitized = Sanitize(mapName);
        if (sanitized == "unknown")
        {
            return;
        }

        CurrentMap = sanitized;
        _zones.Clear();
        try
        {
            Directory.CreateDirectory(MapsFolder);
            if (File.Exists(MapFile))
            {
                var list = JsonSerializer.Deserialize<List<ZoneData>>(File.ReadAllText(MapFile), Json);
                if (list != null)
                {
                    foreach (var z in list.Where(z => z.Mins?.Length == 3 && z.Maxs?.Length == 3))
                    {
                        // Mins <= Maxs siralamasini garanti altina al
                        z.Box = ZoneBox.FromArrays(z.Mins!, z.Maxs!);
                        _zones.Add(z);
                    }
                }
            }
            _core.Logger.LogInformation($"[Bolgeler] {CurrentMap}: {_zones.Count} bolge yuklendi.");
        }
        catch (Exception ex)
        {
            _core.Logger.LogError($"[Bolgeler] {CurrentMap} bolgeleri yuklenemedi: {ex.Message}");
        }
        Changed?.Invoke();
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(CurrentMap) || CurrentMap == "unknown")
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(MapsFolder);
            File.WriteAllText(MapFile, JsonSerializer.Serialize(_zones, Json));
        }
        catch (Exception ex)
        {
            _core.Logger.LogError($"[Bolgeler] {CurrentMap} bolgeleri kaydedilemedi: {ex.Message}");
        }
        Changed?.Invoke();
    }

    public ZoneData? Get(string id) => _zones.FirstOrDefault(z => z.Id == id);

    public ZoneData? FindByName(string name)
        => _zones.FirstOrDefault(z => z.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? _zones.FirstOrDefault(z => z.Id.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Add(ZoneData zone)
    {
        _zones.Add(zone);
        Save();
    }

    public bool Remove(string id)
    {
        var removed = _zones.RemoveAll(z => z.Id == id) > 0;
        if (removed) Save();
        return removed;
    }

    public string NextFreeName(string prefix = "Bölge")
    {
        for (int i = 1; ; i++)
        {
            var name = $"{prefix} {i}";
            if (FindByName(name) == null) return name;
        }
    }

    public static string Sanitize(string? mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName)) return "unknown";
        var cleaned = mapName.Trim();
        // Workshop haritalari "workshop/123/de_map" gibi gelebilir - sadece son parcayi al.
        var slash = cleaned.LastIndexOfAny(new[] { '/', '\\' });
        if (slash >= 0 && slash < cleaned.Length - 1) cleaned = cleaned[(slash + 1)..];
        foreach (var c in Path.GetInvalidFileNameChars()) cleaned = cleaned.Replace(c, '_');
        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    // ------------------------------------------------------------------
    // Plugin config
    // ------------------------------------------------------------------

    public static PluginConfig LoadConfig(ISwiftlyCore core)
    {
        var baseDir = ResolveBasePath(core);
        var path = Path.Combine(baseDir, "config.json");
        try
        {
            Directory.CreateDirectory(baseDir);
            PluginConfig cfg;
            if (File.Exists(path))
            {
                cfg = JsonSerializer.Deserialize<PluginConfig>(File.ReadAllText(path), Json) ?? new PluginConfig();
            }
            else
            {
                cfg = new PluginConfig();
            }
            cfg.RowsPerPage = Math.Clamp(cfg.RowsPerPage, 3, 8);
            // Eksik yeni alanlar eklensin diye her yuklemede tekrar yaz.
            File.WriteAllText(path, JsonSerializer.Serialize(cfg, Json));
            return cfg;
        }
        catch (Exception ex)
        {
            core.Logger.LogError($"[Bolgeler] config.json okunamadi, varsayilanlar kullaniliyor: {ex.Message}");
            return new PluginConfig();
        }
    }
}

