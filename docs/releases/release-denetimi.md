# Discorder Release ve Tag Denetimi

Son denetim: 2026-06-14

Bu doküman, GitHub Releases yüzeyindeki sürüm karmaşasını güvenli şekilde sınıflandırır. Canlı release ve tag silme işlemi yapılmadı. Release/tag/asset silme veya `--cleanup-tag` kullanımı yıkıcı işlem sayılır; bunun için ayrıca tam olarak `APPROVED - CLEAN RELEASES` onayı gerekir.

## İnceleme Kaynakları

- `gh release list --repo ucsahinn/discorder --limit 100`
- `gh release view <tag> --repo ucsahinn/discorder --json tagName,name,isDraft,isPrerelease,isImmutable,publishedAt,url,assets,targetCommitish`
- `git tag --list`
- `git ls-remote --tags origin`
- Yerel release notları ve build script'leri
- GitHub CLI ve GitHub Releases dokümantasyonu
- Semantic Versioning 2.0.0 kuralları

## Sürüm Kararı

Yeni public hedef `v2.1.0` olarak belirlendi.

Neden `v2.1.0`?

- `v2.0.30` zaten tag'lenmiş ve yayınlanmış durumda.
- Yeni değişiklik yalnızca dar bir hotfix değil; opt-in debug tanılama, ağ/performance gözlem katmanı, daha güvenli routing profile hash'i, tanılama UI ayarı ve release doğrulama kapısı ekliyor.
- Davranış geriye uyumlu: Discord-only ürün amacı korunuyor, tarayıcı modu yine kullanıcı tercihine bağlı.
- SemVer'e göre geriye uyumlu işlev eklemeleri minor sürüme uygundur; patch zincirini `v2.0.31` diye uzatmak yerine `v2.1.0` daha anlaşılırdır.

## Yayınlanmış Release Envanteri

| Release | Canlı durum | Asset durumu | Karar | Gerekçe | Risk | Komut/API aksiyonu |
| --- | --- | --- | --- | --- | --- | --- |
| `v2.1.0` | Yeni latest hedefi | 4 asset beklenir | Oluştur / latest yap | Enterprise debug tanılama ve stabilizasyon minor sürümü. | İmzasız build SmartScreen/AV uyarısı gösterebilir. | `gh release create v2.1.0 ... --latest` veya release workflow. |
| `v2.0.30` | Yayınlandı, eski latest | 4 asset | Koru / arşiv | Discord-only varsayılan kapsam, Tarayıcı modu opt-in, WireSock cleanup ve redaction hattı. | Latest olarak kalırsa `v2.1.0` kullanıcıya ulaşmaz. | `v2.1.0` latest yapılır; silme yok. |
| `v2.0.29` | Yayınlandı | 4 asset | Koru / arşiv | Discord updater döngüsü hotfix'i. | Eski kullanıcılar bu hotfix'e link vermiş olabilir. | Silme yok. |
| `v2.0.28` | Yayınlandı | 4 asset | Koru / arşiv | Discord güncelleme ekranı recovery düzeltmesi. | Link kırılması güven kaybı yaratır. | Silme yok. |
| `v2.0.27` | Yayınlandı | 2 asset | Koru / arşiv | Kaldırma temizliği ve Discord açılış yolu düzeltmeleri. | Eski indirme linkleri kırılabilir. | Silme yok. |
| `v2.0.26` | Yayınlandı | 2 asset | Koru / arşiv | Discord açılış doğrulaması. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.25` | Yayınlandı | 2 asset | Koru / arşiv | Tanılama metrikleri hotfix'i. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.24` | Yayınlandı | 2 asset | Koru / arşiv | Discord yenileme ve tünel uyumluluğu hotfix'i. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.23` | Yayınlandı | 2 asset | Koru / arşiv | Discord bağlantı doğrulama hotfix'i. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.22` | Yayınlandı | 2 asset | Koru / arşiv | Premium UI ve tarayıcı modu hotfix'i. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.21` | Yayınlandı | 2 asset | Koru / arşiv | Bellek, tanılama ve UI hotfix'i. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.20` | Yayınlandı | 2 asset | Koru / arşiv | Update log ve motion polish geçişi. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.19` | Yayınlandı | 2 asset | Koru / arşiv | Premium arayüz polish. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.18` | Yayınlandı | 2 asset | Koru / arşiv | Portable Windows Discord bağlantı yöneticisi hattı. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.17` | Yayınlandı | 2 asset | Koru / arşiv | WireSock ilk kurulum ve protected staging iyileştirmeleri. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.16` | Yayınlandı | 2 asset | Koru / arşiv | Görünür güncelleme penceresi ve helper staging düzeltmesi. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.15` | Yayınlandı | 2 asset | Koru / arşiv | Tarayıcı modu kapsamı, DNS retry ve kapanış hızı düzeltmeleri. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.14` | Yayınlandı | 2 asset | Koru / arşiv | Sertifikasız ama hash/manifest doğrulamalı update hattı. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.13` | Tag var, canlı release yok | Release asset yok | Tag-only / silme adayı | Workflow paket üretiminde durduğu için release oluşmamış. | Tag silmek geçmişi değiştirir. | Ayrı onay olursa `gh release delete v2.0.13 --cleanup-tag` değil; önce tag-only kararını ayrıca doğrula. |
| `v2.0.12` | Tag var, canlı release yok | Release asset yok | Tag-only / silme adayı | Yayın oluşmamış ara tag. | Tag silmek geçmişi değiştirir. | Ayrı onay olursa remote tag cleanup ayrıca planlanır. |
| `v2.0.11` | Yayınlandı | 2 asset | Koru / arşiv | Erken stabil yayın hattı. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.10` | Yayınlandı | 2 asset | Koru / arşiv | v2.0.11 öncesi geçiş yayını. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.9` | Yayınlandı | 2 asset | Koru / arşiv | İlk büyük paket büyümesi ve release hattı. | Yerelde çok sayıda tekrar artifact var; yanlış upload riski. | Public release silme yok; yerel artifact cleanup ayrı onay ister. |
| `v2.0.8` | Yayınlandı | 2 asset | Koru / arşiv | Erken geçiş yayını. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.7` | Yayınlandı | 2 asset | Koru / arşiv | Erken release doğrulama hattı. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.6` | Yayınlandı | 2 asset | Koru / arşiv | Erken public release. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.5` | Yayınlandı | 2 asset | Koru / arşiv | Erken public release. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.4` | Canlı release yok, remote tag yok | Yerel artifact var | Yerel cleanup adayı | Public yüzeyde sürüm yok, yalnızca yerel kalıntı görünüyor. | Yerel artifact silmek de cleanup sayılır. | Ayrı cleanup onayı olmadan işlem yok. |
| `v2.0.3` | Yayınlandı | 2 asset | Koru / arşiv | Erken public release. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.2` | Yayınlandı | 2 asset | Koru / arşiv | Erken public release. | Eski kullanıcı linkleri kırılabilir. | Silme yok. |
| `v2.0.1` | Tag var, canlı release yok | Yerel not/artifact var | Tag-only / silme adayı değil | İlk patch tag'i; canlı release listesinde yok. | Tag silmek geçmişi değiştirir. | Silme yok. |
| `v2.0.0` | Yayınlandı | 2 asset | Koru | 2.x hattının başlangıç noktası. | Silmek geçmişi gereksiz kırar. | Silme yok. |

## Cleanup Kararı

Önerilen güvenli politika:

- Public release'leri silme: eski indirme linklerini kırar ve güven geçmişini zedeler.
- Latest işaretini sadece `v2.1.0` üzerinde tut.
- Tag-only kalan `v2.0.12` ve `v2.0.13` için silme yalnızca ayrıca onaylanırsa yapılmalı.
- Yerel `artifacts/` altındaki eski ZIP/log/smoke dosyaları public release'e yüklenmemeli; silme/temizleme için ayrı cleanup onayı gerekir.
- Eski release asset'leri `--clobber` ile değiştirilmemeli. Değişiklik yeni sürümle yayınlanmalı.

## Yayın Kapısı

`v2.1.0` yayınlanmadan önce şu koşullar gerekir:

- Proje ve updater sürümü `2.1.0` ile aynı olmalı.
- `v2.1.0` tag'i commit sonrası oluşturulmalı ve remote'a pushlanmalı.
- Release build `Discorder-2.1.0-win-x64.zip` ve `Discorder-win-x64.zip` üretmeli.
- `.sha256.txt` dosyaları ZIP ile eşleşmeli.
- `scripts\verify.ps1`, `git diff --check`, Gitleaks ve release packaging geçmeli.
- Release body `docs/releases/v2.1.0.md` ile uyumlu olmalı.
