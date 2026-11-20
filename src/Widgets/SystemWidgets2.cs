using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Networking.Connectivity;

namespace Iskele;

// ═════════════════════════ 49. CPU sıcaklığı ═════════════════════════

/// <summary>
/// Termal bölgelerin sıcaklığı (Windows performans sayaçları, yönetici izni gerekmez). Kelvin → °C.
/// Sensör seçilebilir (Ayarlar); birim °C ya da °F.
/// </summary>
static class Thermal
{
    public record Zone(string Name, double Celsius);
    static readonly Cached<List<Zone>> cache = new(TimeSpan.FromSeconds(4), () => Task.Run(Read));

    static List<Zone> Read()
    {
        var list = new List<Zone>();
        using var s = new ManagementObjectSearcher("root\\cimv2", "SELECT Name, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
        foreach (ManagementObject o in s.Get())
        {
            double k = Convert.ToDouble(o["Temperature"]);
            if (k > 200) list.Add(new Zone(((string)o["Name"]).Replace("\\_TZ.", ""), k - 273.15));
        }
        return list;
    }

    public static List<Zone> Zones { get { cache.Ensure(); return cache.Value ?? []; } }

    public static Zone? Current
    {
        get
        {
            var z = Zones;
            if (z.Count == 0) return null;
            return z.FirstOrDefault(x => x.Name == App.Settings.TempSensor) ?? z.MaxBy(x => x.Celsius);
        }
    }

    public static string Format(double c) => App.Settings.TempUnit == "F" ? $"{c * 9 / 5 + 32:0}°F" : $"{c:0}°C";
}

class TemperatureWidget : Widget
{
    readonly RingGauge ring = new() { Stroke = 4 };
    readonly TextBlock value, sub;

    public TemperatureWidget()
    {
        double r = H - F(10);
        var g = new Grid { Width = r, Height = r };
        g.Children.Add(ring);
        g.Children.Add(Ui.Glyph("", F(13)));
        value = Ui.Text("—", F(15), "Fg", FontWeights.Bold);
        sub = Ui.Text("", F(9.5), "FgDim");
        var col = Ui.Col(value, sub);
        col.Margin = new Thickness(F(8), 0, 0, 0);
        col.MinWidth = F(70);
        Child = Ui.Row(g, col);
        OnClick(Pick);
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        var z = Thermal.Current;
        if (z == null) { value.Text = "—"; sub.Text = "sensör yok"; return; }
        value.Text = Thermal.Format(z.Celsius);
        SysStats.Update();
        sub.Text = $"CPU %{SysStats.Cpu * 100:0} · RAM %{SysStats.Ram * 100:0}";
        ring.Value = Math.Clamp((z.Celsius - 30) / 70, 0, 1);   // 30–100 °C
        var a = z.Celsius >= 85 ? "Warn" : z.Celsius >= 70 ? "Accent" : "Good";
        ToolTip = $"Sensör: {z.Name} · tıkla: sensör / birim";
    }

    void Pick()
    {
        var m = new ContextMenu();
        foreach (var z in Thermal.Zones)
        {
            var name = z.Name;
            m.Items.Add(DockWindow.Item($"{(App.Settings.TempSensor == name ? "● " : "")}{name} — {Thermal.Format(z.Celsius)}", () => { App.Settings.TempSensor = name; App.Settings.Save(); Tick(DateTime.Now); }));
        }
        m.Items.Add(DockWindow.Item("En sıcak sensör", () => { App.Settings.TempSensor = ""; App.Settings.Save(); Tick(DateTime.Now); }));
        m.Items.Add(new Separator());
        m.Items.Add(DockWindow.Item(App.Settings.TempUnit == "F" ? "°C kullan" : "°F kullan", () => { App.Settings.TempUnit = App.Settings.TempUnit == "F" ? "C" : "F"; App.Settings.Save(); Tick(DateTime.Now); }));
        m.PlacementTarget = this;
        m.IsOpen = true;
    }
}

// ═════════════════════════ 50–51. Pil göstergeleri ═════════════════════════

/// <summary>Yakıt göstergesi: E'den F'ye yay ve ibre.</summary>
class FuelGauge : FrameworkElement
{
    double value;
    public double Value { get => value; set { this.value = Math.Clamp(value, 0, 1); InvalidateVisual(); } }
    public string Key { get; set; } = "Good";

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0) return;
        var c = new Point(w / 2, h * 0.92);
        double r = Math.Min(w / 2, h * 0.85) - 2;
        Point At(double t, double rr) { double a = Math.PI * (1 - t); return new Point(c.X + rr * Math.Cos(a), c.Y - rr * Math.Sin(a)); }
        var track = new Pen(Ui.B("Track"), 3.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var fill = new Pen(Ui.B(Key), 3.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        void Arc(Pen p, double t0, double t1)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open()) { ctx.BeginFigure(At(t0, r), false, false); ctx.ArcTo(At(t1, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false); }
            dc.DrawGeometry(null, p, g);
        }
        Arc(track, 0, 1);
        if (value > 0.01) Arc(fill, 0, value);
        dc.DrawLine(new Pen(Ui.B("Fg"), 1.8) { EndLineCap = PenLineCap.Round }, c, At(value, r * 0.72));
        dc.DrawEllipse(Ui.B("Fg"), null, c, 2.2, 2.2);
        var tf = new Typeface(Ui.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var e = new FormattedText("E", Tr.C, FlowDirection.LeftToRight, tf, 7.5, Ui.B("FgDim"), dpi);
        var f = new FormattedText("F", Tr.C, FlowDirection.LeftToRight, tf, 7.5, Ui.B("FgDim"), dpi);
        dc.DrawText(e, new Point(c.X - r - 1, c.Y - e.Height + 1));
        dc.DrawText(f, new Point(c.X + r - f.Width + 1, c.Y - f.Height + 1));
    }
}

class BatteryGaugeWidget : Widget
{
    readonly FuelGauge gauge = new();
    readonly TextBlock text;

    public BatteryGaugeWidget()
    {
        gauge.Width = H * 1.15;
        gauge.Height = H * 0.72;
        text = Ui.Text("", F(13), "Fg", FontWeights.Bold);
        text.Margin = new Thickness(F(6), 0, 0, 0);
        Child = Ui.Row(gauge, text);
        OnClick(() => App.Open("ms-settings:batterysaver"));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        var (pct, ch, has) = Power.Read();
        gauge.Key = ch ? "Accent" : pct <= 20 ? "Warn" : "Good";
        gauge.Value = pct / 100.0;
        text.Text = has ? (ch ? "⚡" : "") + $"%{pct}" : "AC";
        ToolTip = has ? $"Pil %{pct}{(ch ? " · şarj oluyor" : "")}" : "Pil yok";
    }
}

/// <summary>On iki hücreli pil.</summary>
class BatteryCoreWidget : Widget
{
    readonly UniformGrid cells = new() { Rows = 2, Columns = 6 };
    readonly TextBlock text;

    public BatteryCoreWidget()
    {
        double cw = Math.Round(H * 0.17), ch = Math.Round(H * 0.3);
        for (int i = 0; i < 12; i++)
            cells.Children.Add(new Border { Width = cw, Height = ch, CornerRadius = new CornerRadius(F(2)), Margin = new Thickness(F(1)) });
        var shell = new Border { Child = cells, Padding = new Thickness(F(2)), CornerRadius = new CornerRadius(F(5)), BorderThickness = new Thickness(1.5) };
        shell.SetResourceReference(BorderBrushProperty, "FgDim");
        var nub = new Border { Width = F(3), Height = H * 0.25, CornerRadius = new CornerRadius(0, F(2), F(2), 0), VerticalAlignment = VerticalAlignment.Center };
        nub.SetResourceReference(BackgroundProperty, "FgDim");
        text = Ui.Text("", F(13), "Fg", FontWeights.Bold);
        text.Margin = new Thickness(F(8), 0, 0, 0);
        Child = Ui.Row(shell, nub, text);
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        var (pct, ch, has) = Power.Read();
        int on = (int)Math.Ceiling(pct / 100.0 * 12);
        string key = ch ? "Accent" : pct <= 20 ? "Warn" : "Good";
        // hücreler soldan sağa: üst ve alt satır birlikte dolar
        for (int i = 0; i < 12; i++)
        {
            int col = i % 6, rowIdx = i / 6;
            int order = col * 2 + rowIdx;
            ((Border)cells.Children[i]).SetResourceReference(BackgroundProperty, order < on ? key : "Track");
        }
        text.Text = has ? (ch ? "⚡" : "") + $"%{pct}" : "AC";
    }
}

// ═════════════════════════ 53. Rahatsız etmeyin ═════════════════════════

/// <summary>
/// Windows "Rahatsız etmeyin" (eski adıyla Odak yardımı). Durum, Windows'un kendi WNF bildirim durumu
/// WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED üzerinden okunur ve yazılır: 0 kapalı, 1 yalnızca öncelikli, 2 yalnızca alarmlar.
/// </summary>
static class DoNotDisturb
{
    [DllImport("ntdll.dll")] static extern int NtQueryWnfStateData(ref ulong name, IntPtr type, IntPtr scope, out uint stamp, byte[] buf, ref uint size);
    [DllImport("ntdll.dll")] static extern int NtUpdateWnfStateData(ref ulong name, byte[] buf, uint size, IntPtr type, IntPtr scope, uint stamp, uint check);
    static ulong State = 0x0D83063EA3BF1C75;

    /// <summary>null: okunamadı.</summary>
    public static bool? On
    {
        get
        {
            var buf = new byte[4];
            uint size = 4;
            try { return NtQueryWnfStateData(ref State, IntPtr.Zero, IntPtr.Zero, out _, buf, ref size) == 0 ? BitConverter.ToInt32(buf, 0) != 0 : null; }
            catch { return null; }
        }
    }

    public static void Set(bool on)
    {
        var buf = BitConverter.GetBytes(on ? 1 : 0);
        try { NtUpdateWnfStateData(ref State, buf, 4, IntPtr.Zero, IntPtr.Zero, 0, 0); }
        catch (Exception ex) { App.Log(ex.Message); }
    }

    public static void Toggle()
    {
        if (On is bool b) Set(!b); else App.Open("ms-settings:notifications");
    }
}

// ═════════════════════════ 57. Gerçek internet bağlantısı ═════════════════════════

static class NetState
{
    /// <summary>Bağlantı var mı, internete çıkılabiliyor mu, ağın adı.</summary>
    public static (bool Connected, bool Internet, string Name) Read()
    {
        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            if (p == null) return (false, false, "");
            var lvl = p.GetNetworkConnectivityLevel();
            string name = p.IsWlanConnectionProfile ? p.WlanConnectionProfileDetails.GetConnectedSsid() : p.ProfileName;
            return (lvl != NetworkConnectivityLevel.None, lvl == NetworkConnectivityLevel.InternetAccess, name ?? "");
        }
        catch { return (false, false, ""); }
    }
}

// ═════════════════════════ 58. Depolama: tüm sürücüler + dolma tahmini ═════════════════════════

/// <summary>
/// Her sürücünün boş alanı günde bir kez %AppData%\Iskele\storage.json dosyasına yazılır; son 30 günün
/// eğiliminden "N gün içinde dolacak" tahmini yapılır.
/// </summary>
static class StorageHistory
{
    static string FilePath => Path.Combine(Settings.Dir, "storage.json");
    static Dictionary<string, SortedDictionary<string, long>>? data;

    static Dictionary<string, SortedDictionary<string, long>> Data
    {
        get
        {
            if (data != null) return data;
            try { data = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, SortedDictionary<string, long>>>(File.ReadAllText(FilePath)) : null; }
            catch { }
            return data ??= [];
        }
    }

    public static void Record()
    {
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        bool changed = false;
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            if (!Data.TryGetValue(d.Name, out var h)) Data[d.Name] = h = [];
            if (!h.ContainsKey(day)) { h[day] = d.AvailableFreeSpace; changed = true; }
            while (h.Count > 30) h.Remove(h.Keys.First());
        }
        if (changed) try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Data)); } catch { }
    }

    /// <summary>Boş alan bu hızla azalırsa kaç gün sonra biter; tahmin yoksa null.</summary>
    public static int? DaysUntilFull(DriveInfo d)
    {
        if (!Data.TryGetValue(d.Name, out var h) || h.Count < 2) return null;
        var first = h.First();
        double days = (DateTime.Today - DateTime.Parse(first.Key)).TotalDays;
        if (days < 1) return null;
        double perDay = (first.Value - d.AvailableFreeSpace) / days;
        if (perDay <= 1024 * 1024) return null;   // azalmıyor
        return (int)Math.Min(9999, d.AvailableFreeSpace / perDay);
    }

    public static UIElement Panel()
    {
        Record();
        var p = new StackPanel();
        p.Children.Add(Ui.Text("Depolama", 16, "Fg", FontWeights.Bold));
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable))
        {
            double free = d.AvailableFreeSpace / 1073741824.0, total = d.TotalSize / 1073741824.0;
            var head = new DockPanel { Margin = new Thickness(0, 12, 0, 4) };
            var right = Ui.Text($"{free:0} GB boş / {total:0} GB", 12, "FgDim");
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
            var label = d.VolumeLabel.Length > 0 ? $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})" : d.Name.TrimEnd('\\');
            head.Children.Add(Ui.Text(label, 13, "Fg", FontWeights.SemiBold));
            p.Children.Add(head);
            p.Children.Add(new MiniSlider { Interactive = false, Height = 6, Value = 1 - free / total, FillKey = free / total < 0.1 ? "Warn" : "Accent" });
            var days = DaysUntilFull(d);
            if (days != null)
            {
                var t = Ui.Text(days < 365 ? $"Bu hızla yaklaşık {days} gün içinde dolacak" : "Yavaş doluyor", 11.5, days < 30 ? "Warn" : "FgDim");
                t.Margin = new Thickness(0, 4, 0, 0);
                p.Children.Add(t);
            }
        }
        var note = Ui.Text("Dolma tahmini için günlük ölçüm birikir (en az 1 gün).", 11, "FgDim");
        note.Margin = new Thickness(0, 12, 0, 0);
        p.Children.Add(note);
        return p;
    }
}
