# Discorder Güncelleme ve Portable ZIP Davranışı

Discorder portable ZIP olarak çalışır. Bu yüzden güncelleme akışı, klasik kurulum dizini yerine kullanıcının çıkardığı mevcut Discorder klasörünü hedef alır.

GitHub Releases üzerinden indirilen ZIP paketi manuel kullanım içindir. Uygulama içi otomatik güncelleme, aynı paketi GitHub release yolu, asset digest, SHA-256 dosyası ve manifest eşleşmesiyle doğrular.

## Kullanıcının Gördüğü Akış

1. Kullanıcı **Güncelle** düğmesine basar.
2. Discorder GitHub Releases üzerinden yeni sürüm olup olmadığını denetler.
3. Yeni sürüm yoksa sakin bir "güncel" durumu gösterilir.
4. Yeni sürüm varsa ayrı **Yükle** düğmesi görünür.
5. **Yükle** düğmesi, az önce denetlenip bulunan sürümü indirir.
6. Paket doğrulanır.
7. Discorder güncelleme için hazırlanır.
8. Uygulama kapanır, dosyalar güvenli şekilde değiştirilir ve `Discorder.exe` yeniden başlatılır.

## İndirme ve Staging

Güncelleme paketi doğrudan uygulama klasörünün üstüne açılmaz. Önce yöneticiye özel staging alanına alınır:

```text
%PROGRAMDATA%\Discorder\updates
```

Bu alan, yarım inen veya doğrulanamayan paketlerin çalışan portable klasörü bozmasını engeller.

## Doğrulama

Güncelleme uygulanmadan önce şu kontroller yapılır:

- GitHub release asset adı beklenen `Discorder-<version>-win-x64.zip` biçimiyle eşleşir.
- Asset GitHub HTTPS indirme yolundan gelir.
- Asset boyutu ve durum bilgisi beklenen sınırlar içindedir.
- GitHub `sha256:` digest bilgisi ve `.sha256.txt` dosyası ZIP ile eşleşir.
- ZIP içinde `discorder.update-manifest.json` bulunur.
- Manifest sürümü, denetlenen release sürümüyle eşleşir.
- Kod imzalama varsa Authenticode imzası ayrıca korunur; sertifika yoksa güncelleme GitHub release yolu, asset digest, SHA-256 ve manifest doğrulamasıyla ilerler.

Bu kontrollerden biri başarısız olursa güncelleme uygulanmaz.

## Portable Klasör Hedefi

Güncelleme, çalışmakta olan `Discorder.exe` dosyasının bulunduğu çıkarılmış klasöre uygulanır. Kullanıcı uygulamayı hangi klasörden çalıştırıyorsa güncelleme hedefi de odur.

Örnek:

```text
C:\Tools\Discorder\Discorder.exe
```

Bu durumda güncelleme `C:\Tools\Discorder` klasörünü hedef alır.

## Güvenli Değiştirme ve Geri Dönüş

Çalışan executable Windows tarafından kilitlenebileceği için dosya değişimi ayrı updater süreciyle yapılır.

Güncelleme sırasında:

- Yeni dosyalar staging alanında hazırlanır.
- Mevcut uygulama klasörü değiştirilmeden önce yedeklenir.
- Hata oluşursa önceki dosyalar geri taşınmaya çalışılır.
- Başarılı olursa yeni `Discorder.exe` başlatılır.

Amaç, indirme veya doğrulama hatasının çalışan uygulama klasörünü kullanılmaz hale getirmemesidir. Dosya kilidi, antivirüs/EDR veya yetki problemi geri yüklemeyi de engellerse kullanıcı doğrulanmış ZIP paketini yeniden çıkararak kurtarabilir.

## Kısayol ve Başlangıç Kaydı

Discorder portable klasörü yerinde güncellendiği için mevcut kısayollar ve Windows başlangıç kaydı aynı `Discorder.exe` yolunu kullanmaya devam eder.

Kullanıcı klasörü elle taşırsa kısayol veya başlangıç kaydını yeniden oluşturması gerekebilir.

## Bilinen Sınırlar

- Kod imzalama yoksa paket imzasız yayınlanabilir; bu durumda güven sınırı GitHub yayın yetkisi, release yolu, asset digest, SHA-256 dosyası ve manifest doğrulamasıdır.
- GitHub digest, SHA-256 dosyası veya manifest doğrulaması geçmeyen paketler uygulanmaz.
- GitHub erişimi olmayan ağlarda güncelleme denetimi başarısız olabilir; uygulama mevcut sürümle çalışmaya devam eder.
- Çok yavaş bağlantılarda indirme zaman aşımı yaşanırsa kullanıcı daha sonra tekrar deneyebilir.
