using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Iskele;

// ═════════════════════════ Saatler ═════════════════════════

/// <summary>İki kartlı flip saat (SS : DD).</summary>
class FlipClockWidget : Widget
{
    readonly TextBlock hh, mm;

    public FlipClockWidget() : base(card: false)
    {
        hh = Digit(out var a);
        mm = Digit(out var b);
        b.Margin = new Thickness(F(4), 0, 0, 0);
        Child = Ui.Row(a, b);
        OnClick(() => ClockActions.Open(this));
        Tick(DateTime.Now);
    }

    TextBlock Digit(out Border card)
    {
        var t = new TextBlock
        {
            FontFamily = Ui.Font, FontSize = F(24), FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var g = new Grid();
        g.Children.Add(t);
        g.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), VerticalAlignment = VerticalAlignment.Center });
        card = new Border
        {
            Width = H * 1.05, Height = H, CornerRadius = new CornerRadius(F(9)), Child = g,
            Background = new LinearGradientBrush(Color.FromRgb(0x2A, 0x2A, 0x30), Color.FromRgb(0x16, 0x16, 0x1A), 90),
        };
        return t;
    }

    public override void Tick(DateTime now)
    {
        hh.Text = App.Settings.Clock24 ? now.ToString("HH") : now.ToString("hh");
        mm.Text = now.ToString("mm");
    }
}

class AnalogWidget : Widget
{
    readonly AnalogFace face;
    public AnalogWidget() : base(card: false)
    {
        Width = H;
        face = new AnalogFace { Width = H, Height = H };
        Child = face;
        OnClick(() => ClockActions.Open(this));
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now) { face.Time = now; ToolTip = now.ToString("HH:mm:ss"); }
}

/// <summary>Yedi segmentli dijital saat.</summary>
class SegmentDisplay : FrameworkElement
{
    string text = "";
    public string Text { get => text; set { if (text != value) { text = value; InvalidateVisual(); } } }
    public string BrushKey { get; set; } = "Accent";

    // a b c d e f g
    static readonly Dictionary<char, int> Map = new()
    {
        ['0'] = 0b1111110, ['1'] = 0b0110000, ['2'] = 0b1101101, ['3'] = 0b1111001, ['4'] = 0b0110011,
        ['5'] = 0b1011011, ['6'] = 0b1011111, ['7'] = 0b1110000, ['8'] = 0b1111111, ['9'] = 0b1111011,
    };

    protected override void OnRender(DrawingContext dc)
    {
        if (text.Length == 0 || ActualHeight <= 0) return;
        double h = ActualHeight, w = h * 0.52, t = Math.Max(2, h * 0.11), gap = h * 0.14;
        var on = Ui.B(BrushKey);
        var off = Ui.B("Track");
        double x = 0;
        foreach (var ch in text)
        {
            if (ch == ':')
            {
                dc.DrawEllipse(on, null, new Point(x + t / 2, h * 0.32), t / 2, t / 2);
                dc.DrawEllipse(on, null, new Point(x + t / 2, h * 0.68), t / 2, t / 2);
                x += t + gap;
                continue;
            }
            int bits = Map.GetValueOrDefault(ch);
            Rect[] seg =
            [
                new(x + t, 0, w - 2 * t, t),                 // a
                new(x + w - t, t, t, h / 2 - t * 1.5),        // b
                new(x + w - t, h / 2 + t / 2, t, h / 2 - t * 1.5), // c
                new(x + t, h - t, w - 2 * t, t),             // d
                new(x, h / 2 + t / 2, t, h / 2 - t * 1.5),   // e
                new(x, t, t, h / 2 - t * 1.5),               // f
                new(x + t, h / 2 - t / 2, w - 2 * t, t),     // g
            ];
            for (int i = 0; i < 7; i++)
                dc.DrawRoundedRectangle((bits >> (6 - i) & 1) == 1 ? on : off, null, seg[i], t / 2, t / 2);
            x += w + gap;
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        double h = double.IsInfinity(available.Height) ? 24 : available.Height;
        double w = 0;
        foreach (var ch in text) w += ch == ':' ? h * 0.11 + h * 0.14 : h * 0.52 + h * 0.14;
        return new Size(w, h);
    }
}

class SegmentClockWidget : Widget
{
    readonly SegmentDisplay seg = new();
    public SegmentClockWidget()
    {
        seg.Height = H * 0.5;
        seg.VerticalAlignment = VerticalAlignment.Center;
        Child = seg;
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        seg.Text = (App.Settings.Clock24 ? now.ToString("HH:mm") : now.ToString("hh:mm"));
        seg.InvalidateMeasure();
    }
}

class WorldTimeWidget : Widget
{
    readonly TextBlock city, time, offset;
    public WorldTimeWidget()
    {
        city = Ui.Text("", F(9.5), "Accent", FontWeights.Bold);
        time = Ui.Text("", F(17), "Fg", FontWeights.Bold);
        offset = Ui.Text("", F(9.5), "FgDim");
        offset.Margin = new Thickness(F(8), F(14), 0, 0);
        Child = Ui.Row(Ui.Col(city, time), offset);
        ToolTip = "Şehri değiştirmek için tıkla";
        OnClick(Pick);
        Tick(DateTime.Now);
    }

    static readonly (string Name, string Tz)[] Cities =
    [
        ("Londra", "GMT Standard Time"), ("Berlin", "W. Europe Standard Time"), ("Dubai", "Arabian Standard Time"),
        ("Moskova", "Russian Standard Time"), ("Tokyo", "Tokyo Standard Time"), ("Pekin", "China Standard Time"),
        ("Sidney", "AUS Eastern Standard Time"), ("New York", "Eastern Standard Time"), ("Los Angeles", "Pacific Standard Time"),
        ("Taşkent", "West Asia Standard Time"), ("Bakü", "Azerbaijan Standard Time"), ("Delhi", "India Standard Time"),
    ];

    public override void Tick(DateTime now)
    {
        var (name, tzId) = Cities.FirstOrDefault(c => c.Tz == App.Settings.WorldCity);
        if (name == null) (name, tzId) = Cities[4];
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            var t = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var diff = t - now;
            city.Text = name.ToUpper(Tr.C);
            time.Text = Tr.Time(t);
            int hrs = (int)Math.Round(diff.TotalHours);
            offset.Text = hrs == 0 ? "aynı" : (hrs > 0 ? $"+{hrs} sa" : $"{hrs} sa");
        }
        catch { city.Text = name; }
    }

    void Pick()
    {
        var m = new ContextMenu();
        foreach (var (n, tz) in Cities)
            m.Items.Add(DockWindow.Item(n, () => { App.Settings.WorldCity = tz; App.Settings.Save(); Tick(DateTime.Now); }));
        m.PlacementTarget = this;
        m.IsOpen = true;
    }
}

class TodayWidget : Widget
{
    readonly TextBlock day, date, time, temp;
    public TodayWidget()
    {
        day = Ui.Text("", F(9.5), "Warn", FontWeights.Bold);
        date = Ui.Text("", F(13), "Fg", FontWeights.Bold);
        time = Ui.Text("", F(17), "Fg", FontWeights.Bold);
        temp = Ui.Text("", F(10), "FgDim", FontWeights.SemiBold);
        var left = Ui.Col(day, date);
        left.Margin = new Thickness(0, 0, F(12), 0);
        var right = Ui.Col(time, temp);
        temp.HorizontalAlignment = HorizontalAlignment.Right;
        Child = Ui.Row(left, right);
        OnClick(() => ClockActions.Open(this));
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        WeatherService.Ensure();
        day.Text = now.ToString("dddd", Tr.C).ToUpper(Tr.C);
        date.Text = now.ToString("d MMMM", Tr.C);
        time.Text = Tr.Time(now);
        temp.Text = WeatherService.Temp is double t ? $"{WeatherService.Describe(WeatherService.Code).Icon} {t:0}°" : "";
    }
}

class DateWidget : Widget
{
    readonly TextBlock top, bottom;
    public DateWidget()
    {
        top = Ui.Text("", F(15), "Fg", FontWeights.Bold);
        bottom = Ui.Text("", F(10), "FgDim", FontWeights.SemiBold);
        Child = Ui.Col(top, bottom);
        OnClick(() => ClockActions.Open(this));
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        top.Text = now.ToString("d MMMM", Tr.C);
        bottom.Text = now.ToString("dddd, yyyy", Tr.C);
    }
}

/// <summary>Haftalık şerit: Pazartesi–Pazar, bugün vurgulu; altta bugünün sıcaklığı.</summary>
class WeekStripWidget : Widget
{
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    DateTime drawn;
    public WeekStripWidget()
    {
        Child = row;
        OnClick(() => ClockActions.Open(this));
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        if (drawn.Date == now.Date && drawn.Minute == now.Minute) return;
        drawn = now;
        WeatherService.Ensure();
        row.Children.Clear();
        var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        for (int i = 0; i < 7; i++)
        {
            var d = monday.AddDays(i);
            bool today = d == now.Date;
            var name = Ui.Text(d.ToString("ddd", Tr.C)[..1].ToUpper(Tr.C), F(8.5), today ? "Accent" : "FgDim", FontWeights.Bold);
            var num = Ui.Text(d.Day.ToString(), F(12), today ? "Fg" : "FgDim", today ? FontWeights.Bold : FontWeights.SemiBold);
            name.HorizontalAlignment = num.HorizontalAlignment = HorizontalAlignment.Center;
            var col = Ui.Col(name, num);
            var cell = new Border { Child = col, Width = F(22), CornerRadius = new CornerRadius(F(7)), Padding = new Thickness(0, F(2), 0, F(2)) };
            if (today) cell.SetResourceReference(BackgroundProperty, "Track");
            row.Children.Add(cell);
        }
        if (WeatherService.Temp is double t)
        {
            var tt = Ui.Text($"{t:0}°", F(13), "Fg", FontWeights.Bold);
            tt.Margin = new Thickness(F(8), 0, 0, 0);
            row.Children.Add(tt);
        }
    }
}

/// <summary>Üç renkli blok: saat, tarih, hava.</summary>
class ClockWeatherWidget : Widget
{
    readonly TextBlock time, date, temp;
    public ClockWeatherWidget() : base(card: false)
    {
        Border Block(Color c, TextBlock t)
        {
            t.Foreground = Brushes.White;
            return new Border { Background = new SolidColorBrush(c), CornerRadius = new CornerRadius(F(10)), Padding = new Thickness(F(10), 0, F(10), 0), Margin = new Thickness(0, 0, F(4), 0), Child = t, Height = H };
        }
        time = Ui.Text("", F(16), "Fg", FontWeights.Bold);
        date = Ui.Text("", F(13), "Fg", FontWeights.Bold);
        temp = Ui.Text("", F(13), "Fg", FontWeights.Bold);
        Child = Ui.Row(Block(Color.FromRgb(0xFF, 0x6B, 0x3D), time), Block(Color.FromRgb(0x7C, 0x5C, 0xFF), date), Block(Color.FromRgb(0x1F, 0xA8, 0xE0), temp));
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        WeatherService.Ensure();
        time.Text = Tr.Time(now);
        date.Text = now.ToString("d MMM", Tr.C);
        temp.Text = WeatherService.Temp is double t ? $"{WeatherService.Describe(WeatherService.Code).Icon} {t:0}°" : "—";
    }
}

class ProgressWidget : Widget
{
    readonly bool month;
    readonly RingGauge ring = new() { Stroke = 3.5 };
    readonly TextBlock pct, label, sub;

    public ProgressWidget(bool month)
    {
        this.month = month;
        double r = H - F(12);
        pct = Ui.Text("", F(10), "Fg", FontWeights.Bold);
        pct.HorizontalAlignment = HorizontalAlignment.Center;
        var g = new Grid { Width = r, Height = r, Margin = new Thickness(0, 0, F(9), 0) };
        g.Children.Add(ring);
        g.Children.Add(pct);
        label = Ui.Text(month ? "Ay" : "Gün", F(9.5), "FgDim", FontWeights.SemiBold);
        sub = Ui.Text("", F(12), "Fg", FontWeights.Bold);
        Child = Ui.Row(g, Ui.Col(label, sub));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        double frac;
        if (month)
        {
            var start = new DateTime(now.Year, now.Month, 1);
            frac = (now - start).TotalSeconds / (start.AddMonths(1) - start).TotalSeconds;
            sub.Text = $"{DateTime.DaysInMonth(now.Year, now.Month) - now.Day} gün kaldı";
        }
        else
        {
            frac = now.TimeOfDay.TotalSeconds / 86400;
            var left = TimeSpan.FromDays(1) - now.TimeOfDay;
            sub.Text = $"{(int)left.TotalHours} sa {left.Minutes} dk kaldı";
        }
        ring.Value = frac;
        pct.Text = $"{frac * 100:0}";
    }
}

// ═════════════════════════ Pil çeşitleri ═════════════════════════

static class Power
{
    public static (int Pct, bool Charging, bool HasBattery) Read()
    {
        if (!Native.GetSystemPowerStatus(out var p) || p.BatteryFlag == 128 || p.BatteryLifePercent > 100) return (100, true, false);
        return (p.BatteryLifePercent, p.ACLineStatus == 1, true);
    }
}

class BatteryRingWidget : Widget
{
    readonly RingGauge ring = new() { Stroke = 4 };
    readonly TextBlock text;
    public BatteryRingWidget()
    {
        Width = H;
        Padding = new Thickness(F(4));
        text = Ui.Text("", F(11), "Fg", FontWeights.Bold);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        var g = new Grid();
        g.Children.Add(ring);
        g.Children.Add(text);
        Child = g;
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        var (pct, ch, has) = Power.Read();
        ring.Value = pct / 100.0;
        text.Text = has ? (ch ? "⚡" : "") + pct : "AC";
        ToolTip = has ? $"Pil %{pct}{(ch ? " · şarj oluyor" : "")}" : "Pil yok";
    }
}

class BatteryDotsWidget : Widget
{
    readonly UniformGrid dots = new() { Rows = 2, Columns = 10 };
    readonly TextBlock text;
    public BatteryDotsWidget()
    {
        double d = Math.Round(H * 0.16);
        for (int i = 0; i < 20; i++)
            dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = d, Height = d, Margin = new Thickness(Math.Max(1, d * 0.2)) });
        text = Ui.Text("", F(13), "Fg", FontWeights.Bold);
        text.Margin = new Thickness(F(8), 0, 0, 0);
        Child = Ui.Row(dots, text);
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        var (pct, ch, has) = Power.Read();
        int on = (int)Math.Round(pct / 5.0);
        string key = ch ? "Accent" : pct <= 20 ? "Warn" : "Good";
        for (int i = 0; i < 20; i++)
            ((System.Windows.Shapes.Ellipse)dots.Children[i]).SetResourceReference(System.Windows.Shapes.Shape.FillProperty, i < on ? key : "Track");
        text.Text = has ? $"%{pct}" : "AC";
    }
}

// ═════════════════════════ Sistem ═════════════════════════

/// <summary>En çok CPU ya da RAM kullanan üç uygulama (aynı adlı süreçler toplanır).</summary>
class TopProcessesWidget : Widget
{
    readonly bool cpu;
    readonly StackPanel list = new() { VerticalAlignment = VerticalAlignment.Center };
    static Dictionary<int, TimeSpan> prev = [];
    static DateTime prevAt;
    static List<(string Name, double Value)> cpuTop = [], ramTop = [];
    static DateTime computed;
    int n;

    public TopProcessesWidget(bool cpu)
    {
        this.cpu = cpu;
        list.Width = F(150);
        var head = Ui.Text(cpu ? "En çok CPU" : "En çok RAM", F(8.5), "Accent", FontWeights.Bold);
        Child = Ui.Col(head, list);
        OnClick(() => App.Open("taskmgr.exe"));
    }

    static void Compute()
    {
        if ((DateTime.Now - computed).TotalSeconds < 2.5) return;
        computed = DateTime.Now;
        var now = DateTime.Now;
        var cur = new Dictionary<int, TimeSpan>();
        var cpuBy = new Dictionary<string, double>();
        var ramBy = new Dictionary<string, double>();
        double elapsed = (now - prevAt).TotalMilliseconds * Environment.ProcessorCount;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0) continue;
                string name = p.ProcessName;
                ramBy[name] = ramBy.GetValueOrDefault(name) + p.WorkingSet64;
                var t = p.TotalProcessorTime;
                cur[p.Id] = t;
                if (prev.TryGetValue(p.Id, out var old) && elapsed > 0)
                    cpuBy[name] = cpuBy.GetValueOrDefault(name) + (t - old).TotalMilliseconds / elapsed;
            }
            catch { }
            finally { p.Dispose(); }
        }
        prev = cur;
        prevAt = now;
        cpuTop = cpuBy.OrderByDescending(kv => kv.Value).Take(3).Select(kv => (kv.Key, kv.Value)).ToList();
        ramTop = ramBy.OrderByDescending(kv => kv.Value).Take(3).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    public override void Tick(DateTime now)
    {
        if (n++ % 3 != 0) return;
        Compute();
        list.Children.Clear();
        foreach (var (name, v) in cpu ? cpuTop : ramTop)
        {
            var d = new DockPanel();
            var val = Ui.Text(cpu ? $"%{v * 100:0}" : $"{v / 1073741824:0.0} GB", F(9.5), "Fg", FontWeights.Bold);
            DockPanel.SetDock(val, Dock.Right);
            d.Children.Add(val);
            d.Children.Add(Ui.Text(name, F(9.5), "FgDim"));
            list.Children.Add(d);
        }
    }
}

class LanguageWidget : Widget
{
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    readonly TextBlock code;
    public LanguageWidget()
    {
        Width = H;
        Padding = new Thickness(0);
        code = Ui.Text("", F(13), "Fg", FontWeights.Bold);
        code.HorizontalAlignment = HorizontalAlignment.Center;
        Child = code;
        ToolTip = "Klavye dili · tıkla: değiştir";
        OnClick(() => ShowDesktopWidget.Keys(0x5B, 0x20));   // Win + Space
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        var fg = Native.GetForegroundWindow();
        uint thread = Native.GetWindowThreadProcessId(fg, out _);
        int lang = (int)GetKeyboardLayout(thread) & 0xFFFF;
        try { code.Text = new System.Globalization.CultureInfo(lang).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch { code.Text = "?"; }
    }
}

/// <summary>Tek dokunuşlu sistem düğmeleri (tema, uyanık tut, masaüstü simgeleri, ekran modu, klavye, ekran görüntüsü, mikrofon).</summary>
class ActionWidget : Widget
{
    readonly Func<bool?>? state;
    readonly TextBlock glyph;
    readonly TextBlock? label;

    public ActionWidget(string g, string tip, Action act, Func<bool?>? state = null, string? text = null)
    {
        this.state = state;
        Padding = new Thickness(text == null ? 0 : F(10), 0, text == null ? 0 : F(12), 0);
        if (text == null) Width = H;
        glyph = Ui.Glyph(g, F(16));
        if (text != null)
        {
            label = Ui.Text(text, F(11.5), "Fg", FontWeights.SemiBold);
            label.Margin = new Thickness(F(7), 0, 0, 0);
            Child = Ui.Row(glyph, label);
        }
        else Child = glyph;
        ToolTip = tip;
        OnClick(() => { act(); Tick(DateTime.Now); });
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        if (state == null) return;
        bool? on = state();
        if (on == true)
        {
            SetResourceReference(BackgroundProperty, "Accent");
            glyph.Foreground = Brushes.White;
            if (label != null) label.Foreground = Brushes.White;
        }
        else
        {
            SetResourceReference(BackgroundProperty, "CardBg");
            glyph.SetResourceReference(TextBlock.ForegroundProperty, on == null ? "FgDim" : "Fg");
            label?.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        }
    }
}

static class SystemActions
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    public static bool LightTheme
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return (k?.GetValue("AppsUseLightTheme") as int?) == 1;
        }
    }

    public static void ToggleTheme()
    {
        int v = LightTheme ? 0 : 1;
        using (var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey, true))
        {
            k?.SetValue("AppsUseLightTheme", v, RegistryValueKind.DWord);
            k?.SetValue("SystemUsesLightTheme", v, RegistryValueKind.DWord);
        }
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "ImmersiveColorSet", 2, 500, out _);
    }

    public static bool KeepAwake { get; private set; }

    public static void ToggleAwake()
    {
        KeepAwake = !KeepAwake;
        // ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED (bu iş parçacığı yaşadıkça geçerli)
        SetThreadExecutionState(KeepAwake ? 0x80000003u : 0x80000000u);
    }

    static IntPtr DefView()
    {
        var progman = Native.FindWindow("Progman", null);
        var view = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view != IntPtr.Zero) return view;
        var w = IntPtr.Zero;
        while ((w = Native.FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
        {
            view = Native.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero) return view;
        }
        return IntPtr.Zero;
    }

    public static bool DesktopIconsVisible
    {
        get
        {
            var list = Native.FindWindowEx(DefView(), IntPtr.Zero, "SysListView32", null);
            return list != IntPtr.Zero && Native.IsWindowVisible(list);
        }
    }

    public static void ToggleDesktopIcons()
    {
        var v = DefView();
        if (v != IntPtr.Zero) SendMessage(v, 0x0111 /* WM_COMMAND */, (IntPtr)0x7402, IntPtr.Zero);
    }
}

/// <summary>Ekran parlaklığı (dizüstü ekranlarında WMI ile).</summary>
class BrightnessWidget : Widget
{
    readonly MiniSlider slider = new();
    readonly TextBlock pct;
    static int? level;
    static bool supported = true, reading;
    DispatcherTimer? apply;
    double pending;

    public BrightnessWidget()
    {
        var head = new DockPanel();
        pct = Ui.Text("", F(10), "FgDim", FontWeights.SemiBold);
        DockPanel.SetDock(pct, Dock.Right);
        head.Children.Add(pct);
        head.Children.Add(Ui.Row(Ui.Glyph("", F(11), "FgDim"), Pad(Ui.Text("Parlaklık", F(11), "Fg", FontWeights.Bold))));
        slider.Height = F(14);
        slider.Margin = new Thickness(0, F(4), 0, 0);
        slider.ValueChanged += v =>
        {
            pending = v;
            pct.Text = $"%{v * 100:0}";
            apply ??= new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) => { apply!.Stop(); Set((int)Math.Round(pending * 100)); }, Dispatcher);
            apply.Stop();
            apply.Start();
        };
        var col = Ui.Col(head, slider);
        col.Width = F(118);
        Child = col;
        MouseWheel += (_, e) => { e.Handled = true; if (level is int l) { Set(Math.Clamp(l + Math.Sign(e.Delta) * 5, 0, 100)); Tick(DateTime.Now); } };
        if (level == null) _ = ReadAsync();
        Tick(DateTime.Now);
    }

    static FrameworkElement Pad(FrameworkElement e) { e.Margin = new Thickness(5, 0, 0, 0); return e; }

    static async Task ReadAsync()
    {
        if (reading) return;
        reading = true;
        await Task.Run(() =>
        {
            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in s.Get()) { level = Convert.ToInt32(o["CurrentBrightness"]); break; }
                if (level == null) supported = false;
            }
            catch { supported = false; }
        });
        reading = false;
    }

    static void Set(int v)
    {
        level = v;
        Task.Run(() =>
        {
            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject o in s.Get()) o.InvokeMethod("WmiSetBrightness", [1u, (byte)v]);
            }
            catch (Exception ex) { App.Log(ex.Message); }
        });
    }

    public override void Tick(DateTime now)
    {
        if (!supported) { pct.Text = "desteklenmiyor"; slider.IsEnabled = false; return; }
        if (level is int l && !slider.IsMouseCaptured)
        {
            slider.Value = l / 100.0;
            pct.Text = $"%{l}";
        }
        if (now.Second % 20 == 0) _ = ReadAsync();
    }
}

// ═════════════════════════ Uygulamalar & dosyalar ═════════════════════════

class RecycleBinWidget : Widget
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd; public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr h, string? root, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    readonly TextBlock count, size;
    int n;

    public RecycleBinWidget()
    {
        count = Ui.Text("", F(12.5), "Fg", FontWeights.Bold);
        size = Ui.Text("", F(9.5), "FgDim");
        var col = Ui.Col(count, size);
        col.Margin = new Thickness(F(8), 0, 0, 0);
        col.MinWidth = F(60);
        Child = Ui.Row(Chip("", Color.FromRgb(0x8E, 0x8E, 0x93), H * 0.62), col);
        ToolTip = "Geri dönüşüm kutusu · dosya sürükle: sil · sağ tık: boşalt";
        OnClick(() => App.Open("shell:RecycleBinFolder"));
        ContextMenu = new ContextMenu();
        ContextMenu.Items.Add(DockWindow.Item("Aç", () => App.Open("shell:RecycleBinFolder")));
        ContextMenu.Items.Add(DockWindow.Item("Boşalt…", () => { SHEmptyRecycleBin(IntPtr.Zero, null, 0); n = 0; Tick(DateTime.Now); }));
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            var op = new SHFILEOPSTRUCT { wFunc = 3 /* FO_DELETE */, pFrom = string.Join("\0", files) + "\0\0", fFlags = 0x40 /* FOF_ALLOWUNDO */ };
            SHFileOperation(ref op);
            n = 0;
            Tick(DateTime.Now);
        };
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        if (n++ % 10 != 0) return;
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        if (SHQueryRecycleBin(null, ref info) != 0) return;
        count.Text = info.i64NumItems == 0 ? "Boş" : $"{info.i64NumItems} öğe";
        size.Text = info.i64Size < 1048576 ? $"{info.i64Size / 1024} KB" : info.i64Size < 1073741824 ? $"{info.i64Size / 1048576.0:0} MB" : $"{info.i64Size / 1073741824.0:0.0} GB";
    }
}

class RecentWidget : Widget
{
    readonly TextBlock name, when;
    int n;
    static string RecentDir => Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    public RecentWidget()
    {
        name = Ui.Text("", F(11.5), "Fg", FontWeights.Bold);
        when = Ui.Text("Son açılan", F(9.5), "FgDim");
        var col = Ui.Col(when, name);
        col.Width = F(120);
        col.Margin = new Thickness(F(8), 0, 0, 0);
        Child = Ui.Row(Chip("", Color.FromRgb(0xFF, 0x9F, 0x0A), H * 0.62), col);
        OnClick(ShowList);
        Tick(DateTime.Now);
    }

    static List<FileInfo> Items(int take) =>
        Directory.Exists(RecentDir)
            ? new DirectoryInfo(RecentDir).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(take).ToList()
            : [];

    public override void Tick(DateTime now)
    {
        if (n++ % 15 != 0) return;
        var f = Items(1).FirstOrDefault();
        name.Text = f == null ? "—" : Path.GetFileNameWithoutExtension(f.Name);
    }

    void ShowList()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("Son açılanlar", 16, "Fg", FontWeights.Bold));
        foreach (var f in Items(12))
        {
            var file = f;
            var icon = IconCache.Get(file.FullName);
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var t = Ui.Text(file.LastWriteTime.ToString("d MMM HH:mm", Tr.C), 11, "FgDim");
            DockPanel.SetDock(t, Dock.Right);
            row.Children.Add(t);
            if (icon != null) row.Children.Add(new Image { Source = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(Ui.Text(Path.GetFileNameWithoutExtension(file.Name), 13, "Fg"));
            row.MouseLeftButtonUp += (_, _) => { Flyout.CloseCurrent(); App.Open(file.FullName); };
            panel.Children.Add(row);
        }
        Flyout.Show(this, panel, 360);
    }
}

/// <summary>Ara ve çalıştır: uygulama adı, klasör yolu, web adresi ya da web araması.</summary>
class SearchWidget : Widget
{
    static List<(string Name, string Path)>? apps;

    public SearchWidget()
    {
        var t = Ui.Text("Ara veya çalıştır", F(11.5), "FgDim");
        t.Margin = new Thickness(F(7), 0, F(6), 0);
        Child = Ui.Row(Ui.Glyph("", F(13), "FgDim"), t);
        OnClick(Open);
    }

    static List<(string Name, string Path)> Apps()
    {
        if (apps != null) return apps;
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        apps = dirs.Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.lnk", SearchOption.AllDirectories))
            .Select(f => (Path.GetFileNameWithoutExtension(f), f))
            .Where(a => !a.Item1.Contains("uninstall", StringComparison.OrdinalIgnoreCase) && !a.Item1.Contains("kaldır", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(a => a.Item1.ToLowerInvariant())
            .OrderBy(a => a.Item1)
            .ToList();
        return apps;
    }

    void Open()
    {
        var panel = new StackPanel();
        var input = TodosWidget.Input("Uygulama, klasör yolu, adres ya da arama…");
        var results = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(input.Host);
        panel.Children.Add(results);
        List<(string Name, string Path)> current = [];

        void Run(string q)
        {
            Flyout.CloseCurrent();
            q = q.Trim();
            if (q.Length == 0) return;
            if (Directory.Exists(q) || File.Exists(q)) App.Open(q);
            else if (q.StartsWith("http", StringComparison.OrdinalIgnoreCase) || (q.Contains('.') && !q.Contains(' ') && q.Length > 3)) App.Open(q.StartsWith("http") ? q : "https://" + q);
            else App.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(q));
        }

        void Update()
        {
            var q = input.Box.Text.Trim();
            results.Children.Clear();
            current = q.Length == 0 ? [] : Apps().Where(a => a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(a => a.Name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1).Take(7).ToList();
            foreach (var (n, p) in current)
            {
                var path = p;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                var ic = IconCache.Get(path);
                if (ic != null) row.Children.Add(new Image { Source = ic, Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0) });
                row.Children.Add(Ui.Text(n, 13, "Fg"));
                row.MouseLeftButtonUp += (_, _) => { Flyout.CloseCurrent(); App.Open(path); };
                results.Children.Add(row);
            }
            if (q.Length > 0)
            {
                var web = Ui.Text(Directory.Exists(q) ? $"📁 {q} klasörünü aç" : $"🔍 Web'de ara: {q}", 12.5, "Accent");
                web.Margin = new Thickness(0, 6, 0, 0);
                web.Cursor = Cursors.Hand;
                web.MouseLeftButtonUp += (_, _) => Run(q);
                results.Children.Add(web);
            }
        }

        input.Box.TextChanged += (_, _) => Update();
        input.Box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (current.Count > 0) { Flyout.CloseCurrent(); App.Open(current[0].Path); }
            else Run(input.Box.Text);
        };
        Flyout.Show(this, panel, 380);
        input.Box.Focus();
    }
}
