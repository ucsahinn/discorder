# Discorder Mimarisi

Discorder tek amaçlı bir Windows uygulamasıdır: Discord uygulamasını Cloudflare WARP tabanlı WireGuard profili üzerinden WireSock ile tünellemek. Discord web erişimi için desteklenen tarayıcı modu kullanıcı tarafından ayrıca açılır.

## Ana Kararlar

- Uygulama taşınabilir çalışır; kendi kurucusu, servis kaydı veya görev zamanlayıcısı yoktur. WireSock sürücüsüne erişmek için yönetici yetkisiyle açılır.
- WireSock ve wgcf repoya gömülmez.
- Sistem DNS'i değiştirilmez.
- Kalıcı paket filtre sürücüsü veya DPI aşma motoru çalıştırılmaz.
- Tünel kapsamı `AllowedApps` ile varsayılan olarak Discord uygulamalarına daraltılır.
- Desteklenen tarayıcı süreçleri yalnızca web modu açıldığında kapsama eklenir.
- Kapalı durumda Discorder'a ait yönetilen kilit Discord alan adlarına giden trafiği kapatır. Ana kilit hosts marker bloğudur; Windows Firewall policy izin verirse çözülen IP listesi de bloklanır.
- Profil, ayar ve log dosyaları `%LOCALAPPDATA%\Discorder` altında tutulur.

## Bileşenler

### `Discorder.App`

WPF arayüzünü, WireSock kurulum onay ekranını, Authenticode doğrulamasını ve Windows UAC ile kurucu başlatmayı içerir.

### `Discorder.Core`

Tünel yaşam döngüsü, Discord kapsamı, profil üretimi, indirme doğrulaması, WireSock hazırlığı ve süreç yönetimi burada yer alır.

### Testler

- `Discorder.Core.Tests`: Discord kapsamı, profil sertleştirme, WireSock hazırlığı, wgcf profil üretimi ve denetleyici yaşam döngüsünü doğrular.
- `Discorder.Windows.Tests`: WireSock imza doğrulaması ve kurulum onay penceresinin gerçek WPF görsel kontrolünü yapar.

## Bağlantı Akışı

1. Uygulama kapalı/boş durumdayken `Discorder.BlockDiscordDomains` kuralı etkin tutulur.
2. Kullanıcı **Bağlan** düğmesine basar.
3. Discorder kendi Discord firewall kilidini devre dışı bırakır.
4. WireSock kurulumu için kullanıcı onayı yoksa onay penceresi açılır.
5. WireSock kurulu değilse resmi kurucu indirilir.
6. Kurucu SHA-256, Authenticode, yayıncı ve sürüm kontrolünden geçer.
7. Kurucu Windows UAC ile sessiz modda çalıştırılır.
8. Kurulu WireSock komut satırı aracı bulunur ve imzası doğrulanır.
9. `wgcf` indirilir, doğrulanır ve Cloudflare WARP profili üretilir.
10. Profildeki eski veya geniş `AllowedApps` satırları kaldırılır.
11. Discord uygulamaları yeni `AllowedApps` satırına yazılır; web modu açıksa desteklenen tarayıcılar da eklenir.
12. WireSock VPN Client `run -config <discord.conf> -log-level error` komutuyla kullanıcı süreci olarak başlar.
13. Bağlantı kesilirken çalışan WireSock süreci sonlandırılır ve Discord firewall kilidi tekrar etkinleştirilir.

## WireSock Komut Satırı Uyumluluğu

WireSock VPN Client `1.4.7.1` kurulumunda güncel komut satırı yolu:

```text
C:\Program Files\WireSock VPN Client\bin\wiresock-client.exe
```

Discorder yalnızca resmi WireSock VPN Client komut satırı aracını kullanır. WireSock'un ayrı grafik arayüz/tray profili ana akışa dahil edilmez.

Güncel akış:

```text
wiresock-client.exe run -config <discord.conf> -log-level error
```

## Güvenlik Sınırları

- TLS doğrulaması devre dışı bırakılamaz.
- İndirilen her ikili dosya sabit hash ile doğrulanır.
- Kurulu WireSock komut satırı dosyası imza ve yayıncı kontrolünden geçmeden çalıştırılmaz.
- Discord uygulaması ve isteğe bağlı tarayıcı kapsamı dışındaki özel uygulama adları `AllowedApps` içine testlerle alınmaz.
- Virgül, satır sonu ve boş değer enjeksiyonu reddedilir.
- Discorder yalnızca kendi hosts marker bloğunu ve `Discorder.BlockDiscordDomains` Windows Firewall kuralını yönetir; servis veya görev zamanlayıcı kuralı eklemez.
- Kapalı durum kilidi Discord alan adlarını hedefler; Chrome veya Edge gibi tarayıcı süreçlerinin tamamını bloklamaz.

## Kapalı Durum VPN Kilidi

Discorder kapalıyken Discord'a doğrudan erişimi azaltmak için iki yönetilen katman kullanır:

```text
# BEGIN Discorder Discord kilidi
0.0.0.0 discord.com
::1 discord.com
...
# END Discorder Discord kilidi
```

Windows Firewall yerel kuralları policy tarafından uygulanıyorsa ek IP kuralı da kullanılır:

```text
Discorder.BlockDiscordDomains
```

Hosts kilidi Discord alan adlarını yerel döngü adresine yönlendirir. Firewall katmanı dışa giden trafiği, Discorder'ın başlangıçta çözdüğü Discord alan adı IP'leri üzerinden bloklar. Bağlantı açılırken hosts marker bloğu kaldırılır ve Firewall kuralı devre dışı bırakılır, WireSock tüneli çalışır. Bağlantı kesilirken veya uygulama kapanırken kilit tekrar etkinleştirilir.

Canlı doğrulama için `scripts/verify-firewall-lock.ps1` yönetici PowerShell oturumunda çalıştırılır. Script kilidi etkinleştirir, devre dışı bırakır ve finalde tekrar etkin bırakarak hosts marker durumunu ve Firewall kural durumunu raporlar. `-ProbeNetwork` seçeneği Discord'a TCP 443 bağlantısını da dener; ağ veya ISS tarafı Discord'a zaten ulaşamıyorsa bu prob kanıt olarak zorlanmaz.

## Tarayıcı Kapsamı

Desteklenen tarayıcı süreçleri:

- Chrome: `chrome.exe`
- Edge: `msedge.exe`
- Firefox: `firefox.exe`
- Brave: `brave.exe`
- Opera: `opera.exe`
- Vivaldi: `vivaldi.exe`

Tarayıcı modu varsayılan kapalıdır. Açıldığında WireSock süreç bazlı filtreleme kullandığı için tarayıcı içinde yalnızca tek bir sekmeyi ayırmak yerine desteklenen tarayıcı sürecini tüneller. Bu karar Discord web erişimini çalıştırmak için bilinçli olarak alınır; oyunlar, sistem DNS'i, kalıcı servisler ve eski DPI motorları yine kapsam dışındadır.

## Yayın Modeli

`scripts/build-release.ps1` doğrulama scriptini çalıştırır, Windows x64 bağımsız yayın çıktısı üretir ve `artifacts/Discorder-2.0.0-win-x64.zip` arşivini oluşturur.
