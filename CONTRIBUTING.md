# Katkı rehberi

Iskele'ye ilginiz için teşekkürler!

## Hata bildirimi

[Issues](../../issues) sayfasında yeni bir kayıt açın ve şunları ekleyin:

- Iskele sürümü (Ayarlar penceresinin sol üstünde yazar) ve Windows sürümü
- Sorunu yeniden oluşturma adımları
- Mümkünse Ayarlar → Geri bildirim ve hata raporu ile kaydedilen rapor

Raporu paylaşmadan önce içinde kişisel bilgi (dosya yolları, pencere başlıkları vb.) olup olmadığını kontrol edin.

## Kod katkısı

1. Depoyu fork'layın ve bir dal açın.
2. Değişikliğinizi yapın; `dotnet build src -c Release` uyarı eklemeden derlenmeli.
3. `Iskele.exe --feature-tests <klasör>` çalıştırıp `results.txt` içinde `FAIL` olmadığını kontrol edin.
4. Kullanıcıya görünen yeni metinler için `src/Localization.cs` içine İngilizce karşılığını ekleyin.
5. Pull request açın; ne değiştiğini ve nasıl denediğinizi kısaca yazın.

Görev çubuğuna dokunan kodda (`TaskbarGuard.cs`, `DockRecovery.cs`) özellikle dikkatli olun: Iskele hangi durumda kapanırsa kapansın görev çubuğu geri gelmelidir.

Katkılarınız projenin lisansı olan [GPL v3](LICENSE) ile yayınlanır.
