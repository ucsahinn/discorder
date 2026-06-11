# Katkı Rehberi

Discorder'ın ürün sınırı bilinçli olarak dardır: Windows üzerinde tek düğmeyle yalnızca Discord trafiğini tünellemek.

## Kabul Edilen Değişiklikler

- Discord bağlantı kararlılığı.
- WireSock ve wgcf doğrulama zinciri.
- Kurulum, tanılama ve hata mesajları.
- Türkçe kullanıcı deneyimi.
- Test, derleme ve yayın güvenliği.
- Dokümantasyon ve bildirim şablonları.

## Kabul Edilmeyen Değişiklikler

- Tarayıcı, oyun veya özel uygulama tünelleme.
- Genel cihaz VPN'i.
- DNS, DoH, proxy veya sistem servis ayarı ekleme.
- Üçüncü taraf ikili dosya, sürücü, kurucu veya arşiv gömme.
- TLS, imza, hash, kimlik doğrulama veya hata kontrolünü zayıflatma.
- Eski çoklu aşma motorlarını geri getirme.

## PR Kontrol Listesi

- [ ] Yalnızca Discord kapsamı korunuyor.
- [ ] DNS, servis, görev zamanlayıcı veya kalıcı ağ mutasyonu eklenmedi.
- [ ] Üçüncü taraf ikili dosya veya kurucu repoya eklenmedi.
- [ ] Kullanıcıya görünen metinler Türkçe.
- [ ] `.\scripts\verify.ps1` geçti.
- [ ] Gerekliyse ekran görüntüsü veya canlı test notu eklendi.

## Hata Bildirimi

Hata bildirirken sürüm, Windows derlemesi, Discord kanalı, WireSock sürümü, internet sağlayıcısı ve redakte edilmiş tanılama bilgisini ekleyin. Gizli anahtar veya profil içeriği paylaşmayın.
