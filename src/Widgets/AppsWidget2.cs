using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Iskele;

/// <summary>Fotoğraf ve videolar için Windows'un kendi küçük resimleri (IShellItemImageFactory).</summary>
static class Thumbs
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbmp); }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

    static readonly HashSet<string> Media = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm" };
    static readonly Dictionary<string, ImageSource?> cache = new(StringComparer.OrdinalIgnoreCase);
    public static void Trim() { if (cache.Count > 64) cache.Clear(); }

    public static bool IsMedia(string path) => path.Length > 0 && Media.Contains(Path.GetExtension(path));

    public static ImageSource? Get(string path, int px)
    {
        if (cache.TryGetValue(path, out var img)) return img;
        img = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var f);
            if (f.GetImage(new SIZE { cx = px, cy = px }, 0x0 /* SIIGBF_RESIZETOFIT */, out var hbmp) == 0 && hbmp != IntPtr.Zero)
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                DeleteObject(hbmp);
                img = src;
            }
            Marshal.ReleaseComObject(f);
        }
        catch (Exception ex) { App.Log(ex.Message); }
        cache[path] = img;
        return img;
    }

    /// <summary>Küçük resmi yuvarlak köşeli kare olarak göster.</summary>
    public static FrameworkElement Rounded(ImageSource src, double size) => new Border
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.22),
        Background = new ImageBrush(src) { Stretch = Stretch.UniformToFill },
        BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1),
    };
}

partial class AppsWidget
{
    // ───────── 25. sabit yerleşim: açık uygulamaların sırası hatırlanır, alan ayrılır ─────────

    static readonly List<string> openOrder = [];

    /// <summary>Sabitlenmemiş açık uygulamalar ilk açıldıkları sırada kalsın (pencere odağı değişince kaymasın).</summary>
    static List<Entry> StableOrder(List<Entry> entries)
    {
        var pinned = entries.Where(e => e.Pinned).ToList();
        var open = entries.Where(e => !e.Pinned).ToList();
        foreach (var e in open) if (!openOrder.Contains(e.Key)) openOrder.Add(e.Key);
        openOrder.RemoveAll(k => open.All(e => e.Key != k));
        pinned.AddRange(open.OrderBy(e => openOrder.IndexOf(e.Key)));
        return pinned;
    }

    double TileWidth => (App.Settings.AppCards ? H : Math.Round(H * (IconFactor + 0.2))) + 2 * F(1);

    /// <summary>
    /// Uygulamalar alanı en fazla görülen simge sayısı kadar yer ayırır; yeni bir uygulama açılınca diğer widget'lar
    /// ve balonun boyu kaymaz, simge ayrılmış boşluğa yerleşir.
    /// </summary>
    void ApplyReserve()
    {
        row.HorizontalAlignment = HorizontalAlignment.Left;
        if (!App.Settings.FixedAppArea) { row.MinWidth = 0; return; }
        int n = row.Children.Count;
        if (n > App.Settings.AppSlots) { App.Settings.AppSlots = n; App.Settings.Save(); }
        row.MinWidth = App.Settings.AppSlots * TileWidth;
    }

    // ───────── 20. özel Başlat resmi ─────────

    FrameworkElement? StartImage()
    {
        var p = App.Settings.StartImage;
        if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 96;
            bmp.UriSource = new Uri(p);
            bmp.EndInit();
            bmp.Freeze();
            return Thumbs.Rounded(bmp, H * 0.55);
        }
        catch (Exception ex) { App.Log(ex.Message); return null; }
    }

    // ───────── 24. müzik çalan uygulamada ekolayzer ─────────

    readonly List<(Entry Entry, FrameworkElement Eq)> eqs = [];

    FrameworkElement Equalizer(Entry e)
    {
        var p = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, F(3), F(2), 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        var bg = new Border { CornerRadius = new CornerRadius(F(4)), Padding = new Thickness(F(2.5), F(2), F(2.5), F(2)), Child = p };
        bg.SetResourceReference(Border.BackgroundProperty, "Accent");
        bg.HorizontalAlignment = HorizontalAlignment.Right;
        bg.VerticalAlignment = VerticalAlignment.Top;
        bg.Margin = new Thickness(0, 0, F(2), 0);
        bg.Visibility = Visibility.Collapsed;
        bg.IsHitTestVisible = false;
        p.Visibility = Visibility.Visible;
        p.Margin = new Thickness(0);
        int i = 0;
        foreach (var ms in new[] { 420, 300, 520 })
        {
            var s = new ScaleTransform(1, 0.4);
            var bar = new Border { Width = F(2), Height = F(9), Margin = new Thickness(i++ == 0 ? 0 : F(1), 0, 0, 0), Background = Brushes.White, CornerRadius = new CornerRadius(F(1)), RenderTransform = s, RenderTransformOrigin = new Point(0.5, 1) };
            s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(ms)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            p.Children.Add(bar);
        }
        eqs.Add((e, bg));
        return bg;
    }

    public override void Tick(DateTime now)
    {
        UpdateBadges();
        if (eqs.Count == 0) return;
        var src = MediaWidget.PlayingApp().ToLowerInvariant();
        foreach (var (e, eq) in eqs)
        {
            bool on = src.Length > 0 && Names(e).Any(n => n.Length > 1 && src.Contains(n));
            var v = on ? Visibility.Visible : Visibility.Collapsed;
            if (eq.Visibility != v) eq.Visibility = v;
        }
    }

    static IEnumerable<string> Names(Entry e)
    {
        foreach (var w in e.Windows) if (w.Exe.Length > 0) yield return Path.GetFileNameWithoutExtension(w.Exe).ToLowerInvariant();
        if (e.MatchName.Length > 0) yield return Path.GetFileNameWithoutExtension(e.MatchName).ToLowerInvariant();
    }

    // ───────── 10. sürükleyerek sırala; dock'un dışına sürükleyince sabitlemeyi kaldır ─────────

    sealed class AppDrag
    {
        public Border Tile = null!;
        public Entry Entry = null!;
        public POINT Start;
        public bool Active;
        public TranslateTransform Lift = new();
    }

    AppDrag? appDrag, appPending;
    const int UnpinDistance = 70;
    FrameworkElement? groupTarget;
    DateTime groupSince;
    bool groupReady;

    void SetGroupHighlight(bool on)
    {
        groupReady = on;
        if (groupTarget is Border b && b.Child is Grid g && g.Children.Count > 0 && g.Children[0] is Border bg)
        {
            if (on) bg.SetResourceReference(Border.BackgroundProperty, "Accent");
            else if (App.Settings.AppCards) bg.SetResourceReference(Border.BackgroundProperty, "CardBg");
            else bg.Background = Brushes.Transparent;
        }
    }

    void AttachReorder(Border tile, Entry e)
    {
        tile.PreviewMouseLeftButtonDown += (_, _) =>
        {
            Native.GetCursorPos(out var p);
            appPending = new AppDrag { Tile = tile, Entry = e, Start = p };
            App.Debug($"uygulama surukle: basildi {e.Name}");
            DockWindow.BlockDockDrag = true;   // uygulama simgesinden tutulan sürükleme dock'u taşımaz, simgeyi taşır
        };
        tile.PreviewMouseMove += (_, ev) =>
        {
            if (appPending == null || appPending.Tile != tile) return;
            if (ev.LeftButton != MouseButtonState.Pressed) { App.Debug("uygulama surukle: tus birakilmis"); appPending = null; DockWindow.BlockDockDrag = false; return; }
            Native.GetCursorPos(out var p);
            var d = appPending;
            if (!d.Active)
            {
                int dx = Math.Abs(p.X - d.Start.X), dy = Math.Abs(p.Y - d.Start.Y);
                if (Math.Max(dx, dy) < 8) return;
                App.Debug("uygulama surukle: basladi");
                d.Active = true;
                appDrag = d;
                DockWindow.BlockDockDrag = true;
                WindowPreview.HideNow();
                HideName();
                tile.CaptureMouse();
                tile.RenderTransform = d.Lift;
                Panel.SetZIndex(tile, 50);
                tile.Opacity = 0.85;
            }
            DragMove(p);
            ev.Handled = true;
        };
        tile.PreviewMouseLeftButtonUp += (_, ev) =>
        {
            var d = appDrag;
            appPending = null;
            if (d == null) DockWindow.BlockDockDrag = false;
            if (d == null || d.Tile != tile) return;
            ev.Handled = true;   // tıklama sayılmasın
            EndDrag(d);
        };
        tile.LostMouseCapture += (_, _) => { if (appDrag?.Tile == tile) EndDrag(appDrag); };
    }

    void DragMove(POINT p)
    {
        var d = appDrag!;
        // sütun içinde yeri: imlecin satırdaki konumuna göre komşular yer açar
        var pos = row.PointFromScreen(new Point(p.X, p.Y));
        int cur = row.Children.IndexOf(d.Tile);

        // 3. gruplama: başka bir simgenin ortasında ~0,45 sn tutulursa onunla grup olur
        FrameworkElement? over = null;
        foreach (FrameworkElement c in row.Children)
        {
            if (c == d.Tile || c.Tag is not Entry) continue;
            var x0 = c.TranslatePoint(new Point(0, 0), row).X;
            if (pos.X >= x0 + c.ActualWidth * 0.3 && pos.X <= x0 + c.ActualWidth * 0.7) { over = c; break; }
        }
        if (over != null && d.Entry.Pinned && d.Entry.Group == null)
        {
            if (groupTarget != over) { SetGroupHighlight(false); groupTarget = over; groupSince = DateTime.Now; }
            else if ((DateTime.Now - groupSince).TotalMilliseconds > 450) SetGroupHighlight(true);
            d.Lift.Y = (p.Y - d.Start.Y) / Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleY);
            return;   // ortadayken yer değiştirme
        }
        SetGroupHighlight(false);
        groupTarget = null;
        int target = 1;   // 0: Başlat düğmesi
        for (int i = 1; i < row.Children.Count; i++)
        {
            var c = (FrameworkElement)row.Children[i];
            if (c == d.Tile) continue;
            double mid = c.TranslatePoint(new Point(c.ActualWidth / 2, 0), row).X;
            if (pos.X > mid) target++;
        }
        target = Math.Clamp(target, 1, row.Children.Count - 1);
        if (target != cur)
        {
            row.Children.RemoveAt(cur);
            row.Children.Insert(target, d.Tile);
        }
        // yatayda imleçte, dikeyde imleci izle; uzaklaşınca "kaldır" belli olsun
        double scale = Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleY);
        double dy = (p.Y - d.Start.Y) / scale;
        d.Lift.Y = dy;
        bool remove = d.Entry.Pinned && Math.Abs(p.Y - d.Start.Y) > UnpinDistance * scale;
        d.Tile.Opacity = remove ? 0.35 : 0.85;
        d.Tile.ToolTip = null;
        Cursor = remove ? Cursors.No : Cursors.SizeWE;
    }

    void EndDrag(AppDrag d)
    {
        if (appDrag != d) return;
        appDrag = null;
        DockWindow.BlockDockDrag = false;
        Cursor = null;
        if (d.Tile.IsMouseCaptured) d.Tile.ReleaseMouseCapture();
        Native.GetCursorPos(out var p);
        double scale = Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleY);
        bool remove = d.Entry.Pinned && Math.Abs(p.Y - d.Start.Y) > UnpinDistance * scale;

        if (groupTarget != null && (DateTime.Now - groupSince).TotalMilliseconds > 450) groupReady = true;   // imleç kıpırdamadan tutulduysa
        if (groupReady && groupTarget?.Tag is Entry target && !remove)
        {
            MakeGroup(d.Entry, target);
        }
        else if (remove)
        {
            Pins.RemoveAll(x => x.Equals(d.Entry.Launch, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // yeni sıra: sabitlenmişler görsel sıradaki yerlerine; sabitlenmemiş bir uygulama sabitlenmişlerin
            // arasına bırakıldıysa onu da sabitle
            var order = row.Children.OfType<FrameworkElement>().Select(c => c.Tag as Entry).Where(x => x != null).Cast<Entry>().ToList();
            int lastPinned = order.FindLastIndex(x => x.Pinned && x != d.Entry);
            int mine = order.IndexOf(d.Entry);
            var newPins = new List<string>();
            foreach (var x in order)
            {
                if (x.Pinned) newPins.Add(x.Launch);
                else if (x == d.Entry && mine <= lastPinned + 1 && lastPinned >= 0 && x.Launch.Length > 0) newPins.Add(x.Launch);
            }
            // listede görünmeyen (dosyası silinmiş vb.) sabitlenmişleri kaybetme
            foreach (var old in Pins) if (!newPins.Contains(old, StringComparer.OrdinalIgnoreCase) && !(d.Entry.Pinned && old == d.Entry.Launch)) newPins.Add(old);
            Pins.Clear();
            Pins.AddRange(newPins);
            // sabitlenmemişlerin sırası da sürüklenen yerde kalsın
            openOrder.Clear();
            openOrder.AddRange(order.Where(x => !x.Pinned && !(x == d.Entry && newPins.Contains(x.Launch))).Select(x => x.Key));
        }
        SetGroupHighlight(false);
        groupTarget = null;
        App.Settings.Save();
        signature = "";
        Refresh();
    }
}
