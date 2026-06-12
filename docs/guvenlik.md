# Discorder Güvenlik Sınırları

Discorder'ın güvenlik modeli dar kapsam, doğrulanmış indirme ve kullanıcı onayı üzerine kuruludur.

## Güven Sınırı

Discorder şu bileşenlere güvenir:

- Windows işletim sistemi, GitHub Releases ve varsa Authenticode doğrulaması.
- GitHub Releases üzerinden yayınlanan Discorder paketleri.
- Resmi WireSock VPN Client kurucusu.
- Cloudflare WARP profil üretimi için kullanılan wgcf akışı.

Bu bileşenlerden gelen içerik doğrulanmadan uygulanmaz.

## İndirilen Dosyalar

WireSock kurucusu resmi kaynaktan alınır ve şu kontrollerden geçer:

- SHA-256 hash doğrulaması.
- Authenticode imza zinciri.
- Beklenen yayıncı adı.
- Beklenen ürün ve sürüm bilgisi.

Discorder'ın uygulama içi otomatik güncelleme paketleri için GitHub release yolu, GitHub asset digest, SHA-256 dosyası ve manifest kontrolleri kullanılır. Kod imzalama sertifikası varsa Authenticode kontrolü ayrıca devreye alınabilir. Sertifika yoksa güven sınırı GitHub release yetkisi ve yayınlanan doğrulama verileridir; bu, imzalı yayıncı kimliğiyle aynı güven seviyesini sağlamaz. Manuel indirilen GitHub Release ZIP'lerinde kullanıcı yayınlanan SHA-256 dosyasını kontrol etmelidir.

## Ne Yapmaz?

Discorder bilinçli olarak şunları yapmaz:

- TLS doğrulamasını kapatma.
- İmza veya hash hatasını yoksayma.
- WireSock ya da wgcf ikili dosyalarını repoya gömme.
- Discord dışı uygulamaları sessizce kapsama alma.
- Sistem DNS'ini kalıcı değiştirme.
- Kullanıcının gizli profil veya hesap dosyalarını release paketine ekleme.

## Kullanıcı Verisi

Yerel ayar, log, profil ve tanılama dosyaları kullanıcının makinesinde kalır:

```text
%LOCALAPPDATA%\Discorder
```

Tanılama paketi paylaşmadan önce özel anahtar, token, cookie, tam profil dosyası veya kişisel veri içermediğini kontrol edin.

## Public Screenshot ve Doküman Kuralı

Repo görselleri ve dokümanları şu verileri içermemelidir:

- Yerel kullanıcı adı veya tam yerel yol.
- Token, cookie, private key veya bağlantı dizesi.
- Redakte edilmemiş log.
- Gizli IP, müşteri verisi veya hesap bilgisi.

Bu nedenle README görsellerinde yalnızca ürün arayüzü ve genel DNS sağlayıcı örnekleri gösterilir; yerel yol, hesap, token, log veya kişisel veri yer almaz.

## Güvenlik Açığı Bildirme

Güvenlik açığını public issue olarak paylaşmayın. GitHub Security Advisory kullanın:

<https://github.com/ucsahinn/discorder/security/advisories/new>
