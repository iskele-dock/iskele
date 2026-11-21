# Değişiklik günlüğü

Sürümler [Semantic Versioning](https://semver.org/lang/tr/) düzenini izler.

## [1.0.2] — 2026-11-21

### Düzeltme
- Dock bazen Chrome gibi normal pencerelerin arkasında kalıyordu: Windows "her zaman üstte" bayrağı açık pencereyi zaman zaman normal pencerelerin altına sıralayabiliyor. Iskele artık her pencere değişiminde (ve yarım saniyede bir) bunu denetleyip kendini yeniden en üste alıyor. Menülere, açılır panellere ve tam ekranda gizlenmeye dokunmaz.

## [1.0.1] — 2026-11-20

### Yeni
- Uygulama içi güncelleme: Iskele yeni sürümleri GitHub'dan denetler (açılıştan kısa süre sonra ve 12 saatte bir), dock menüsünde ve Ayarlar → Genel'de **Güncelle** seçeneği çıkar. Onaylayınca kurulum dosyası indirilir, SHA256 ile doğrulanır, sessizce kurulur ve Iskele yeniden açılır; ayarlar korunur.
- Ayarlar → Genel → **Güncellemeler**: otomatik denetimi kapatma anahtarı ve "Şimdi denetle".

## [1.0.0] — 2026-11-20

İlk herkese açık sürüm.

### Dock
- Şerit ve Balon yerleşimleri; alt/üst kenar, hap/tam genişlik, boyut, aralık ve iç boşluk ayarları.
- Sürükleyerek kenar değiştirme, kilit, otomatik gizleme, tam ekranda gizlenme, AppBar kaydı.
- Çoklu monitör: ana ekran, her ekran ya da imleci takip et.
- Siyah, Beyaz, Gece, Cam, iOS Cam (HLSL canlı kırılma) ve Sade temaları; 8 vurgu rengi.
- Görev çubuğu güvencesi: durum dosyası, bekçi süreç ve `--restore` komutu; kurulum ve kaldırma, kalmış durumu dosyalara dokunmadan önce geri yükler.

### Uygulamalar
- Sabitlenmiş uygulamalar ve açık pencereler, canlı önizlemeler, Mac tarzı büyütme.
- Win+1…0 ve Win+T kısayolları, sürükleyerek sabitleme ve sıralama, Steam oyun simgeleri.
- Sistem tepsisi, Wi‑Fi ve Bluetooth listeleri.

### Widget'lar
- 10 kategoride 79 widget: saatler, verimlilik, sistem, uygulamalar & dosyalar, medya & ses, hava durumu, AI limitleri, geliştirici, finans ve borsa.
- Widget seçicide canlı önizlemeli kart ızgarası, kategori listesi ve arama; kart içi ayarlar.

### Diğer
- Türkçe ve İngilizce arayüz.
- Uyarılar ve otomasyonlar: toplantı öncesi müzik duraklatma, AI limit uyarıları, yenileme hatırlatıcıları, sessiz saatler.
- Kurulum programı: kullanıcı başına kurulum (yönetici izni yok), Başlat menüsü kısayolu, sessiz kurulum (`--silent`).
- Kaldırma: Iskele Ayarlar → Genel → **Iskele'yi kaldır** ya da Windows Ayarlar → Uygulamalar; kişisel verilerin silinmesi isteğe bağlı.

[1.0.2]: ../../releases/tag/v1.0.2
[1.0.1]: ../../releases/tag/v1.0.1
[1.0.0]: ../../releases/tag/v1.0.0
