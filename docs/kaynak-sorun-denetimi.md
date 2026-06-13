# Sorun Kapanış Haritası

Discorder, eski çoklu aşma yaklaşımındaki sorun sınıflarını tek tek taşımak yerine ürün kapsamını Discord kullanımına göre daraltarak kapatır. Hedef, her aracı destekleyen karmaşık bir panel değil; Discord uygulaması için varsayılan, Discord web için isteğe bağlı, aç/kapat çalışan ve sistemin geri kalanını kalıcı ayarlarla bozmayan bir tüneldir.

## Kapatılan Sorun Sınıfları

| Sorun sınıfı | Discorder kararı | Sonuç |
| --- | --- | --- |
| Tarayıcıdan Discord web'e erişilememesi | Chrome, Edge, Firefox, Brave, Opera ve Vivaldi süreçleri varsayılan açık Tarayıcı modu ile kapsama alındı | Tarayıcı modu açıldığında Discord uygulaması ve desteklenen tarayıcılar birlikte kapsama alınır; kapalıyken desteklenen tarayıcılar için geçici Discord web engeli uygulanır |
| IPTV, Steam, Spotify veya oyun trafiğinin etkilenmesi | Discord ve desteklenen tarayıcılar dışındaki süreçler kapsam dışında bırakıldı | Özel uygulamalar ve oyunlar tünellenmez |
| Roblox ve oyun gecikmesi | Oyun desteği ürün dışı | Oyun trafiği tünellenmez |
| DNS, DoH veya sistem ağ ayarının bozulması | DNS ayarı hiç değiştirilmez | Bağlantı kapandığında sistem DNS'i eski halindedir |
| Zapret, GoodbyeDPI, ByeDPI, ProxiFyre ve sürücü kaynaklı kararsızlık | Bu motorlar tamamen kaldırıldı | Repoda ve yayın paketinde bu ikili dosyalar yoktur |
| Kalıcı servis, görev zamanlayıcı ve silinemeyen bileşenler | Discorder taşınabilir uygulama olarak çalışır | Uygulama kapandığında kendi süreci biter |
| Discord dışı `Update.exe` kapsamı | Discord klasörleri, bilinen Discord exe adları ve tarayıcı modu açıksa desteklenen tarayıcı adları kullanılır | Genel güncelleyici veya üçüncü taraf güncelleyici kapsama alınmaz |
| Eski/şüpheli kurucu paketleri | WireSock resmi kurucusu çalışma zamanında doğrulanır | Repoya kurucu gömülmez |
| Profil bozulması veya geniş `AllowedApps` | Profil yeniden yazılır ve testlerle korunur | Varsayılanda Discord uygulamaları kalır; tarayıcı modu açıksa desteklenen tarayıcılar eklenir |
| Kullanıcıya belirsiz kurulum | İlk kurulum ekranı WireSock ve WARP koşullarını açıkça gösterir | Onay olmadan kurulum/profil akışı başlamaz |

## Ana Fork Açık Issue Denetimi

2026-06-11 tarihinde `cagritaskn/SplitWire-Turkey` GitHub API'sinde 86 açık kayıt görüldü: 85 açık issue ve 1 açık pull request. Discorder bu kayıtları eski ürün yüzeyini büyüterek değil, Discord dışı riskleri üründen çıkararak ele alır.

| Açık issue sınıfı | Örnek kayıtlar | Discorder kararı |
| --- | --- | --- |
| Genel internet yavaşlaması, kopması, donması veya yeniden başlatma gerektirmesi | `#173`, `#172`, `#171`, `#145`, `#134`, `#133`, `#122`, `#81`, `#65` | DPI motorları, sistem proxy'si, servis ve görev zamanlayıcı kaldırıldı. Discorder sadece WireSock VPN Client sürecini başlatır; kapalı durumda yalnızca kendi Discord alan adı kilidi etkin kalır ve bağlanırken bu marker bloğu kaldırılır. |
| Discord mesaj, profil, güncelleme, başlangıç, çökme veya giriş sorunları | `#168`, `#154`, `#152`, `#143`, `#140`, `#135`, `#86`, `#68` | Ürün Discord Stable, PTB, Canary ve Development süreçlerini WARP profiline alır. Profil her çalıştırmada dar kapsamla yeniden üretilir. |
| Tarayıcıdan Discord web, otomatik sayfa veya tarayıcı bağlantısı davranışı | `#167`, `#127`, `#121`, `#97`, `#94` | Tarayıcı desteği yeni kurulumda varsayılan açık gelir. Tarayıcı modu açıkken Discord uygulaması ve desteklenen tarayıcı süreçleri birlikte kapsama alınır. Tarayıcı modu kapalı bağlı oturumda desteklenen tarayıcıların Discord'a düz internetten çıkmasını azaltan geçici firewall kapsamı uygulanır; bağlantı kapanınca bu geçici kapsam temizlenir ve Discorder'a ait Discord bağlantı koruması yeniden açılır. |
| IPTV, Ssport, Steam, Spotify, World of Trucks veya Discord dışı servis sorunları | `#165`, `#155`, `#118`, `#112` | Bu özel uygulamalar Discorder kapsamı dışındadır. Tarayıcı süreci içinde açılan diğer siteler WireSock'un süreç bazlı modeli nedeniyle aynı tarayıcı sürecinden geçebilir. |
| Roblox, Call of Duty ve diğer oyun gecikmesi veya bağlantı çakışmaları | `#170`, `#163`, `#107`, `#91`, `#84`, `#83` | Oyun modu yoktur. Oyun süreçleri `AllowedApps` listesine alınmaz. |
| Zapret, GoodbyeDPI, ByeDPI, ProxiFyre, servis kurulumu veya silinemeyen bileşenler | `#132`, `#126`, `#95`, `#82`, `#76`, `#74` | Bu motorlar repodan, uygulamadan ve yayın paketinden kaldırıldı. Discorder Zapret/GoodbyeDPI/ProxiFyre servisi kurmaz veya yönetmez. |
| DNS/DoH ayarının opsiyonel olması | `#88` | Discorder DNS veya DoH ayarı yapmaz; bu yüzden opsiyon ihtiyacı ortadan kalkar. |
| Şüpheli ikili dosya, VirusTotal ve kurucu güveni | `#80` | Repoya ve yayın arşivine WireSock kurucusu, wgcf ikilisi, WinDivert veya DPI aracı eklenmez. WireSock kurucusu çalışma zamanında SHA-256, Authenticode, yayıncı ve sürüm kontrolüyle kabul edilir. |
| Linux, macOS veya platform dışı paketleme | `#153`, `#128`, `#116`, `#98`, `#79`, `#78` | Bilinçli olarak uygulanmaz. Discorder Windows x64 odaklıdır. |
| WARP+ anahtarı, Zapret autohostlist, profil sistemi veya servis kontrolü gibi eski yüzeyi büyüten öneriler | `#157`, `#115`, `#82` | Ürün hedefi tek tuşlu Discord tünelidir. Eski çoklu motor/profil yüzeyi geri getirilmez. |
| Windows etkinleştirme, Discord kurulum dosyası bozulması veya içeriği boş/genel hata kayıtları | `#169`, `#162`, `#161`, `#160`, `#159`, `#156`, `#151`, `#150`, `#149`, `#148`, `#147`, `#146`, `#144`, `#142`, `#141`, `#139`, `#138`, `#136`, `#131`, `#125`, `#124`, `#123`, `#120`, `#114`, `#113`, `#111`, `#108`, `#106`, `#104`, `#101`, `#100`, `#96`, `#87`, `#85`, `#77`, `#75` | Discorder bu alanlarda sistem onarımı veya Discord kurucu desteği iddia etmez. Issue şablonları Discorder sürümü, Windows sürümü, Discord kanalı, WireSock sürümü ve tekrar üretme adımı ister. |

## Kalan Canlı Kabul Alanları

Bazı sorun türleri yalnızca kaynak kodla kapanmış sayılamaz; gerçek ağ ve internet sağlayıcı ortamında kabul testi gerektirir:

- Discord mesaj, profil ve medya yükleme davranışı.
- Farklı internet sağlayıcılarında bağlantı kararlılığı.
- WireSock kurulumundan sonra yeniden başlatma gerektiren makineler.
- Discord Stable, PTB, Canary ve Development kanallarında süreç kapsamı.
- Chrome, Edge, Firefox, Brave, Opera ve Vivaldi ile Discord web erişimi.

Bu yüzden yayın öncesi kabul testi şu sırayla yapılır:

1. `.\scripts\verify.ps1`
2. `.\scripts\build-release.ps1`
3. Yayın uygulamasını açma.
4. WireSock kurulum/onay akışı.
5. Discord Stable ile bağlanma.
6. Desteklenen tarayıcıyla Discord web erişimini kontrol etme.
7. Bağlantıyı kesip WireSock sürecinin kapandığını doğrulama.

## Ürün Dışı Talepler

Aşağıdaki talepler Discorder kapsamına alınmaz:

- Özel uygulama listesi.
- Oyun modu.
- Genel VPN modu.
- Alternatif DPI aşma motorları.
- Linux veya macOS desteği.

Bu sınırlar ürünün güvenilir kalması için bilinçli olarak korunur.
