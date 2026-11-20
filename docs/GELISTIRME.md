# Geliştirme notları

## Proje yapısı

```
src/                    Iskele uygulaması (C# + WPF, .NET 9)
  Widgets/              Widget'lar; her biri Widget sınıfından türeyen küçük bir sınıf
  Assets/Providers/     Gömülü sağlayıcı logoları ve lisansları
vendor/LiquidGlass/     GPL-3.0 HLSL cam görüntüleyici (değişiklikler NOTICE.md içinde)
installer/              Kurulum programı (Setup.cs) ve derleme betiği (build.ps1)
docs/                   Belgeler ve ekran görüntüleri
```

Yeni bir widget, `src/Widgets/` altında `Widget` sınıfından türeyen bir sınıf olarak yazılır ve `Widget.cs` içindeki `WidgetRegistry` listesine bir satırla eklenir.

## Derleme

```powershell
dotnet build src -c Release        # derle
dotnet run --project src           # çalıştır
./installer/build.ps1              # dist\IskeleSetup-<sürüm>.exe + .sha256
```

Sürüm numarası tek yerde tutulur: `src/Iskele.csproj` → `<Version>`. Kurulum programındaki `Version` sabitini (`installer/Setup.cs`) ve `src/app.manifest` sürümünü de aynı değere getirin.

Kurulum programı Windows'un yerleşik .NET Framework derleyicisiyle (`csc.exe`, C# 5) derlenir; ek bir araç gerekmez. Bu yüzden `Setup.cs` içinde yeni C# özellikleri kullanılmaz.

## Testler

```powershell
Iskele.exe --feature-tests <boş-çıktı-klasörü>
```

Davranış kontrollerini çalıştırır ve dock/ayarlar pencerelerinin çevrimdışı renderlarını PNG olarak üretir. Sonuç: `<klasör>\results.txt`. Test modu kullanıcı ayarlarını kaydetmez, dock'a ve görev çubuğuna dokunmaz.

Kurulum arşivini sisteme dokunmadan doğrulamak için:

```powershell
dist\IskeleSetup-1.0.0.exe --extract-test <boş-klasör>
```

## Komut satırı

| Komut | Etkisi |
|---|---|
| `Iskele.exe --restore` | Görev çubuğunu her koşulda geri getirir |
| `IskeleSetup.exe --silent` | Sessiz kurulum |
| `IskeleSetup.exe --uninstall` | Kaldırma |

## Ortam değişkenleri (yalnızca o çalıştırma için)

| Değişken | Etkisi |
|---|---|
| `ISKELE_DEBUG=1` | Ayrıntılı günlüğü `%AppData%\Iskele\log.txt` dosyasına yazar |
| `ISKELE_OPEN=settings`, `picker` ya da `picker:<kategori>` | Açılışta ilgili pencereyi açar |
| `ISKELE_OPEN=wifi` ya da `bluetooth` | Açılışta Wi‑Fi ya da Bluetooth listesini açar |
| `ISKELE_PREVIEW_TEST=1` | Fareye dokunmadan bir pencere önizlemesini açar |
| `ISKELE_THEME`, `ISKELE_MODE=balloon`, `ISKELE_AUTOHIDE=1`, `ISKELE_HIDETB`, `ISKELE_BALLOONEDGE` | Geçici görünüm; ayarlar diske kaydedilmez |

Iskele tek kopya çalışır. Geliştirirken kurulu Iskele'den çıkış yapın; test bittiğinde görev çubuğunun geri geldiğini kontrol edin.

## Veri konumları

| Yol | İçerik |
|---|---|
| `%LocalAppData%\Programs\Iskele` | Kurulu program |
| `%AppData%\Iskele` | `settings.json`, notlar, ekran süresi, `log.txt`, `crash.txt` |

## Yeni sürüm yayınlama

1. Sürüm numarasını `src/Iskele.csproj`, `src/app.manifest` ve `installer/Setup.cs` içinde güncelleyin; `CHANGELOG.md`'ye ekleyin.
2. Commit'leyip etiketleyin: `git tag v1.0.1 && git push --tags`.
3. GitHub Actions (`.github/workflows/release.yml`) kurulum dosyasını derler ve taslak bir Release oluşturur. Notları kontrol edip yayınlayın.
