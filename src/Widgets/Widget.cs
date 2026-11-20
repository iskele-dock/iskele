using System.Windows.Media.Effects;

namespace Iskele;

/// <summary>Tüm widget'ların tabanı. Her widget kendi kartını çizer; dock saniyede bir Tick çağırır.</summary>
abstract class Widget : Border
{
    public string Id { get; set; } = "";
    protected static double S => App.Settings.Scale;
    protected static double H => DockWindow.CardH;

    protected Widget(bool card = true)
    {
        Height = H;
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
        if (card)
        {
            CornerRadius = new CornerRadius(Math.Round(12 * S));
            BorderThickness = new Thickness(1);
            Padding = new Thickness(Math.Round(10 * S), 0, Math.Round(10 * S), 0);
            SetResourceReference(BackgroundProperty, "CardBg");
            SetResourceReference(BorderBrushProperty, "CardBorder");
        }
        else Background = Brushes.Transparent;
    }

    public virtual void Tick(DateTime now) { }
    public virtual void Detach() { }

    protected void OnClick(Action a)
    {
        Cursor = Cursors.Hand;
        MouseLeftButtonDown += (_, e) => e.Handled = true;
        MouseLeftButtonUp += (_, e) => { e.Handled = true; a(); };
    }

    protected static double F(double size) => Math.Round(size * S, 1);

    /// <summary>Renkli simge rozeti (uygulama simgesi gibi küçük yuvarlak kare).</summary>
    protected static Border Chip(string glyph, Color color, double size)
    {
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph, FontFamily = Ui.IconFont, FontSize = size * 0.5, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}

record WidgetInfo(string Id, string Name, string Category, string Description, Func<Widget> Create);

static class WidgetRegistry
{
    public static readonly string[] Categories =
        ["Saatler", "Verimlilik", "Sistem", "Uygulamalar & Dosyalar", "Medya & Ses", "AI", "Geliştirici", "Finans", "Borsa", "Hava durumu"];

    public static readonly WidgetInfo[] All =
    [
        new("clock", "Saat", "Saatler", "Gün, tarih ve saat; tıklayınca takvim", () => new ClockWidget()),
        new("bigclock", "Büyük saat", "Saatler", "Saniyeli büyük saat", () => new BigClockWidget()),
        new("daynum", "Takvim yaprağı", "Saatler", "Günün numarası", () => new DayNumberWidget()),
        new("worldclock", "Dünya saatleri", "Saatler", "Londra, New York, Tokyo", () => new WorldClockWidget()),
        new("yearprogress", "Yıl ilerlemesi", "Saatler", "Yılın ne kadarı geçti, nokta ızgarası", () => new YearProgressWidget()),
        new("flipclock", "Flip saat", "Saatler", "İki kartlı çevrilen saat", () => new FlipClockWidget()),
        new("analog", "Analog saat", "Saatler", "Sade kadran", () => new AnalogWidget()),
        new("segclock", "Dijital saat", "Saatler", "Yedi segmentli ekran", () => new SegmentClockWidget()),
        new("worldtime", "Dünya saati", "Saatler", "Tek şehir ve saat farkı; tıkla: şehir seç", () => new WorldTimeWidget()),
        new("today", "Bugün", "Saatler", "Gün, tarih, saat ve hava", () => new TodayWidget()),
        new("date", "Tarih", "Saatler", "Gün ve ay", () => new DateWidget()),
        new("week", "7 gün", "Saatler", "Hafta şeridi ve bugünün sıcaklığı", () => new WeekStripWidget()),
        new("clockweather", "Saat & hava", "Saatler", "Üç renkli blok", () => new ClockWeatherWidget()),
        new("dayprogress", "Gün ilerlemesi", "Saatler", "Günün ne kadarı geçti", () => new ProgressWidget(false)),
        new("monthprogress", "Ay ilerlemesi", "Saatler", "Ayın ne kadarı geçti", () => new ProgressWidget(true)),

        new("daytimeline", "Gün çizelgesi", "Verimlilik", "Günü tek çizgide: uygulama türleri, odak turları, toplantılar, gün doğumu/batımı", () => new DayTimelineWidget()),
        new("screentime", "Ekran süresi", "Verimlilik", "Bugün bilgisayar başında geçen süre, saat saat", () => new ScreenTimeWidget()),
        new("pomodoro", "Pomodoro", "Verimlilik", "25 dk odak, 5 dk mola turları", () => new PomodoroWidget()),
        new("timer", "Zamanlayıcı", "Verimlilik", "Tek tıkla geri sayım; tekerle süreyi ayarla", () => new TimerWidget()),
        new("stopwatch", "Kronometre", "Verimlilik", "Başlat / duraklat; sağ tıkla sıfırla", () => new StopwatchWidget()),
        new("todos", "Yapılacaklar", "Verimlilik", "Açık işler ve ilerleme halkası", () => new TodosWidget()),
        new("notes", "Notlar", "Verimlilik", "Son not; tıklayınca düzenle", () => new NotesWidget()),

        new("system", "Sistem", "Sistem", "CPU ve RAM halkaları", () => new SystemWidget()),
        new("cpu", "CPU", "Sistem", "İşlemci yükü", () => new CpuWidget()),
        new("ram", "RAM", "Sistem", "Bellek kullanımı", () => new RamWidget()),
        new("network", "Ağ", "Sistem", "İndirme / yükleme hızı ve canlı grafik", () => new NetworkWidget()),
        new("battery", "Pil", "Sistem", "Dolan pil göstergesi", () => new BatteryWidget()),
        new("toggles", "Hızlı ayarlar", "Sistem", "Wi‑Fi ve Bluetooth aç/kapat", () => new QuickTogglesWidget()),
        new("showdesktop", "Masaüstünü göster", "Sistem", "Pencereleri gizle / geri getir", () => new ShowDesktopWidget()),
        new("tray", "Sistem tepsisi", "Sistem", "Gizli tepsi simgeleri; sağ tık ve çift tık görev çubuğundaki gibi", () => new TrayWidget()),
        new("notifications", "Bildirimler", "Sistem", "Bildirim merkezini aç", () => new NotificationsWidget()),
        new("batteryring", "Pil halkası", "Sistem", "Halka biçiminde pil", () => new BatteryRingWidget()),
        new("batterygauge", "Pil göstergesi", "Sistem", "E'den F'ye yakıt göstergesi", () => new BatteryGaugeWidget()),
        new("batterycore", "Pil çekirdeği", "Sistem", "On iki hücreli pil", () => new BatteryCoreWidget()),
        new("temperature", "Sıcaklık", "Sistem", "CPU sıcaklığı, sensör seçimi, °C/°F", () => new TemperatureWidget()),
        new("dnd", "Rahatsız etmeyin", "Sistem", "Windows'un rahatsız etmeyin modu", () => new ActionWidget("\uE708", "Rahatsız etmeyin", DoNotDisturb.Toggle, () => DoNotDisturb.On)),
        new("batterydots", "Pil noktaları", "Sistem", "Nokta matrisi pil", () => new BatteryDotsWidget()),
        new("topcpu", "En çok CPU", "Sistem", "İşlemciyi en çok kullanan üç uygulama", () => new TopProcessesWidget(true)),
        new("topram", "En çok RAM", "Sistem", "Belleği en çok kullanan üç uygulama", () => new TopProcessesWidget(false)),
        new("brightness", "Parlaklık", "Sistem", "Ekran parlaklığı kaydırıcısı", () => new BrightnessWidget()),
        new("language", "Klavye dili", "Sistem", "Geçerli dil; tıkla: değiştir", () => new LanguageWidget()),
        new("theme", "Açık / koyu", "Sistem", "Windows temasını değiştir", () => new ActionWidget("", "Açık / koyu tema", SystemActions.ToggleTheme, () => !SystemActions.LightTheme)),
        new("awake", "Uyanık tut", "Sistem", "Bilgisayarın uyumasını engelle", () => new ActionWidget("", "Uyanık tut", SystemActions.ToggleAwake, () => SystemActions.KeepAwake)),
        new("desktopicons", "Masaüstü simgeleri", "Sistem", "Masaüstü simgelerini gizle / göster", () => new ActionWidget("", "Masaüstü simgeleri", SystemActions.ToggleDesktopIcons, () => SystemActions.DesktopIconsVisible)),
        new("mic", "Mikrofon", "Medya & Ses", "Mikrofonu sustur / aç", () => new ActionWidget("", "Mikrofon (vurgulu: açık)", Audio.ToggleMic, () => Audio.MicMuted is bool m ? !m : null)),
        new("displaymode", "Ekran modu", "Sistem", "Yansıt / genişlet (Win+P)", () => new ActionWidget("", "Ekran modu", () => ShowDesktopWidget.Keys(0x5B, 0x50))),
        new("keyboard", "Ekran klavyesi", "Sistem", "Dokunmatik klavyeyi aç", () => new ActionWidget("", "Ekran klavyesi", () => App.Open("osk.exe"))),
        new("screenshot", "Ekran görüntüsü", "Uygulamalar & Dosyalar", "Ekran, pencere ya da bölge yakala", () => new ActionWidget("", "Ekran görüntüsü (Win+Shift+S)", () => ShowDesktopWidget.Keys(0x5B, 0x10, 0x53))),
        new("settingsbtn", "Iskele ayarları", "Sistem", "Ayarlar penceresini aç", () => new ActionWidget("", "Iskele ayarları", SettingsWindow.Open)),

        new("apps", "Uygulamalar", "Uygulamalar & Dosyalar", "Başlat, sabitlenmiş uygulamalar ve açık pencereler", () => new AppsWidget()),
        new("storage", "Depolama", "Uygulamalar & Dosyalar", "Sürücüdeki boş alan", () => new StorageWidget()),
        new("recycle", "Geri dönüşüm kutusu", "Uygulamalar & Dosyalar", "İçerik; dosya sürükle: sil; sağ tık: boşalt", () => new RecycleBinWidget()),
        new("recent", "Son açılanlar", "Uygulamalar & Dosyalar", "Son açılan dosyalar", () => new RecentWidget()),
        new("search", "Ara & çalıştır", "Uygulamalar & Dosyalar", "Uygulama, klasör, adres ya da web araması", () => new SearchWidget()),

        new("media", "Şimdi çalan", "Medya & Ses", "Kapak, şarkı ve kontroller", () => new MediaWidget()),
        new("mediacompact", "Şimdi çalan (sade)", "Medya & Ses", "Kapak, şarkı, oynat", () => new MediaWidget("compact")),
        new("mediamini", "Şimdi çalan (mini)", "Medya & Ses", "İlerleme halkalı oynat düğmesi", () => new MediaWidget("mini")),
        new("mediabar", "Şimdi çalan (şerit)", "Medya & Ses", "Şarkı, ilerleme çubuğu, atla", () => new MediaWidget("bar")),
        new("volume", "Ses", "Medya & Ses", "Çıkış aygıtı ve ses düzeyi", () => new VolumeWidget()),

        new("weather", "Hava durumu", "Hava durumu", "Sıcaklık, durum, en yüksek / en düşük", () => new WeatherWidget()),
        new("weatherdetails", "Hava ayrıntıları", "Hava durumu", "Rüzgâr, nem, yağış, hissedilen", () => new WeatherDetailsWidget()),
        new("forecast", "Bugün & tahmin", "Hava durumu", "Bugün ve sonraki üç gün", () => new ForecastWidget()),
        new("hourly", "Saatlik", "Hava durumu", "Önümüzdeki altı saat", () => new HourlyWidget()),
        new("sun", "Gün doğumu & batımı", "Hava durumu", "Bugünün gün doğumu ve batımı", () => new SunWidget()),
        new("weatherstatus", "Hava özeti", "Hava durumu", "Durum sözcüklerle", () => new WeatherStatusWidget()),

        new("ailimits", "AI limitleri", "AI", "Claude ve Codex 5 saatlik limitleri tek kartta", () => new AiLimitsWidget()),
        new("claude", "Claude kullanımı", "AI", "Oturum ve haftalık limit, sıfırlanma, en çok kullanılan model", () => new AiLimitWidget("Claude")),
        new("gemini", "Gemini kullanımı", "AI", "Antigravity CLI kotası (Pro / Flash), sıfırlanma", () => new AiLimitWidget("Gemini")),
        new("opencode", "OpenCode kullanımı", "AI", "Bugün ve bu hafta token, en çok kullanılan model", () => new OpenCodeWidget()),
        new("codex", "ChatGPT / Codex kullanımı", "AI", "Codex 5 saatlik ve haftalık pencereler", () => new AiLimitWidget("Codex")),
        new("githuboverview", "GitHub Genel Bakış", "Geliştirici", "Bildirimler, açık PR ve depo yıldızları tek kartta", () => new GitHubWidget(true)),
        new("github", "GitHub", "Geliştirici", "Okunmamış bildirimler, açık PR'lar, yıldızlar", () => new GitHubWidget()),
        new("githubactivity", "GitHub etkinliği", "Geliştirici", "Katkı ızgarası ve seri", () => new GitHubActivityWidget()),

        new("whatsapp", "WhatsApp bildirimleri", "Verimlilik", "Windows bildirimleri; paket yetkisi ve kullanıcı izni gerekir", () => new WhatsAppWidget()),
        new("calendar", "Takvim", "Verimlilik", "Sıradaki etkinlik (Google / Outlook / iCloud iCal)", () => new CalendarWidget()),
        new("currency", "Döviz", "Finans", "Güncel kurla çevirici; sağ tık: yer değiştir", () => new CurrencyWidget()),
        new("crypto", "Kripto", "Borsa", "Binance fiyatı ve 24 saatlik değişim", () => new QuoteListWidget(true)),
        new("stocks", "Borsa listesi", "Borsa", "Üç sembol, fiyat ve günlük değişim", () => new QuoteListWidget(false)),
        new("stockchart", "Borsa grafiği", "Borsa", "Tek sembol, gün içi çizgi", () => new StockChartWidget()),
    ];

    public static bool Exists(string id) => All.Any(w => w.Id == id) || LaunchWidget.IsLaunch(id);
    public static WidgetInfo? Info(string id) => All.FirstOrDefault(w => w.Id == id) ??
        (LaunchWidget.IsLaunch(id) ? new WidgetInfo(id, LaunchWidget.NameOf(id), "Uygulamalar & Dosyalar", LaunchWidget.PathOf(id), () => new LaunchWidget(LaunchWidget.PathOf(id))) : null);

    public static Widget? Create(string id)
    {
        var info = Info(id);
        if (info == null) return null;
        try
        {
            var w = info.Create();
            w.Id = id;
            if (id != "apps") w.ToolTip ??= info.Name;   // uygulama simgelerinin kendi ipuçları var
            return w;
        }
        catch (Exception ex)
        {
            App.Log($"Widget '{id}' oluşturulamadı: {ex}");
            return null;
        }
    }
}

/// <summary>Widget'a tıklayınca dock'un üstünde (veya altında) açılan, odak alabilen küçük pencere.</summary>
class Flyout : Window
{
    static Flyout? current;
    public event Action? Closing2;

    Flyout(UIElement content, double width)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = Ui.Font;
        SizeToContent = SizeToContent.Height;
        Width = width + 36;

        // The shared optical surface keeps the shader/shadow off text and controls.
        Content = new PopupSurface(content,16,new Thickness(14));

        // Native outside clicks also dismiss non-activating dock popups.
        Loaded += (_,_) => TransientDismissal.Add(this,p=>TransientDismissal.Contains(this,p),Close);
        Deactivated += (_,_) => Dispatcher.BeginInvoke(()=>{if(IsVisible&&!IsActive)Close();},DispatcherPriority.Input);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => { TransientDismissal.Remove(this); if (current == this) current = null; Closing2?.Invoke(); };
    }

    public static Flyout? Show(FrameworkElement anchor, UIElement content, double width)
    {
        current?.Close();
        var src = PresentationSource.FromVisual(anchor);
        if (src?.CompositionTarget == null) return null;

        var f = new Flyout(content, width);
        var topLeft = src.CompositionTarget.TransformFromDevice.Transform(anchor.PointToScreen(new Point(0, 0)));
        ((FrameworkElement)f.Content).Measure(new Size(f.Width, double.PositiveInfinity));
        double h = ((FrameworkElement)f.Content).DesiredSize.Height;
        double sw = SystemParameters.PrimaryScreenWidth;

        f.Left = Math.Clamp(topLeft.X + anchor.ActualWidth / 2 - f.Width / 2, -10, sw - f.Width + 10);
        f.Top = DockWindow.IsTop
            ? topLeft.Y + anchor.ActualHeight - 6
            : topLeft.Y - h + 6;
        current = f;
        // Initialize opacity BEFORE Show: no fully opaque first-frame flash.
        // 90: dock yönünden kayarak ve belirerek aç
        if (f.Content is FrameworkElement fr)
        {
            var tt = new TranslateTransform(0, DockWindow.IsTop ? -14 : 14);
            fr.RenderTransform = tt;
            fr.Opacity = SystemParameters.ClientAreaAnimation ? 0 : 1;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            f.Loaded += (_,_) =>
            {
            if(!SystemParameters.ClientAreaAnimation){tt.Y=0;return;}
            tt.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            fr.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            };
        }
        f.Show(); // WPF activates once; do not activate again during the opening transition.
        return f;
    }

    public static void CloseCurrent() => current?.Close();
    public static bool IsOpen => current != null;
}

/// <summary>Küçük çubuk grafik (ör. ekran süresinin saatlik dağılımı).</summary>
class MiniBars : FrameworkElement
{
    double[] values = [];
    public string BrushKey { get; set; } = "Accent";
    public double[] Values { get => values; set { values = value; InvalidateVisual(); } }

    protected override void OnRender(DrawingContext dc)
    {
        if (values.Length == 0 || ActualWidth <= 0) return;
        double max = Math.Max(values.Max(), 1e-9);
        double step = ActualWidth / values.Length;
        double bw = Math.Clamp(step * 0.55, 1.2, 14);
        var brush = Ui.B(BrushKey);
        var baseline = new Pen(Ui.B("Track"), 1);
        dc.DrawLine(baseline, new Point(0, ActualHeight - 0.5), new Point(ActualWidth, ActualHeight - 0.5));
        for (int i = 0; i < values.Length; i++)
        {
            double h = values[i] <= 0 ? 0 : Math.Max(1.5, values[i] / max * (ActualHeight - 1));
            if (h <= 0) continue;
            double rad = Math.Min(Math.Min(bw / 2, h / 2), 3);
            dc.DrawRoundedRectangle(brush, null, new Rect(i * step + (step - bw) / 2, ActualHeight - 1 - h, bw, h), rad, rad);
        }
    }
}

/// <summary>Canlı çizgi grafik (ağ hızı gibi).</summary>
class Sparkline : FrameworkElement
{
    readonly Queue<double> data = new();
    public int Capacity { get; set; } = 30;

    public void Push(double v)
    {
        data.Enqueue(v);
        while (data.Count > Capacity) data.Dequeue();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (data.Count < 2 || ActualWidth <= 0) return;
        var arr = data.ToArray();
        double max = Math.Max(arr.Max(), 1);
        double step = ActualWidth / (Capacity - 1);
        double x0 = ActualWidth - (arr.Length - 1) * step;
        var g = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var c = g.Open())
        using (var f = fill.Open())
        {
            for (int i = 0; i < arr.Length; i++)
            {
                var p = new Point(x0 + i * step, ActualHeight - 1 - arr[i] / max * (ActualHeight - 2));
                if (i == 0) { c.BeginFigure(p, false, false); f.BeginFigure(new Point(p.X, ActualHeight), true, true); f.LineTo(p, false, false); }
                else { c.LineTo(p, true, true); f.LineTo(p, false, false); }
            }
            f.LineTo(new Point(ActualWidth, ActualHeight), false, false);
        }
        var accent = (SolidColorBrush)Ui.B("Accent");
        var area = new SolidColorBrush(Color.FromArgb(60, accent.Color.R, accent.Color.G, accent.Color.B));
        dc.DrawGeometry(area, null, fill);
        dc.DrawGeometry(null, new Pen(accent, 1.5) { LineJoin = PenLineJoin.Round }, g);
    }
}
