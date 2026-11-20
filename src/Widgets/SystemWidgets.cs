using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows.Media.Effects;
using Ellipse = System.Windows.Shapes.Ellipse;
using Shape = System.Windows.Shapes.Shape;
using Windows.Devices.Radios;

namespace Iskele;

/// <summary>Tüm widget'ların paylaştığı sistem ölçümleri (saniyede en fazla bir kez güncellenir).</summary>
static class SysStats
{
    static DateTime last = DateTime.MinValue;
    static ulong prevIdle, prevTotal;
    static long prevRx = -1, prevTx = -1;
    static DateTime prevNet;

    public static double Cpu { get; private set; }
    public static double Ram { get; private set; }
    public static double RamUsedGb { get; private set; }
    public static double RamTotalGb { get; private set; }
    public static double Down { get; private set; }   // bayt/sn
    public static double Up { get; private set; }

    public static void Update()
    {
        var now = DateTime.Now;
        if ((now - last).TotalMilliseconds < 900) return;
        last = now;

        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong total = kernel.Value + user.Value;
            ulong dIdle = idle.Value - prevIdle, dTotal = total - prevTotal;
            if (prevTotal != 0 && dTotal > 0) Cpu = Math.Clamp(1.0 - (double)dIdle / dTotal, 0, 1);
            prevIdle = idle.Value;
            prevTotal = total;
        }

        var m = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref m))
        {
            RamTotalGb = m.ullTotalPhys / 1073741824.0;
            RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            Ram = RamUsedGb / Math.Max(RamTotalGb, 0.001);
        }

        long rx = 0, tx = 0;
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = n.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
        }
        catch { }
        if (prevRx >= 0)
        {
            double sec = Math.Max((now - prevNet).TotalSeconds, 0.1);
            Down = Math.Max(0, (rx - prevRx) / sec);
            Up = Math.Max(0, (tx - prevTx) / sec);
        }
        prevRx = rx; prevTx = tx; prevNet = now;
    }

    public static string Speed(double bps)
    {
        if (bps < 1024) return $"{bps:0} B/sn";
        if (bps < 1024 * 1024) return $"{bps / 1024:0} KB/sn";
        return $"{bps / 1048576:0.0} MB/sn";
    }
}

static class Tr
{
    public static CultureInfo C => Localization.Culture;
    public static string Time(DateTime t, bool seconds = false)
    {
        if (App.Settings.Clock24) return t.ToString(seconds ? "HH:mm:ss" : "HH:mm", C);
        return t.ToString(seconds ? "h:mm:ss" : "h:mm", C);
    }
    public static string AmPm(DateTime t) => App.Settings.Clock24 ? "" : (App.Settings.Language == "en" ? (t.Hour < 12 ? "AM" : "PM") : (t.Hour < 12 ? "ÖÖ" : "ÖS"));
}

// ───────────────────────── Saatler ─────────────────────────

class ClockWidget : Widget
{
    readonly TextBlock day, date, time, ampm;

    public ClockWidget()
    {
        day = Ui.Text("", F(9.5), "Warn", FontWeights.Bold);
        date = Ui.Text("", F(15), "Fg", FontWeights.Bold);
        time = Ui.Text("", F(17), "Fg", FontWeights.Bold);
        ampm = Ui.Text("", F(9.5), "FgDim", FontWeights.SemiBold);
        ampm.Margin = new Thickness(2, F(4), 0, 0);
        ampm.VerticalAlignment = VerticalAlignment.Top;
        var left = Ui.Col(day, date);
        left.Margin = new Thickness(0, 0, F(12), 0);
        Child = Ui.Row(left, Ui.Row(time, ampm));
        OnClick(() => ClockActions.Open(this));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        day.Text = now.ToString("dddd", Tr.C).ToUpper(Tr.C);
        date.Text = now.ToString("d MMM", Tr.C);
        time.Text = Tr.Time(now);
        ampm.Text = Tr.AmPm(now);
    }
}

class BigClockWidget : Widget
{
    readonly TextBlock time, sec;

    public BigClockWidget()
    {
        time = Ui.Text("", F(22), "Fg", FontWeights.Bold);
        sec = Ui.Text("", F(12), "Accent", FontWeights.Bold);
        sec.Margin = new Thickness(F(3), F(5), 0, 0);
        sec.VerticalAlignment = VerticalAlignment.Top;
        Child = Ui.Row(time, sec);
        OnClick(() => ClockActions.Open(this));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        time.Text = Tr.Time(now);
        sec.Text = now.ToString("ss");
    }
}

class DayNumberWidget : Widget
{
    readonly TextBlock top, num;

    public DayNumberWidget() : base(card: false)
    {
        Width = H;
        top = new TextBlock { FontSize = F(8.5), FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = Ui.Font };
        num = Ui.Text("", F(19), "Fg", FontWeights.Bold);
        num.HorizontalAlignment = HorizontalAlignment.Center;
        var head = new Border { Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)), Child = top, Padding = new Thickness(0, 1, 0, 1) };
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.Children.Add(head);
        Grid.SetRow(num, 1);
        body.Children.Add(num);
        var page = new Border { CornerRadius = new CornerRadius(F(10)), ClipToBounds = true, Child = body, BorderThickness = new Thickness(1) };
        page.SetResourceReference(Border.BackgroundProperty, "CardBg");
        page.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        page.Clip = null;
        Child = page;
        OnClick(() => ClockActions.Open(this));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        top.Text = now.ToString("ddd", Tr.C).ToUpper(Tr.C);
        num.Text = now.Day.ToString();
    }
}

/// <summary>Analog saat kadranı.</summary>
class AnalogFace : FrameworkElement
{
    DateTime time;
    public DateTime Time { get => time; set { time = value; InvalidateVisual(); } }

    protected override void OnRender(DrawingContext dc)
    {
        double r = Math.Min(ActualWidth, ActualHeight) / 2;
        if (r <= 1) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        bool day = time.Hour is >= 7 and < 19;
        dc.DrawEllipse(day ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x22)), new Pen(Ui.B("CardBorder"), 1), c, r - 0.5, r - 0.5);
        var ink = day ? Brushes.Black : Brushes.White;
        void Hand(double angleDeg, double len, double thick, Brush b)
        {
            double a = angleDeg * Math.PI / 180;
            dc.DrawLine(new Pen(b, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, c,
                new Point(c.X + Math.Sin(a) * len, c.Y - Math.Cos(a) * len));
        }
        Hand((time.Hour % 12 + time.Minute / 60.0) * 30, r * 0.5, Math.Max(1.6, r * 0.12), ink);
        Hand((time.Minute + time.Second / 60.0) * 6, r * 0.75, Math.Max(1.2, r * 0.08), ink);
        Hand(time.Second * 6, r * 0.8, 1, new SolidColorBrush(Color.FromRgb(0xFF, 0x95, 0x00)));
        dc.DrawEllipse(ink, null, c, 1.6, 1.6);
    }
}

class WorldClockWidget : Widget
{
    static readonly (string City, string Tz)[] Cities =
        [("Londra", "GMT Standard Time"), ("New York", "Eastern Standard Time"), ("Tokyo", "Tokyo Standard Time")];
    readonly List<(AnalogFace face, TimeZoneInfo? tz)> faces = [];

    public WorldClockWidget()
    {
        var row = Ui.Row();
        foreach (var (city, tzId) in Cities)
        {
            TimeZoneInfo? tz = null;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); } catch { }
            var face = new AnalogFace { Width = H * 0.62, Height = H * 0.62, HorizontalAlignment = HorizontalAlignment.Center };
            var label = Ui.Text(city, F(8), "FgDim", FontWeights.SemiBold);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var col = Ui.Col(face, label);
            col.Margin = new Thickness(F(4), 0, F(4), 0);
            row.Children.Add(col);
            faces.Add((face, tz));
        }
        Child = row;
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        var utc = DateTime.UtcNow;
        foreach (var (face, tz) in faces)
            face.Time = tz == null ? now : TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }
}

class YearProgressWidget : Widget
{
    readonly UniformGrid dots = new() { Rows = 3, Columns = 16 };
    readonly TextBlock label, pct;

    public YearProgressWidget()
    {
        double d = Math.Round(H * 0.13);
        for (int i = 0; i < 48; i++)
            dots.Children.Add(new Ellipse { Width = d, Height = d, Margin = new Thickness(Math.Max(1, d * 0.18)) });
        label = Ui.Text("", F(8), "FgDim", FontWeights.SemiBold);
        pct = Ui.Text("", F(8), "Fg", FontWeights.Bold);
        var foot = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(pct, Dock.Right);
        foot.Children.Add(label);
        foot.Children.Add(pct);
        Child = Ui.Col(dots, foot);
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        var start = new DateTime(now.Year, 1, 1);
        double frac = (now - start).TotalDays / (start.AddYears(1) - start).TotalDays;
        int filled = (int)Math.Round(frac * 48);
        for (int i = 0; i < 48; i++)
        {
            var e = (Ellipse)dots.Children[i];
            e.SetResourceReference(Shape.FillProperty, i < filled ? "Fg" : "Track");
        }
        label.Text = now.ToString("d MMM", Tr.C);
        pct.Text = $"%{frac * 100:0}";
        ToolTip = $"{now.Year} yılının %{frac * 100:0.0}'i geçti";
    }
}

/// <summary>Takvim açılır paneli: ay ızgarası, önceki/sonraki ay.</summary>
class MonthCalendar : StackPanel
{
    DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    readonly TextBlock title = Ui.Text("", 15, "Fg", FontWeights.Bold);
    readonly UniformGrid grid = new() { Columns = 7 };

    public MonthCalendar()
    {
        var prev = new IconButton("\uE76B", 11);
        var next = new IconButton("\uE76C", 11);
        prev.Click += () => { month = month.AddMonths(-1); Fill(); };
        next.Click += () => { month = month.AddMonths(1); Fill(); };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(next, Dock.Right);
        DockPanel.SetDock(prev, Dock.Right);
        head.Children.Add(next);
        head.Children.Add(prev);
        head.Children.Add(title);
        Children.Add(Ui.Text(DateTime.Now.ToString("dddd, d MMMM yyyy", Tr.C), 12, "FgDim"));
        Children.Add(head);
        Children.Add(grid);
        Fill();
    }

    void Fill()
    {
        title.Text = month.ToString("MMMM yyyy", Tr.C);
        grid.Children.Clear();
        foreach (var n in Localization.En ? new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" } : new[] { "Pt", "Sa", "Ça", "Pe", "Cu", "Ct", "Pz" })
        {
            var t = Ui.Text(n, 11, "FgDim", FontWeights.SemiBold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 0, 0, 4);
            grid.Children.Add(t);
        }
        int offset = ((int)month.DayOfWeek + 6) % 7;
        for (int i = 0; i < offset; i++) grid.Children.Add(new Border());
        int days = DateTime.DaysInMonth(month.Year, month.Month);
        for (int d = 1; d <= days; d++)
        {
            bool today = month.Year == DateTime.Today.Year && month.Month == DateTime.Today.Month && d == DateTime.Today.Day;
            var t = Ui.Text(d.ToString(), 13, "Fg", today ? FontWeights.Bold : FontWeights.Normal);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            var date = new DateTime(month.Year, month.Month, d);
            var renewals = App.Settings.Renewals.Where(r => r.On(date)).Select(r => r.Name).ToList();
            bool hasEvent = renewals.Count > 0 || CalendarService.Events.Any(e => e.Start.Date <= date && e.End > date);
            var inner = new Grid();
            inner.Children.Add(t);
            if (hasEvent)
            {
                var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) };
                if (today) dot.Background = Brushes.White; else dot.SetResourceReference(Border.BackgroundProperty, "Accent");
                inner.Children.Add(dot);
            }
            var cell = new Border { Height = 32, Width = 32, CornerRadius = new CornerRadius(16), Child = inner, HorizontalAlignment = HorizontalAlignment.Center };
            if (renewals.Count > 0) cell.ToolTip = string.Join("\n", renewals);
            if (today) { cell.SetResourceReference(Border.BackgroundProperty, "Accent"); t.Foreground = Brushes.White; }
            grid.Children.Add(cell);
        }
    }
}

// ───────────────────────── Sistem ─────────────────────────

class RingStat : Grid
{
    readonly RingGauge ring;
    readonly TextBlock value;

    public RingStat(string label, double size, double scale)
    {
        Width = Height = size;
        ring = new RingGauge { Stroke = Math.Max(2.5, size * 0.09) };
        value = Ui.Text("", Math.Round(size * 0.3), "Fg", FontWeights.Bold);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.Margin = new Thickness(0, -size * 0.08, 0, 0);
        var lbl = Ui.Text(label, Math.Round(size * 0.16, 1), "FgDim", FontWeights.SemiBold);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        lbl.VerticalAlignment = VerticalAlignment.Bottom;
        lbl.Margin = new Thickness(0, 0, 0, size * 0.17);
        Children.Add(ring);
        Children.Add(value);
        Children.Add(lbl);
    }

    public void Set(double v)
    {
        ring.Value = v;
        value.Text = $"{v * 100:0}";
    }
}

class SystemWidget : Widget
{
    readonly RingStat cpu, ram;

    public SystemWidget()
    {
        double s = H - 6;
        cpu = new RingStat("CPU", s, S);
        ram = new RingStat("RAM", s, S) { Margin = new Thickness(F(6), 0, 0, 0) };
        Padding = new Thickness(F(4), 0, F(4), 0);
        Child = Ui.Row(cpu, ram);
        OnClick(() => App.Open("taskmgr.exe"));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        SysStats.Update();
        cpu.Set(SysStats.Cpu);
        ram.Set(SysStats.Ram);
        ToolTip = $"CPU %{SysStats.Cpu * 100:0} · RAM {SysStats.RamUsedGb:0.0} / {SysStats.RamTotalGb:0.0} GB";
    }
}

class CpuWidget : Widget
{
    readonly RingStat ring;
    public CpuWidget()
    {
        Width = H; Padding = new Thickness(2);
        Child = ring = new RingStat("CPU", H - 6, S);
        OnClick(() => App.Open("taskmgr.exe"));
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now) { SysStats.Update(); ring.Set(SysStats.Cpu); }
}

class RamWidget : Widget
{
    readonly RingStat ring;
    public RamWidget()
    {
        Width = H; Padding = new Thickness(2);
        Child = ring = new RingStat("RAM", H - 6, S);
        OnClick(() => App.Open("taskmgr.exe"));
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        SysStats.Update();
        ring.Set(SysStats.Ram);
        ToolTip = $"RAM {SysStats.RamUsedGb:0.0} / {SysStats.RamTotalGb:0.0} GB";
    }
}

class NetworkWidget : Widget
{
    readonly TextBlock down, up;
    readonly Sparkline chart;

    public NetworkWidget()
    {
        down = Ui.Text("", F(12.5), "Fg", FontWeights.Bold);
        up = Ui.Text("", F(10), "FgDim", FontWeights.SemiBold);
        chart = new Sparkline { Width = F(64), Height = H * 0.5, Margin = new Thickness(F(10), 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var col = Ui.Col(down, up);
        col.Width = F(78);
        Child = Ui.Row(col, chart);
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        SysStats.Update();
        down.Text = "↓ " + SysStats.Speed(SysStats.Down);
        up.Text = "↑ " + SysStats.Speed(SysStats.Up);
        chart.Push(SysStats.Down);
        if (now.Second % 5 == 0)
        {
            var (conn, inet, name) = NetState.Read();
            up.Text += conn ? (inet ? "" : "  · internet yok") : "  · bağlantı yok";
            ToolTip = !conn ? "Bağlantı yok" : inet ? $"{name} · internet var" : $"{name} · bağlı ama internet yok";
        }
    }
}

class BatteryWidget : Widget
{
    readonly Border fill;
    readonly TextBlock text;
    readonly Grid box;

    public BatteryWidget() : base(card: false)
    {
        Width = H;
        box = new Grid { ClipToBounds = true };
        fill = new Border { VerticalAlignment = VerticalAlignment.Bottom };
        text = new TextBlock
        {
            FontFamily = Ui.Font, FontSize = F(14), FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.5 },
        };
        box.Children.Add(fill);
        box.Children.Add(text);
        Child = new Border { CornerRadius = new CornerRadius(F(12)), Child = box };
        Loaded += (_, _) => ClipBox();
        SizeChanged += (_, _) => ClipBox();
        OnClick(() => App.Open("ms-settings:batterysaver"));
        Tick(DateTime.Now);
    }

    void ClipBox() => box.Clip = new RectangleGeometry(new Rect(0, 0, box.ActualWidth, box.ActualHeight), F(12), F(12));

    public override void Tick(DateTime now)
    {
        if (!Native.GetSystemPowerStatus(out var p) || p.BatteryFlag == 128 || p.BatteryLifePercent > 100)
        {
            text.Text = "AC";
            fill.Height = H;
            fill.Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x8C, 0xFF));
            ((Border)Child).Background = fill.Background;
            ToolTip = "Pil yok — prize bağlı";
            return;
        }
        int pct = p.BatteryLifePercent;
        bool charging = p.ACLineStatus == 1;
        var c = charging ? Color.FromRgb(0x2F, 0x8C, 0xFF) : pct <= 20 ? Color.FromRgb(0xFF, 0x45, 0x3A) : Color.FromRgb(0x30, 0xB8, 0x50);
        fill.Background = new SolidColorBrush(c);
        ((Border)Child).Background = new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B));
        fill.Height = H * pct / 100.0;
        text.Text = (charging ? "⚡" : "") + pct;
        string left = p.BatteryLifeTime > 0 ? $" · {p.BatteryLifeTime / 3600} sa {p.BatteryLifeTime % 3600 / 60} dk kaldı" : "";
        ToolTip = $"Pil %{pct}" + (charging ? " · şarj oluyor" : left);
    }
}

class StorageWidget : Widget
{
    readonly TextBlock title, sub;
    readonly MiniSlider bar = new() { Interactive = false, Height = 5 };
    int n;

    public StorageWidget()
    {
        title = Ui.Text("", F(12.5), "Fg", FontWeights.Bold);
        sub = Ui.Text("", F(9.5), "FgDim");
        bar.Width = F(110);
        bar.Margin = new Thickness(0, F(3), 0, F(2));
        Child = Ui.Row(Chip("\uEDA2", Color.FromRgb(0x63, 0x66, 0xF1), H * 0.62), WithMargin(Ui.Col(title, bar, sub)));
        OnClick(() => Flyout.Show(this, StorageHistory.Panel(), 340));
        Tick(DateTime.Now);
    }

    static FrameworkElement WithMargin(FrameworkElement e) { e.Margin = new Thickness(F(8), 0, 0, 0); return e; }

    public override void Tick(DateTime now)
    {
        if (n++ % 30 != 0) return;
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            double free = d.AvailableFreeSpace / 1073741824.0, total = d.TotalSize / 1073741824.0;
            StorageHistory.Record();
            title.Text = $"{free:0} GB boş";
            var full = StorageHistory.DaysUntilFull(d);
            sub.Text = full is int days && days < 365 ? $"{days} gün içinde dolacak" : $"{d.Name.TrimEnd('\\')} · {total:0} GB";
            sub.SetResourceReference(TextBlock.ForegroundProperty, full is int dd && dd < 30 ? "Warn" : "FgDim");
            bar.FillKey = free / total < 0.1 ? "Warn" : "Accent";
            bar.Value = 1 - free / total;
        }
        catch { }
    }
}

class ShowDesktopWidget : Widget
{
    public ShowDesktopWidget()
    {
        Width = H;
        Padding = new Thickness(0);
        Child = Ui.Glyph("\uE7F4", F(17));
        ToolTip = "Masaüstünü göster";
        OnClick(() => Keys(0x5B, 0x44));   // Win + D
    }

    public static void Keys(params byte[] keys)
    {
        foreach (var k in keys) Native.keybd_event(k, 0, 0, UIntPtr.Zero);
        foreach (var k in keys.Reverse()) Native.keybd_event(k, 0, 2, UIntPtr.Zero);
    }
}

class NotificationsWidget : Widget
{
    public NotificationsWidget()
    {
        Width = H;
        Padding = new Thickness(0);
        Child = Ui.Glyph("\uEA8F", F(17));
        ToolTip = "Bildirim merkezi";
        OnClick(() => ShowDesktopWidget.Keys(0x5B, 0x4E));   // Win + N
    }
}

/// <summary>Wi‑Fi ve Bluetooth için hızlı aç/kapat; ok düğmeleri ilgili Windows ayarını açar.</summary>
class QuickTogglesWidget : Widget
{
    static Radio? wifi, bt;
    static bool loaded, loading;
    readonly Segment wifiSeg, btSeg;

    class Segment : Border
    {
        readonly TextBlock icon;
        public Segment(string glyph, Func<UIElement> panel, Func<Task> toggle, double h, double s)
        {
            Height = h;
            CornerRadius = new CornerRadius(h / 2);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(Math.Round(10 * s), 0, Math.Round(4 * s), 0);
            Margin = new Thickness(Math.Round(2 * s), 0, Math.Round(2 * s), 0);
            icon = Ui.Glyph(glyph, Math.Round(14 * s));
            var iconHost = new Border { Child = icon, Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(0, 0, Math.Round(8 * s), 0) };
            iconHost.MouseLeftButtonDown += (_, e) => e.Handled = true;
            iconHost.MouseLeftButtonUp += async (_, e) => { e.Handled = true; await toggle(); };
            var line = new Border { Width = 1, Height = h * 0.45 };
            line.SetResourceReference(BackgroundProperty, "Track");
            var arrow = new IconButton("\uE76C", Math.Round(10 * s), h * 0.8);
            // ok: Ayarlar'a gitmek yerine bağlanılabilecek ağları / cihazları listele
            arrow.Click += () => Flyout.Show(this, panel(), 340);
            arrow.ToolTip = "Listele";
            Child = Ui.Row(iconHost, line, arrow);
            SetOn(false);
        }

        public void SetOn(bool on)
        {
            if (on)
            {
                var a = ((SolidColorBrush)Ui.B("Accent")).Color;
                Background = new SolidColorBrush(Color.FromArgb(46, a.R, a.G, a.B));
                SetResourceReference(BorderBrushProperty, "Accent");
                icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            }
            else
            {
                SetResourceReference(BackgroundProperty, "Track");
                BorderBrush = Brushes.Transparent;
                icon.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            }
        }
    }

    readonly List<QuickButton> extras = [];

    public QuickTogglesWidget()
    {
        Padding = new Thickness(F(4), 0, F(4), 0);
        double h = H - F(12);
        wifiSeg = new Segment("\uE701", () => new WifiPanel(), () => Toggle(wifi, "ms-settings:network-wifi"), h, S);
        btSeg = new Segment("\uE702", () => new BluetoothPanel(), () => Toggle(bt, "ms-settings:bluetooth"), h, S);
        var row = Ui.Row(wifiSeg, btSeg);
        // seçilen ek hızlı ayarlar (Ayarlar → Hızlı ayarlar)
        foreach (var id in App.Settings.QuickToggles)
            if (ExtraToggles.Create(id, h) is { } b) { b.Margin = new Thickness(F(2), 0, F(2), 0); extras.Add(b); row.Children.Add(b); }
        Child = row;
        _ = Load();
        Tick(DateTime.Now);
    }

    static async Task Load()
    {
        if (loaded || loading) return;
        loading = true;
        try
        {
            if (await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed)
            {
                var radios = await Radio.GetRadiosAsync();
                wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
                bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            }
        }
        catch (Exception ex) { App.Log(ex); }
        loaded = true;
        loading = false;
    }

    async Task Toggle(Radio? r, string fallback)
    {
        if (r == null) { App.Open(fallback); return; }
        try
        {
            await r.SetStateAsync(r.State == RadioState.On ? RadioState.Off : RadioState.On);
        }
        catch (Exception ex) { App.Log(ex); App.Open(fallback); }
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        foreach (var x in extras) x.Refresh();
        wifiSeg.SetOn(wifi?.State == RadioState.On);
        btSeg.SetOn(bt?.State == RadioState.On);
        var net = NetState.Read();
        wifiSeg.ToolTip = wifi == null ? "Wi‑Fi" : $"Wi‑Fi: {(wifi.State == RadioState.On ? (net.Connected ? (net.Internet ? $"{net.Name} · internet var" : $"{net.Name} · internet yok") : "açık, bağlı değil") : "kapalı")}";
        btSeg.ToolTip = bt == null ? "Bluetooth" : $"Bluetooth: {(bt.State == RadioState.On ? "açık" : "kapalı")}";
    }
}

/// <summary>Saate / tarihe tıklama: Iskele takvimi ya da Windows'un bildirim + takvim paneli (Win+N).</summary>
static class ClockActions
{
    public static void Open(FrameworkElement anchor)
    {
        if (App.Settings.ClockClick == "windows") ShowDesktopWidget.Keys(0x5B, 0x4E);
        else Flyout.Show(anchor, new MonthCalendar(), 300);
    }
}
