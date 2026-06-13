# Discorder Kullanım Rehberi

Discorder, Windows için portable bir Discord bağlantı yöneticisidir. ZIP içinden çalışır; klasik bir kurulum sihirbazı gerektirmez.

## Discorder Kimler İçin?

- Windows'ta Discord bağlantı sorunu yaşayan kullanıcılar.
- Discord uygulamasının bağlantısını sade bir arayüzle yönetmek isteyenler.
- Sistem genelinde VPN açmadan, dar kapsamlı bir bağlantı aracı kullanmak isteyenler.
- Tanılama paketiyle bağlantı sorununu daha anlaşılır raporlamak isteyenler.

Discorder resmi Discord ürünü değildir ve Discord hesabınızı yönetmez.

## İlk Çalıştırma

1. [GitHub Releases](https://github.com/ucsahinn/discorder/releases) sayfasından en güncel `Discorder-*-win-x64.zip` dosyasını indirin.
2. ZIP içeriğini örneğin `C:\Tools\Discorder` gibi kalıcı bir klasöre çıkarın.
3. `Discorder.exe` dosyasını çalıştırın.
4. İlk kurulum ekranında WireSock VPN Client ve Cloudflare WARP koşullarını okuyun.
5. **Kabul et ve kur** düğmesiyle devam edin.
6. Windows yönetici onayı gelirse resmi WireSock kurulumuna izin verin.
7. Ana ekranda **Bağlan** düğmesine basın.

Portable klasörü geçici indirme klasöründe bırakmamak daha sağlıklıdır. Güncelleme ve yeniden başlatma akışı mevcut çıkarılmış klasörü hedef alır.

## Ana Ekran

- **Bağlan**: Discord bağlantısını başlatır.
- **Tarayıcı modu**: Varsayılan açık gelir; Discord web gerekiyorsa desteklenen tarayıcıları da kapsama alır.
- **Arka planda çalıştır**: Pencere kapanınca uygulamanın bildirim alanında açık kalmasını sağlar.
- **Windows açılışında çalıştır**: Windows oturumu açıldığında Discorder'ı başlatır.
- **Tanılama**: Bağlantı sorunlarını incelemek için rapor hazırlar.
- **Onar**: Profil, araç ve kurulum önbelleğini yeniden hazırlanacak hale getirir.
- **Uygulamayı kaldır**: Discorder'ın yönettiği yerel ayarları ve kendi kurduğu bileşenleri geri alır.

## Tarayıcı Modu

Tarayıcı modu yeni kurulumda açık gelir. Böylece Discord web kullanan tarayıcılar da bağlantı kapsamına dahil olur.

Tarayıcı modu kapatılırsa Discorder yalnızca Discord masaüstü uygulamasına odaklanır. Tarayıcı süreci kapsamlı çalıştığı için açık modda aynı tarayıcıdaki diğer sekmelerin davranışı da tarayıcı sürecinden etkilenebilir.

## Tanılama Paketi

Tanılama paketi kullanıcıya görünen bağlantı durumunu ve teknik olayları tek arşivde toplar. Paylaşmadan önce kişisel veri içermediğini kontrol edin.

Paket genellikle şunları içerir:

- `events.jsonl`
- `errors.log`
- `health.json`
- `diagnostics.md`
- `runtime.json`
- `tunnel.log`
- `update.log`
- varsa `bundle-warnings.txt`

`runtime.json`; RAM, GC heap, handle, thread ve calisma suresi gibi sayisal performans metriklerini icerir. Token, sertifika, gizli anahtar veya ozel hesap bilgisi yazmaz. Yine de paketi paylasmadan once dosyalari gozden gecirin.

## Kapsam Dışı

Discorder şunları yapmaz:

- Discord hesabı açma, kapatma veya yönetme.
- Discord'u kurma ya da güncelleme.
- Sistem DNS'ini kalıcı değiştirme.
- Tüm cihaz trafiğini genel VPN gibi yönlendirme.
- Oyun, IPTV, yayın uygulaması veya özel uygulama tünelleme.
- WireSock lisans koşullarını atlatma.
