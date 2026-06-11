# Discorder Mimarisi

Discorder tek amaçlı bir Windows uygulamasıdır: Discord uygulamasını ve Discord web erişimi için desteklenen tarayıcıları Cloudflare WARP tabanlı WireGuard profili üzerinden WireSock ile tünellemek.

## Ana Kararlar

- Uygulama taşınabilir çalışır; kendi kurucusu, servis kaydı veya görev zamanlayıcısı yoktur. WireSock sürücüsüne erişmek için yönetici yetkisiyle açılır.
- WireSock ve wgcf repoya gömülmez.
- Sistem DNS'i değiştirilmez.
- Kalıcı paket filtre sürücüsü veya DPI aşma motoru çalıştırılmaz.
- Tünel kapsamı `AllowedApps` ile Discord uygulamalarına ve desteklenen tarayıcı süreçlerine daraltılır.
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

1. Kullanıcı **Bağlan** düğmesine basar.
2. WireSock kurulumu için kullanıcı onayı yoksa onay penceresi açılır.
3. WireSock kurulu değilse resmi kurucu indirilir.
4. Kurucu SHA-256, Authenticode, yayıncı ve sürüm kontrolünden geçer.
5. Kurucu Windows UAC ile sessiz modda çalıştırılır.
6. Kurulu WireSock komut satırı aracı bulunur ve imzası doğrulanır.
7. `wgcf` indirilir, doğrulanır ve Cloudflare WARP profili üretilir.
8. Profildeki eski veya geniş `AllowedApps` satırları kaldırılır.
9. Discord uygulamaları ve desteklenen tarayıcılar yeni `AllowedApps` satırına yazılır.
10. WireSock VPN Client `run -config <discord.conf> -log-level error` komutuyla kullanıcı süreci olarak başlar.
11. Bağlantı kesilirken çalışan WireSock süreci sonlandırılır.

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
- Discord uygulaması ve desteklenen tarayıcılar dışındaki özel uygulama adları `AllowedApps` içine testlerle alınmaz.
- Virgül, satır sonu ve boş değer enjeksiyonu reddedilir.
- Discorder kapalıyken kalıcı firewall, DNS, servis veya görev zamanlayıcı kuralı bırakılmaz; kapalı durum VPN sürecinin durduğu anlamına gelir.

## Tarayıcı Kapsamı

Desteklenen tarayıcı süreçleri:

- Chrome: `chrome.exe`
- Edge: `msedge.exe`
- Firefox: `firefox.exe`
- Brave: `brave.exe`
- Opera: `opera.exe`
- Vivaldi: `vivaldi.exe`

WireSock süreç bazlı filtreleme kullandığı için tarayıcı içinde yalnızca tek bir sekmeyi ayırmak yerine desteklenen tarayıcı sürecini tüneller. Bu karar Discord web erişimini çalıştırmak için bilinçli olarak alınır; oyunlar, sistem DNS'i, kalıcı servisler ve eski DPI motorları yine kapsam dışındadır.

## Yayın Modeli

`scripts/build-release.ps1` doğrulama scriptini çalıştırır, Windows x64 bağımsız yayın çıktısı üretir ve `artifacts/Discorder-2.0.0-win-x64.zip` arşivini oluşturur.
