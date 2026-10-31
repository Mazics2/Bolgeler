# Bolgeler (CS2 Zones Plugin)

Counter-Strike 2 için **SwiftlyS2** tabanlı gelişmiş bölge (zone) yönetim eklentisi.

Geliştirici / Yapımcı: **surfevo**

---

## 🌟 Özellikler

- **Mouse / Aim ile Canlı Bölge Çizimi**: 2 nokta seçerek harita üzerinde 3D lazer çizgileri ile anında bölge oluşturma.
- **Akıllı Izgara & Yükseklik Desteği**: Grid (ızgara) adımları ve otomatik zemin yüksekliği ayarı.
- **Harita Bazlı Kayıt**: Her harita için `configs/plugins/Bolgeler/maps/<map>.json` konumunda otomatik JSON saklama.
- **Kapsamlı Efektler & Kurallar**:
  - **GodMode / Ölümsüzlük**: Bölgedeki oyuncunun hasar almasını engelleme.
  - **Hasar Koruması**:
    - `NoDamageOut`: Bölge içinden dışarı hasar vermeyi engelleme.
    - `NoDamageIn`: Bölge dışından içeriye hasar vermeyi engelleme.
    - `NoDamageDealt`: Bölgedekilerin kimseye hasar verememesi.
    - `NoFallDamage`: Düşme hasarını engelleme.
  - **Can / Zırh Yenileme**: Saniye başına can ve zırh yenileme (maksimum limit ayarlanabilir).
  - **Yerçekimi (Gravity)**: Bölgeye özel yerçekimi çarpanı (düşük / yüksek).
  - **Giriş Engeli / Bloklama (Pushback)**: Belirli bir oyuncu sayısının altında veya her zaman bölgeye girişi itme kuvveti ile engelleme.
  - **Takım Filtresi**: Herkes, T veya CT takımlarına özel bölge kuralları.
- **Modern HUD & Arayüz**:
  - **Panorama CenterHTML HUD**: Herhangi bir Workshop Addon (.vpk) gerektirmeden tüm sunucularda anında çalışır.
  - **Workshop Custom HUD Desteği**: `.vpk` kullanan sunucular için tam teşekküllü Panorama UI entegrasyonu.
  - **WASD / Tuş Navigasyonu**: Menü açıkken W, A, S, D, E, R tuşları ile kolay kontrol.
  - **Bölge Bilgi Bildirimi**: Bölgeye girildiğinde ekranın sol altında bölge adı ve aktif efektleri gösteren HUD badge.

---

## 🚀 Gereksinimler

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [SwiftlyS2](https://github.com/swiftlys2/swiftlys2) (CS2 Server Framework) - v1.5.0 veya üzeri

---

## 📦 Kurulum

1. Projeyi derleyin:
   ```bash
   dotnet build -c Release
   ```
2. Çıktı klasöründeki (`bin/Release/net10.0/` veya `publish/`) dosyaları sunucunuzun Swiftly eklenti dizinine kopyalayın:
   ```text
   csgo/addons/swiftlys2/plugins/Bolgeler/
   ```
3. Sunucuyu başlatın veya eklentiyi yükleyin.

---

## 🎮 Komutlar & Yetkiler

| Komut | Yetki | Açıklama |
| :--- | :--- | :--- |
| `!bolgeler`, `/bolgeler` | `bolgeler.admin` / `admin.root` | Ana bölge menüsünü açar (Chat komutu). |
| `sw_bolgeler`, `bolgeler` | `bolgeler.admin` / `admin.root` | Ana bölge menüsünü açar (Konsol komutu). |
| `!bolge`, `/bolge`, `!zones` | `bolgeler.admin` / `admin.root` | Alternatif kısayol komutları. |

> **Not:** `configs/plugins/Bolgeler/config.json` dosyasından `RequirePermission = false` yapılarak yetki kontrolü devre dışı bırakılabilir.

---

## ⚙️ Yapılandırma (`config.json`)

```json
{
  "Command": "bolgeler",
  "Aliases": ["bolge", "zones", "zone"],
  "RequirePermission": true,
  "Permissions": ["bolgeler.admin", "admin.root"],
  "ChatPrefix": "[Lime]Bölgeler[Default] »",
  "DefaultHudMode": 0,
  "EnableWorkshopCustomHud": false,
  "HudLayoutPath": "panorama/layout/custom_game/bolgeler.xml",
  "RowsPerPage": 6,
  "FreezeWhileMenuOpen": true,
  "DefaultHeight": 128.0,
  "DefaultGridSize": 8.0,
  "BeamWidth": 1.5,
  "ShowZoneInfoHud": true,
  "ChatEnterExitMessages": true,
  "MessageEnter": "[Green]{zone}[Default] bölgesine girdin.",
  "MessageExit": "[Red]{zone}[Default] bölgesinden çıktın."
}
```

---

## 📄 Lisans & Yapımcı

- **Yapımcı:** [surfevo](https://github.com/surfevo)

