# Güvenlik Politikası

Discorder dar kapsamlı bir Windows aracıdır: yalnızca Discord trafiğini tüneller ve sistem ağ ayarlarını kalıcı olarak değiştirmez.

## Desteklenen Sürüm

Yalnızca `main` dalı ve en son yayınlanan sürüm güvenlik düzeltmesi alır.

## Güvenlik Açığı Bildirimi

Güvenlik açıklarını herkese açık bildirim olarak paylaşmayın. Şu durumlarda GitHub özel güvenlik bildirimi kullanın:

<https://github.com/ucsahinn/discorder/security/advisories/new>

Özellikle şunları gizli bildirin:

- İmza, hash veya yayıncı doğrulamasını atlatan açıklar.
- WireSock veya wgcf indirme zincirinde bütünlük zayıflığı.
- Discord dışı uygulamaların tünele alınmasına yol açan hatalar.
- Gizli profil, hesap veya anahtar sızıntısı.
- TLS doğrulamasını zayıflatan regresyonlar.

## Gizli Veri Paylaşmayın

Bildirimlere şunları eklemeyin:

- WireGuard özel anahtarı.
- `wgcf-account.toml`.
- Token, cookie, bağlantı dizesi veya kişisel veri.
- Tam profil dosyası.
- Redakte edilmemiş log.

## Güvenlik Sınırları

- WireSock VPN Client `1.4.7.1` yalnızca resmi indirme noktasından alınır.
- Kurucu sabit SHA-256 değeriyle doğrulanır.
- Authenticode imzası, yayıncı, MSI ürün adı ve sürüm bilgisi kontrol edilir.
- WireSock ve Cloudflare WARP koşulları kullanıcı onayı olmadan kabul edilmiş sayılmaz.
- Repoda ve yayın arşivinde WireSock veya wgcf ikili dosyası bulunmaz.
- Üretilen hesap ve profil dosyaları `%LOCALAPPDATA%\Discorder` altında kalır.

## Yerel Veri Temizliği

Discorder ayarlarını, loglarını ve oluşturulan profilleri silmek için `%LOCALAPPDATA%\Discorder` klasörünü kaldırabilirsiniz. WireSock ayrı bir Windows uygulamasıdır; kaldırmak için Windows Ayarları üzerinden işlem yapın.
