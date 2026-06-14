# Discorder Release ve Tag Denetimi

Son denetim: 2026-06-14
Son cleanup: 2026-06-14

Bu doküman, GitHub Releases yüzeyindeki sürüm karmaşasını güvenli şekilde sınıflandırır. `APPROVED - CLEAN RELEASES` onayı sonrası ara deneme/hotfix yayınları temizlendi ve public release yüzeyi anlamlı milestone sürümlere indirildi.

Release/tag/asset silme veya `--cleanup-tag` kullanımı yıkıcı işlem sayılır. Bu işlem sadece 2026-06-14 tarihinde verilen açık cleanup onayı kapsamında yapıldı.

## İnceleme Kaynakları

- `gh release list --repo ucsahinn/discorder --limit 100`
- `gh release view <tag> --repo ucsahinn/discorder --json tagName,name,isDraft,isPrerelease,isImmutable,publishedAt,url,assets,targetCommitish`
- `git tag --list`
- `git ls-remote --tags origin`
- Yerel release notları ve build script'leri
- GitHub CLI ve GitHub Releases dokümantasyonu
- Semantic Versioning 2.0.0 kuralları

## Sürüm Kararı

Yeni patch hedef `v2.1.1` olarak belirlendi.

Neden `v2.1.1`?

- `v2.1.0` hâlâ anlamlı minor baseline olarak korunur.
- Yeni değişiklikler geriye uyumlu hotfix niteliğindedir: legacy tarayıcı modu migration, doğrulanmış yerel WireSock kurucu fallback'i ve Discord updater fallback launch planı.
- Ana ürün amacı değişmedi; `BAĞLAN` yine Discord bağlantısını yönetir, tarayıcı kapsamı kullanıcı tercihine bağlıdır.

Önceki minor public hedef `v2.1.0` olarak belirlenmişti.

Neden `v2.1.0`?

- `v2.0.30` zaten tag'lenmiş ve yayınlanmış durumdaydı.
- Yeni değişiklik yalnızca dar bir hotfix değil; opt-in debug tanılama, ağ/performance gözlem katmanı, daha güvenli routing profile hash'i, tanılama UI ayarı ve release doğrulama kapısı ekliyor.
- Davranış geriye uyumlu: Discord-only ürün amacı korunuyor, tarayıcı modu yine kullanıcı tercihine bağlı.
- SemVer'e göre geriye uyumlu işlev eklemeleri minor sürüme uygundur; patch zincirini `v2.0.31` diye uzatmak yerine `v2.1.0` daha anlaşılırdır.

## Cleanup Sonrası Public Release Yüzeyi

| Release | Canlı durum | Asset durumu | Karar | Gerekçe | Not |
| --- | --- | --- | --- | --- | --- |
| `v2.1.1` | Hazırlanıyor | 4 asset bekleniyor | Yayınla | Bağlantı hazırlığı, tarayıcı modu ve Discord launch hotfix'i. | Yeni Latest hedefi. |
| `v2.1.0` | Yayında | 4 asset | Koru | Enterprise debug tanılama ve stabilizasyon minor sürümü. | Önceki minor baseline. |
| `v2.0.30` | Yayında | 4 asset | Koru | Discord-only bağlantı stabilizasyonu ve varsayılan kapsam daraltma. | Başlık temizlendi. |
| `v2.0.24` | Yayında | 2 asset | Koru | Discord doğrulama ve tünel uyumluluğu milestone'u. | Başlık temizlendi. |
| `v2.0.20` | Yayında | 2 asset | Koru | Güncelleme görünürlüğü ve UI polish milestone'u. | Başlık temizlendi. |
| `v2.0.14` | Yayında | 2 asset | Koru | Güvenli otomatik güncelleme temel hattı. | Başlık temizlendi. |
| `v2.0.0` | Yayında | 2 asset | Koru | 2.x hattının başlangıç noktası. | Başlık temizlendi. |

## Silinen Release ve Tag'ler

`APPROVED - CLEAN RELEASES` onayı sonrası aşağıdaki release'ler `gh release delete <tag> --cleanup-tag -y` ile silindi:

`v2.0.2`, `v2.0.3`, `v2.0.5`, `v2.0.6`, `v2.0.7`, `v2.0.8`, `v2.0.9`, `v2.0.10`, `v2.0.11`, `v2.0.15`, `v2.0.16`, `v2.0.17`, `v2.0.18`, `v2.0.19`, `v2.0.21`, `v2.0.22`, `v2.0.23`, `v2.0.25`, `v2.0.26`, `v2.0.27`, `v2.0.28`, `v2.0.29`.

Aşağıdaki canlı release'i olmayan tag-only kalıntılar remote ve local tag listesinden silindi:

`v2.0.1`, `v2.0.12`, `v2.0.13`.

`v2.0.4` için canlı release veya remote tag bulunmadı; yalnızca eski yerel artifact kalıntıları vardı. Yerel `artifacts/` temizliği ayrıca dosya temizliği sayıldığı için yapılmadı.

## Cleanup Kararı

Uygulanan güvenli politika:

- Public release yüzeyi 6 anlamlı milestone sürüme indirildi.
- Cleanup sonrası Latest işareti `v2.1.0` üzerindeydi; `v2.1.1` yayını tamamlanınca Latest hedefi v2.1.1'e taşınır.
- Ara hotfix ve deneme yayınları silindi.
- Tag-only başarısız yayın kalıntıları silindi.
- Kalan milestone release başlıkları okunur ve tutarlı hale getirildi.
- Eski release asset'leri `--clobber` ile değiştirilmedi; yeni davranış yeni sürümle yayınlandı.
- Yerel `artifacts/` altındaki eski ZIP/log/smoke dosyaları silinmedi ve source commit'e alınmadı.

## Yayın Kapısı

`v2.1.1` yayını için hedeflenen koşullar:

- Proje ve updater sürümü `2.1.1` ile aynı.
- `v2.1.1` tag'i remote'a pushlanır.
- Release assetleri `Discorder-2.1.1-win-x64.zip`, `Discorder-2.1.1-win-x64.sha256.txt`, `Discorder-win-x64.zip`, `Discorder-win-x64.sha256.txt`.
- Sürümlü ve sabit ZIP hash'leri eşleşir.
- ZIP içindeki manifest sürümü `2.1.1`.
- ZIP içinde doğrulanmış yerel fallback için `installers/wiresock-vpn-client-x64-1.4.7.1.msi` bulunur.
- `scripts\verify.ps1`, `git diff --check`, Gitleaks ve release packaging geçer.

`v2.1.0` yayını tamamlandıktan sonra doğrulanan koşullar:

- Proje ve updater sürümü `2.1.0` ile aynı.
- `v2.1.0` tag'i remote'a pushlandı.
- GitHub Actions release workflow başarılı.
- Release assetleri `Discorder-2.1.0-win-x64.zip`, `Discorder-2.1.0-win-x64.sha256.txt`, `Discorder-win-x64.zip`, `Discorder-win-x64.sha256.txt`.
- Sürümlü ve sabit ZIP hash'leri eşleşiyor.
- ZIP içindeki manifest sürümü `2.1.0`.
- `scripts\verify.ps1`, `git diff --check`, Gitleaks ve release packaging geçti.
