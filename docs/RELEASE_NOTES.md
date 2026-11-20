## Iskele 1.0.0 — ilk herkese açık sürüm

Windows 10/11 için görev çubuğunun yerine geçen, 79 widget'lı açık kaynak dock.

### Kurulum
1. Aşağıdan `IskeleSetup-1.0.0.exe` dosyasını indirip çalıştırın. Gerekli .NET bileşenleri pakete dahildir; yönetici izni gerekmez.
2. SmartScreen uyarısı çıkarsa **Ek bilgi → Yine de çalıştır** (dosya imzasızdır; SHA256 değeri `.sha256` dosyasında).

### Kaldırma
Iskele Ayarlar → Genel → **Iskele'yi kaldır**, ya da Windows Ayarlar → Uygulamalar → Iskele.

### Öne çıkanlar
- Şerit ve Balon yerleşimleri, 6 tema (iOS Cam: HLSL canlı kırılma), çoklu monitör
- Canlı pencere önizlemeleri, Win+1…0 ve Win+T kısayolları, sistem tepsisi, Wi‑Fi/Bluetooth listeleri
- Saat, hava durumu, sistem, müzik, yapılacaklar, takvim, AI limitleri, GitHub, borsa ve kripto widget'ları
- Hesap ya da telemetri yok; veriler yalnızca bilgisayarınızda
- Görev çubuğu her durumda geri gelir (bekçi süreç + `--restore`); kurulum ve kaldırma da kalmış durumu geri yükler
- Türkçe ve İngilizce arayüz (Ayarlar → Genel → Arayüz dili)

---

**English:** Iskele is a free, open-source dock with 79 widgets that replaces the Windows 10/11 taskbar. Download `IskeleSetup-1.0.0.exe` below and run it (no admin rights or separate .NET install needed). The interface is available in English under Settings → General → Interface language. If the taskbar ever stays hidden, run `%LOCALAPPDATA%\Programs\Iskele\Iskele.exe --restore`.

Tüm değişiklikler için depodaki `CHANGELOG.md` dosyasına bakın.
