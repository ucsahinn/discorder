# Discorder

🟣 **Discorder**, Windows üzerinde Discord uygulamasını WireSock VPN Client ve Cloudflare WARP üzerinden tünelleyen tek tuşlu bir VPN aracıdır. Discord web için desteklenen tarayıcı modu ayrıca açılabilir.

Amaç basit: **Bağlan** düğmesine basınca Discord uygulaması çalışsın, web modu açılmışsa Discord web de desteklenen tarayıcılarla çalışsın, **Bağlantıyı Kes** düğmesine basınca Discorder VPN sürecini kapatsın. Oyunlar, yayın uygulamaları, sistem DNS'i ve eski DPI motorları Discorder kapsamına girmez.

## 🎯 Ürün Sözü

- ✅ Varsayılan kapsam: Discord, Discord PTB, Discord Canary ve Discord Development.
- ✅ İsteğe bağlı web modu: Chrome, Edge, Firefox, Brave, Opera ve Vivaldi.
- ✅ Sistem DNS, DoH, proxy, görev zamanlayıcı ve kalıcı servis ayarı değiştirilmez.
- ✅ Discorder kapalıyken Windows Firewall üzerinde yalnızca Discord alan adlarını kilitleyen yönetilen VPN kuralı aktif kalır.
- ✅ Eski DPI aşma motorları, oyun yönlendirmeleri ve paket filtre sürücüleri yoktur.
- ✅ WireSock ve wgcf ikili dosyaları repoya gömülmez; ilk kullanımda resmi kaynaklardan indirilip doğrulanır.
- ✅ WireSock kurucusu SHA-256, Authenticode imzası, yayıncı ve sürüm bilgisiyle doğrulanır.
- ✅ Yerel profil ve tanılama dosyaları `%LOCALAPPDATA%\Discorder` altında tutulur.

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
10. Kapanışta Discord alan adları Windows Firewall üzerinden tekrar kilitlenir.

> Tarayıcı modu varsayılan olarak kapalıdır. Açıldığında WireSock kapsamı süreç bazlı çalışır: desteklenen tarayıcıdan Discord web'e girebilirsiniz; aynı tarayıcı sürecindeki diğer sekmeler de tarayıcı süreci üzerinden çalışır. Discorder kapalıyken VPN süreci çalışmaz ve Discorder'a ait Windows Firewall kilidi Discord alan adlarını kapalı tutar.

## ⚙️ Gereksinimler

- Windows 10 1809 veya üzeri.
- x64 sistem.
- İlk kurulumda internet bağlantısı.
- Discorder açılırken ve ilk WireSock kurulumunda Windows yönetici onayı.
- Ticari veya kurumsal kullanımda geçerli WireSock lisansı.

> Discorder taşınabilir çalışır; kendi başına sürücü, servis veya kurucu paketlemez. WireSock VPN Client ayrı bir üçüncü taraf ürünüdür ve sürücüye erişmek için yönetici yetkisi ister.

## 🚀 Kullanım

1. Yayın arşivinden `Discorder-2.0.0-win-x64.zip` dosyasını indirin.
2. Zip içeriğini istediğiniz klasöre çıkarın.
3. `Discorder.exe` dosyasını çalıştırın ve Windows UAC onayını verin.
4. **Bağlan** düğmesine basın.
5. Discord web kullanacaksanız ana ekrandaki **Discord web** seçeneğini açın.
6. İlk kurulum penceresinde WireSock ve Cloudflare WARP koşullarını okuyup onaylayın.
7. Windows UAC penceresi gelirse resmi WireSock kurulumuna izin verin.
8. Durum **AÇIK** olduğunda seçilen kapsam tünellenir.

## 🔐 Güvenlik Modeli

- WireSock kurucusu sabit SHA-256 değeriyle doğrulanır.
- Windows Authenticode zinciri ve yayıncı adı kontrol edilir.
- Beklenen WireSock sürümü: `1.4.7.1`.
- Beklenen yayıncı: `IP SMIRNOV VADIM VALERIEVICH`.
- TLS doğrulaması devre dışı bırakılmaz.
- Discord uygulaması ve isteğe bağlı tarayıcı kapsamı `AllowedApps` satırında testlerle kilitlenir.
- Geniş uygulama adları, satır enjeksiyonu ve `Update.exe` gibi riskli kapsamlar reddedilir.
- Kapalı durum kilidi `Discorder.BlockDiscordDomains` adlı Windows Firewall kuralıyla yönetilir.

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
