using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Iskele;

/// <summary>
/// Uygulama simgesinin üzerinde durunca açılan canlı pencere önizlemeleri (DWM küçük resimleri).
/// DWM küçük resimleri katmanlı (AllowsTransparency) pencerelerde çizilmez; bu yüzden bu pencere
/// katmansızdır ve köşeleri Windows 11'in kendi yuvarlatmasıyla yumuşatılır.
/// </summary>
class WindowPreview : Window
{
    [StructLayout(LayoutKind.Sequential)]
    struct THUMB_PROPS
    {
        public int dwFlags;
        public RECT rcDestination, rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMB_PROPS props);
    [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

    const double ThumbW = 220, ThumbH = 130;

    // ── 2. göz atma: Windows'un canlı önizlemesi (Aero Peek) — pencere öne çıkar, diğerleri saydamlaşır
    [DllImport("dwmapi.dll", EntryPoint = "#113")] static extern int DwmpActivateLivePreview(int peekOn, IntPtr hPeekWindow, IntPtr hTopmostWindow, int peekType, IntPtr unknown);
    DispatcherTimer? peekTimer;
    bool peeking;

    void StartPeek(IntPtr target)
    {
        peekTimer?.Stop();
        peekTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            try { peeking = DwmpActivateLivePreview(1, target, hwnd, 1, IntPtr.Zero) == 0; App.Debug($"gozat: {peeking}"); }
            catch (Exception ex) { App.Log(ex.Message); }
        }, Dispatcher);
    }

    void StopPeek()
    {
        peekTimer?.Stop();
        if (!peeking) return;
        peeking = false;
        try { DwmpActivateLivePreview(0, IntPtr.Zero, hwnd, 1, IntPtr.Zero); } catch { }
    }

    static WindowPreview? instance;
    static readonly DispatcherTimer showTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
    static readonly DispatcherTimer watchTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    static FrameworkElement? pendingAnchor;
    static Func<List<WinInfo>>? pendingWindows;
    static string pendingName = "";
    static ImageSource? pendingIcon;
    static int misses;

    IntPtr hwnd;
    RECT anchorRect;
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal };
    readonly List<(IntPtr Thumb, Border Host, WinInfo Win)> thumbs = [];
    Func<List<WinInfo>> source = () => [];
    string appName = "";
    ImageSource? appIcon;

    static WindowPreview()
    {
        showTimer.Tick += (_, _) =>
        {
            showTimer.Stop();
            App.Debug($"zamanlayici: anchor={(pendingAnchor != null)} mouseOver={pendingAnchor?.IsMouseOver} bagli={pendingAnchor?.IsLoaded}");
            if (pendingAnchor != null && pendingWindows != null && pendingAnchor.IsMouseOver)
                Open(pendingAnchor, pendingWindows, pendingName, pendingIcon);
        };
        watchTimer.Tick += (_, _) => instance?.Watch();
    }

    // ───────── dışarıya açık API ─────────

    /// <summary>Simgenin üzerine gelindi: kısa bir gecikmeyle (önizleme zaten açıksa hemen) göster.</summary>
    public static void Hover(FrameworkElement anchor, Func<List<WinInfo>> windows, string name, ImageSource? icon)
    {
        int n = windows().Count;
        App.Debug($"hover '{name}' pencere={n} acik={instance != null}");
        if (!App.Settings.Previews || n == 0) return;
        pendingAnchor = anchor;
        pendingWindows = windows;
        pendingName = name;
        pendingIcon = icon;
        if (instance != null) { Open(anchor, windows, name, icon); return; }
        showTimer.Stop();
        showTimer.Start();
    }

    static bool pinned;
    public static bool IsOpen => instance != null;

    /// <summary>Test için: fareye gerek kalmadan önizlemeyi aç ve açık tut.</summary>
    public static void ShowPinned(FrameworkElement anchor, Func<List<WinInfo>> windows, string name, ImageSource? icon)
    {
        pinned = true;
        pendingAnchor = anchor;
        Open(anchor, windows, name, icon);
    }

    public static void Unhover(FrameworkElement anchor)
    {
        if (pendingAnchor == anchor) showTimer.Stop();
    }

    public static void HideNow()
    {
        if (instance != null) App.Debug("onizleme kapatiliyor");
        showTimer.Stop();
        pendingAnchor = null;
        instance?.Close();
    }

    // ───────── iç işleyiş ─────────

    static void Open(FrameworkElement anchor, Func<List<WinInfo>> windows, string name, ImageSource? icon)
    {
        if (instance == null)
        {
            instance = new WindowPreview();
            instance.Closed += (_, _) => { instance = null; watchTimer.Stop(); };
        }
        instance.source = windows;
        instance.appName = name;
        instance.appIcon = icon;
        instance.PlaceAndShow(anchor);
    }

    WindowPreview()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = Ui.Font;
        SetResourceReference(BackgroundProperty, "MenuBg");
        Content = new Border { Padding = new Thickness(8), Child = row };
        FirstFrame.Guard(this);
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            long ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
            int round = 2;   // DWMWCP_ROUND (Windows 11)
            DwmSetWindowAttribute(hwnd, 33, ref round, 4);
            int noAnim = 1;  // DWMWA_TRANSITIONS_FORCEDISABLED: anında görünsün
            DwmSetWindowAttribute(hwnd, 3, ref noAnim, 4);
        };
        Closed += (_, _) => { StopPeek(); ClearThumbs(); };
    }

    void PlaceAndShow(FrameworkElement anchor)
    {
        var src = PresentationSource.FromVisual(anchor);
        if (src?.CompositionTarget == null) return;

        var tl = anchor.PointToScreen(new Point(0, 0));
        var br = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight));
        anchorRect = new RECT { Left = (int)tl.X, Top = (int)tl.Y, Right = (int)br.X, Bottom = (int)br.Y };

        BuildCards();
        if (thumbs.Count == 0) { Close(); return; }

        var content = (FrameworkElement)Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = content.DesiredSize;
        var dip = src.CompositionTarget.TransformFromDevice;
        var aTop = dip.Transform(tl);
        var aBottom = dip.Transform(br);
        double sw = SystemParameters.PrimaryScreenWidth;
        Left = Math.Clamp((aTop.X + aBottom.X) / 2 - size.Width / 2, 6, Math.Max(6, sw - size.Width - 6));
        Top = DockWindow.IsTop ? aBottom.Y + 10 : aTop.Y - size.Height - 10;

        App.Debug($"onizleme: kart={thumbs.Count} konum=({Left:0},{Top:0}) boyut=({size.Width:0}x{size.Height:0}) anchor=({anchorRect.Left},{anchorRect.Top},{anchorRect.Right},{anchorRect.Bottom})");
        if (!IsVisible) Show();
        UpdateLayout();
        Dispatcher.BeginInvoke(PlaceThumbs, DispatcherPriority.Render);
        misses = 0;
        watchTimer.Start();
    }

    void BuildCards()
    {
        ClearThumbs();
        row.Children.Clear();
        foreach (var w in source().Take(6))
        {
            if (!Native.IsWindow(w.Hwnd)) continue;
            var win = w;

            var title = Ui.Text(win.Title, 12, "Fg", FontWeights.SemiBold);
            title.Margin = new Thickness(6, 0, 6, 0);
            var close = new IconButton("", 9, 22) { ToolTip = "Pencereyi kapat" };
            close.Click += () => CloseWindow(win);
            var head = new DockPanel { Margin = new Thickness(2, 0, 0, 6) };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            if (appIcon != null) head.Children.Add(new Image { Source = appIcon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(title);

            var host = new Border { Width = ThumbW, Height = ThumbH, CornerRadius = new CornerRadius(6) };
            if (Native.IsIconic(win.Hwnd))
            {
                // Simge durumundaki pencerelerin çizilecek yüzeyi yoktur: büyük uygulama simgesi göster.
                host.SetResourceReference(Border.BackgroundProperty, "Track");
                host.Child = appIcon != null
                    ? new Image { Source = appIcon, Width = 48, Height = 48, Opacity = 0.85 }
                    : Ui.Glyph("", 28, "FgDim");
            }

            var card = new Border
            {
                Padding = new Thickness(8), CornerRadius = new CornerRadius(10), Margin = new Thickness(2, 0, 2, 0),
                Child = Ui.Col(head, host), Cursor = Cursors.Hand, Background = Brushes.Transparent, Width = ThumbW + 16,
                ToolTip = win.Title,
            };
            card.MouseEnter += (_, _) => { card.SetResourceReference(Border.BackgroundProperty, "Track"); StartPeek(win.Hwnd); };
            card.MouseLeave += (_, _) => { card.Background = Brushes.Transparent; StopPeek(); };
            card.MouseLeftButtonUp += (_, e) =>
            {
                if (e.Handled) return;
                e.Handled = true;
                HideNow();
                WindowTracker.Activate(win.Hwnd);
            };
            card.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) CloseWindow(win); };
            row.Children.Add(card);
            thumbs.Add((IntPtr.Zero, host, win));
        }
        if (thumbs.Count == 0)
            row.Children.Add(Ui.Text(appName, 12, "FgDim"));
    }

    void PlaceThumbs()
    {
        if (hwnd == IntPtr.Zero || !IsVisible) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        for (int i = 0; i < thumbs.Count; i++)
        {
            var (thumb, host, win) = thumbs[i];
            if (Native.IsIconic(win.Hwnd)) continue;
            if (thumb == IntPtr.Zero)
            {
                int hr = DwmRegisterThumbnail(hwnd, win.Hwnd, out thumb);
                App.Debug($"thumbnail kaydi hr=0x{hr:X8}");
                if (hr != 0) continue;
            }
            thumbs[i] = (thumb, host, win);

            // kutunun bu penceredeki yeri (fiziksel piksel)
            var p = host.TransformToAncestor(this).Transform(new Point(0, 0));
            double bx = p.X * scale, by = p.Y * scale, bw = host.ActualWidth * scale, bh = host.ActualHeight * scale;

            // en-boy oranını koruyarak sığdır, ortala
            DwmQueryThumbnailSourceSize(thumb, out var s);
            double fit = s.cx > 0 && s.cy > 0 ? Math.Min(bw / s.cx, bh / s.cy) : 1;
            double w = s.cx > 0 ? s.cx * fit : bw, h = s.cy > 0 ? s.cy * fit : bh;
            var props = new THUMB_PROPS
            {
                dwFlags = 0x1 | 0x4 | 0x8 | 0x10,   // DESTINATION | OPACITY | VISIBLE | SOURCECLIENTAREAONLY
                rcDestination = new RECT
                {
                    Left = (int)(bx + (bw - w) / 2), Top = (int)(by + (bh - h) / 2),
                    Right = (int)(bx + (bw + w) / 2), Bottom = (int)(by + (bh + h) / 2),
                },
                opacity = 255,
                fVisible = true,
                fSourceClientAreaOnly = false,
            };
            DwmUpdateThumbnailProperties(thumb, ref props);
        }
    }

    void ClearThumbs()
    {
        foreach (var (thumb, _, _) in thumbs)
            if (thumb != IntPtr.Zero) DwmUnregisterThumbnail(thumb);
        thumbs.Clear();
    }

    void CloseWindow(WinInfo win)
    {
        Native.PostMessage(win.Hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        // pencerenin kapanması için kısa bir süre tanı, sonra kalanlarla yeniden çiz
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            WindowTracker.Refresh();
            if (pendingAnchor == null || !IsVisible) return;
            var left = source().Where(w => Native.IsWindow(w.Hwnd)).ToList();
            if (left.Count == 0) { Close(); return; }
            PlaceAndShow(pendingAnchor);
        };
        t.Start();
    }

    /// <summary>İmleç ne simgede ne önizlemede ise (ve aradaki boşlukta değilse) kapat.</summary>
    void Watch()
    {
        if (hwnd == IntPtr.Zero || pinned) return;
        Native.GetCursorPos(out var c);
        GetWindowRect(hwnd, out var me);
        bool inMe = c.X >= me.Left && c.X < me.Right && c.Y >= me.Top && c.Y < me.Bottom;
        bool inAnchor = c.X >= anchorRect.Left - 4 && c.X < anchorRect.Right + 4 && c.Y >= anchorRect.Top - 4 && c.Y < anchorRect.Bottom + 4;
        // simge ile önizleme arasındaki köprü
        int bridgeTop = Math.Min(me.Bottom, anchorRect.Top), bridgeBottom = Math.Max(me.Top, anchorRect.Bottom);
        bool inBridge = c.X >= anchorRect.Left - 4 && c.X < anchorRect.Right + 4 && c.Y >= Math.Min(bridgeTop, bridgeBottom) && c.Y < Math.Max(bridgeTop, bridgeBottom);

        if (inMe || inAnchor || inBridge) { misses = 0; return; }
        if (++misses >= 3)
        {
            App.Debug($"izleyici kapatti: imlec=({c.X},{c.Y}) pencere=({me.Left},{me.Top},{me.Right},{me.Bottom})");
            HideNow();
        }
    }
}
