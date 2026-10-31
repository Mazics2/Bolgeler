namespace Bolgeler.Config;

public enum HudMode
{
    /// <summary>Panorama CenterHTML paneli. Hicbir workshop addon gerektirmez, her sunucuda aninda calisir.</summary>
    CenterHtml = 0,

    /// <summary>Gercek Panorama custom HUD (custom_hud_layout entity). Istemcide derlenmis workshop addon (.vpk) gerekir.</summary>
    Custom = 1,
}

public class PluginConfig
{
    /// <summary>Ana komut. "!bolgeler" / "/bolgeler" / "sw_bolgeler" / "bolgeler" olarak calisir.</summary>
    public string Command { get; set; } = "bolgeler";

    public List<string> Aliases { get; set; } = new() { "bolge", "zones", "zone" };

    /// <summary>Yetki kontrolu aktif olsun mu? (false = herkes kullanabilir, test/gelistirme icin ideal).</summary>
    public bool RequirePermission { get; set; } = true;

    /// <summary>RequirePermission = true ise bu yetkilerden herhangi birine sahip olan oyuncu menuyu kullanabilir.</summary>
    public List<string> Permissions { get; set; } = new() { "bolgeler.admin", "admin.root" };

    public string ChatPrefix { get; set; } = "[Lime]Bölgeler[Default] »";

    /// <summary>Varsayilan HUD modu. CenterHtml her sunucuda addonsuz calisir.</summary>
    public HudMode DefaultHudMode { get; set; } = HudMode.CenterHtml;

    /// <summary>
    /// Sunucuda bolgeler.xml iceren derlenmis bir Workshop Addon (.vpk) yuklu ise true yapin.
    /// false iken istemcide gorunmez panel olusmamasi icin her zaman CenterHtml Panorama HUD kullanilir.
    /// </summary>
    public bool EnableWorkshopCustomHud { get; set; } = false;

    /// <summary>Workshop addon icindeki derlenmis layout dosyasinin yolu.</summary>
    public string HudLayoutPath { get; set; } = "panorama/layout/custom_game/bolgeler.xml";

    /// <summary>HUD menusunde sayfa basina satir (en fazla 8).</summary>
    public int RowsPerPage { get; set; } = 6;

    /// <summary>Menu acikken W/A/S/D ile gezinirken oyuncu yerinden oynamasin.</summary>
    public bool FreezeWhileMenuOpen { get; set; } = true;

    /// <summary>Iki nokta ayni zemindeyse bolgeye verilecek yukseklik.</summary>
    public float DefaultHeight { get; set; } = 128f;

    /// <summary>Mouse ile cizimde varsayilan izgara (0 = kapali).</summary>
    public float DefaultGridSize { get; set; } = 8f;

    public float BeamWidth { get; set; } = 1.5f;

    /// <summary>Oyuncu bir bolgedeyken HUD'da bolge bilgisini goster.</summary>
    public bool ShowZoneInfoHud { get; set; } = true;

    /// <summary>Bolgeye giris/cikista chat mesaji (bolge bazinda da kapatilabilir).</summary>
    public bool ChatEnterExitMessages { get; set; } = true;

    public string MessageEnter { get; set; } = "[Green]{zone}[Default] bölgesine girdin.";
    public string MessageExit { get; set; } = "[Red]{zone}[Default] bölgesinden çıktın.";
    public string MessageBlocked { get; set; } = "[Red]{zone}[Default] bölgesi şu an kapalı.";
}
