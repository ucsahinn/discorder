# Discorder Release ve Tag Denetimi

Bu tablo, v2.0.20 public yüzeyi hazırlanırken çıkarılan non-destructive release temizliği planıdır. GitHub Release veya tag silme işlemi yapılmadı. Silme/cleanup için ayrıca `APPROVED - CLEAN RELEASES` onayı gerekir.

## İnceleme Kaynakları

- Yerel git tag listesi.
- Remote tag listesi.
- Yerel `artifacts/` klasöründeki ZIP, hash ve release note dosyaları.
- Repo release workflow ve build script'leri.

Aşağıdaki kararlar, erişilebilen tag ve artifact kanıtına göre hazırlanmış güvenli önerilerdir. Herhangi bir release veya tag silme kararı, canlı GitHub Release listesi release sahibi tarafından ayrıca doğrulandıktan sonra uygulanmalıdır.

## Cleanup Tablosu

| Release/tag | Öneri | Gerekçe | Risk | Komut/API aksiyonu |
| --- | --- | --- | --- | --- |
| `v2.0.20` | Oluştur / latest yap | Update indirme progress logları seyreltilir, tanılama dosyası şişmesi azaltılır, logo mikro animasyonu ve footer buton polish tamamlanır. | Authenticode yoksa kullanıcı Windows SmartScreen uyarısı görebilir. | GitHub Release workflow veya `gh release create v2.0.20 ... --latest`. |
| `v2.0.19` | Koru | Ana ekran premium görsel polish, daha güçlü header/logo, bağlantı dairesi, kart derinliği ve canlı durum mikro-kartları içerir. | Latest olarak kalırsa v2.0.20 update log/performance düzeltmesi kullanıcıya ulaşmaz. | v2.0.20 yayınlanınca latest güncellenir. |
| `v2.0.18` | Koru | WireSock sonrası Cloudflare WARP aracı ve profil hazırlığı artık canlı durum, retry, max size ve sade hata mesajları içerir. | Latest olarak kalırsa v2.0.20 düzeltmeleri kullanıcıya ulaşmaz. | v2.0.20 yayınlanınca latest güncellenir. |
| `v2.0.17` | Koru | WireSock ilk kurulum indirmesi bağlantı kurma, retry, boyut ilerlemesi, max size ve protected staging davranışı içerir. | Latest olarak kalırsa v2.0.20 düzeltmeleri kullanıcıya ulaşmaz. | v2.0.20 yayınlanınca latest güncellenir. |
| `v2.0.16` | Koru | Görünür güncelleme penceresi, helper staging düzeltmesi ve target reparse guard bu sürümde yayınlandı. | Latest olarak kalırsa v2.0.20 düzeltmeleri kullanıcıya ulaşmaz. | v2.0.20 yayınlanınca latest güncellenir. |
| `v2.0.15` | Koru | Tanılama loglarından çıkan Tarayıcı modu kapalı kapsamı, kötü internet/DNS retry davranışı ve kapanış hızı düzeltmeleri bu sürümde yayınlandı. | Latest olarak kalırsa sonraki runtime düzeltmeleri kullanıcıya ulaşmaz. | v2.0.20 yayınlanınca latest güncellenir. |
| `v2.0.14` | Koru | Sertifikasız ama GitHub digest, SHA-256 ve manifest doğrulamalı otomatik update modu bu sürümde yayınlandı. | Latest olarak kalırsa sonraki runtime düzeltmeleri kullanıcıya ulaşmaz. | Latest v2.0.20 olmalı. |
| `v2.0.13` | Koru / release oluşturma | Tag pushlandı fakat workflow test çalıştırma yolu yüzünden paket üretiminde durdu; tag taşımak force/destructive olacağı için korunur. | Eski tag release'siz kalır; latest v2.0.20 ile düzeltilir. | Silme veya retag yok. |
| `v2.0.12` | Koru / release oluşturma | Tag daha önce pushlandı fakat release oluşmadı. Tag silmek destructive olduğundan korunur. | Eski tag release'siz kalır; latest v2.0.20 ile düzeltilir. | Silme yok. |
| `v2.0.11` | Koru | Remote ve local tag mevcut; önceki stabil yayın hattı. | Latest olarak kalırsa README v2.0.20 hazırlığıyla çelişebilir. | v2.0.20 yayınlanınca latest otomatik/manuel güncellenir. |
| `v2.0.10` | Koru / release notunu eski olarak bırak | v2.0.11 öncesi anlamlı geçiş sürümü olabilir. | Canlı release body görülmeden silmek yanlış olabilir. | Silme yok. |
| `v2.0.9` | Koru, local artifact tekrarlarını temizleme adayı | `artifacts/` altında `complete`, `fix`, `complete2`, `complete3` gibi local tekrarlar var. | Public release silinirse indirme geçmişi ve güven kaybı oluşabilir. | Release silme yok; local artifact cleanup ayrı onay gerektirir. |
| `v2.0.8` | Koru / arşiv olarak bırak | 2.0.9 öncesi tag mevcut. | Canlı release durumu bilinmiyor. | Silme yok. |
| `v2.0.7` | Koru / arşiv olarak bırak | Tag ve release-check kalıntısı var. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.6` | Koru / arşiv olarak bırak | Tag ve artifact mevcut. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.5` | Koru / arşiv olarak bırak | Tag ve artifact mevcut. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.4` | Tag yok, local artifact var | Local artifact karmaşası; public tag görünmüyor. | Silme local dosya temizliği sayılır ve ayrıca onay ister. | Şimdilik işlem yok. |
| `v2.0.3` | Koru / arşiv olarak bırak | Tag ve artifact mevcut. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.2` | Koru / arşiv olarak bırak | Tag ve artifact mevcut. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.1` | Koru / arşiv olarak bırak | Tag, artifact ve release note mevcut. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.0` | Koru | 2.x hattının başlangıç noktası. | Silmek geçmişi gereksiz kırar. | Silme yok. |

## Sürüm Kararı

Bu public yüzey için anlamlı SemVer sürümü `v2.0.20` olarak belirlendi.

Neden patch?

- Ürün adı, portable model ve çalışma kapsamı değişmedi.
- Kullanıcıya görünen UI dili, güncelleme akışı, bağlantı kapsamı, kötü internet davranışı ve güvenlik doğrulama ayrıntıları iyileştirildi.
- Breaking change veya yeni major/minor ürün hattı yok.

## Yayın Kapısı

v2.0.20 GitHub Release yayınlanmadan önce şu koşullar gerekir:

- GitHub yayın yetkisi geçerli olmalı.
- `v2.0.20` tag'i doğru commit'e işaret etmeli.
- Release workflow veya local build `Discorder-2.0.20-win-x64.zip` üretmeli.
- `.sha256.txt` dosyası ZIP ile eşleşmeli.
- Secret scan temiz olmalı.
- Release body `docs/releases/v2.0.20.md` ile uyumlu olmalı.
