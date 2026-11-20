using System.Globalization;
namespace Iskele;
partial class Settings { public string Language { get; set; } = "tr"; }
static class Localization
{
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(App.Settings.Language == "en" ? "en-US" : "tr-TR");
    static readonly Dictionary<string, string> English = new(StringComparer.OrdinalIgnoreCase);
    static Localization()
    {
        foreach (var line in Translations.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = line.Trim().Split('|', 2);
            if (pair.Length == 2) English[pair[0]] = pair[1];
        }
    }
    public static string T(string text) => App.Settings.Language == "en" && English.TryGetValue(text, out var translated) ? translated : text;
    public static int Count => English.Count;
    public static bool En => App.Settings.Language == "en";
    /// <summary>Çevrilmiş biçim dizesi; değişken içeren metinler için.</summary>
    public static string F(string format, params object?[] args) => string.Format(Culture, T(format), args);
    const string Translations = """
Ayarlar|Settings
Widget’lar|Widgets
Tüm widget’lar|All widgets
Widget’ları düzenle…|Edit widgets…
Ayarlarda ara|Search settings
iOS Cam özelleştirme|iOS Glass options
iOS Cam|iOS Glass
Sade|Minimal
Kart: ekle / çıkar · Ok: özelleştir|Card: add / remove · Arrow: customize
Eşleşen widget yok.|No matching widgets.
Kendi kısayolların|Your shortcuts
Şehri Ayarlar'dan değiştirebilirsin · Open‑Meteo|Change the city in Settings · Open‑Meteo
Çalan bir şey yok|Nothing playing
Mola|Break
Odak {0}/4|Focus {0}/4
Oturum|Session
Hafta|Week
Ekran süresi|Screen time
Zamanlayıcı|Timer
Yapılacaklar|To-dos
Henüz veri yok.|No data yet.
Liste boş.|The list is empty.
Y:{0:0}° D:{1:0}°|H:{0:0}° L:{1:0}°
Rüzgâr {0:0} km/sa · Nem %{1:0} · Hissedilen {2:0}° · Gün doğumu {3:HH:mm} · batımı {4:HH:mm}|Wind {0:0} km/h · Humidity {1:0}% · Feels like {2:0}° · Sunrise {3:HH:mm} · Sunset {4:HH:mm}
{0} widget · {1} tanesi dock'ta|{0} widgets · {1} in the dock
AI kullanım limitleri|AI usage limits
5 saatlik oturum|5-hour session
Haftalık|Weekly
{0} · {1} sonra sıfırlanır|{0} · resets in {1}
Bu hafta:|This week:
Son güncelleme: {0:HH:mm}|Last updated: {0:HH:mm}
Claude Code · bugün|Claude Code · today
{0} yanıt · {1} çıktı · {2} girdi · {3} önbellek okuma|{0} replies · {1} output · {2} input · {3} cache reads
{0}: {1} çıktı|{0}: {1} output
{0} — son güncelleme {1:HH:mm}|{0} — last updated {1:HH:mm}
Ek kullanım açık|Extra usage on
Ek kullanım kapalı|Extra usage off
Claude Code ya da Codex verisi bulunamadı.|No Claude Code or Codex data found.
Veriler bu bilgisayardaki yerel dosyalardan okunur; Claude limitleri Claude Code girişinle yalnızca Anthropic'e sorulur.|Data is read from local files on this PC; Claude limits are requested only from Anthropic using your Claude Code sign-in.
Bugün {0}|Today {0}
Hafta {0} · {1}|Week {0} · {1}
kayıt bulunamadı|no records found
sıfırlandı|reset
Gün, tarih ve saat; tıklayınca takvim|Day, date and time; click for calendar
Saniyeli büyük saat|Big clock with seconds
Günün numarası|Today's date number
Londra, New York, Tokyo|London, New York, Tokyo
Yılın ne kadarı geçti, nokta ızgarası|How much of the year has passed, dot grid
İki kartlı çevrilen saat|Two-card flip clock
Sade kadran|Clean dial
Yedi segmentli ekran|Seven-segment display
Tek şehir ve saat farkı; tıkla: şehir seç|One city and its time offset; click to pick
Gün, tarih, saat ve hava|Day, date, time and weather
Gün ve ay|Day and month
Hafta şeridi ve bugünün sıcaklığı|Week strip and today's temperature
Üç renkli blok|Three-colour block
Günün ne kadarı geçti|How much of the day has passed
Ayın ne kadarı geçti|How much of the month has passed
Günü tek çizgide: uygulama türleri, odak turları, toplantılar, gün doğumu/batımı|Your day on one line: app types, focus rounds, meetings, sunrise/sunset
Bugün bilgisayar başında geçen süre, saat saat|Time at the computer today, hour by hour
25 dk odak, 5 dk mola turları|25 min focus, 5 min break rounds
Tek tıkla geri sayım; tekerle süreyi ayarla|One-click countdown; scroll to set the time
Başlat / duraklat; sağ tıkla sıfırla|Start / pause; right-click to reset
Açık işler ve ilerleme halkası|Open tasks and a progress ring
Son not; tıklayınca düzenle|Latest note; click to edit
CPU ve RAM halkaları|CPU and RAM rings
İşlemci yükü|Processor load
Bellek kullanımı|Memory usage
İndirme / yükleme hızı ve canlı grafik|Download / upload speed and a live graph
Dolan pil göstergesi|Filling battery gauge
Wi‑Fi ve Bluetooth aç/kapat|Wi‑Fi and Bluetooth toggles
Pencereleri gizle / geri getir|Hide / restore windows
Gizli tepsi simgeleri; sağ tık ve çift tık görev çubuğundaki gibi|Hidden tray icons; right-click and double-click work like the taskbar
Bildirim merkezini aç|Open the notification center
Halka biçiminde pil|Battery as a ring
E'den F'ye yakıt göstergesi|E-to-F fuel gauge
On iki hücreli pil|Twelve-cell battery
CPU sıcaklığı, sensör seçimi, °C/°F|CPU temperature, sensor choice, °C/°F
Windows'un rahatsız etmeyin modu|Windows do-not-disturb mode
Nokta matrisi pil|Dot-matrix battery
İşlemciyi en çok kullanan üç uygulama|Top three apps by CPU
Belleği en çok kullanan üç uygulama|Top three apps by memory
Ekran parlaklığı kaydırıcısı|Screen brightness slider
Geçerli dil; tıkla: değiştir|Current language; click to switch
Windows temasını değiştir|Switch the Windows theme
Bilgisayarın uyumasını engelle|Keep the PC awake
Masaüstü simgelerini gizle / göster|Hide / show desktop icons
Mikrofonu sustur / aç|Mute / unmute the microphone
Yansıt / genişlet (Win+P)|Duplicate / extend (Win+P)
Dokunmatik klavyeyi aç|Open the touch keyboard
Ekran, pencere ya da bölge yakala|Capture the screen, a window or a region
Ayarlar penceresini aç|Open the settings window
Başlat, sabitlenmiş uygulamalar ve açık pencereler|Start, pinned apps and open windows
Sürücüdeki boş alan|Free space on your drive
İçerik; dosya sürükle: sil; sağ tık: boşalt|Contents; drop a file to delete it; right-click to empty
Son açılan dosyalar|Recently opened files
Uygulama, klasör, adres ya da web araması|Apps, folders, addresses or web search
Kapak, şarkı ve kontroller|Cover, track and controls
Kapak, şarkı, oynat|Cover, track, play
İlerleme halkalı oynat düğmesi|Play button with a progress ring
Şarkı, ilerleme çubuğu, atla|Track, progress bar, skip
Çıkış aygıtı ve ses düzeyi|Output device and volume
Sıcaklık, durum, en yüksek / en düşük|Temperature, condition, high / low
Rüzgâr, nem, yağış, hissedilen|Wind, humidity, rain, feels like
Bugün ve sonraki üç gün|Today and the next three days
Önümüzdeki altı saat|The next six hours
Bugünün gün doğumu ve batımı|Today's sunrise and sunset
Durum sözcüklerle|Conditions in words
Claude ve Codex 5 saatlik limitleri tek kartta|Claude and Codex 5-hour limits on one card
Oturum ve haftalık limit, sıfırlanma, en çok kullanılan model|Session and weekly limits, reset time, top model
Antigravity CLI kotası (Pro / Flash), sıfırlanma|Antigravity CLI quota (Pro / Flash), reset time
Bugün ve bu hafta token, en çok kullanılan model|Tokens today and this week, top model
Codex 5 saatlik ve haftalık pencereler|Codex 5-hour and weekly windows
Bildirimler, açık PR ve depo yıldızları tek kartta|Notifications, open PRs and repo stars on one card
Okunmamış bildirimler, açık PR'lar, yıldızlar|Unread notifications, open PRs, stars
Katkı ızgarası ve seri|Contribution grid and streak
Windows bildirimleri; paket yetkisi ve kullanıcı izni gerekir|Windows notifications; needs package access and your permission
Sıradaki etkinlik (Google / Outlook / iCloud iCal)|Next event (Google / Outlook / iCloud iCal)
Güncel kurla çevirici; sağ tık: yer değiştir|Live-rate converter; right-click to swap
Binance fiyatı ve 24 saatlik değişim|Binance price and 24-hour change
Üç sembol, fiyat ve günlük değişim|Three symbols, price and daily change
Tek sembol, gün içi çizgi|One symbol, intraday line
Görünüm|Appearance
Dock ve kartların renkleri|Dock and card colors
Vurgu rengi|Accent color
Düğmeler, halkalar, göstergeler|Buttons, rings and indicators
Boyut|Size
Kartların büyüklüğü|Card size
Biçim|Shape
Ortada hap şeklinde ya da tam genişlik|Centered pill or full width
Hap|Pill
Tam genişlik|Full width
Aralık|Spacing
Widget'lar arasındaki boşluk|Space between widgets
Dar|Compact
Geniş|Wide
İç boşluk|Padding
Dock kenarı ile kartlar arası|Space between the dock edge and cards
Az|Small
Çok|Large
Yerleşim|Layout
Şerit: ekran kenarında satır · Balon: en üstte ortada yüzen, üzerine gelince açılan ada|Bar: a row at the screen edge. Balloon: a floating island that expands on hover.
Şerit|Bar
Balon|Balloon
Küçük haldeyken saat ve çalan şarkıyı gösterir|Shows the time and current track when collapsed
Üzerine gelince aç|Expand on hover
Hep açık|Always expanded
Şerit konumu|Bar position
Şerit ekranın hangi kenarında dursun|Screen edge for the bar
Alt|Bottom
Üst|Top
Balon konumu|Balloon position
Balon ekranın üstünde mi altında mı yüzsün|Float at the top or bottom of the screen
Kenardan uzaklık|Edge distance
Dock'u ekran kenarından kaldırır (hap biçiminde)|Distance from the screen edge in pill mode
Yok|None
Otomatik gizle|Auto hide
Çalışırken kenara kayar; imleç kenara değince gelir|Hides at the edge; move the pointer to the edge to reveal
Kapalı|Off
Açık|On
Masaüstünde göster|Show on desktop
Otomatik gizlemede, boş masaüstündeyken dock açık kalır|Keep the dock visible on an empty desktop when auto hide is enabled
Dock'u kilitle|Lock dock
Sürükleyerek kenar değiştirmeyi engeller|Prevents dragging the dock to another edge
Windows görev çubuğu|Windows taskbar
Iskele açıkken gizlenir; kapatınca geri gelir|Hidden while Iskele runs; restored on exit
Gizle|Hide
Göster|Show
Gizli görev çubuğu|Hidden taskbar
Tamamen gizli ya da imleç ekranın alt kenarına değince görünsün|Fully hidden or revealed when the pointer touches the bottom edge
Gizli|Hidden
Görev çubuğu alanı|Taskbar space
Koru: ekran alanı değişmez, masaüstü simgeleri ve pencereler yerinden oynamaz · Kullan: pencereler tüm ekranı kaplar (düzen bir kez değişir)|Preserve: keep desktop icons and windows in place. Use: allow windows to fill the screen.
Koru|Preserve
Kullan|Use
Uygulamalar|Applications
Simge boyutu|Icon size
Dock'taki uygulama simgeleri|Application icons in the dock
Küçük|Small
Orta|Medium
Büyük|Large
Açık uygulama göstergesi|Running application indicator
Açık uygulamaların altındaki işaret|Mark below running applications
Nokta|Dot
Büyük nokta|Large dot
Çizgi|Line
Ekranlar|Displays
Ana ekran: tek dock · Her ekran: her monitörde bir dock · Takip et: imleç bir ekranın kenarında durunca dock oraya geçer|Main: one dock. All: a dock on each display. Follow: moves to the display whose edge the pointer touches.
Ana ekran|Main display
Her ekran|All displays
Takip et|Follow pointer
Büyütme|Magnification
Mac tarzı: simgelerin üzerine gelince büyür|Magnify icons on hover
Simge stili|Icon style
Çerçeve: macOS tarzı yuvarlatılmış kare; tek renkli simgeler dock'un rengini alır|Frame: rounded square. Monochrome icons follow the dock color.
Çerçeve|Frame
Uygulama kartları|Application cards
Uygulamalar yalnızca simge ya da widget boyutunda kart olarak|Show applications as icons or widget-sized cards
Simge|Icon
Kart|Card
Kart stili|Card style
Widget kartları temadan bağımsız siyah, beyaz ya da buzlu|Black, white or frosted cards independently of the theme
Tema|Theme
Siyah|Black
Beyaz|White
Buzlu|Frosted
Gece|Midnight
Cam|Glass
Uygulama alanı|Application area
Sabit: yeni bir uygulama açılınca diğer widget'lar ve balon kaymaz (alan en fazla görülen simge kadar ayrılır) · Esnek: alan simge sayısına göre büyüyüp küçülür|Fixed: reserves space to prevent other widgets from shifting. Flexible: resizes to fit the icons.
Sabit|Fixed
Esnek|Flexible
Başlat resmi|Start image
Başlat düğmesinde Windows logosu yerine kendi resmin|Use your own image for the Start button
Dock'taki 1.–10. uygulamayı klavyeyle aç ya da öne getir|Launch or activate the first ten dock applications
Ad etiketi|Name tooltip
Simgenin üzerine gelince uygulamanın adı görünsün|Show the application name on hover
Pencere önizlemeleri|Window previews
Simgenin üzerinde durunca açık pencereleri canlı göster|Show live window previews on hover
Widget verileri|Widget data
Müzik çalmıyorken gizle|Hide inactive media
Şimdi çalan widget'ı müzik yokken dock'ta yer kaplamasın (duraklatılmışsa görünür)|Hide media cards when no media is available; paused media stays visible
Albüm kapağı rengi|Album art color
Müzik çalarken vurgu rengi kapağın rengini alsın|Use album artwork for the accent color
Ekran süresinde siteler|Track website time
Tarayıcıda hangi sitede ne kadar kaldığını da say (yalnızca bu bilgisayarda)|Track time spent on websites locally on this computer
Her yerden hızlı yapılacak kutusu|Open quick tasks from any application
Hızlı ayarlar|Quick settings
Wi‑Fi ve Bluetooth'un yanında görünecek düğmeler|Buttons alongside Wi-Fi and Bluetooth
Saate tıklayınca|Clock click action
Iskele takvimi ya da Windows'un bildirim ve takvim paneli|Iskele calendar or the Windows notification and calendar panel
Saat biçimi|Time format
24 saat ya da ÖÖ/ÖS|24-hour or AM/PM
24 saat|24-hour
12 saat|12-hour
Hava durumu şehri|Weather city
Hava durumu widget'ları için|Location for weather widgets
Borsa sembolleri|Stock symbols
Virgülle ayır (ör. AAPL, MSFT, THYAO.IS)|Comma separated, e.g. AAPL, MSFT, THYAO.IS
Kripto çiftleri|Crypto pairs
Binance çiftleri, virgülle (ör. BTCUSDT, ETHUSDT)|Comma-separated Binance pairs, e.g. BTCUSDT, ETHUSDT
Takvim (iCal) bağlantıları|Calendar (iCal) links
Google/Outlook/iCloud 'gizli iCal adresi'; birden çoksa boşlukla ayır|Private Google/Outlook/iCloud iCal URLs, separated by spaces
Ödemeler ve plan yenilemeleri…|Payments and renewals…
Uyarılar ve otomasyonlar|Alerts and automation
Yenileme hatırlatıcıları|Renewal reminders
Kaydettiğiniz AI planları ve abonelikler için 3 gün önce|Three days before your saved AI plans and subscriptions renew
AI limit uyarısı|AI limit alerts
Claude veya Codex oturumunda, her sıfırlama dönemi için bir uyarı|One Claude or Codex alert per session reset period
AI uyarı eşiği|AI alert threshold
Kullanım yüzdesi|Usage percentage
Toplantı öncesi müzik|Pause before meetings
iCal toplantısından 2 dakika önce çalan medyayı duraklat|Pause playing media two minutes before an iCal meeting
Sessiz saatler|Quiet hours
Bu aralıkta Iskele bildirim ve ses göstermez|Mute Iskele notifications and sounds during these hours
Sessiz başlangıç|Quiet hours start
Saat: 0–23|Hour: 0–23
Sessiz bitiş|Quiet hours end
Saat: 0–23; aynı saat seçilirse tüm gün sessiz|Hour: 0–23; equal start and end means all day
Uyarı sesi|Alert sound
Windows sistem sesleri|Windows system sounds
Sessiz|Silent
Bilgi|Information
Uyarı|Warning
Geri bildirim ve hata raporu…|Feedback and diagnostics…
CPU ve bellek ölçümü…|CPU and memory usage…
Genel|General
Windows ile başlat|Launch at sign-in
Veri klasörü|Data folder
Ayarlar, notlar ve ekran süresi yalnızca bu bilgisayarda tutulur|Settings, notes and screen time stay on this computer
Klasörü aç|Open folder
Iskele'yi kaldır|Uninstall Iskele
Iskele kapanır, görev çubuğu geri gelir ve program bu bilgisayardan silinir|Iskele closes, the taskbar comes back and the app is removed from this PC
Bu kopya kurulum programıyla kurulmamış; klasörü silmeniz yeterli|This copy was not installed with the setup; just delete its folder
Kaldır…|Uninstall…
Klasörü göster|Show folder
Iskele bu bilgisayardan kaldırılsın mı?|Uninstall Iskele from this PC?
Iskele kapanacak ve Windows görev çubuğu geri gelecek. Kişisel ayarlarınızı silip silmemeyi bir sonraki adımda seçebilirsiniz.|Iskele will close and the Windows taskbar will come back. You can choose whether to delete your personal settings in the next step.
Kaldırma başlatılamadı. Windows Ayarlar → Uygulamalar üzerinden kaldırabilirsiniz.|Could not start the uninstaller. You can remove Iskele from Windows Settings → Apps.
Oturum açınca Iskele kendiliğinden açılır|Start Iskele automatically when you sign in
Widget'ları düzenle…|Edit widgets…
Resim seç…|Choose image…
Windows logosu|Windows logo
Widget'lar|Widgets
Widget ara|Search widgets
Tıkla: ekle / çıkar · Dock'ta sürükle: sırala · kırmızı rozet: kaldır|Click to add/remove. Drag in the dock to reorder. Red badge removes.
Tüm widget'lar|All widgets
Uygulamalar & Dosyalar|Apps & Files
Eşleşen widget yok.|No matching widgets.
Kendi kısayolların|Your shortcuts
Uygulama ya da dosya ekle…|Add application or file…
Dock'a eklenecek uygulama ya da dosya|Application or file to add to the dock
Klasör ekle…|Add folder…
Dock'a eklenecek klasör|Folder to add to the dock
Ödemeler ve yenilemeler|Payments and renewals
Ödeme veya abonelik ekleyin|Add a payment or subscription
Kaydet|Save
Geri bildirim ve hata raporu|Feedback and diagnostics
Son 100 günlük satırını ekle (kişisel bilgi içerebilir)|Include the last 100 log lines (may contain personal information)
Rapor yalnızca bilgisayarınıza kaydedilir.|Reports are saved only to your computer.
Raporu kaydet…|Save report…
Rapor kaydedildi.|Report saved.
Iskele performansı|Iskele performance
Uygulamalar · Win+T|Applications · Win+T
← → / ↑ ↓: uygulama · Tab: pencere · Enter: aç · Esc: kapat|Arrows: application · Tab: window · Enter: open · Esc: close
Arayüz dili|Interface language
Türkçe veya İngilizce; mevcut pencereler yeniden açılır|Turkish or English; open panels are recreated
Saatler|Clocks
Verimlilik|Productivity
Sistem|System
Medya & Ses|Media & Audio
Hava durumu|Weather
Geliştirici|Developer
Finans|Finance
Borsa|Stocks
Saat|Clock
Büyük saat|Large clock
Takvim yaprağı|Calendar page
Dünya saatleri|World clocks
Yıl ilerlemesi|Year progress
Flip saat|Flip clock
Analog saat|Analog clock
Dijital saat|Digital clock
Dünya saati|World time
Bugün|Today
Tarih|Date
7 gün|7 days
Saat & hava|Clock & weather
Gün ilerlemesi|Day progress
Ay ilerlemesi|Month progress
Gün çizelgesi|Day timeline
Ekran süresi|Screen time
Zamanlayıcı|Timer
Kronometre|Stopwatch
Yapılacaklar|Tasks
Notlar|Notes
Ağ|Network
Pil|Battery
Masaüstünü göster|Show desktop
Sistem tepsisi|System tray
Bildirimler|Notifications
Pil halkası|Battery ring
Pil göstergesi|Battery gauge
Pil çekirdeği|Battery core
Sıcaklık|Temperature
Rahatsız etmeyin|Do not disturb
Pil noktaları|Battery dots
En çok CPU|Top CPU
En çok RAM|Top RAM
Parlaklık|Brightness
Klavye dili|Keyboard language
Açık / koyu|Light / dark
Uyanık tut|Keep awake
Masaüstü simgeleri|Desktop icons
Mikrofon|Microphone
Ekran modu|Display mode
Ekran klavyesi|On-screen keyboard
Ekran görüntüsü|Screenshot
Iskele ayarları|Iskele settings
Depolama|Storage
Geri dönüşüm kutusu|Recycle bin
Son açılanlar|Recent files
Ara & çalıştır|Search & run
Şimdi çalan|Now playing
Şimdi çalan (sade)|Now playing (compact)
Şimdi çalan (mini)|Now playing (mini)
Şimdi çalan (şerit)|Now playing (bar)
Ses|Volume
Hava ayrıntıları|Weather details
Bugün & tahmin|Today & forecast
Saatlik|Hourly
Gün doğumu & batımı|Sunrise & sunset
Hava özeti|Weather summary
AI limitleri|AI limits
Claude kullanımı|Claude usage
Gemini kullanımı|Gemini usage
OpenCode kullanımı|OpenCode usage
ChatGPT / Codex kullanımı|ChatGPT / Codex usage
GitHub Genel Bakış|GitHub Overview
GitHub etkinliği|GitHub activity
Takvim|Calendar
Döviz|Currency
Kripto|Crypto
Borsa listesi|Stock watchlist
Borsa grafiği|Stock chart
Başlat|Start
Çıkış|Exit
Aç|Open
Kapat|Close
Sil|Delete
Ekle|Add
Sabitle|Pin
Sabitlemeyi kaldır|Unpin
Yeni not|New note
Notlarda ara|Search notes
Not|Note
Yükleniyor…|Loading…
Bağlantı yok|No connection
Bildirim yok|No notifications
Yaklaşan etkinlik yok|No upcoming events
""";
}
