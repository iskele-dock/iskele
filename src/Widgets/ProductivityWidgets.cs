using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Iskele;

// ───────────────────────── Ekran süresi ─────────────────────────

/// <summary>
/// Ön plandaki uygulamayı saniyede bir örnekler ve günlük süreyi
/// %AppData%\Iskele\screentime\YYYY-MM-DD.json içine yazar. Veri bilgisayardan çıkmaz.
/// </summary>
static class ScreenTime
{
    public class Day
    {
        public int Total { get; set; }
        public int[] Hours { get; set; } = new int[24];
        public Dictionary<string, int> Apps { get; set; } = [];
        /// <summary>Uygulama türü → 24 saatlik saniye dağılımı (gün çizelgesi için).</summary>
        public Dictionary<string, int[]> Kinds { get; set; } = [];
        /// <summary>Tarayıcıda geçen süre, site başına (isteğe bağlı).</summary>
        public Dictionary<string, int> Sites { get; set; } = [];
        /// <summary>Tamamlanan pomodoro odak turları ("HH:mm-HH:mm").</summary>
        public List<string> Focus { get; set; } = [];
    }

    /// <summary>Bir günün kaydı (bugün için canlı veri).</summary>
    public static Day LoadDay(DateTime date)
    {
        if (date.Date == DateTime.Today) return today;
        var f = Path.Combine(Folder, date.ToString("yyyy-MM-dd") + ".json");
        try { return File.Exists(f) ? JsonSerializer.Deserialize<Day>(File.ReadAllText(f)) ?? new Day() : new Day(); }
        catch { return new Day(); }
    }

    public static void AddFocus(DateTime start, DateTime end)
    {
        today.Focus.Add($"{start:HH:mm}-{end:HH:mm}");
        Save();
    }

    static readonly string[] Browsers = ["chrome", "msedge", "firefox", "browser", "brave", "opera", "vivaldi", "arc", "zen"];

    /// <summary>Uygulamanın türü: gün çizelgesinde renklendirme için.</summary>
    public static string Kind(string exe)
    {
        var n = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        if (Browsers.Contains(n)) return "Tarayıcı";
        if (n is "code" or "devenv" or "rider64" or "idea64" or "windowsterminal" or "cmd" or "powershell" or "pwsh" or "claude" or "codex" or "cursor" or "sublime_text" or "notepad++" or "git-bash") return "Geliştirme";
        if (n is "telegram" or "whatsapp" or "discord" or "slack" or "teams" or "ms-teams" or "zoom" or "signal") return "İletişim";
        if (n is "spotify" or "vlc" or "mpc-hc64" or "potplayermini64" or "steam" or "epicgameslauncher" || n.Contains("game")) return "Eğlence";
        if (n is "winword" or "excel" or "powerpnt" or "onenote" or "obsidian" or "notion" or "acrord32" or "acrobat" or "sumatrapdf" or "pdf24" || n.Contains("pdf")) return "Belgeler";
        return "Diğer";
    }

    public static readonly Dictionary<string, Color> KindColors = new()
    {
        ["Geliştirme"] = Color.FromRgb(0x2F, 0x8C, 0xFF), ["Tarayıcı"] = Color.FromRgb(0xFF, 0x9F, 0x0A), ["İletişim"] = Color.FromRgb(0x30, 0xD1, 0x58),
        ["Belgeler"] = Color.FromRgb(0xBF, 0x5A, 0xF2), ["Eğlence"] = Color.FromRgb(0xFF, 0x45, 0x3A), ["Diğer"] = Color.FromRgb(0x8E, 0x8E, 0x93),
    };

    /// <summary>Tarayıcı başlığından site adı: "Sayfa – Site - Google Chrome" → "Site".</summary>
    static string Site(string title)
    {
        var parts = title.Split([" - ", " – ", " — ", " | "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (parts.Count > 1) parts.RemoveAt(parts.Count - 1);   // tarayıcı adı
        var s = parts.LastOrDefault() ?? "";
        return s.Length is > 0 and < 40 ? s : "Diğer siteler";
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);

    static Day today = new();
    static string key = "";
    static int sinceSave;
    static readonly Dictionary<uint, (string Name, string Exe)> names = [];
    static readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };

    static string Folder
    {
        get { var d = Path.Combine(Settings.Dir, "screentime"); Directory.CreateDirectory(d); return d; }
    }

    public static Day Today => today;

    public static void Start()
    {
        Load(DateTime.Now);
        timer.Tick += (_, _) => Sample();
        timer.Start();
    }

    static void Load(DateTime now)
    {
        key = now.ToString("yyyy-MM-dd");
        var f = Path.Combine(Folder, key + ".json");
        try { today = File.Exists(f) ? JsonSerializer.Deserialize<Day>(File.ReadAllText(f)) ?? new Day() : new Day(); }
        catch { today = new Day(); }
        if (today.Hours.Length != 24) today.Hours = new int[24];
    }

    public static void Save()
    {
        if (key.Length == 0) return;
        try { AtomicFile.Write(Path.Combine(Folder, key + ".json"), JsonSerializer.Serialize(today)); }
        catch (Exception ex) { App.Log(ex); }
        sinceSave = 0;
    }

    static void Sample()
    {
        var now = DateTime.Now;
        if (now.ToString("yyyy-MM-dd") != key) { Save(); Load(now); }

        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref lii) && (uint)Environment.TickCount - lii.dwTime > 120_000) return;   // 2 dk boşta

        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        if (!names.TryGetValue(pid, out var info))
        {
            if (names.Count > 400) names.Clear();
            var exe = Native.ProcessPath(pid);
            info = (FriendlyName(exe), exe);
            names[pid] = info;
        }
        var name = info.Name;
        if (name == "LockApp") return;

        today.Total++;
        today.Hours[now.Hour]++;
        today.Apps[name] = today.Apps.GetValueOrDefault(name) + 1;
        var kind = Kind(info.Exe);
        if (!today.Kinds.TryGetValue(kind, out var kh) || kh.Length != 24) today.Kinds[kind] = kh = new int[24];
        kh[now.Hour]++;
        if (App.Settings.TrackSites && kind == "Tarayıcı")
        {
            var site = Site(Native.WindowText(fg));
            today.Sites[site] = today.Sites.GetValueOrDefault(site) + 1;
        }
        if (++sinceSave >= 60) Save();
    }

    public static string FriendlyName(string exe)
    {
        if (exe.Length == 0) return "Diğer";
        try
        {
            var d = FileVersionInfo.GetVersionInfo(exe).FileDescription;
            if (!string.IsNullOrWhiteSpace(d) && d.Length < 40) return d.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(exe);
    }

    public static string Format(int sec)
    {
        int h = sec / 3600, m = sec % 3600 / 60;
        return h > 0 ? $"{h}sa {m}dk" : $"{m}dk";
    }
}

class ScreenTimeWidget : Widget
{
    readonly TextBlock total;
    readonly MiniBars bars;

    public ScreenTimeWidget()
    {
        var label = Ui.Text("Ekran süresi", F(9.5), "FgDim", FontWeights.SemiBold);
        total = Ui.Text("", F(16), "Fg", FontWeights.Bold);
        var col = Ui.Col(label, total);
        col.Margin = new Thickness(F(8), 0, F(10), 0);
        col.MinWidth = F(66);
        bars = new MiniBars { Width = F(50), Height = H * 0.45, VerticalAlignment = VerticalAlignment.Center };
        Child = Ui.Row(Chip("", Color.FromRgb(0x2F, 0x8C, 0xFF), H * 0.62), col, bars);
        OnClick(() => Flyout.Show(this, ScreenTimePanel.Build(), 380));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        var d = ScreenTime.Today;
        total.Text = ScreenTime.Format(d.Total);
        // gece yarısından şimdiye kadarki saatler
        bars.Values = d.Hours.Take(Math.Max(now.Hour + 1, 8)).Select(x => (double)x).ToArray();
    }

    void ShowDetails()
    {
        var d = ScreenTime.Today;
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("Bugün", 12, "FgDim"));
        panel.Children.Add(Ui.Text(ScreenTime.Format(d.Total), 26, "Fg", FontWeights.Bold));
        var hours = new MiniBars { Height = 56, Margin = new Thickness(0, 8, 0, 4), Values = d.Hours.Select(x => (double)x).ToArray() };
        panel.Children.Add(hours);
        var axis = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var a1 = Ui.Text("00", 10, "FgDim"); var a2 = Ui.Text("12", 10, "FgDim"); var a3 = Ui.Text("23", 10, "FgDim");
        DockPanel.SetDock(a1, Dock.Left); DockPanel.SetDock(a3, Dock.Right);
        a2.HorizontalAlignment = HorizontalAlignment.Center;
        axis.Children.Add(a1); axis.Children.Add(a3); axis.Children.Add(a2);
        panel.Children.Add(axis);

        int max = d.Apps.Count == 0 ? 1 : d.Apps.Values.Max();
        foreach (var (app, sec) in d.Apps.OrderByDescending(kv => kv.Value).Take(8))
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var t = Ui.Text(ScreenTime.Format(sec), 12, "FgDim");
            DockPanel.SetDock(t, Dock.Right);
            row.Children.Add(t);
            row.Children.Add(Ui.RawText(app, 12.5, "Fg", FontWeights.SemiBold));
            panel.Children.Add(row);
            panel.Children.Add(new MiniSlider { Interactive = false, Height = 4, Value = (double)sec / max, Margin = new Thickness(0, 3, 0, 2) });
        }
        if (d.Apps.Count == 0) panel.Children.Add(Ui.Text("Henüz veri yok.", 12, "FgDim"));
        Flyout.Show(this, panel, 320);
    }
}

// ───────────────────────── Pomodoro / zamanlayıcı / kronometre ─────────────────────────

class PomodoroWidget : Widget
{
    const int FocusMin = 25, ShortBreak = 5, LongBreak = 15;
    bool focus = true, running;
    int round = 1;
    TimeSpan remaining = TimeSpan.FromMinutes(FocusMin);
    DateTime end;
    readonly TextBlock label, time;
    readonly IconButton play;

    public PomodoroWidget()
    {
        label = Ui.Text("", F(9.5), "Warn", FontWeights.Bold);
        time = Ui.Text("", F(17), "Fg", FontWeights.Bold);
        var col = Ui.Col(label, time);
        col.MinWidth = F(62);
        col.Margin = new Thickness(0, 0, F(8), 0);
        var skip = new IconButton("", F(11), F(28));
        skip.Click += Advance;
        play = new IconButton("", F(12), F(30), accent: true);
        play.Click += Toggle;
        Child = Ui.Row(col, skip, play);
        ToolTip = "Pomodoro · sağ tık: sıfırla";
        MouseRightButtonUp += (_, e) => { e.Handled = true; running = false; focus = true; round = 1; remaining = TimeSpan.FromMinutes(FocusMin); Paint(); };
        Paint();
    }

    void Toggle()
    {
        running = !running;
        if (running) end = DateTime.Now + remaining;
        Paint();
    }

    void Advance()
    {
        if (focus) { focus = false; remaining = TimeSpan.FromMinutes(round % 4 == 0 ? LongBreak : ShortBreak); }
        else { focus = true; round = round % 4 + 1; remaining = TimeSpan.FromMinutes(FocusMin); }
        if (running) end = DateTime.Now + remaining;
        Paint();
    }

    public override void Tick(DateTime now)
    {
        if (!running) return;
        remaining = end - now;
        if (remaining <= TimeSpan.Zero)
        {
            SystemSounds.Exclamation.Play();
            if (focus) ScreenTime.AddFocus(now - TimeSpan.FromMinutes(FocusMin), now);
            Advance();
        }
        Paint();
    }

    void Paint()
    {
        label.Text = focus ? Localization.F("Odak {0}/4", round) : Localization.T("Mola");
        label.SetResourceReference(TextBlock.ForegroundProperty, focus ? "Warn" : "Good");
        var r = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        time.Text = $"{(int)r.TotalMinutes:00}:{r.Seconds:00}";
        play.GlyphText = running ? "" : "";
        play.SetResourceReference(BackgroundProperty, focus ? "Warn" : "Good");
        SetResourceReference(BorderBrushProperty, running ? (focus ? "Warn" : "Good") : "CardBorder");
    }
}

class TimerWidget : Widget
{
    int minutes = 5;
    bool running;
    DateTime end;
    TimeSpan remaining = TimeSpan.FromMinutes(5);
    readonly TextBlock time;
    readonly IconButton play;

    public TimerWidget()
    {
        var label = Ui.Text("Zamanlayıcı", F(9.5), "Accent", FontWeights.Bold);
        time = Ui.Text("", F(17), "Fg", FontWeights.Bold);
        var col = Ui.Col(label, time);
        col.MinWidth = F(70);
        col.Margin = new Thickness(0, 0, F(8), 0);
        play = new IconButton("", F(12), F(30), accent: true);
        play.Click += () =>
        {
            if (!running && remaining <= TimeSpan.Zero) remaining = TimeSpan.FromMinutes(minutes);
            running = !running;
            if (running) end = DateTime.Now + remaining;
            Paint();
        };
        Child = Ui.Row(col, play);
        ToolTip = "Tekerle süreyi ayarla · sağ tık: sıfırla";
        MouseWheel += (_, e) =>
        {
            if (running) return;
            minutes = Math.Clamp(minutes + Math.Sign(e.Delta) * (minutes >= 10 ? 5 : 1), 1, 180);
            remaining = TimeSpan.FromMinutes(minutes);
            e.Handled = true;
            Paint();
        };
        MouseRightButtonUp += (_, e) => { e.Handled = true; running = false; remaining = TimeSpan.FromMinutes(minutes); Paint(); };
        Paint();
    }

    public override void Tick(DateTime now)
    {
        if (!running) return;
        remaining = end - now;
        if (remaining <= TimeSpan.Zero)
        {
            running = false;
            remaining = TimeSpan.Zero;
            SystemSounds.Exclamation.Play();
        }
        Paint();
    }

    void Paint()
    {
        var r = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        time.Text = r.TotalHours >= 1 ? $"{(int)r.TotalHours}:{r.Minutes:00}:{r.Seconds:00}" : $"{(int)r.TotalMinutes}:{r.Seconds:00}";
        play.GlyphText = running ? "" : "";
        SetResourceReference(BorderBrushProperty, running ? "Accent" : "CardBorder");
    }
}

class StopwatchWidget : Widget
{
    readonly Stopwatch sw = new();
    readonly TextBlock time;
    readonly IconButton play;
    static readonly SolidColorBrush Orange = new(Color.FromRgb(0xFF, 0x9F, 0x0A));

    public StopwatchWidget()
    {
        var label = new TextBlock { Text = "Kronometre", FontSize = F(9.5), FontWeight = FontWeights.Bold, Foreground = Orange, FontFamily = Ui.Font };
        time = Ui.Text("0:00", F(17), "Fg", FontWeights.Bold);
        var col = Ui.Col(label, time);
        col.MinWidth = F(70);
        col.Margin = new Thickness(0, 0, F(8), 0);
        play = new IconButton("", F(12), F(30)) { Background = Orange };
        play.Click += () => { if (sw.IsRunning) sw.Stop(); else sw.Start(); Paint(); };
        Child = Ui.Row(col, play);
        ToolTip = "Sağ tık: sıfırla";
        MouseRightButtonUp += (_, e) => { e.Handled = true; sw.Reset(); Paint(); };
        Paint();
    }

    public override void Tick(DateTime now) { if (sw.IsRunning) Paint(); }

    void Paint()
    {
        var t = sw.Elapsed;
        time.Text = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        play.GlyphText = sw.IsRunning ? "" : "";
    }
}

// ───────────────────────── Yapılacaklar / notlar ─────────────────────────

static class Notes
{
    public static event Action? Changed;
    public static void Save() { App.Settings.Save(); Changed?.Invoke(); }
}

class TodosWidget : Widget
{
    readonly RingGauge ring = new() { Stroke = 3 };
    readonly TextBlock count;
    readonly StackPanel list = new() { VerticalAlignment = VerticalAlignment.Center };

    public TodosWidget()
    {
        double r = H - F(14);
        count = Ui.Text("", F(12), "Fg", FontWeights.Bold);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        var g = new Grid { Width = r, Height = r, Margin = new Thickness(0, 0, F(10), 0) };
        g.Children.Add(ring);
        g.Children.Add(count);
        list.MinWidth = F(80);
        list.MaxWidth = F(150);
        Child = Ui.Row(g, list);
        OnClick(Edit);
        Notes.Changed += Paint;
        Paint();
    }

    public override void Detach() => Notes.Changed -= Paint;

    void Paint()
    {
        var all = App.Settings.Todos;
        var open = all.Where(t => !t.Done).ToList();
        count.Text = open.Count.ToString();
        ring.Value = all.Count == 0 ? 0 : (double)(all.Count - open.Count) / all.Count;
        list.Children.Clear();
        if (open.Count == 0) list.Children.Add(Ui.Text(all.Count == 0 ? "Yapılacak ekle" : "Hepsi bitti", F(11), "FgDim"));
        foreach (var t in open.Take(2))
            list.Children.Add(Ui.Row(Ui.Glyph("", F(9), "FgDim"), Pad(Ui.RawText(t.Text, F(11), "Fg", FontWeights.SemiBold))));
    }

    static FrameworkElement Pad(FrameworkElement e) { e.Margin = new Thickness(F(5), 0, 0, 0); return e; }

    void Edit()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("Yapılacaklar", 16, "Fg", FontWeights.Bold));
        var items = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        panel.Children.Add(new ScrollViewer { Content = items, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        void Fill()
        {
            items.Children.Clear();
            foreach (var t in App.Settings.Todos.ToList())
            {
                var check = new IconButton(t.Done ? "" : "", 15, 26);
                check.Click += () => { t.Done = !t.Done; Notes.Save(); Fill(); };
                var del = new IconButton("", 10, 24);
                del.Click += () => { App.Settings.Todos.Remove(t); Notes.Save(); Fill(); };
                var text = Ui.RawText(t.Text, 13, t.Done ? "FgDim" : "Fg");
                if (t.Done) text.TextDecorations = TextDecorations.Strikethrough;
                text.Margin = new Thickness(6, 0, 6, 0);
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                DockPanel.SetDock(check, Dock.Left);
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(check);
                row.Children.Add(del);
                row.Children.Add(text);
                items.Children.Add(row);
            }
            if (App.Settings.Todos.Count == 0) items.Children.Add(Ui.Text("Liste boş.", 12, "FgDim"));
        }

        var input = Input("Yeni iş yaz, Enter'a bas");
        input.Box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Box.Text)) return;
            App.Settings.Todos.Add(new TodoItem { Text = input.Box.Text.Trim() });
            input.Box.Clear();
            Notes.Save();
            Fill();
        };
        panel.Children.Add(input.Host);
        var clear = new TextBlock { Text = "Bitenleri temizle", FontSize = 12, Margin = new Thickness(2, 10, 0, 0), Cursor = Cursors.Hand };
        clear.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        clear.MouseLeftButtonUp += (_, _) => { App.Settings.Todos.RemoveAll(t => t.Done); Notes.Save(); Fill(); };
        panel.Children.Add(clear);
        Fill();
        Flyout.Show(this, panel, 320);
        input.Box.Focus();
    }

    public static (Border Host, TextBox Box) Input(string placeholder, bool multiline = false)
    {
        var box = new TextBox
        {
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 13, FontFamily = Ui.Font,
            AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
        };
        box.SetResourceReference(TextBox.ForegroundProperty, "Fg");
        box.SetResourceReference(TextBox.CaretBrushProperty, "Fg");
        var hint = Ui.Text(placeholder, 13, "FgDim");
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(3, multiline ? 1 : 0, 0, 0);
        hint.VerticalAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
        void Upd() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Upd();
        Upd();
        var g = new Grid();
        g.Children.Add(box);
        g.Children.Add(hint);
        var host = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Child = g };
        host.SetResourceReference(Border.BackgroundProperty, "Track");
        return (host, box);
    }
}

