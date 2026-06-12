# Discorder

🟣 **Discorder**, Windows üzerinde Discord uygulamasını WireSock VPN Client ve Cloudflare WARP üzerinden tünelleyen tek tuşlu bir VPN aracıdır. Discord web için desteklenen tarayıcı modu ayrıca açılabilir.

Amaç basit: **Bağlan** düğmesine basınca Discord uygulaması çalışsın, web modu açılmışsa Discord web de desteklenen tarayıcılarla çalışsın, **Bağlantıyı Kes** düğmesine basınca Discorder VPN sürecini kapatsın. Oyunlar, yayın uygulamaları, sistem DNS'i ve eski DPI motorları Discorder kapsamına girmez.

## 🎯 Ürün Sözü

- ✅ Varsayılan kapsam: Discord, Discord PTB, Discord Canary ve Discord Development.
- ✅ İsteğe bağlı web modu: Chrome, Edge, Firefox, Brave, Opera ve Vivaldi.
- ✅ Web modu kapalıyken bağlantı açık olsa bile desteklenen tarayıcılar için geçici Discord web engeli uygulanır; web modu açıksa Discord uygulaması ve desteklenen tarayıcılar birlikte tünellenir.
- ✅ Sistem DNS, DoH, proxy, görev zamanlayıcı ve kalıcı servis ayarı değiştirilmez.
- ✅ Discorder kapalıyken yönetilen Discord kilidi aktif kalır: hosts kilidi alan adlarını kapatır, Windows Firewall kuralı policy izin verirse çözülen IP'leri de bloklar.
- ✅ Eski DPI aşma motorları, oyun yönlendirmeleri ve paket filtre sürücüleri yoktur.
- ✅ WireSock ve wgcf ikili dosyaları repoya gömülmez; ilk kullanımda resmi kaynaklardan indirilip doğrulanır.
- ✅ WireSock kurucusu SHA-256, Authenticode imzası, yayıncı ve sürüm bilgisiyle doğrulanır.
- ✅ Yerel profil ve tanılama dosyaları `%LOCALAPPDATA%\Discorder` altında tutulur.
- ✅ Premium arka plan videosu arayüz sahnesinin sabit parçasıdır; yayın paketinde yerel dosya olarak gelir ve kullanıcı tarafından kapatılıp görsel düzen bozulmaz.
- ✅ İlk kurulum ve bağlantı adımları ana ekrandaki süreç çubuğunda izlenir.
- ✅ **Onar** işlemi ayarları, WireSock kurulumunu ve tanılama kayıtlarını koruyup profil, wgcf ve kurucu önbelleğini yeniden üretilecek hale getirir.
- ✅ **Tanılama** JSONL olay akışı, sağlık raporu, okunabilir özet ve aktif WireSock loglarını kilitlenmeden tek zip içinde hazırlar.
- ✅ İsteğe bağlı arka plan modu pencere kapansa bile Discorder'ı bildirim alanında çalıştırır.
- ✅ İsteğe bağlı başlangıç ayarı Discorder'ı oturum açılışında başlatır.
- ✅ **Temiz kaldır** işlemi Discorder kilidini, başlangıç kaydını, yerel veriyi ve Discorder'ın kurduğu WireSock VPN Client kurulumunu geri alır.

## 🧭 Kapsam Dışı

Discorder şunları bilinçli olarak yapmaz:

- ❌ Roblox, Steam, Spotify, IPTV veya özel uygulama tünelleme.
- ❌ Genel cihaz VPN'i.
- ❌ DNS değiştirme.
- ❌ GoodbyeDPI, ByeDPI, Zapret, ProxiFyre, WinDivert veya benzeri motorları çalıştırma.
- ❌ Discord kurma, onarma veya güncelleme.
- ❌ WireSock lisansını atlatma veya üçüncü taraf koşullarını gizleme.

## 🖥️ Uygulama Akışı

1. Discorder açılır.
2. **Bağlan** düğmesine basılır.
3. WireSock yüklü değilse resmi WireSock VPN Client x64 MSI kurucusu indirilir.
4. Kurucu doğrulanır ve Windows yönetici onayıyla başlatılır.
5. Cloudflare WARP profili `wgcf` ile üretilir.
6. Profil varsayılan olarak Discord uygulamalarını içerir; web modu açıksa desteklenen tarayıcı süreçleri de eklenir.
7. WireSock VPN Client `discord.conf` dosyasını `run -config` modeliyle çalıştırır.
8. Bağlanırken Discord VPN kilidi kaldırılır.
9. **Bağlantıyı Kes** düğmesi çalışan WireSock sürecini kapatır.
10. Kapanışta Discord alan adları yönetilen hosts kilidiyle, policy izin verirse Windows Firewall IP kuralıyla tekrar kilitlenir.

> Tarayıcı modu varsayılan olarak kapalıdır. Açıldığında WireSock kapsamı süreç bazlı çalışır: desteklenen tarayıcıdan Discord web'e girebilirsiniz; aynı tarayıcı sürecindeki diğer sekmeler de tarayıcı süreci üzerinden çalışır. Discorder kapalıyken VPN süreci çalışmaz ve Discorder'a ait yönetilen kilit Discord alan adlarını kapalı tutar.

## ⚙️ Gereksinimler

- Windows 10 1809 veya üzeri.
- x64 sistem.
- İlk kurulumda internet bağlantısı.
- Discorder açılırken ve ilk WireSock kurulumunda Windows yönetici onayı.
- Ticari veya kurumsal kullanımda geçerli WireSock lisansı.

> Discorder taşınabilir çalışır; kendi başına sürücü, servis veya kurucu paketlemez. WireSock VPN Client ayrı bir üçüncü taraf ürünüdür ve sürücüye erişmek için yönetici yetkisi ister.

## 🚀 Kullanım

1. Yayın arşivinden `Discorder-2.0.11-win-x64.zip` dosyasını indirin.
2. Zip içeriğini istediğiniz klasöre çıkarın.
3. `Discorder.exe` dosyasını çalıştırın ve Windows UAC onayını verin.
4. Discord web kullanacaksanız ana ekrandaki **Web modu** seçeneğini açın.
5. Pencere kapansa bile çalışmasını istiyorsanız **Arka plan** seçeneğini açın.
6. Windows oturumu açılınca başlamasını istiyorsanız **Başlangıçta çalış** seçeneğini açın.
7. **Bağlan** düğmesine basın.
8. İlk kurulum penceresinde WireSock ve Cloudflare WARP koşullarını okuyup onaylayın.
9. Windows UAC penceresi gelirse resmi WireSock kurulumuna izin verin.
10. Durum **BAĞLI** olduğunda seçilen kapsam tünellenir.

## 🛠️ Onar

Ana ekrandaki **Onar** düğmesi profil bozulması, eksik `wgcf` çıktısı veya yarım kalan kurucu önbelleği gibi durumlar için ayarları kaybetmeden temiz başlangıç sağlar:

- Çalışan Discorder tünelini kapatır.
- Discord kilidini güvenli kapalı duruma alır.
- `%LOCALAPPDATA%\Discorder\profiles`, `tools` ve `installers` klasörlerini temizler.
- Tarayıcı modu, arka planda çalışma, başlangıç, WireSock onayı, WireSock kurulum izi ve tanılama kayıtlarını korur.
- Sonraki **Bağlan** işleminde profil ve `wgcf` dosyaları yeniden üretilir.

## 🧾 Tanılama

Ana ekrandaki **Tanılama** düğmesi devops incelemesi için şu dosyaları üretir ve oluşan paketin klasörünü açar:

- `%LOCALAPPDATA%\Discorder\logs\events.jsonl`: uygulama, UI, kilit, profil ve tünel olayları.
- `%LOCALAPPDATA%\Discorder\logs\health.json`: son bilinen durum, sürüm, ortam ve redakte edilmiş path bilgisi.
- `%LOCALAPPDATA%\Discorder\logs\diagnostics.md`: okunabilir tanılama özeti.
- `%LOCALAPPDATA%\Discorder\logs\tunnel.log`: WireSock süreç çıktısı.
- `%LOCALAPPDATA%\Discorder\diagnostic-bundles\discorder-diagnostics-*.zip`: paylaşılabilir tanılama paketi.

Paket oluşturulurken loglar önce güvenli bir geçici kopyaya alınır. WireSock açıkken `tunnel.log` yazılıyor olsa bile paket üretimi beklenmeyen hata penceresine düşmez; okunamayan dosya varsa pakete `bundle-warnings.txt` eklenir.

## 🧹 Temiz Kaldırma

Ana ekrandaki **Temiz kaldır** düğmesi Discorder'ı taşınabilir uygulama mantığıyla sıfırlar:

- Çalışan Discorder tünelini kapatır.
- Discorder'ın hosts dosyasına eklediği yönetilen Discord kilidi bloğunu kaldırır.
- `Discorder.BlockDiscordDomains` Windows Firewall kuralını siler.
- Discorder'ın kurduğu WireSock VPN Client genel Windows kurulumunu settings ve marker izi üzerinden kaldırır.
- Discorder'a ait Windows başlangıç kaydını siler.
- `%LOCALAPPDATA%\Discorder` altındaki ayar, profil, wgcf, kurucu, marker, tanılama paketi ve log dosyalarını siler.
- İşlem bitince uygulamayı kapatır.

WireSock VPN Client Discorder'dan önce sistemde zaten kuruluysa korunur; Discorder yalnızca kendi ilk kurulumda başlattığı WireSock kurulumunu kaldırır.

## 🪪 Kod İmzalama ve SmartScreen

Windows SmartScreen'de **Publisher: Unknown publisher** uyarısının kalkması için `Discorder.exe` güvenilir bir Authenticode kod imzalama sertifikasıyla imzalanmalıdır. Manifest, ikon, uygulama adı veya repo ayarı bu publisher bilgisini tek başına düzeltemez.

Release workflow'u imzalamaya hazırdır:

- `DISCORDER_CODESIGN_PFX_B64`: OV/EV ya da kurumsal güvenilir kod imzalama sertifikasının PFX dosyası, Base64 olarak.
- `DISCORDER_CODESIGN_PFX_PASSWORD`: PFX parolası.

Bu secret'lar GitHub repo ayarlarına eklendiğinde tag yayını sırasında `Discorder.exe` ve yayın klasöründeki `.dll` dosyaları SHA-256 timestamp ile imzalanır. Secret yoksa paket bilinçli olarak imzasız üretilir ve release notunda SmartScreen uyarısı beklenebileceği yazılır.

Self-signed sertifika yalnızca yerel test içindir; public kullanıcılarda SmartScreen güveni sağlamaz.

## 🔐 Güvenlik Modeli

- WireSock kurucusu sabit SHA-256 değeriyle doğrulanır.
- Windows Authenticode zinciri ve yayıncı adı kontrol edilir.
- Beklenen WireSock sürümü: `1.4.7.1`.
- Beklenen yayıncı: `IP SMIRNOV VADIM VALERIEVICH`.
- TLS doğrulaması devre dışı bırakılmaz.
- Discord uygulaması ve isteğe bağlı tarayıcı kapsamı `AllowedApps` satırında testlerle kilitlenir.
- Web modu kapalı bağlantılarda desteklenen tarayıcıların Discord alan adlarına düz internetten çıkmasını azaltmak için bağlantı süresince geçici `Discorder.TunnelScope.Browsers` Windows Firewall kuralları yönetilir.
- Geniş uygulama adları, satır enjeksiyonu ve `Update.exe` gibi riskli kapsamlar reddedilir.
- Kapalı durum kilidi Discorder'ın marker bloğuyla hosts dosyasında, policy izin verirse `Discorder.BlockDiscordDomains` adlı Windows Firewall kuralında yönetilir.

## 🧪 Geliştirme

```powershell
dotnet build Discorder.sln --configuration Release
dotnet run --project tests\Discorder.Core.Tests --configuration Release
dotnet run --project tests\Discorder.Windows.Tests --configuration Release
dotnet run --project src\Discorder.App
```

Tam doğrulama:

```powershell
.\scripts\verify.ps1
```

Yayın paketi:

```powershell
.\scripts\build-release.ps1
```

Kapalı durumdaki Discord kilidini canlı Windows üzerinde doğrulamak
için PowerShell'i yönetici olarak açıp şunu çalıştırın:

```powershell
.\scripts\verify-firewall-lock.ps1
```

İsteğe bağlı ağ probu da isterseniz:

```powershell
.\scripts\verify-firewall-lock.ps1 -ProbeNetwork
```

Yayın exe'siyle canlı app + web kabul testi için PowerShell'i yönetici
olarak açıp şunu çalıştırın:

```powershell
.\scripts\smoke-live-connect.ps1
```

## 📦 Yayın İçeriği

Yayın arşivi yalnızca Discorder uygulamasını ve .NET bağımsız çalışma dosyalarını içerir. Şunlar bilerek pakete dahil edilmez:

- WireSock kurucusu veya WireSock ikili dosyaları.
- wgcf ikili dosyası.
- DPI aşma araçları.
- Üçüncü taraf sürücüler.
- Log, profil, hesap veya kullanıcı ayar dosyaları.

## 🧯 Sorun Bildirme

Ana forkta açık kalan issue sınıflarının Discorder'da nasıl ele alındığını görmek için [kaynak sorun denetimi](docs/kaynak-sorun-denetimi.md) sayfasına bakın.

Hata bildirirken şunları ekleyin:

- Discorder sürümü.
- Discord kullanım yolu: uygulama mı, tarayıcı mı.
- Windows sürümü.
- Discord kanalı: Stable, PTB, Canary veya Development.
- WireSock sürümü.
- İnternet sağlayıcısı.
- Tekrar üretme adımları.
- `%LOCALAPPDATA%\Discorder\logs` altındaki redakte edilmiş tanılama çıktısı.

Özel anahtar, `wgcf-account.toml`, WireGuard profil içeriği, token veya kişisel veri paylaşmayın.

## ⚖️ Üçüncü Taraf Notu

Discorder; Discord, Cloudflare veya WireSock tarafından üretilen, onaylanan ya da desteklenen resmi bir ürün değildir. İsimler ve markalar ilgili sahiplerine aittir.
