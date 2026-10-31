using System.Text.Json.Serialization;

namespace Bolgeler.Models;

public enum ZoneTeam
{
    Herkes = 0,
    T = 1,
    CT = 2,
}

/// <summary>Haritaya ozel kaydedilen tek bir bolge.</summary>
public class ZoneData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Bolge";
    public float[] Mins { get; set; } = new float[3];
    public float[] Maxs { get; set; } = new float[3];

    public bool Enabled { get; set; } = true;
    public string Color { get; set; } = "#00C8FF";

    /// <summary>Kenar cizgileri herkese gorunsun (kapaliysa sadece duzenleyen adminler gorur).</summary>
    public bool ShowBorder { get; set; } = false;

    /// <summary>Efektlerin hangi takima uygulanacagi.</summary>
    public ZoneTeam Team { get; set; } = ZoneTeam.Herkes;

    // --- Hasar efektleri ---
    /// <summary>Olumsuzluk: bolgedeki oyuncu hasar almaz.</summary>
    public bool GodMode { get; set; } = true;

    /// <summary>Bolge icinden bolge disindaki oyunculara hasar verilemez.</summary>
    public bool NoDamageOut { get; set; } = true;

    /// <summary>Bolge disindan bolge icindeki oyunculara hasar verilemez.</summary>
    public bool NoDamageIn { get; set; } = false;

    /// <summary>Bolgedeki oyuncu hic kimseye hasar veremez.</summary>
    public bool NoDamageDealt { get; set; } = false;

    public bool NoFallDamage { get; set; } = false;

    // --- Iyilestirme ---
    public bool Heal { get; set; } = true;
    public float HealPerSecond { get; set; } = 10f;
    public int HealMax { get; set; } = 100;

    public bool Armor { get; set; } = false;
    public float ArmorPerSecond { get; set; } = 10f;
    public int ArmorMax { get; set; } = 100;

    // --- Hareket ---
    public bool Gravity { get; set; } = false;
    public float GravityScale { get; set; } = 0.5f;

    // --- Giris engeli (BlockingZones mantigi) ---
    public bool BlockEntry { get; set; } = false;

    /// <summary>0 = her zaman engelli. Aksi halde sunucudaki oyuncu sayisi bu degerin altindayken engelli.</summary>
    public int BlockBelowPlayers { get; set; } = 0;

    public bool Messages { get; set; } = true;

    [JsonIgnore]
    public ZoneBox Box
    {
        get => ZoneBox.FromArrays(Mins, Maxs);
        set
        {
            Mins = value.MinsArray();
            Maxs = value.MaxsArray();
        }
    }
}

