using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Iskele;

class AppGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Grup";
    public List<string> Items { get; set; } = [];
}

/// <summary>Store (UWP / paketli) uygulamaları: pencereden uygulama kimliği, adı, simgesi, başlatma yolu (hedef 15).</summary>
static class Uwp
{
    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore { int GetCount(out int n); int GetAt(int i, out PROPERTYKEY k); int GetValue(ref PROPERTYKEY k, out PROPVARIANT v); }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem { int BindToHandler(IntPtr a, ref Guid b, ref Guid c, out IntPtr d); int GetParent(out IntPtr p); int GetDisplayName(uint sigdn, out IntPtr name); }

    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

    static readonly Dictionary<IntPtr, string> byWindow = [];
    static readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);

    public static string AumidOf(IntPtr hwnd)
    {
        if (byWindow.TryGetValue(hwnd, out var a)) return a;
        a = "";
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) == 0)
            {
                var k = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
                if (store.GetValue(ref k, out var v) == 0)
                {
                    if (v.vt == 31) a = Marshal.PtrToStringUni(v.p) ?? "";
                    PropVariantClear(ref v);
                }
            }
        }
        catch { }
        // pencere henüz içeriğini bağlamadıysa boş döner; o durumda önbelleğe yazma
        if (a.Length > 0) byWindow[hwnd] = a;
        return a;
    }

    public static string Path(string aumid) => @"shell:AppsFolder\" + aumid;
    public static bool IsPath(string p) => p.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase);
    public static string AumidFromPath(string p) => p[@"shell:AppsFolder\".Length..];

    public static string Name(string aumid, string fallback = "")
    {
        if (names.TryGetValue(aumid, out var n)) return n;
        n = fallback;
        try
        {
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(Path(aumid), IntPtr.Zero, ref iid, out var item) == 0 && item.GetDisplayName(0 /* NORMALDISPLAY */, out var p) == 0)
            {
                n = Marshal.PtrToStringUni(p) ?? fallback;
                CoTaskMemFree(p);
            }
        }
        catch { }
        if (n.Length > 0) names[aumid] = n;
        return n.Length > 0 ? n : aumid.Split('!').Last();
    }
}

partial class AppsWidget
{
    partial class Entry
    {
        public string Aumid = "";
        public AppGroup? Group;
    }

    static List<AppGroup> Groups => App.Settings.Groups;

    /// <summary>Sabitlenmiş listede grup ("group:&lt;id&gt;") ya da Store uygulaması ("shell:AppsFolder\…") girişleri.</summary>
    static Entry? SpecialPin(string pin)
    {
        if (pin.StartsWith("group:"))
        {
            var g = Groups.FirstOrDefault(x => x.Id == pin[6..]);
            return g == null ? null : new Entry { Key = pin, Launch = "", Name = g.Name, Pinned = true, Group = g };
        }
        if (Uwp.IsPath(pin))
        {
            var aumid = Uwp.AumidFromPath(pin);
            return new Entry { Key = "aumid:" + aumid.ToLowerInvariant(), Launch = pin, Name = Uwp.Name(aumid), Aumid = aumid, Pinned = true };
        }
        return null;
    }

    /// <summary>Gruptaki bir uygulamanın penceresi grubun simgesinde toplanır.</summary>
    static Entry? GroupFor(List<Entry> entries, string key, string exe)
    {
        var exeName = System.IO.Path.GetFileName(exe);
        foreach (var e in entries.Where(x => x.Group != null))
            foreach (var item in e.Group!.Items)
            {
                if (Uwp.IsPath(item) ? key == "aumid:" + Uwp.AumidFromPath(item).ToLowerInvariant() :
                    (Target(item) is { Length: > 0 } t && t.Equals(exe, StringComparison.OrdinalIgnoreCase)) ||
                    (MatchNameFor(item) is { Length: > 0 } mn && mn.Equals(exeName, StringComparison.OrdinalIgnoreCase)))
                    return e;
            }
        return null;
    }

    // ───────── simge: Store uygulaması, grup, çerçeve, tek renkli simgeler (hedef 15, 17) ─────────

    FrameworkElement TileIcon(Entry e, ImageSource? iconSrc)
    {
        double size = H * IconFactor;
        if (e.Group != null) return GroupIcon(e.Group, size);
        if (Thumbs.IsMedia(e.Launch) && Thumbs.Get(e.Launch, 128) is { } thumb) return Thumbs.Rounded(thumb, size);
        if (e.Aumid.Length > 0 || Uwp.IsPath(e.Launch)) iconSrc = Thumbs.Get(e.Launch, 96) ?? iconSrc;
        if (iconSrc == null) return Ui.Glyph("", F(18));
        return StyledIcon(iconSrc, size);
    }

    FrameworkElement StyledIcon(ImageSource src, double size)
    {
        FrameworkElement img = new Image { Source = src, Width = size, Height = size };
        if (src is BitmapSource bs && Analyze(bs) is { Mono: true } a)
        {
            bool framed = App.Settings.IconStyle == "frame";
            if (a.SingleTone)
            {
                // tek tonlu glif (saydam zemin üstünde tek renk): dock'un yazı rengini alsın
                var r = new System.Windows.Shapes.Rectangle { Width = size, Height = size, OpacityMask = new ImageBrush(src) };
                if (framed) r.Fill = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));   // açık çerçevede koyu
                else r.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Fg");
                img = r;
            }
            else
            {
                // iç ayrıntılı siyah-beyaz simge (ör. terminal: siyah kare içinde beyaz ">_"): siluete çevirince ayrıntı
                // kayboluyordu. Zeminle karışıyorsa renklerini ters çevir, ayrıntı kalsın.
                bool darkBg = !framed && TryFindResource("Fg") is SolidColorBrush fg && Luma(fg.Color) > 0.5;
                if (darkBg ? a.Lum < 0.4 : a.Lum > 0.65) img = new Image { Source = Inverted(bs), Width = size, Height = size };
            }
        }
        if (App.Settings.IconStyle != "frame") return img;
        // macOS tarzı yuvarlatılmış kare çerçeve
        img.Width = img.Height = size * 0.72;
        var frame = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), Child = img, BorderThickness = new Thickness(1) };
        frame.Background = new LinearGradientBrush(Color.FromRgb(0xFC, 0xFC, 0xFE), Color.FromRgb(0xE4, 0xE6, 0xEC), 90);
        frame.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
        return frame;
    }

    internal record struct IconTone(bool Mono, bool SingleTone, double Lum);
    static readonly Dictionary<ImageSource, IconTone> monoCache = [];

    static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    /// <summary>Simge gri tonlu mu, tek tonlu mu (yalnızca saydamlıkla şekillenen glif), ortalama parlaklığı ne?</summary>
    static IconTone Analyze(BitmapSource src)
    {
        if (monoCache.TryGetValue(src, out var m)) return m;
        try
        {
            var b = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            var px = new byte[b.PixelWidth * b.PixelHeight * 4];
            b.CopyPixels(px, b.PixelWidth * 4, 0);
            m = Analyze(px);
        }
        catch { m = default; }
        monoCache[src] = m;
        App.Debug($"icon tone {m}");
        return m;
    }

    internal static IconTone Analyze(byte[] bgra)
    {
        int seen = 0, colored = 0;
        double sum = 0, sum2 = 0;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 160) continue;   // kenar yumuşatması (yarı saydam) pikselleri tonu bozmasın
            seen++;
            int max = Math.Max(bgra[i], Math.Max(bgra[i + 1], bgra[i + 2])), min = Math.Min(bgra[i], Math.Min(bgra[i + 1], bgra[i + 2]));
            if (max - min > 24) colored++;
            double l = (0.0722 * bgra[i] + 0.7152 * bgra[i + 1] + 0.2126 * bgra[i + 2]) / 255;
            sum += l; sum2 += l * l;
        }
        if (seen <= 20 || colored >= seen * 0.04) return default;
        double mean = sum / seen, sd = Math.Sqrt(Math.Max(0, sum2 / seen - mean * mean));
        return new IconTone(true, sd < 0.12, mean);
    }

    static BitmapSource Inverted(BitmapSource src)
    {
        var b = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int stride = b.PixelWidth * 4;
        var px = new byte[stride * b.PixelHeight];
        b.CopyPixels(px, stride, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            // önceden çarpılmamış BGRA: renk kanallarını ters çevir, saydamlık aynı
            px[i] = (byte)(255 - px[i]); px[i + 1] = (byte)(255 - px[i + 1]); px[i + 2] = (byte)(255 - px[i + 2]);
        }
        var o = BitmapSource.Create(b.PixelWidth, b.PixelHeight, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, px, stride);
        o.Freeze();
        return o;
    }

    FrameworkElement GroupIcon(AppGroup g, double size)
    {
        var grid = new UniformGrid { Rows = 2, Columns = 2, Width = size * 0.8, Height = size * 0.8 };
        foreach (var item in g.Items.Take(4))
        {
            var src = Uwp.IsPath(item) ? Thumbs.Get(item, 64) : IconCache.Get(item);
            grid.Children.Add(src != null ? new Image { Source = src, Margin = new Thickness(1) } : new Border());
        }
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), Child = grid, BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "Track");
        b.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        return b;
    }

    // ───────── 4. rozetler: okunmamış sayısı pencere başlığından ("(3) Discord", "Telegram (12)") ─────────

    readonly List<(Entry Entry, Border Badge, TextBlock Text)> badges = [];
    static readonly Regex CountRe = new(@"^\((\d{1,4})\)\s|\s\((\d{1,4})\)$", RegexOptions.Compiled);

    FrameworkElement Badge(Entry e)
    {
        var t = new TextBlock { FontFamily = Ui.Font, FontSize = F(8.5), FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        var b = new Border
        {
            MinWidth = F(15), Height = F(15), CornerRadius = new CornerRadius(F(7.5)), Padding = new Thickness(F(3), 0, F(3), 0),
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)), Child = t, BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, F(1), F(1), 0),
            Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        badges.Add((e, b, t));
        return b;
    }

    void UpdateBadges()
    {
        var titles = WindowTracker.Windows.ToDictionary(w => w.Hwnd, w => w.Title);
        foreach (var (e, b, t) in badges)
        {
            int n = 0;
            foreach (var w in e.Windows)
            {
                var m = CountRe.Match(titles.GetValueOrDefault(w.Hwnd, w.Title));
                if (m.Success) n = Math.Max(n, int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value));
            }
            if (n > 0) t.Text = n > 99 ? "99+" : n.ToString();
            var v = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (b.Visibility != v) b.Visibility = v;
        }
    }

    // ───────── 5. dikkat isteyen uygulama: simge zıplar, gösterge kırmızı olur ─────────

    readonly HashSet<Entry> bouncing = [];

    void UpdateAttention()
    {
        foreach (var (e, dot) in indicators)
        {
            bool want = e.Windows.Any(w => WindowTracker.Attention.Contains(w.Hwnd));
            if (want) dot.SetResourceReference(Border.BackgroundProperty, "Warn");
            var tile = Ui.Ancestor<Border>(VisualTreeHelper.GetParent(dot) as DependencyObject);
            if (tile?.Child is not Grid g) continue;
            if (want && bouncing.Add(e))
            {
                var tt = new TranslateTransform();
                g.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, DockWindow.IsTop ? F(5) : -F(5), TimeSpan.FromMilliseconds(260))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                });
            }
            else if (!want && bouncing.Remove(e)) g.RenderTransform = null;
        }
    }

    // ───────── 1. jump list: son dosyalar ve uygulama görevleri ─────────

    void AddJumpItems(ContextMenu m, Entry e)
    {
        var exe = e.Windows.FirstOrDefault()?.Exe is { Length: > 0 } x ? x : Target(e.Launch);
        if (exe.Length == 0) return;
        var tasks = JumpLists.Tasks(exe);
        var recent = JumpLists.Recent(exe, 7);
        if (recent.Count > 0)
        {
            m.Items.Add(DockWindow.Item("Son kullanılanlar", () => { }, enabled: false));
            foreach (var (title, path) in recent)
                m.Items.Add(DockWindow.Item("   " + (title.Length > 42 ? title[..42] + "…" : title), () => App.Open(path)));
            m.Items.Add(new Separator());
        }
        if (tasks.Count > 0)
        {
            foreach (var (title, run) in tasks) m.Items.Add(DockWindow.Item(title, run));
            m.Items.Add(new Separator());
        }
    }

    // ───────── 12. klasör yığını, 3. grup kartı ─────────

    bool OpenSpecial(FrameworkElement tile, Entry e)
    {
        if (e.Group != null) { Flyout.Show(tile, GroupPanel(e), 340); return true; }
        if (e.Windows.Count == 0 && e.Launch.Length > 0 && Directory.Exists(e.Launch)) { Flyout.Show(tile, FolderStack(e.Launch), 380); return true; }
        return false;
    }

    static UIElement FolderStack(string dir)
    {
        var p = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var open = PanelRows.Link("Klasörde aç", () => { Flyout.CloseCurrent(); App.Open(dir); });
        open.Margin = new Thickness(0);
        DockPanel.SetDock(open, Dock.Right);
        head.Children.Add(open);
        head.Children.Add(Ui.RawText(System.IO.Path.GetFileName(dir.TrimEnd('\\')), 16, "Fg", FontWeights.Bold));
        p.Children.Add(head);
        var wrap = new WrapPanel();
        IEnumerable<FileSystemInfo> items;
        try { items = new DirectoryInfo(dir).EnumerateFileSystemInfos().Where(i => !i.Attributes.HasFlag(FileAttributes.Hidden)).OrderByDescending(i => i.LastWriteTime).Take(24).ToList(); }
        catch { items = []; }
        foreach (var i in items)
        {
            var item = i;
            var src = Thumbs.IsMedia(item.FullName) ? Thumbs.Get(item.FullName, 96) : IconCache.Get(item.FullName);
            var icon = src != null ? (FrameworkElement)new Image { Source = src, Width = 40, Height = 40 } : Ui.Glyph("", 24);
            var name = Ui.RawText(System.IO.Path.GetFileNameWithoutExtension(item.Name), 11, "Fg");
            name.TextAlignment = TextAlignment.Center;
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.MaxWidth = 76;
            var cell = new Border { Width = 82, Padding = new Thickness(3, 6, 3, 6), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = item.Name, Child = Ui.Col(icon, name) };
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            cell.MouseEnter += (_, _) => cell.SetResourceReference(Border.BackgroundProperty, "Track");
            cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
            cell.MouseLeftButtonUp += (_, _) => { Flyout.CloseCurrent(); App.Open(item.FullName); };
            wrap.Children.Add(cell);
        }
        if (wrap.Children.Count == 0) p.Children.Add(Ui.Text("Klasör boş.", 12, "FgDim"));
        p.Children.Add(new ScrollViewer { Content = wrap, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return p;
    }

    UIElement GroupPanel(Entry ge)
    {
        var g = ge.Group!;
        var p = new StackPanel();
        var name = TodosWidget.Input("Grup adı");
        name.Box.Text = g.Name;
        name.Box.FontWeight = FontWeights.Bold;
        name.Box.LostFocus += (_, _) => { if (name.Box.Text.Trim().Length > 0) { g.Name = name.Box.Text.Trim(); App.Settings.Save(); } };
        name.Box.KeyDown += (_, k) => { if (k.Key == Key.Enter) { g.Name = name.Box.Text.Trim(); App.Settings.Save(); } };
        p.Children.Add(name.Host);
        var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var item in g.Items.ToList())
        {
            var it = item;
            var src = Uwp.IsPath(it) ? Thumbs.Get(it, 96) : IconCache.Get(it);
            var key = Uwp.IsPath(it) ? "aumid:" + Uwp.AumidFromPath(it).ToLowerInvariant() : (Target(it) is { Length: > 0 } t ? t.ToLowerInvariant() : it.ToLowerInvariant());
            var mn = MatchNameFor(it);
            var wins = ge.Windows.Where(w => (w.Exe.Length > 0 && (w.Exe.ToLowerInvariant() == key || System.IO.Path.GetFileName(w.Exe).Equals(mn, StringComparison.OrdinalIgnoreCase))) || (Uwp.AumidOf(w.Hwnd) is { Length: > 0 } a && "aumid:" + a.ToLowerInvariant() == key)).ToList();
            var icon = src != null ? (FrameworkElement)new Image { Source = src, Width = 44, Height = 44 } : Ui.Glyph("", 26);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            var dot = new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(2.5), Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Visibility = wins.Count > 0 ? Visibility.Visible : Visibility.Hidden };
            dot.SetResourceReference(Border.BackgroundProperty, "Accent");
            var label = Ui.RawText(Uwp.IsPath(it) ? Uwp.Name(Uwp.AumidFromPath(it)) : System.IO.Path.GetFileNameWithoutExtension(it), 11, "Fg");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.MaxWidth = 84;
            var cell = new Border { Width = 92, Padding = new Thickness(4, 8, 4, 6), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = Ui.Col(icon, dot, label) };
            cell.MouseEnter += (_, _) => cell.SetResourceReference(Border.BackgroundProperty, "Track");
            cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
            cell.MouseLeftButtonUp += (_, _) => { Flyout.CloseCurrent(); if (wins.Count > 0) WindowTracker.Activate(wins[0].Hwnd); else App.Open(it); };
            var menu = new ContextMenu();
            menu.Items.Add(DockWindow.Item("Gruptan çıkar", () =>
            {
                g.Items.Remove(it);
                int at = Pins.IndexOf("group:" + g.Id);
                Pins.Insert(Math.Max(0, at + 1), it);
                if (g.Items.Count < 2) Ungroup(g);
                App.Settings.Save();
                Flyout.CloseCurrent();
                signature = "";
                Refresh();
            }));
            cell.ContextMenu = menu;
            wrap.Children.Add(cell);
        }
        p.Children.Add(wrap);
        var split = PanelRows.Link("Grubu dağıt", () => { Ungroup(g); App.Settings.Save(); Flyout.CloseCurrent(); signature = ""; Refresh(); });
        p.Children.Add(split);
        return p;
    }

    void GroupMenu(ContextMenu m, Entry e)
    {
        m.Items.Add(DockWindow.Item("Grubu aç", () => { var tile = row.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Tag == e); if (tile != null) OpenSpecial(tile, e); }));
        m.Items.Add(DockWindow.Item("Grubu dağıt", () => { Ungroup(e.Group!); App.Settings.Save(); signature = ""; Refresh(); }));
    }

    static void Ungroup(AppGroup g)
    {
        int at = Pins.IndexOf("group:" + g.Id);
        if (at < 0) at = Pins.Count;
        else Pins.RemoveAt(at);
        Pins.InsertRange(Math.Min(at, Pins.Count), g.Items.Where(i => !Pins.Contains(i)));
        Groups.Remove(g);
    }

    /// <summary>Sürüklenen sabitlenmiş uygulamayı hedef simgeyle grupla (hedef bir grupsa içine ekle).</summary>
    static void MakeGroup(Entry dragged, Entry target)
    {
        if (!dragged.Pinned || dragged.Group != null || dragged.Launch.Length == 0) return;
        Pins.RemoveAll(p => p.Equals(dragged.Launch, StringComparison.OrdinalIgnoreCase));
        if (target.Group != null)
        {
            if (!target.Group.Items.Contains(dragged.Launch)) target.Group.Items.Add(dragged.Launch);
            return;
        }
        if (target.Launch.Length == 0) return;
        var g = new AppGroup { Name = "Grup", Items = [target.Launch, dragged.Launch] };
        Groups.Add(g);
        int at = Pins.FindIndex(p => p.Equals(target.Launch, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) Pins[at] = "group:" + g.Id;
        else Pins.Add("group:" + g.Id);
    }

    // ───────── 6. Mac tarzı büyütme ─────────

    // 2026-11-17: continuous hover wave, a single visible icon and smooth release.
    sealed class MagnifiedTile
    {
        public required FrameworkElement Tile;
        public required FrameworkElement Source;
        public required System.Windows.Shapes.Rectangle Copy;
        public required ScaleTransform Scale;
        public required TranslateTransform Offset;
        public double Amount = 1;
        public double OriginalOpacity;
        public Rect Bounds; // Includes the running indicator and badge overflow at their original pixel scale.
    }
    readonly Dictionary<FrameworkElement, MagnifiedTile> magnified = [];
    DockWindow? magnifyDock;
    Point magnifyCursor;
    bool magnifyHover, magnifyRendering;
    long magnifyLastFrame;

    // Compact cosine-squared kernel: zero slope at both the peak and the edge.
    internal static double MagnifyWeight(double distance, double radius)
    {
        if (radius <= 0 || Math.Abs(distance) >= radius) return 0;
        double c = Math.Cos(Math.Abs(distance) / radius * Math.PI / 2);
        return c * c;
    }
    internal static double MagnifyApproach(double current, double target, double seconds, double response)
        => target + (current - target) * Math.Exp(-Math.Max(0, seconds) / response);

    internal static Rect MagnifyViewBox(FrameworkElement source)
    {
        var bounds=VisualTreeHelper.GetDescendantBounds(source);
        bounds.Union(new Rect(0,0,source.ActualWidth,source.ActualHeight));
        return bounds;
    }

    internal static (System.Windows.Shapes.Rectangle Copy,Rect Bounds) MagnifyCopy(FrameworkElement source,bool top)
    {
        var bounds=MagnifyViewBox(source);
        // Absolute VisualBrush coordinates include the source's layout offset.
        // Normalize against its own visible bounds so switching to the overlay cannot shift it.
        var visualBounds=VisualTreeHelper.GetDescendantBounds(source);
        if(visualBounds.IsEmpty||visualBounds.Width<=0||visualBounds.Height<=0)visualBounds=bounds;
        var relative=new Rect((bounds.X-visualBounds.X)/visualBounds.Width,(bounds.Y-visualBounds.Y)/visualBounds.Height,bounds.Width/visualBounds.Width,bounds.Height/visualBounds.Height);
        var copy=new System.Windows.Shapes.Rectangle
        {
            Width=bounds.Width,Height=bounds.Height,
            Fill=new VisualBrush(source){ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Viewbox=relative,Stretch=Stretch.Fill},
            IsHitTestVisible=false,
            RenderTransformOrigin=new Point((source.ActualWidth/2-bounds.X)/bounds.Width,((top?0:source.ActualHeight)-bounds.Y)/bounds.Height)
        };
        return(copy,bounds);
    }

    void Magnify(Point cursorInRow, DockWindow? preview = null)
    {
        var dock = preview ?? Window.GetWindow(this) as DockWindow;
        if (App.Settings.Magnify <= 1.01 || dock == null || appDrag != null) { ClearMagnify(); return; }
        magnifyDock = dock;
        magnifyCursor = cursorInRow;
        magnifyHover = true;
        StartMagnifyFrames();
    }

    void StartMagnifyFrames()
    {
        if (magnifyRendering) return;
        magnifyRendering = true;
        magnifyLastFrame = System.Diagnostics.Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += MagnifyFrame;
    }
    void StopMagnifyFrames()
    {
        if (!magnifyRendering) return;
        CompositionTarget.Rendering -= MagnifyFrame;
        magnifyRendering = false;
    }
    void MagnifyFrame(object? sender, EventArgs args)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double dt = Math.Clamp((now - magnifyLastFrame) / (double)System.Diagnostics.Stopwatch.Frequency, 0, 0.05);
        magnifyLastFrame = now;
        if (StepMagnify(dt)) StopMagnifyFrames();
    }

    bool StepMagnify(double dt)
    {
        var dock = magnifyDock;
        if (dock == null || appDrag != null) { ClearMagnify(); return true; }
        double max = Math.Clamp(App.Settings.Magnify, 1, 2);
        bool moving = false;
        var states = new List<MagnifiedTile>();
        foreach (FrameworkElement tile in row.Children)
        {
            if (tile.ActualWidth <= 0 || tile is not Border { Child: FrameworkElement source }) continue;
            var center = tile.TranslatePoint(new Point(tile.ActualWidth / 2, 0), row).X;
            double weight = magnifyHover ? MagnifyWeight(magnifyCursor.X - center, TileWidth * 2.4) : 0;
            double target = 1 + (max - 1) * weight;
            if (!magnified.TryGetValue(tile, out var state))
            {
                if (!magnifyHover) continue;
                // Grow the icon content, not the rectangular hover backing.
                if (!App.Settings.AppCards && source is Grid shell && shell.Children.Count > 1 && shell.Children[^1] is FrameworkElement iconContent)
                    source = iconContent;
                if (source.ActualWidth <= 0 || source.ActualHeight <= 0) continue;
                var scale = new ScaleTransform(1, 1);
                var offset = new TranslateTransform();
                var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(offset);
                var (copy,bounds)=MagnifyCopy(source,DockWindow.IsTop);
                copy.RenderTransform=transform;
                RenderOptions.SetBitmapScalingMode(copy, BitmapScalingMode.HighQuality);
                state = new MagnifiedTile { Tile = tile, Source = source, Copy = copy, Scale = scale, Offset = offset, OriginalOpacity = tile.Opacity,Bounds=bounds };
                magnified[tile] = state;
                dock.Overlay.Children.Add(copy);
                // Hide the shell, not the brush source: exactly one visible icon remains.
                // Opacity does not change the original hit target, tooltip or drag handlers.
                tile.Opacity = 0;
            }
            state.Amount = SystemParameters.ClientAreaAnimation ? MagnifyApproach(state.Amount, target, dt, (magnifyHover ? 0.065 : 0.045)*InteractionTiming.Factor) : target;
            if (Math.Abs(state.Amount - target) > 0.0005) moving = true; else state.Amount = target;
            states.Add(state);
        }
        double extra = states.Sum(s => s.Source.ActualWidth * (s.Amount - 1));
        double before = 0;
        foreach (var state in states)
        {
            var tile = state.Tile;
            var pos = state.Source.TranslatePoint(new Point(0, 0), dock.RootGrid);
            double growth = state.Source.ActualWidth * (state.Amount - 1);
            // Share the added width evenly: neighbors separate rather than overlap.
            state.Offset.X = before + growth / 2 - extra / 2;
            state.Offset.Y = (DockWindow.IsTop ? 1 : -1) * F(2) * (state.Amount - 1) / Math.Max(0.01, max - 1);
            before += growth;
            state.Scale.ScaleX = state.Scale.ScaleY = state.Amount;
            Canvas.SetLeft(state.Copy, pos.X+state.Bounds.X); Canvas.SetTop(state.Copy, pos.Y+state.Bounds.Y);
            Panel.SetZIndex(state.Copy, (int)(state.Amount * 100));
        }
        if (!moving && !magnifyHover) ClearMagnify();
        return !moving;
    }

    void ClearMagnify()
    {
        StopMagnifyFrames();
        magnifyHover = false;
        foreach (var state in magnified.Values)
        {
            state.Tile.Opacity = state.OriginalOpacity;
            (VisualTreeHelper.GetParent(state.Copy) as Panel)?.Children.Remove(state.Copy);
        }
        magnified.Clear();
        magnifyDock = null;
    }

    void AttachMagnify()
    {
        row.MouseMove += (_, e) => Magnify(e.GetPosition(row));
        row.MouseLeave += (_, _) => { magnifyHover = false; if (magnified.Count > 0) StartMagnifyFrames(); };
        Unloaded += (_, _) => ClearMagnify();
    }

    internal static (int Copies, int Hidden, double Peak) PreviewMagnify(DockWindow dock, double fraction, int frames, bool leave = false)
    {
        var apps = instances.FirstOrDefault(i => i.IsDescendantOf(dock.RootGrid));
        if (apps == null) return (0, 0, 1);
        if (leave) apps.magnifyHover = false;
        else apps.Magnify(new Point(apps.row.ActualWidth * fraction, 0), dock);
        if(frames==0)apps.StepMagnify(0); // Inspect the actual source-to-overlay swap before growth.
        for (int i = 0; i < frames; i++) apps.StepMagnify(1d / 60);
        apps.StopMagnifyFrames();
        return (apps.magnified.Count, apps.magnified.Values.Count(s => s.Tile.Opacity == 0), apps.magnified.Values.Select(s => s.Amount).DefaultIfEmpty(1).Max());
    }

}

/// <summary>
/// Jump list (hedef 1): uygulamanın son açtığı dosyalar (Windows'un "son kullanılanlar" kısayollarından, dosya türü bu
/// uygulamayla açılanlar) ve bilinen uygulamaların görevleri.
/// </summary>
static class JumpLists
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int AssocQueryString(int flags, int str, string assoc, string? extra, StringBuilder? outBuf, ref uint outLen);

    static readonly Dictionary<string, string> handler = new(StringComparer.OrdinalIgnoreCase);
    static List<(string Lnk, string Target, DateTime When)>? recent;
    static DateTime recentAt;

    static string Handler(string ext)
    {
        if (handler.TryGetValue(ext, out var h)) return h;
        uint len = 1024;
        var sb = new StringBuilder((int)len);
        h = AssocQueryString(0, 2 /* ASSOCSTR_EXECUTABLE */, ext, null, sb, ref len) == 0 ? sb.ToString() : "";
        handler[ext] = h;
        return h;
    }

    static List<(string Lnk, string Target, DateTime When)> RecentItems()
    {
        if (recent != null && (DateTime.Now - recentAt).TotalSeconds < 30) return recent;
        recentAt = DateTime.Now;
        var list = new List<(string, string, DateTime)>();
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        try
        {
            foreach (var f in new DirectoryInfo(dir).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(120))
            {
                var t = LnkTarget(f.FullName);
                if (t.Length > 0) list.Add((f.FullName, t, f.LastWriteTime));
            }
        }
        catch { }
        return recent = list;
    }

    static readonly Dictionary<string, string> lnk = new(StringComparer.OrdinalIgnoreCase);

    static string LnkTarget(string path)
    {
        if (lnk.TryGetValue(path, out var t)) return t;
        t = "";
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type != null)
            {
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic sc = shell.CreateShortcut(path);
                t = (string)sc.TargetPath ?? "";
                Marshal.FinalReleaseComObject(sc);
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch { }
        return lnk[path] = t;
    }

    public static List<(string Title, string Path)> Recent(string exe, int max)
    {
        var name = System.IO.Path.GetFileName(exe);
        bool explorer = name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
        var result = new List<(string, string)>();
        foreach (var (_, target, _) in RecentItems())
        {
            if (result.Count >= max) break;
            bool dir = Directory.Exists(target);
            if (explorer ? !dir : dir || !File.Exists(target)) continue;
            if (!explorer)
            {
                var h = Handler(System.IO.Path.GetExtension(target));
                if (!System.IO.Path.GetFileName(h).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            }
            if (!result.Any(r => r.Item2 == target)) result.Add((System.IO.Path.GetFileName(target.TrimEnd('\\')) is { Length: > 0 } n ? n : target, target));
        }
        return result;
    }

    public static List<(string Title, Action Run)> Tasks(string exe)
    {
        var n = System.IO.Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        void Start(string args) { try { System.Diagnostics.Process.Start(exe, args); } catch (Exception ex) { App.Log(ex.Message); } }
        return n switch
        {
            "chrome" or "brave" or "browser" or "vivaldi" => [("Yeni pencere", () => Start("--new-window")), ("Yeni gizli pencere", () => Start("--incognito"))],
            "msedge" => [("Yeni pencere", () => Start("--new-window")), ("Yeni InPrivate pencere", () => Start("--inprivate"))],
            "firefox" => [("Yeni pencere", () => Start("-new-window")), ("Yeni gizli pencere", () => Start("-private-window"))],
            "explorer" => [("Yeni pencere", () => Start("")), ("İndirilenler", () => App.Open(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")))],
            "windowsterminal" => [("Yeni pencere", () => Start("-w new"))],
            "code" => [("Yeni pencere", () => Start("--new-window"))],
            "telegram" => [],
            _ => [],
        };
    }
}
