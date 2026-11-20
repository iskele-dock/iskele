using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Iskele;

record WinInfo(IntPtr Hwnd, string Title, string Exe, uint Pid);

/// <summary>Açık üst düzey pencereleri izler (görev çubuğunun göstereceği pencerelerle aynı süzgeç).</summary>
static class WindowTracker
{
    public static List<WinInfo> Windows { get; private set; } = [];
    public static IntPtr Foreground { get; private set; }

    /// <summary>Dikkat isteyen (görev çubuğunda yanıp sönecek) pencereler; öne gelince listeden çıkar.</summary>
    public static readonly HashSet<IntPtr> Attention = [];
    public static void Flash(IntPtr h) { if (h != Native.GetForegroundWindow() && Attention.Add(h)) Changed?.Invoke(); }
    public static void Unflash(IntPtr h) { if (Attention.Remove(h)) Changed?.Invoke(); }
    public static event Action? Changed;

    static Native.WinEventProc? proc;
    static readonly List<IntPtr> hooks = [];
    static DispatcherTimer? debounce;
    static readonly Dictionary<uint, string> exeCache = [];

    public static void Start()
    {
        proc = OnEvent;
        // CREATE..HIDE (0x8000-0x8003), NAMECHANGE (0x800C), FOREGROUND (3), MINIMIZESTART/END (0x16-0x17)
        hooks.Add(Native.SetWinEventHook(0x8000, 0x8003, IntPtr.Zero, proc, 0, 0, 2));
        hooks.Add(Native.SetWinEventHook(0x800C, 0x800C, IntPtr.Zero, proc, 0, 0, 2));
        hooks.Add(Native.SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, proc, 0, 0, 2));
        hooks.Add(Native.SetWinEventHook(0x0016, 0x0017, IntPtr.Zero, proc, 0, 0, 2));
        debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        debounce.Tick += (_, _) => { debounce.Stop(); Refresh(); };
        Refresh();
    }

    static void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0) return;
        debounce?.Stop();
        debounce?.Start();
    }

    public static void Refresh()
    {
        var list = new List<WinInfo>();
        uint self = (uint)Environment.ProcessId;
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h)) return true;
            if (Native.GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            long ex = (long)Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
            bool appWin = (ex & Native.WS_EX_APPWINDOW) != 0;
            if (!appWin && ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0)) return true;
            if (Native.DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cloaked, 4) == 0 && cloaked != 0) return true;
            var title = Native.WindowText(h);
            if (title.Length == 0) return true;
            var cls = Native.ClassName(h);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow") return true;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == self) return true;
            if (!exeCache.TryGetValue(pid, out var exe))
            {
                if (exeCache.Count > 500) exeCache.Clear();
                exe = Native.ProcessPath(pid);
                exeCache[pid] = exe;
            }
            list.Add(new WinInfo(h, title, exe, pid));
            return true;
        }, IntPtr.Zero);

        var fg = Native.GetForegroundWindow();
        bool flashCleared = Attention.Remove(fg);
        Attention.RemoveWhere(h => !Native.IsWindow(h));
        bool same = fg == Foreground && list.Count == Windows.Count &&
                    list.Zip(Windows).All(p => p.First.Hwnd == p.Second.Hwnd && p.First.Title == p.Second.Title);
        Foreground = fg;
        Windows = list;
        if (!same || flashCleared) Changed?.Invoke();
    }

    /// <summary>Pencereyi öne getirir; simge durumundaysa geri yükler.</summary>
    public static void Activate(IntPtr h)
    {
        if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
        if (!Native.SetForegroundWindow(h) || Native.GetForegroundWindow() != h)
        {
            // Windows ön plan kilidi: kısa bir Alt basımı izni açar.
            Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
            Native.SetForegroundWindow(h);
        }
    }
}

/// <summary>Dosya / exe / kısayol simgelerini 48 px olarak alır ve önbelleğe koyar.</summary>
static class IconCache
{
    static readonly Dictionary<string, ImageSource?> cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Trim() { if (cache.Count > 128) cache.Clear(); }

    public static ImageSource? Get(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (cache.TryGetValue(path, out var img)) return img;
        img = null;
        try
        {
            path = SteamIcons.Resolve(path) ?? path;
            var info = new SHFILEINFO();
            if (Native.SHGetFileInfo(path, 0, ref info, Marshal.SizeOf<SHFILEINFO>(), 0x4000 /* SYSICONINDEX */) != IntPtr.Zero)
            {
                var iid = typeof(IImageList).GUID;
                if (Native.SHGetImageList(2 /* SHIL_EXTRALARGE (48) */, ref iid, out var list) == 0 &&
                    list.GetIcon(info.iIcon, 1 /* ILD_TRANSPARENT */, out var hicon) == 0 && hicon != IntPtr.Zero)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(hicon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    Native.DestroyIcon(hicon);
                    img = src;
                }
            }
        }
        catch (Exception ex) { App.Log(ex); }
        cache[path] = img;
        return img;
    }
}

/// <summary>Başlat düğmesi, sabitlenmiş uygulamalar ve açık pencereler (görev çubuğunun yerini tutar).</summary>
partial class AppsWidget : Widget
{
    partial class Entry
    {
        public string Key = "";          // eşleştirme anahtarı (exe yolu, küçük harf)
        public string Launch = "";       // tıklanınca açılacak yol (.lnk veya .exe)
        public string Name = "";
        public string MatchName = "";    // eşleştirme için program dosya adı (ör. claude.exe)
        public bool Pinned;
        public List<WinInfo> Windows = [];
    }

    readonly StackPanel row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    static readonly Dictionary<string, string> lnkTargets = new(StringComparer.OrdinalIgnoreCase);

    public AppsWidget() : base(card: false)
    {
        Child = row;
        AllowDrop = true;
        Drop += OnDrop;
        WindowTracker.Changed += Refresh;
        Refresh();
        instances.Add(this);
        AttachMagnify();

        if (Environment.GetEnvironmentVariable("ISKELE_PREVIEW_TEST") == "1")
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                var target = indicators.Select(i => i.Entry).OrderByDescending(e => e.Windows.Count).FirstOrDefault();
                var tile = target == null ? null : Ui.Ancestor<Border>(indicators.First(i => i.Entry == target).Dot.Parent);
                if (target != null && tile != null)
                    WindowPreview.ShowPinned(tile, () => CurrentWindows(target.Key), target.Name, IconCache.Get(target.Launch.Length > 0 ? target.Launch : target.Windows[0].Exe));
            };
            t.Start();
        }
    }

    public override void Detach()
    {
        ClearMagnify(); // Release render subscription and restore source opacity on detach.
        WindowTracker.Changed -= Refresh;
        instances.Remove(this);
    }

    static readonly List<AppsWidget> instances = [];

    /// <summary>Win+1…Win+0: dock'taki n. uygulamayı aç, öne getir ya da (zaten öndeyse) küçült.</summary>
    public static bool ActivateIndex(int index)
    {
        var w = instances.FirstOrDefault(i => Window.GetWindow(i) is DockWindow);
        if (w == null) return false;
        var entries = w.BuildEntries();
        if (index < 0 || index >= entries.Count) return false;
        var en = entries[index];
        App.Debug($"Win+{index + 1}: {en.Name} pencere={en.Windows.Count} baslat={en.Launch}");
        w.Dispatcher.BeginInvoke(() => Click(en));
        return true;
    }

    // ── üzerine gelince ad etiketi (isteğe bağlı)
    static System.Windows.Controls.Primitives.Popup? nameTip;

    void ShowName(FrameworkElement tile, string name)
    {
        if (!App.Settings.NamesOnHover || name.Length == 0) return;
        nameTip ??= new System.Windows.Controls.Primitives.Popup { AllowsTransparency = true, StaysOpen = true, IsHitTestVisible = false };
        var t = Ui.Text(name, 12, "Fg", FontWeights.SemiBold);
        var b = new Border { Child = t, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 4, 9, 5), BorderThickness = new Thickness(1), Margin = new Thickness(4) };
        b.SetResourceReference(Border.BackgroundProperty, "MenuBg");
        b.SetResourceReference(Border.BorderBrushProperty, "DockBorder");
        nameTip.Child = b;
        nameTip.PlacementTarget = tile;
        nameTip.Placement = DockWindow.IsTop ? System.Windows.Controls.Primitives.PlacementMode.Bottom : System.Windows.Controls.Primitives.PlacementMode.Top;
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        nameTip.HorizontalOffset = (tile.ActualWidth - b.DesiredSize.Width) / 2;
        nameTip.VerticalOffset = DockWindow.IsTop ? 4 : -4;
        nameTip.IsOpen = true;
    }

    static void HideName() { if (nameTip != null) nameTip.IsOpen = false; }

    static List<string> Pins
    {
        get
        {
            if (App.Settings.Pins == null)
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                App.Settings.Pins = Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "*.lnk").OrderBy(f => File.GetCreationTime(f)).ToList()
                    : [];
                App.Settings.Save();
            }
            return App.Settings.Pins;
        }
    }

    static readonly Dictionary<string, string> lnkMatch = new(StringComparer.OrdinalIgnoreCase);

    static string Target(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
        if (lnkTargets.TryGetValue(path, out var t)) return t;
        t = "";
        string args = "";
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type != null)
            {
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic sc = shell.CreateShortcut(path);
                t = (string)sc.TargetPath ?? "";
                args = (string)sc.Arguments ?? "";
                Marshal.FinalReleaseComObject(sc);
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch { }
        lnkTargets[path] = t;
        // Squirrel tipi başlatıcılar: "Update.exe --processStart Discord.exe" → asıl program Discord.exe
        var m = System.Text.RegularExpressions.Regex.Match(args, @"--processStart\s+""?([^""\s]+\.exe)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        lnkMatch[path] = m.Success ? m.Groups[1].Value : Path.GetFileName(t);
        // Hedefi boş kabuk kısayolu "Dosya Gezgini" → açık Gezgini pencereleri (explorer.exe) bu simgede toplansın
        var ln = Path.GetFileNameWithoutExtension(path);
        if (t.Length == 0 && (ln.Contains("Explorer", StringComparison.OrdinalIgnoreCase) || ln.Contains("Gezgini", StringComparison.OrdinalIgnoreCase)))
            lnkMatch[path] = "explorer.exe";
        return t;
    }

    /// <summary>Sabitlenmiş öğenin açılan pencerelerle eşleştirileceği program dosya adı.</summary>
    static string MatchNameFor(string pin)
    {
        if (!pin.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(pin);
        Target(pin);
        return lnkMatch.GetValueOrDefault(pin, "");
    }

    List<Entry> BuildEntries()
    {
        var entries = new List<Entry>();
        foreach (var pin in Pins)
        {
            if (SpecialPin(pin) is { } special) { entries.Add(special); continue; }   // grup ya da Store uygulaması
            if (!File.Exists(pin) && !Directory.Exists(pin)) continue;
            var target = Target(pin);
            entries.Add(new Entry
            {
                Key = (target.Length > 0 ? target : pin).ToLowerInvariant(),
                Launch = pin,
                Name = Path.GetFileNameWithoutExtension(pin),
                MatchName = MatchNameFor(pin),
                Pinned = true,
            });
        }
        foreach (var w in WindowTracker.Windows)
        {
            var key = (w.Exe.Length > 0 ? w.Exe : "hwnd:" + w.Hwnd).ToLowerInvariant();
            // Store uygulamaları (ApplicationFrameHost): uygulama kimliğine göre tek simgede topla
            string aumid = key.EndsWith("applicationframehost.exe") ? Uwp.AumidOf(w.Hwnd) : "";
            if (aumid.Length > 0) key = "aumid:" + aumid.ToLowerInvariant();
            // Birden çok pencereli UWP uygulamaları (ApplicationFrameHost) pencere başına ayrı göster.
            if (key.EndsWith("applicationframehost.exe")) key += ":" + w.Hwnd;
            var e = entries.FirstOrDefault(x => x.Key == key) ?? GroupFor(entries, key, w.Exe);
            // Tam yol tutmuyorsa (ör. kısayol başlatıcıyı, pencere sürüm klasöründeki programı gösteriyor)
            // dosya adıyla sabitlenmiş simgeye bağla; böylece açılan uygulama kendi yerinde görünür.
            if (e == null && w.Exe.Length > 0)
            {
                var exeName = Path.GetFileName(w.Exe);
                e = entries.FirstOrDefault(x => x.Pinned && x.MatchName.Length > 0 && x.MatchName.Equals(exeName, StringComparison.OrdinalIgnoreCase));
            }
            if (e == null)
            {
                e = new Entry { Key = key, Launch = w.Exe, Name = w.Exe.Length > 0 ? ScreenTime.FriendlyName(w.Exe) : w.Title };
                if (key.Contains("applicationframehost")) { e.Name = w.Title; e.Launch = ""; }
                if (aumid.Length > 0) { e.Aumid = aumid; e.Launch = Uwp.Path(aumid); e.Name = Uwp.Name(aumid, w.Title); }
                entries.Add(e);
            }
            e.Windows.Add(w);
        }
        return StableOrder(entries);
    }

    string signature = "";

    void Refresh()
    {
        if (appDrag != null) return;   // sürüklerken yeniden çizme
        var entries = BuildEntries();
        // Yalnızca pencere başlığı değiştiyse yeniden çizme (fare üstündeyken önizleme bozulmasın).
        // Ön plan değişimi de yeniden çizdirmez: yalnızca "etkin" göstergesi yerinde güncellenir.
        var sig = string.Join("|", entries.Select(e => e.Key + ":" + string.Join(",", e.Windows.Select(w => w.Hwnd))))
                  + "#" + App.Settings.Previews;
        if (sig == signature && row.Children.Count > 0)
        {
            UpdateIndicators();
            return;
        }
        signature = sig;
        App.Debug($"apps yeniden cizildi ({entries.Count} oge): " +
                  string.Join(" | ", entries.Select(e => $"{e.Name}{(e.Pinned ? "*" : "")}[{e.Windows.Count}]")));

        indicators.Clear();
        badges.Clear();
        bouncing.Clear();
        ClearMagnify();
        row.Children.Clear();
        row.Children.Add(StartButton());
        foreach (var e in entries) row.Children.Add(Tile(e));
        UpdateIndicators();
        UpdateBadges();
        ApplyReserve();
    }

    readonly List<(Entry Entry, Border Dot)> indicators = [];

    static double IconFactor => App.Settings.IconSize switch { "S" => 0.56, "L" => 0.76, _ => 0.66 };

    void UpdateIndicators()
    {
        var fg = WindowTracker.Foreground;
        string style = App.Settings.Indicator;
        foreach (var (e, dot) in indicators)
        {
            bool active = e.Windows.Any(w => w.Hwnd == fg);
            (double w, double h) = style switch
            {
                "bigdot" => (active ? F(8) : F(6), F(6)),
                "line" => (active ? F(18) : F(10), F(3)),
                _ => (active ? F(14) : F(4), F(3.5)),
            };
            dot.Width = w;
            dot.Height = h;
            dot.CornerRadius = new CornerRadius(h / 2);
            dot.SetResourceReference(Border.BackgroundProperty, active ? "Accent" : "FgDim");
        }
        UpdateAttention();
    }

    /// <summary>Bir uygulamanın güncel pencereleri (önizleme açıkken yeniden sorgulanır).</summary>
    List<WinInfo> CurrentWindows(string key) =>
        BuildEntries().FirstOrDefault(x => x.Key == key)?.Windows ?? [];

    FrameworkElement StartButton()
    {
        double s = H * 0.42, q = s / 2 - 1;
        var logo = new Canvas { Width = s, Height = s };
        var blue = new SolidColorBrush(Color.FromRgb(0x2F, 0x8C, 0xFF));
        foreach (var (x, y) in new[] { (0.0, 0.0), (q + 2, 0.0), (0.0, q + 2), (q + 2, q + 2) })
        {
            var r = new System.Windows.Shapes.Rectangle { Width = q, Height = q, Fill = blue, RadiusX = 1.5, RadiusY = 1.5 };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            logo.Children.Add(r);
        }
        var tile = TileShell(StartImage() ?? logo, "Başlat");
        tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; Native.PressWinKey(); };
        // sağ tık: Windows'un hızlı menüsü (Win+X)
        tile.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowDesktopWidget.Keys(0x5B, 0x58); };
        tile.ToolTip = "Başlat · sağ tık: hızlı menü";
        return tile;
    }

    Border TileShell(UIElement content, string tip)
    {
        var bg = new Border { CornerRadius = new CornerRadius(F(11)), Background = Brushes.Transparent };
        if (App.Settings.AppCards) { bg.SetResourceReference(Border.BackgroundProperty, "CardBg"); bg.SetResourceReference(Border.BorderBrushProperty, "CardBorder"); bg.BorderThickness = new Thickness(1); }
        var g = new Grid { Width = App.Settings.AppCards ? H : Math.Round(H * (IconFactor + 0.2)), Height = H };
        g.Children.Add(bg);
        g.Children.Add(content);
        var shell = new Border { Child = g, Margin = new Thickness(F(1), 0, F(1), 0), Cursor = Cursors.Hand, ToolTip = tip, Background = Brushes.Transparent };
        shell.MouseEnter += (_, _) => bg.SetResourceReference(Border.BackgroundProperty, "Track");
        shell.MouseLeave += (_, _) => { if (App.Settings.AppCards) bg.SetResourceReference(Border.BackgroundProperty, "CardBg"); else bg.Background = Brushes.Transparent; };
        shell.MouseLeftButtonDown += (_, e) => e.Handled = true;
        if (content is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        return shell;
    }

    FrameworkElement Tile(Entry e)
    {
        var iconSrc = IconCache.Get(e.Launch.Length > 0 ? e.Launch : e.Windows.FirstOrDefault()?.Exe ?? "");
        UIElement icon = TileIcon(e, iconSrc);
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var grid = new Grid();
        grid.Children.Add(icon);
        grid.Children.Add(Equalizer(e));
        grid.Children.Add(Badge(e));
        if (e.Windows.Count > 0)
        {
            var dot = new Border
            {
                Height = F(3.5), CornerRadius = new CornerRadius(F(2)),
                VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, -F(5)),
            };
            indicators.Add((e, dot));
            grid.Children.Add(dot);
        }

        string tip = e.Windows.Count switch
        {
            0 => e.Name,
            1 => e.Windows[0].Title,
            _ => $"{e.Name} — {e.Windows.Count} pencere",
        };
        var tile = TileShell(grid, tip);
        bool preview = e.Windows.Count > 0 && App.Settings.Previews;
        if (preview)
        {
            tile.ToolTip = null;   // önizleme başlığı zaten gösteriyor
            var key = e.Key;
            tile.MouseEnter += (_, _) => WindowPreview.Hover(tile, () => CurrentWindows(key), e.Name, iconSrc);
            tile.MouseLeave += (_, _) => WindowPreview.Unhover(tile);
        }
        else if (App.Settings.NamesOnHover)
        {
            tile.ToolTip = null;   // yerine ad etiketi
            tile.MouseEnter += (_, _) => ShowName(tile, e.Name);
        }
        tile.MouseLeave += (_, _) => HideName();

        // Bir dosya açık uygulamanın simgesi üzerinde bekletilince uygulama öne gelsin; bırakılınca o uygulamayla açılsın.
        tile.AllowDrop = true;
        DispatcherTimer? dragWait = null;
        tile.DragEnter += (_, ev) =>
        {
            if (e.Windows.Count == 0) return;
            dragWait?.Stop();
            dragWait = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Normal, (s, _) =>
            {
                ((DispatcherTimer)s!).Stop();
                WindowTracker.Activate(e.Windows[0].Hwnd);
            }, tile.Dispatcher);
        };
        tile.DragLeave += (_, _) => dragWait?.Stop();
        tile.Drop += (_, ev) =>
        {
            dragWait?.Stop();
            var exe = e.Windows.FirstOrDefault()?.Exe is { Length: > 0 } x ? x : Target(e.Launch);
            if (ev.Data.GetData(DataFormats.FileDrop) is not string[] files || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) return;
            ev.Handled = true;   // dock'a sabitleme yerine bu uygulamayla aç
            try { System.Diagnostics.Process.Start(exe, string.Join(" ", files.Select(f => $"\"{f}\""))); }
            catch (Exception ex) { App.Log(ex); }
        };
        tile.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; HideName(); WindowPreview.HideNow(); if (!OpenSpecial(tile, e)) Click(e); };
        tile.MouseUp += (_, ev) => { if (ev.ChangedButton == MouseButton.Middle && e.Launch.Length > 0) App.Open(e.Launch); };
        tile.ContextMenuOpening += (_, _) => { WindowPreview.HideNow(); tile.ContextMenu = Menu(e); };
        tile.ContextMenu = Menu(e);
        tile.Tag = e;
        AttachReorder(tile, e);
        return tile;
    }

    static void Click(Entry e)
    {
        if (e.Windows.Count == 0)
        {
            if (e.Launch.Length > 0) App.Open(e.Launch);
            return;
        }
        var fg = Native.GetForegroundWindow();
        int idx = e.Windows.FindIndex(w => w.Hwnd == fg);
        if (e.Windows.Count == 1)
        {
            var h = e.Windows[0].Hwnd;
            if (idx == 0 && !Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_MINIMIZE);
            else WindowTracker.Activate(h);
            return;
        }
        // birden çok pencere: sırayla dolaş
        WindowTracker.Activate(e.Windows[(idx + 1) % e.Windows.Count].Hwnd);
    }

    ContextMenu Menu(Entry e)
    {
        var m = new ContextMenu();
        m.Items.Add(DockWindow.Item(e.Name, () => { }, enabled: false));
        m.Items.Add(new Separator());
        if (e.Group != null) { GroupMenu(m, e); return m; }
        AddJumpItems(m, e);
        if (e.Launch.Length > 0)
            m.Items.Add(DockWindow.Item(e.Windows.Count > 0 ? "Yeni pencere aç" : "Aç", () => App.Open(e.Launch)));

        foreach (var w in e.Windows.Take(8))
        {
            var win = w;
            var title = win.Title.Length > 40 ? win.Title[..40] + "…" : win.Title;
            if (e.Windows.Count > 1) m.Items.Add(DockWindow.Item("↗ " + title, () => WindowTracker.Activate(win.Hwnd)));
        }

        if (e.Pinned)
            m.Items.Add(DockWindow.Item("Dock'tan kaldır", () => { Pins.RemoveAll(p => p.Equals(e.Launch, StringComparison.OrdinalIgnoreCase)); App.Settings.Save(); Refresh(); }));
        else if (e.Launch.Length > 0)
            m.Items.Add(DockWindow.Item("Dock'a sabitle", () => { Pins.Add(e.Launch); App.Settings.Save(); Refresh(); }));

        if (e.Windows.Count > 0)
        {
            m.Items.Add(new Separator());
            m.Items.Add(DockWindow.Item(e.Windows.Count > 1 ? "Tüm pencereleri kapat" : "Pencereyi kapat", () =>
            {
                foreach (var w in e.Windows) Native.PostMessage(w.Hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }));
            m.Items.Add(DockWindow.Item("Görevi sonlandır", () =>
            {
                foreach (var pid in e.Windows.Select(w => w.Pid).Distinct())
                    try { Process.GetProcessById((int)pid).Kill(); } catch (Exception ex) { App.Log(ex); }
            }));
        }
        return m;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var f in files)
            if (!Pins.Contains(f, StringComparer.OrdinalIgnoreCase)) Pins.Add(f);
        App.Settings.Save();
        Refresh();
        e.Handled = true;
    }
}
