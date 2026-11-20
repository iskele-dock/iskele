using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace Iskele;

/// <summary>
/// Ekranın alt/üst kenarına yapışan dock. Normalde Windows'a "AppBar" olarak kaydolur; böylece
/// ekranın o şeridini ayırır ve büyütülmüş pencereler dock'un altına girmez. Otomatik gizlemede
/// alan ayırmaz; kenara kayar ve imleç kenara değince geri gelir.
/// </summary>
class DockWindow : Window
{
    IntPtr hwnd;
    uint callbackMsg, taskbarCreatedMsg, shellHookMsg;
    [DllImport("user32.dll")] static extern bool RegisterShellHookWindow(IntPtr h);

    // ── gerçek cam: dock'un arkasındaki ayrı, katmansız bulanık pencere
    GlassBackdrop? glass;
    DispatcherTimer? glassTimer;
    static bool GlassTheme => S.Theme is "glass" or "iosglass";

    void UpdateGlass()
    {
        if (!GlassTheme || hwnd == IntPtr.Zero)
        {
            glassTimer?.Stop();
            glass?.Close();
            glass = null;
            return;
        }
        glass ??= new GlassBackdrop();
        glass.ApplyTint();
        glassTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => GlassTick(), Dispatcher);
        glassTimer.Start();
    }

    // Sliders update material/motion in place instead of rebuilding every widget per pixel.
    internal void RefreshMaterialAndTiming()
    {
        hideTimer.Interval=TimeSpan.FromMilliseconds(Math.Clamp(InteractionTiming.Scale(120),16,300));
        glass?.ApplyTint();
    }

    void GlassTick()
    {
        if (glass == null) return;
        FrameworkElement shape = Balloon ? morph : pill;
        bool hidden = !IsVisible || fullscreenHidden || shape.ActualWidth <= 0 || (!Balloon && S.AutoHide && !shown && slide.Y != 0 && Math.Abs(slide.Y) > DockH);
        if (hidden || PresentationSource.FromVisual(shape) == null) { glass.Place(default, 0, hwnd); return; }
        var a = shape.PointToScreen(new Point(0, 0));
        var b = shape.PointToScreen(new Point(shape.ActualWidth, shape.ActualHeight));
        double sc = Dpi;
        int radius = (int)Math.Round((shape is Border br ? br.CornerRadius.TopLeft : 0) * sc);
        glass.Place(new RECT { Left = (int)Math.Round(Math.Min(a.X, b.X)), Top = (int)Math.Round(Math.Min(a.Y, b.Y)), Right = (int)Math.Round(Math.Max(a.X, b.X)), Bottom = (int)Math.Round(Math.Max(a.Y, b.Y)) }, radius, hwnd);
    }

    readonly List<Border> edgeHandles = [];

    /// <summary>Büyütülmüş simgelerin çizildiği, dock'un üstündeki katman.</summary>
    public readonly Canvas Overlay = new() { IsHitTestVisible = false, ClipToBounds = false };
    public Grid RootGrid => root;

    /// <summary>Büyütme için dock'un kenarından uzağa ayrılan saydam ek yükseklik (DIP).</summary>
    static double MagnifyExtra => S.Magnify > 1.01 ? Math.Ceiling((S.Magnify - 1) * CardH) + 6 : 0;
    bool registered, fullscreenHidden, editMode, shown = true;
    int tickCount;
    DateTime lastWanted = DateTime.Now;

    readonly Grid root = new();
    readonly Border pill = new() { BorderThickness = new Thickness(1) };
    readonly Border hotEdge = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Height = 2 };
    readonly TranslateTransform slide = new();
    readonly StackPanel strip = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; // Restore natural widget order without automatic centering.
    readonly ScrollViewer scroller;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer hideTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer hoverOpenTimer=new(DispatcherPriority.Input); // Exact dwell, without waiting for the polling timer.
    readonly List<Slot> slots = [];

    static int openMenus;

    static Settings S => App.Settings;
    public static double CardH => Math.Round(44 * S.Scale);
    static double PadV => Math.Round(Math.Clamp(S.DockPadding, 0, 16) * S.Scale);
    /// <summary>Balon modu: ekranın en üstünde ortada yüzen, üzerine gelince genişleyen ada.</summary>
    public static bool Balloon => S.Mode == "balloon";
    static bool Full => S.FullWidth && !Balloon;
    static double Gap => Balloon ? Math.Round(8 * S.Scale) : Full ? 0 : Math.Round(Math.Clamp(S.EdgeDistance, 0, 40) * S.Scale);
    static double DockH => CardH + 2 * PadV + 2;
    // balonda gölge için alttan pay bırak
    static double Reserved => DockH + Gap + (Balloon ? 18 : 0);
    /// <summary>
    /// Görev çubuğu gizlenirken ekran alanı korunsun mu? Şerit modunda, alt kenarda ve otomatik gizleme kapalıyken
    /// evet: görev çubuğunun ayırdığı alan aynen kalır, dock oraya oturur; masaüstü simgeleri ve pencereler kaymaz.
    /// </summary>
    static bool KeepSpace => S.HideTaskbar && S.TaskbarSpace != "free";

    /// <summary>Dock'un görev çubuğunun yerine oturduğu alan (fiziksel piksel); null ise kendi alanını ayırır.</summary>
    RECT? fitRect;

    void ComputeFit()
    {
        Settings.Fit = 1;
        fitRect = null;
        // dock'u görev çubuğunun yerine oturtma yalnızca alttaki, otomatik gizlenmeyen şeritte
        if (!KeepSpace || Balloon || S.AutoHide || S.Edge == "top" || !IsPrimaryMon) return;
        var p = TaskbarGuard.Position();
        if (p == null || p.Value.Edge != Native.ABE_BOTTOM) return;
        double scale = (hwnd != IntPtr.Zero ? Native.GetDpiForWindow(hwnd) : Native.GetDpiForSystem()) / 96.0;
        if (scale <= 0) scale = 1;
        double avail = (p.Value.Rect.Bottom - p.Value.Rect.Top) / scale;
        Settings.Fit = Math.Clamp(avail / Reserved, 0.55, 1);
        fitRect = p.Value.Rect;
    }

    /// <summary>Balon ve otomatik gizlemede görev çubuğunun alanı uygulamalara verilir (masaüstüne haber vermeden).</summary>
    static bool WantFullArea => S.HideTaskbar && KeepSpace && (Balloon || S.AutoHide);

    static bool FullAreaActive()
    {
        var wa = new RECT();
        return Native.SystemParametersInfo(0x0030, 0, ref wa, 0) && wa.Bottom >= Native.GetSystemMetrics(1) && wa.Top <= 0;
    }

    static void ApplyTaskbar()
    {
        if (!S.HideTaskbar) { TaskbarGuard.Restore(); return; }
        TaskbarGuard.Hide(KeepSpace);
        TaskbarGuard.UseFullArea(WantFullArea);
    }

    /// <summary>Dock üstte mi (üst kenar ya da balon)? Açılır paneller buna göre aşağı/yukarı açılır.</summary>
    public static bool IsTop => (Balloon ? S.BalloonEdge : S.Edge) == "top";

    /// <summary>Balonun ekran kenarından (gölge payı dahil) uzaklığı.</summary>
    static double BalloonOffset => IsTop ? Gap : Gap + 10;

    // balon: küçük hali (ada) ve açık/kapalı durumu
    readonly Border island = new() { BorderThickness = new Thickness(1) };
    readonly TextBlock islandTime = new(), islandMedia = new();
    readonly ScaleTransform pillScale = new(1, 1);
    bool expanded = true;
    DateTime lastHover = DateTime.Now;

    static DockWindow()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((_, _) => openMenus++));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler((_, _) => openMenus = Math.Max(0, openMenus - 1)));
    }

    public DockWindow(IntPtr monitor = default)
    {
        Icon = Brand.Source; // Native application identity.
        MonitorHandle = monitor;
        Title = "Iskele";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = Ui.Font;
        UseLayoutRounding = true;

        scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = strip,
        };
        scroller.PreviewMouseWheel += (_, e) =>
        {
            if (scroller.ScrollableWidth <= 0) return;
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        };

        // 21. uçlarından daraltma: iki uçta ince tutma alanı (dock iki yandan birden daralır/genişler)
        var pillGrid = new Grid();
        pillGrid.Children.Add(scroller);
        foreach (var right in new[] { false, true })
        {
            var h = new Border
            {
                Width = 9, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Cursor = Cursors.SizeWE,
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                ToolTip = "Sürükle: dock'u daralt / genişlet · çift tıkla: eski haline getir",
            };
            edgeHandles.Add(h);
            double startW = 0, startX = 0;
            h.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (e.ClickCount == 2) { S.DockMaxWidth = 0; S.Save(); SetPos(); return; }
                Native.GetCursorPos(out var p);
                startX = p.X;
                startW = pill.ActualWidth;
                BlockDockDrag = true;
                h.CaptureMouse();
            };
            h.MouseMove += (_, e) =>
            {
                if (!h.IsMouseCaptured) return;
                Native.GetCursorPos(out var p);
                double dx = (p.X - startX) / Dpi * (right ? 1 : -1);
                double w = Math.Clamp(startW + 2 * dx, CardH * 2.5, Width - 2 * Gap);
                S.DockMaxWidth = w;
                pill.MaxWidth = w;
            };
            h.MouseLeftButtonUp += (_, e) =>
            {
                if (!h.IsMouseCaptured) return;
                h.ReleaseMouseCapture();
                BlockDockDrag = false;
                if (S.DockMaxWidth >= Width - 2 * Gap - 2) S.DockMaxWidth = 0;   // tam açıldıysa sınırı kaldır
                S.Save();
                e.Handled = true;
            };
            pillGrid.Children.Add(h);
        }
        pill.Child = pillGrid;
        var tg = new TransformGroup();
        tg.Children.Add(pillScale);
        tg.Children.Add(slide);
        pill.RenderTransform = tg;
        pill.RenderTransformOrigin = new Point(0.5, 0);

        foreach (var t in new[] { islandTime, islandMedia })
        {
            t.FontFamily = Ui.Font;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            t.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        islandTime.FontWeight = FontWeights.Bold;
        islandMedia.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
        island.Child = Ui.Row(islandTime, islandMedia);
        island.HorizontalAlignment = HorizontalAlignment.Center;
        island.VerticalAlignment = VerticalAlignment.Top;
        island.SetResourceReference(Border.BackgroundProperty, "DockBg");
        island.SetResourceReference(Border.BorderBrushProperty, "DockBorder");
        island.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black };
        island.ContextMenuOpening += (_, _) => island.ContextMenu = BuildMenu();
        island.ContextMenu = BuildMenu();
        island.MouseEnter+=(_,_)=>StartHoverDwell(); // Animation duration is unaffected by this delay.
        island.MouseLeave+=(_,_)=>{hoverOpenTimer.Stop();hoverSince=DateTime.MinValue;};
        hoverOpenTimer.Tick+=(_,_)=>
        {
            hoverOpenTimer.Stop();
            if(Balloon&&!expanded&&CursorOver(island,10))SetExpanded(true);
        };
        island.MouseLeftButtonDown += (_, e) => { StartDrag(fromIsland: true); e.Handled = true; };
        pill.SetResourceReference(Border.BackgroundProperty, "DockBg");
        pill.SetResourceReference(Border.BorderBrushProperty, "DockBorder");
        pill.ContextMenuOpening += (_, _) => pill.ContextMenu = BuildMenu();
        pill.ContextMenu = BuildMenu();
        pill.MouseLeftButtonDown += OnPillDown;

        root.MouseMove += OnRootMove;
        root.MouseLeftButtonUp += OnRootUp;
        PreviewMouseLeftButtonDown += OnAnyDown;
        PreviewMouseMove += OnAnyMove;
        PreviewMouseLeftButtonUp += (_, _) => dragPending = false;
        root.LostMouseCapture += (_, _) => { if (dragArmed && !dragMoved) dragArmed = false; };
        // balonun şekli: ada ile açık balon arasında gerçek genişlik/yükseklikle büyüyüp küçülür,
        // köşe yarıçapı her an yüksekliğin yarısı (her karede tam yuvarlak uçlu hap)
        morph.SetResourceReference(Border.BackgroundProperty, "DockBg");
        morph.SetResourceReference(Border.BorderBrushProperty, "DockBorder");
        morph.BorderThickness = new Thickness(1);
        morph.HorizontalAlignment = HorizontalAlignment.Center;
        morph.IsHitTestVisible = false;
        morph.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black };
        morph.SizeChanged += (_, _) => morph.CornerRadius = new CornerRadius(morph.ActualHeight / 2);
        pill.SizeChanged += (_, _) => { if (Balloon && expanded && !morphing) FitMorph(); };
        island.SizeChanged += (_, _) => { if (Balloon && !expanded && !morphing) FitMorph(); };
        root.Children.Add(morph);
        root.Children.Add(pill);
        root.Children.Add(island);
        root.Children.Add(Overlay);
        root.Children.Add(hotEdge);
        Content = root;

        strip.PreviewMouseLeftButtonDown += OnStripDown;
        strip.PreviewMouseMove += OnStripMove;
        strip.PreviewMouseLeftButtonUp += OnStripUp;


        timer.Tick += (_, _) => Tick();
        hideTimer.Tick += (_, _) => AutoHideTick();
        fullscreenTimer.Tick += (_, _) => SetFullscreen(FullscreenOnMyMonitor());
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => { timer.Stop(); hideTimer.Stop(); hoverOpenTimer.Stop(); fullscreenTimer.Stop(); glassTimer?.Stop(); glass?.Close(); foreach (var s in slots) s.Widget.Detach(); UnregisterBar(); };

        Build();
        timer.Start();
        hideTimer.Start();
        fullscreenTimer.Start();
    }

    // ───────────────────────── yerleşim ─────────────────────────

    void Build()
    {
        hideTimer.Interval=TimeSpan.FromMilliseconds(Math.Clamp(InteractionTiming.Scale(120),16,300)); // Keep hover polling proportional.
        ComputeFit();
        foreach (var s in slots) s.Widget.Detach();
        slots.Clear();
        strip.Children.Clear();

        S.Widgets = S.Widgets.Distinct().Where(WidgetRegistry.Exists).ToList();
        foreach (var id in S.Widgets)
        {
            var w = WidgetRegistry.Create(id);
            if (w == null) continue;
            Theme.ApplyCardStyle(w, S.CardStyle);
            var slot = new Slot(w, this);
            slots.Add(slot);
            strip.Children.Add(slot);
        }

        double r = Balloon ? DockH / 2 : Full ? 0 : Math.Round(16 * S.Scale);
        pill.CornerRadius = new CornerRadius(r);
        pill.Padding = Balloon ? new Thickness(DockH / 2 - CardH / 2, PadV, DockH / 2 - CardH / 2, PadV) : new Thickness(PadV + 1, PadV, PadV + 1, PadV);
        pill.Height = DockH;
        pill.HorizontalAlignment = Full ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        pill.VerticalAlignment = IsTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        double edgeGap = Balloon ? BalloonOffset : Gap;
        pill.Margin = IsTop ? new Thickness(Gap, edgeGap, Gap, 0) : new Thickness(Gap, 0, Gap, edgeGap);
        pill.BorderThickness = Full ? (IsTop ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0)) : new Thickness(1);
        pill.Effect = null;
        pill.RenderTransformOrigin = new Point(0.5, IsTop ? 0 : 1);
        pillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        pillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        pillScale.ScaleX = pillScale.ScaleY = 1;

        if (Balloon)
        {
            // Balonda şekli ayrı arka plan çizer; dock ve ada yalnızca içerik taşır (görünmez ama tıklanabilir).
            pill.Background = Brushes.Transparent;
            pill.BorderBrush = Brushes.Transparent;
            island.Background = Brushes.Transparent;
            island.BorderBrush = Brushes.Transparent;
            island.Effect = null;
            morph.Visibility = Visibility.Visible;
            morph.VerticalAlignment = pill.VerticalAlignment;
            morph.Margin = new Thickness(0, IsTop ? edgeGap : 0, 0, IsTop ? 0 : edgeGap);
        }
        else
        {
            pill.SetResourceReference(Border.BackgroundProperty, "DockBg");
            pill.SetResourceReference(Border.BorderBrushProperty, "DockBorder");
            morph.Visibility = Visibility.Collapsed;
        }
        scroller.HorizontalAlignment = Full ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        hotEdge.VerticalAlignment = IsTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        hotEdge.Visibility = S.AutoHide && !Balloon ? Visibility.Visible : Visibility.Collapsed;
        foreach (var eh in edgeHandles) eh.Visibility = Full ? Visibility.Collapsed : Visibility.Visible;

        // balonun küçük hali
        double ih = Math.Round(DockH * 0.66);
        island.Height = ih;
        island.CornerRadius = new CornerRadius(ih / 2);
        island.Padding = new Thickness(ih * 0.55, 0, ih * 0.55, 0);
        island.VerticalAlignment = IsTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        island.Margin = IsTop ? new Thickness(0, BalloonOffset, 0, 0) : new Thickness(0, 0, 0, BalloonOffset);
        island.MinWidth = ih * 3.2;
        islandTime.FontSize = Math.Round(14 * S.Scale, 1);
        islandMedia.FontSize = Math.Round(12 * S.Scale, 1);
        islandMedia.MaxWidth = 220 * S.Scale;
        island.Visibility = Balloon ? Visibility.Visible : Visibility.Collapsed;

        SetEditMode(editMode);
        if (!S.AutoHide || Balloon) SlideTo(true, animate: false);
        SetExpanded(!Balloon || S.BalloonAlwaysOpen || editMode, animate: false);
        // boyutlar yerleşimden sonra belli olur: şekli o zaman ayarla
        Dispatcher.BeginInvoke(() => SetExpanded(expanded, animate: false), DispatcherPriority.Loaded);
        Tick(force: true);
    }

    public void ApplySettings()
    {
        Theme.Apply(S.Theme);
        ApplyTaskbar();
        UpdateGlass();
        Build();
        SetPos();
        // Görev çubuğu geri gelince Windows çalışma alanını yeniden hesaplar; bir kez daha yerleş.
        Dispatcher.BeginInvoke(SetPos, DispatcherPriority.Background);
        new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Normal, (t, _) => { ((DispatcherTimer)t!).Stop(); SetPos(); }, Dispatcher);
    }

    void Tick(bool force = false)
    {
        tickCount++;
        if (fullscreenHidden && !force) return;
        if ((following || snapping) && !force) return;   // sürüklerken pencereyi yeniden çizdirme (takılma yapar)
        var now = DateTime.Now;
        foreach (var s in slots)
        {
            bool visible = IsVisible && (!S.AutoHide || shown);
            bool inViewport = false;
            if (s.ActualWidth > 0 && s.IsVisible)
            {
                var pos = s.TranslatePoint(new Point(0, 0), scroller);
                inViewport = pos.X + s.ActualWidth > 0 && pos.X < scroller.ActualWidth;
            }
            bool essential = s.Widget.Id is "timer" or "pomodoro" or "stopwatch" || (s.Widget is MediaWidget && tickCount % 5 == 0);
            if (!Performance.ShouldTick(force, visible, Balloon && !expanded, inViewport, essential)) continue;
            try { s.Widget.Tick(now); } catch (Exception ex) { App.Log(ex); }
        }
        if (Balloon) UpdateIsland(now);
        // Explorer bazı durumlarda görev çubuğunu yeniden gösterebilir; geri gizle.
        if (S.HideTaskbar && tickCount % 2 == 0 && !force)
        {
            var tray = Native.FindWindow("Shell_TrayWnd", null);
            if ((tray != IntPtr.Zero && Native.IsWindowVisible(tray) && !tbRevealed && !TrayWidget.Open_) || (WantFullArea && !FullAreaActive())) ApplyTaskbar();
        }
    }

    // ───────────────────────── otomatik gizleme ─────────────────────────

    // ───────────────────────── balon ─────────────────────────

    void UpdateIsland(DateTime now)
    {
        islandTime.Text = Tr.Time(now) + "  " + now.ToString("d MMM", Tr.C);
        var m = MediaWidget.NowPlaying();
        islandMedia.Text = m is { } x && x.Title.Length > 0 ? $"   {(x.Playing ? "♫" : "❚❚")} {x.Title}" : "";
    }

    static bool CursorOver(FrameworkElement e, double margin)
    {
        if (!e.IsVisible || PresentationSource.FromVisual(e) == null) return false;
        Native.GetCursorPos(out var c);
        var a = e.PointToScreen(new Point(0, 0));
        var b = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
        return c.X >= Math.Min(a.X, b.X) - margin && c.X <= Math.Max(a.X, b.X) + margin &&
               c.Y >= Math.Min(a.Y, b.Y) - margin && c.Y <= Math.Max(a.Y, b.Y) + margin;
    }

    void BalloonTick()
    {
        if (S.BalloonAlwaysOpen) { if (!expanded) SetExpanded(true); return; }
        if (dragArmed) { lastHover = DateTime.Now; return; }
        bool forced = TrayWidget.Open_ || MediaWidget.TransitionActive || editMode || openMenus > 0 || Flyout.IsOpen || WindowPreview.IsOpen || PickerWindow.Current != null || StartOpen();
        bool hover = CursorOver(expanded ? pill : island, 10);
        if (forced || hover)
        {
            lastHover = DateTime.Now;
            if (hoverSince == DateTime.MinValue) hoverSince = DateTime.Now;
            // üzerinden yalnızca geçilirse açılma: kısa bir süre durunca aç
            if (!expanded && (forced || (DateTime.Now - hoverSince).TotalMilliseconds >= InteractionTiming.Delay)) SetExpanded(true);
        }
        else
        {
            hoverSince = DateTime.MinValue;
            if (expanded && (DateTime.Now - lastHover).TotalMilliseconds > 700) SetExpanded(false);
        }
    }

    // A mouse-enter starts the dwell immediately; the slower hide poll is only a fallback.
    void StartHoverDwell()
    {
        if(!Balloon||expanded)return;
        hoverSince=DateTime.Now;
        hoverOpenTimer.Stop();hoverOpenTimer.Interval=TimeSpan.FromMilliseconds(InteractionTiming.Delay);hoverOpenTimer.Start();
    }

    DateTime hoverSince = DateTime.MinValue;
    readonly Border morph = new();
    bool morphing;

    /// <summary>Şekli (animasyonsuz) o anki duruma oturt: açıkken balon, kapalıyken ada boyutu.</summary>
    void FitMorph()
    {
        var src = expanded ? (FrameworkElement)pill : island;
        if (src.ActualWidth <= 0) return;
        morph.BeginAnimation(WidthProperty, null);
        morph.BeginAnimation(HeightProperty, null);
        morph.Width = src.ActualWidth;
        morph.Height = src.ActualHeight;
    }

    /// <summary>
    /// Balonu aç (tüm widget'lar) ya da küçült (saat + çalan şarkı). Tek bir şeklin büyüyüp küçülmesi gibi:
    /// arka plandaki şekil gerçek genişlik/yükseklikle ada boyutundan balon boyutuna (ya da tersi) uzar; köşeler
    /// her karede yüksekliğin yarısı olduğundan uçlar hep tam yuvarlaktır. İçerik şeklin üzerinde belirip söner.
    /// </summary>
    void SetExpanded(bool open, bool animate = true)
    {
        hoverOpenTimer.Stop(); // Prevent a stale dwell tick after forced expansion/closure.
        expanded = open;
        pill.IsHitTestVisible = open;
        island.IsHitTestVisible = !open;
        pill.Opacity = 1;

        if (!Balloon)
        {
            // şerit modunda her şey açık, animasyon yok
            foreach (var (o, p) in new (IAnimatable, DependencyProperty)[] { (island, OpacityProperty), (scroller, OpacityProperty) })
                o.BeginAnimation(p, null);
            scroller.Opacity = 1;
            island.Opacity = 0;
            return;
        }

        var target = open ? (FrameworkElement)pill : island;
        double w = target.ActualWidth, h = target.ActualHeight;

        if (!animate || w <= 0)
        {
            foreach (var (o, p) in new (IAnimatable, DependencyProperty)[] { (island, OpacityProperty), (scroller, OpacityProperty) })
                o.BeginAnimation(p, null);
            island.Opacity = open ? 0 : 1;
            scroller.Opacity = open ? 1 : 0;
            morphing = false;
            FitMorph();
            return;
        }

        static DoubleAnimation A(double to, double ms, double delay, IEasingFunction ease) =>
            new(to, TimeSpan.FromMilliseconds(InteractionTiming.Scale(ms))) { BeginTime = TimeSpan.FromMilliseconds(InteractionTiming.Scale(delay)), EasingFunction = ease };
        var smooth = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var fade = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        // şekil şu anki boyutundan hedefe uzar (yarıda yön değişirse kaldığı yerden döner)
        if (double.IsNaN(morph.Width)) { morph.Width = morph.ActualWidth; morph.Height = morph.ActualHeight; }
        morphing = true;
        double grow = open ? 0 : 120;   // kapanırken önce içerik tamamen sönsün, sonra şekil küçülsün
        var wa = A(w, 360, grow, smooth);
        wa.Completed += (_, _) =>
        {
            if (expanded == open) { morphing = false; FitMorph(); }
        };
        morph.BeginAnimation(WidthProperty, wa);
        morph.BeginAnimation(HeightProperty, A(h, 360, grow, smooth));

        if (open)
        {
            island.BeginAnimation(OpacityProperty, A(0, 110, 0, fade));      // ada yazısı söner
            scroller.BeginAnimation(OpacityProperty, A(1, 170, 290, fade));  // widget'lar şekil neredeyse tam büyüyünce belirir (taşmasın)
        }
        else
        {
            scroller.BeginAnimation(OpacityProperty, A(0, 110, 0, fade));    // önce widget'lar söner
            island.BeginAnimation(OpacityProperty, A(1, 170, 380, fade));    // şekil adaya varınca yazı belirir
            WindowPreview.HideNow();
        }
    }

    void AutoHideTick()
    {
        if (fullscreenHidden || hwnd == IntPtr.Zero) return;
        TaskbarRevealTick();
        if (Balloon) { BalloonTick(); return; }
        if (!S.AutoHide) return;
        if (dragArmed || WantShown())
        {
            lastWanted = DateTime.Now;
            if (!shown) SlideTo(true);
        }
        else if (shown && (DateTime.Now - lastWanted).TotalMilliseconds > 700) SlideTo(false);
    }

    /// <summary>Başlat menüsü ya da Windows araması açık mı?</summary>
    static bool StartOpen()
    {
        var fg = Native.GetForegroundWindow();
        if (Native.ClassName(fg) != "Windows.UI.Core.CoreWindow") return false;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        var exe = Path.GetFileNameWithoutExtension(Native.ProcessPath(pid));
        return exe is "StartMenuExperienceHost" or "SearchHost" or "SearchApp";
    }

    // ── görev çubuğu "otomatik gizle": imleç görev çubuğunun kenarına değince göster, uzaklaşınca yine gizle.
    // Windows'un kendi otomatik gizleme ayarına dokunulmaz (o, çalışma alanını değiştirip simgeleri kaydırır).
    bool tbRevealed;

    void TaskbarRevealTick()
    {
        bool want = S.HideTaskbar && S.TaskbarMode == "autohide";
        if (!want)
        {
            if (tbRevealed) { tbRevealed = false; TaskbarGuard.Reveal(false); }
            return;
        }
        if (!IsPrimaryMon) return;   // Windows görev çubuğu birincil ekranda
        var p = TaskbarGuard.Position();
        if (p == null) return;
        Native.GetCursorPos(out var c);
        var r = p.Value.Rect;
        bool atTop = p.Value.Edge == Native.ABE_TOP;
        int sh = Mon.Bounds.Bottom;
        bool atEdge = atTop ? c.Y <= 0 : c.Y >= sh - 1;
        bool inside = c.X >= r.Left && c.X < r.Right && c.Y >= r.Top && c.Y < r.Bottom;
        if (!tbRevealed && atEdge && !following && !dragArmed)
        {
            tbRevealed = true;
            TaskbarGuard.Reveal(true);
        }
        else if (tbRevealed && !inside && !StartOpen() && openMenus == 0)
        {
            tbRevealed = false;
            TaskbarGuard.Reveal(false);
        }
    }

    bool WantShown()
    {
        if (TrayWidget.Open_ || MediaWidget.TransitionActive || editMode || openMenus > 0 || Flyout.IsOpen || WindowPreview.IsOpen || PickerWindow.Current != null) return true;

        Native.GetCursorPos(out var c);
        if (shown)
        {
            var src = PresentationSource.FromVisual(pill);
            if (src != null)
            {
                var a = pill.PointToScreen(new Point(0, 0));
                var b = pill.PointToScreen(new Point(pill.ActualWidth, pill.ActualHeight));
                if (c.X >= a.X - 8 && c.X <= b.X + 8 && c.Y >= Math.Min(a.Y, b.Y) - 8 && c.Y <= Math.Max(a.Y, b.Y) + 8) return true;
                // kenar boşluğu (pill ile ekran kenarı arası) da sayılır
                int sh = Mon.Bounds.Bottom;
                if (c.X >= a.X && c.X <= b.X && (IsTop ? c.Y <= a.Y : c.Y >= b.Y - 1) && (IsTop || c.Y <= sh)) return true;
            }
        }
        else
        {
            var mr = Mon.Bounds;
            if (c.X >= mr.Left && c.X < mr.Right && (IsTop ? c.Y <= mr.Top + 1 : c.Y >= mr.Bottom - 2)) return true;
        }

        var fg = Native.GetForegroundWindow();
        var cls = Native.ClassName(fg);
        if (cls == "Windows.UI.Core.CoreWindow")
        {
            Native.GetWindowThreadProcessId(fg, out uint pid);
            var exe = Path.GetFileNameWithoutExtension(Native.ProcessPath(pid));
            if (exe is "StartMenuExperienceHost" or "SearchHost" or "SearchApp") return true;   // Başlat / Arama açık
        }
        if (S.ShowOnDesktop && (fg == IntPtr.Zero || cls is "Progman" or "WorkerW")) return true;
        return false;
    }

    void SlideTo(bool show, bool animate = true)
    {
        shown = show;
        double hidden = (IsTop ? -1 : 1) * (DockH + Gap + 6);
        double to = show ? 0 : hidden;
        if (!animate)
        {
            slide.BeginAnimation(TranslateTransform.YProperty, null);
            slide.Y = to;
            return;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(InteractionTiming.Scale(show ? 160 : 220)))
        {
            EasingFunction = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn },
        };
        slide.BeginAnimation(TranslateTransform.YProperty, anim);
        if (!show) WindowPreview.HideNow();
    }

    // ───────────────────────── AppBar ─────────────────────────

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle;
        long ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);

        callbackMsg = Native.RegisterWindowMessage("Iskele.AppBar");
        // kabuk olayları: bir pencere dikkat isteyince (yanıp sönme) haber al
        shellHookMsg = Native.RegisterWindowMessage("SHELLHOOK");
        RegisterShellHookWindow(hwnd);
        taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");

        ApplyTaskbar();
        SetPos();
        UpdateGlass();
    }

    APPBARDATA Data() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

    void RegisterBar()
    {
        if (registered) return;
        var d = Data();
        d.uCallbackMessage = callbackMsg;
        Native.SHAppBarMessage(Native.ABM_NEW, ref d);
        registered = true;
    }

    void UnregisterBar()
    {
        if (!registered) return;
        var d = Data();
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref d);
        registered = false;
    }

    // ── 22. çoklu monitör: her dock kendi ekranında durur
    public IntPtr MonitorHandle;
    public Monitors.Info Mon => Monitors.Get(MonitorHandle) ?? Monitors.Primary;
    public bool IsPrimaryMon => Mon.Primary;

    /// <summary>Dock'u başka bir ekrana taşı (takip modu ya da ekran değişikliği).</summary>
    public void MoveToMonitor(IntPtr h)
    {
        if (h == MonitorHandle) return;
        MonitorHandle = h;
        UnregisterBar();
        ComputeFit();
        SetPos();
    }

    void SetPos()
    {
        if (hwnd == IntPtr.Zero || following || snapping) return;   // sürükleme/oturma animasyonu sürerken dokunma
        BeginAnimation(TopProperty, null);
        double scale = Native.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        var mon = Mon;
        var mr = mon.Bounds;
        int thick = (int)Math.Ceiling(Reserved * scale);
        var rc = DockLayout.Edge(mr, thick, IsTop);

        if (fitRect is RECT fr)
        {
            // Görev çubuğunun bıraktığı alana otur: yeni alan ayırma, çalışma alanı değişmesin.
            UnregisterBar();
            rc = fr;
            thick = fr.Bottom - fr.Top;
        }
        else if (Balloon)
        {
            // Balon alan ayırmaz; görünür bir Windows görev çubuğunun üstüne binmemek için çalışma alanının kenarına yaslanır.
            UnregisterBar();
            var wa = mon.Work;
            {
                // Görev çubuğu gizli ama alanı korunuyorsa o boş alan da balonun: kenara kadar in.
                if (KeepSpace && mon.Primary && TaskbarGuard.Position() is { } tb)
                {
                    if (tb.Edge == Native.ABE_BOTTOM) wa.Bottom = Math.Max(wa.Bottom, tb.Rect.Bottom);
                    else if (tb.Edge == Native.ABE_TOP) wa.Top = Math.Min(wa.Top, tb.Rect.Top);
                }
                rc.Left = wa.Left;
                rc.Right = wa.Right;
                if (IsTop) { rc.Top = wa.Top; rc.Bottom = wa.Top + thick; }
                else { rc.Bottom = wa.Bottom; rc.Top = wa.Bottom - thick; }
            }
        }
        else if (S.AutoHide)
        {
            // Alan ayırma: pencereler tüm ekranı kullanır, dock (ya da balon) üstlerinde süzülür.
            UnregisterBar();
        }
        else
        {
            RegisterBar();
            var d = Data();
            d.uEdge = IsTop ? Native.ABE_TOP : Native.ABE_BOTTOM;
            d.rc = rc;
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref d);
            if (IsTop) d.rc.Bottom = d.rc.Top + thick; else d.rc.Top = d.rc.Bottom - thick;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref d);
            rc = d.rc;
        }

        // büyütme: pencere kenardan uzağa doğru saydam bir ek alanla uzar (ekran alanı ayrılmaz, tıklamalar geçer)
        double extraPx = MagnifyExtra * scale;
        Left = rc.Left / scale;
        Top = (IsTop ? rc.Top : rc.Top - extraPx) / scale;
        Width = (rc.Right - rc.Left) / scale;
        Height = (thick + extraPx) / scale;
        pill.MaxWidth = Math.Max(100, Math.Min(Width - 2 * Gap, S.DockMaxWidth > 0 ? S.DockMaxWidth : double.MaxValue));
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == shellHookMsg && shellHookMsg != 0)
        {
            int code = (int)wParam & 0x7FFF;
            bool high = ((int)wParam & 0x8000) != 0;
            if (code == 6 && high) WindowTracker.Flash(lParam);   // HSHELL_FLASH
            else if (code == 4) WindowTracker.Unflash(lParam);     // HSHELL_WINDOWACTIVATED / RUDEAPPACTIVATED
            return IntPtr.Zero;
        }
        if (msg == callbackMsg && callbackMsg != 0)
        {
            switch ((int)wParam)
            {
                case Native.ABN_POSCHANGED: SetPos(); break;
                case Native.ABN_FULLSCREENAPP: SetFullscreen(FullscreenOnMyMonitor()); break;   // bildirim yalnız tetikler; karar tek yerde
            }
        }
        else if (msg == taskbarCreatedMsg && taskbarCreatedMsg != 0)
        {
            // Explorer yeniden başladı: görev çubuğunu yeniden gizle, kendimizi yeniden kaydet.
            registered = false;
            ApplyTaskbar();
            Build();
            SetPos();
        }
        else if (msg == 0x007E /* WM_DISPLAYCHANGE */ || msg == 0x02E0 /* WM_DPICHANGED */ ||
                 (msg == 0x001A /* WM_SETTINGCHANGE */ && (int)wParam == 0x002F /* SPI_SETWORKAREA */ && Balloon))
        {
            Dispatcher.BeginInvoke(SetPos, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    // ── tam ekran: Windows görev çubuğu gibi, tam ekran bir uygulamanın arkasında kal.
    // ABN_FULLSCREENAPP bildirimi yalnız AppBar olarak kayıtlı şerit modunda geliyor; balon ve otomatik gizlemede
    // gelmiyordu, dock tam ekran videonun/oyunun üstünde kalıyordu. Bu yüzden her modda düzenli sorgula.
    readonly DispatcherTimer fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);

    /// <summary>
    /// Bu dock'un ekranında tam ekran bir uygulama var mı? Windows'un görev çubuğu için kullandığı karar
    /// (QUNS_BUSY / QUNS_RUNNING_D3D_FULL_SCREEN) + ön plandaki pencere bu ekranı kaplıyor mu.
    /// Büyütülmüş normal pencereler (balon modunda tüm ekranı kaplasalar da) tam ekran sayılmaz.
    /// </summary>
    bool FullscreenOnMyMonitor()
    {
        if (SHQueryUserNotificationState(out int state) != 0 || state is not (2 or 3)) return false;
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        // masaüstü (Progman/WorkerW) bazen yanlışlıkla "tam ekran" sayılır
        if (Native.ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;
        if (MonitorFromWindow(fg, 2 /* NEAREST */) != (MonitorHandle != IntPtr.Zero ? MonitorHandle : MonitorFromWindow(hwnd, 2))) return false;
        if (!Native.GetWindowRect(fg, out var r)) return false;
        var b = Mon.Bounds;
        if (r.Left > b.Left || r.Top > b.Top || r.Right < b.Right || r.Bottom < b.Bottom) return false;
        bool caption = ((long)Native.GetWindowLongPtr(fg, -16 /* GWL_STYLE */) & 0x00C00000) == 0x00C00000;
        return !(IsZoomed(fg) && caption);
    }

    void SetFullscreen(bool on)
    {
        if (on == fullscreenHidden) return;
        fullscreenHidden = on;
        if (on) { WindowPreview.HideNow(); Hide(); }
        else { Show(); Tick(force: true); }
    }

    /// <summary>Otomatik gizleme açıkken tam ekran bildirimi gelmez; bu yüzden ön planı da kontrol et.</summary>
    public bool IsFullscreenHidden => fullscreenHidden;

    // ───────────────────────── menü ─────────────────────────

    // Compact navigation first, grouped placement/visibility, destructive exit last.
    internal ContextMenu BuildMenu()
    {
        var m=new ContextMenu { StaysOpen=false };
        m.Items.Add(Item("Widget’ları düzenle…",PickerWindow.Open));
        m.Items.Add(Item("Ayarlar…",SettingsWindow.Open));
        m.Items.Add(new Separator());
        var layout=Item("Yerleşim",()=>{});
        MenuItem Choice(string text,bool selected,Action action)
        {
            var item=Item(text,action);item.IsCheckable=true;item.IsChecked=selected;return item;
        }
        layout.Items.Add(Choice("Şerit",!Balloon,()=>{S.Mode="bar";S.Save();ApplySettings();}));
        layout.Items.Add(Choice("Balon",Balloon,()=>{S.Mode="balloon";S.Save();ApplySettings();}));
        layout.Items.Add(new Separator());
        layout.Items.Add(Choice("Üst kenar",IsTop,()=>MoveEdge("top")));
        layout.Items.Add(Choice("Alt kenar",!IsTop,()=>MoveEdge("bottom")));
        m.Items.Add(layout);
        var visibility=Item("Görünürlük",()=>{});
        visibility.Items.Add(Choice("Her zaman açık",Balloon?S.BalloonAlwaysOpen:!S.AutoHide,()=>{if(Balloon)S.BalloonAlwaysOpen=true;else S.AutoHide=false;S.Save();ApplySettings();}));
        visibility.Items.Add(Choice("Otomatik",Balloon?!S.BalloonAlwaysOpen:S.AutoHide,()=>{if(Balloon)S.BalloonAlwaysOpen=false;else S.AutoHide=true;S.Save();ApplySettings();}));
        visibility.Items.Add(new Separator());
        visibility.Items.Add(Choice("Windows görev çubuğunu gizle",S.HideTaskbar,()=>{S.HideTaskbar=!S.HideTaskbar;S.Save();ApplySettings();}));
        m.Items.Add(visibility);
        m.Items.Add(Choice("Dock'u kilitle",S.LockDock,()=>{S.LockDock=!S.LockDock;S.Save();}));
        m.Items.Add(new Separator());
        m.Items.Add(Item("İskele’yi kapat",App.Quit));
        return m;
    }

    public static MenuItem Item(string header, Action a, bool enabled = true)
    {
        var i = new MenuItem { Header = Localization.T(header), IsEnabled = enabled };
        i.Click += (_, _) => a();
        return i;
    }

    void MoveEdge(string edge)
    {
        if (Balloon)
        {
            if (S.BalloonEdge == edge) return;
            S.BalloonEdge = edge;
        }
        else
        {
            if (S.Edge == edge) return;
            S.Edge = edge;
        }
        S.Save();
        ApplySettings();
    }

    // ───────────────────────── sürükleyerek taşıma ─────────────────────────
    // Dock'u (şerit ya da balon) boş bir yerinden tutup sürükle: imleci izler; ekranın üst yarısına
    // bırakılırsa üst kenara, alt yarısına bırakılırsa alt kenara oturur. Kısa tıklama sürükleme sayılmaz.

    bool dragArmed, dragMoved, dragFromIsland, dragPending;
    POINT dragPendStart;

    // Widget'ların üzerinden de tutulabilsin: basılıyken belirgin dikey hareket olursa sürüklemeye geç.
    // Kısa tıklamalar ve yatay hareketler (ör. ses kaydırıcısı) widget'a kalır.
    /// <summary>Uygulama simgesi sürüklenirken dock'u taşıma başlamasın.</summary>
    public static bool BlockDockDrag;

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    DispatcherTimer? dragPoll;

    void OnAnyDown(object sender, MouseButtonEventArgs e)
    {
        if (editMode || dragArmed || S.LockDock) return;
        Native.GetCursorPos(out dragPendStart);
        dragPending = true;
        // İmleç pencereden hızla çıkınca fare olayları gelmez; sol tuş basılıyken konumu doğrudan izle.
        dragPoll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Input, (_, _) => PollDrag(), Dispatcher);
        dragPoll.Start();
    }

    void PollDrag()
    {
        bool down = (GetAsyncKeyState(0x01 /* VK_LBUTTON */) & 0x8000) != 0;
        if (!dragPending || dragArmed || !down || BlockDockDrag) { dragPending = false; dragPoll?.Stop(); return; }
        Native.GetCursorPos(out var p);
        int dy = Math.Abs(p.Y - dragPendStart.Y), dx = Math.Abs(p.X - dragPendStart.X);
        if (dy < 10 || dy < dx * 1.5) return;
        dragPoll?.Stop();
        BeginDragFromPending();
    }

    void OnAnyMove(object sender, MouseEventArgs e)
    {
        if (!dragPending || dragArmed) return;
        if (BlockDockDrag) { dragPending = false; return; }
        if (e.LeftButton != MouseButtonState.Pressed) { dragPending = false; return; }
        Native.GetCursorPos(out var p);
        int dy = Math.Abs(p.Y - dragPendStart.Y), dx = Math.Abs(p.X - dragPendStart.X);
        if (dy < 10 || dy < dx * 1.5 || S.LockDock) return;
        dragPoll?.Stop();
        BeginDragFromPending();
    }

    void BeginDragFromPending()
    {
        dragPending = false;
        Native.GetWindowRect(hwnd, out var wr);
        dragArmed = true;
        dragMoved = false;
        dragFromIsland = false;
        dragStartY = dragPendStart.Y;
        dragGrabPx = dragPendStart.Y - wr.Top;
        root.CaptureMouse();   // tıklamayı widget'tan al; yakalama sayesinde pencere dışındaki hareketler de gelir
        App.Debug($"surukle: basladi ({dragPendStart.X},{dragPendStart.Y})");
        OnRootMove(root, null!);
    }
    int dragStartY, dragGrabPx;

    void OnPillDown(object sender, MouseButtonEventArgs e)
    {
        if (editMode || Ui.Ancestor<Slot>(e.OriginalSource as DependencyObject) != null) return;
        StartDrag(fromIsland: false);
        e.Handled = true;
    }

    void StartDrag(bool fromIsland)
    {
        if (hwnd == IntPtr.Zero) return;
        Native.GetCursorPos(out var p);
        Native.GetWindowRect(hwnd, out var wr);
        dragArmed = true;
        dragMoved = false;
        dragFromIsland = fromIsland;
        dragStartY = p.Y;
        dragGrabPx = p.Y - wr.Top;
        root.CaptureMouse();
    }

    // ── sürükleme animasyonları: kaldır (hafif büyüt + saydamlaş), yumuşak takip, yerine yaylanarak otur

    readonly ScaleTransform lift = new(1, 1);
    bool following, snapping;

    double Dpi => hwnd == IntPtr.Zero ? 1 : Math.Max(1, Native.GetDpiForWindow(hwnd)) / 96.0;

    void Lift(bool up)
    {
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        if (root.RenderTransform != lift) root.RenderTransform = lift;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(up ? 140 : 220);
        lift.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(up ? 1.04 : 1, dur) { EasingFunction = ease });
        lift.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(up ? 1.04 : 1, dur) { EasingFunction = ease });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(up ? 0.9 : 1, dur) { EasingFunction = ease });
    }

    // Pencere her karede doğrudan Windows'a taşıtılır (SetWindowPos): WPF yeniden yerleşim/çizim yapmaz,
    // katmanlı pencerenin hazır görüntüsü kaydırılır. Konumlar fiziksel piksel.
    double curPx, targetPx;
    int leftPx;
    System.Windows.Media.Effects.Effect? savedPillFx, savedIslandFx, savedMorphFx;

    void MoveWindowPx(double y) =>
        Native.SetWindowPos(hwnd, IntPtr.Zero, leftPx, (int)Math.Round(y), 0, 0, 0x0001 | 0x0004 | 0x0010 /* NOSIZE|NOZORDER|NOACTIVATE */);

    /// <summary>Her karede hedefe aradaki mesafenin bir kısmı kadar yaklaş: imleci yumuşakça izler.</summary>
    int frames;
    double maxGapMs;
    DateTime lastFrame;

    TimeSpan lastRenderTime;

    void OnFollowFrame(object? sender, EventArgs e)
    {
        if (!following) return;
        // Rendering bir karede birden çok kez gelebilir: aynı kareyi iki kez işleme
        if (e is RenderingEventArgs r)
        {
            if (r.RenderingTime == lastRenderTime) return;
            lastRenderTime = r.RenderingTime;
        }
        var now = DateTime.Now;
        double dt = frames++ > 0 ? (now - lastFrame).TotalMilliseconds : 16;
        if (frames > 1) maxGapMs = Math.Max(maxGapMs, dt);
        lastFrame = now;
        // süreye bağlı yumuşatma: kareler düzensiz gelse de hareket sarsılmaz (τ ≈ 28 ms)
        double k = 1 - Math.Exp(-Math.Clamp(dt, 1, 60) / 28.0);
        double next = curPx + (targetPx - curPx) * k;
        if (Math.Abs(targetPx - next) < 0.5) next = targetPx;
        if (Math.Abs(next - curPx) < 0.01) return;
        curPx = next;
        MoveWindowPx(curPx);
    }

    // bırakınca yaylanarak oturma (BackEase benzeri), kare kare
    double snapFrom, snapTo;
    DateTime snapStart;
    Action? snapDone;
    const double SnapMs = 300;

    void OnSnapFrame(object? sender, EventArgs e)
    {
        double t = Math.Clamp((DateTime.Now - snapStart).TotalMilliseconds / SnapMs, 0, 1);
        const double s = 1.2;   // esneme miktarı
        double u = t - 1;
        double k = u * u * ((s + 1) * u + s) + 1;   // easeOutBack
        curPx = snapFrom + (snapTo - snapFrom) * k;
        MoveWindowPx(curPx);
        if (t >= 1)
        {
            CompositionTarget.Rendering -= OnSnapFrame;
            var done = snapDone;
            snapDone = null;
            done?.Invoke();
        }
    }

    void OnRootMove(object sender, MouseEventArgs e)
    {
        if (!dragArmed) return;
        Native.GetCursorPos(out var p);
        if (!dragMoved)
        {
            if (S.LockDock || Math.Abs(p.Y - dragStartY) < 8) return;
            dragMoved = true;
            WindowPreview.HideNow();
            Flyout.CloseCurrent();
            UnregisterBar();   // sürüklerken ekran alanı ayırma
            Cursor = Cursors.SizeNS;
            BeginAnimation(TopProperty, null);
            Native.GetWindowRect(hwnd, out var wr);
            leftPx = wr.Left;
            curPx = targetPx = wr.Top;
            // gölgeler katmanlı pencerede pahalı: sürükleme süresince kapat
            savedPillFx = pill.Effect;
            savedIslandFx = island.Effect;
            pill.Effect = null;
            island.Effect = null;
            savedMorphFx = morph.Effect;
            morph.Effect = null;
            following = true;
            CompositionTarget.Rendering += OnFollowFrame;
            Lift(true);
        }
        var mb = Mon.Bounds;
        targetPx = Math.Clamp(p.Y - dragGrabPx, mb.Top, Math.Max(mb.Top, mb.Bottom - (int)Math.Round(ActualHeight * Dpi)));
    }

    void OnRootUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragArmed) return;
        dragArmed = false;
        root.ReleaseMouseCapture();
        Cursor = null;
        if (!dragMoved)
        {
            // kısa tıklama: kapalı balonu aç
            if (dragFromIsland) { lastHover = DateTime.Now; SetExpanded(true); }
            return;
        }
        dragMoved = false;
        following = false;
        CompositionTarget.Rendering -= OnFollowFrame;
        App.Debug($"surukle: {frames} kare, en uzun kare arasi {maxGapMs:0} ms");
        frames = 0;
        maxGapMs = 0;
        e.Handled = true;

        Native.GetCursorPos(out var c);
        var monNow = Mon;
        string edge = c.Y < (monNow.Bounds.Top + monNow.Bounds.Bottom) / 2 ? "top" : "bottom";
        string current = Balloon ? S.BalloonEdge : S.Edge;

        // hedef kenarın yeri (fiziksel piksel): balonda çalışma alanının kenarı, şeritte ekran kenarı
        double hPx = ActualHeight * Dpi;
        double topPx = monNow.Bounds.Top, bottomPx = monNow.Bounds.Bottom;
        if (Balloon) { topPx = monNow.Work.Top; bottomPx = monNow.Work.Bottom; }
        else if (fitRect is RECT fr && edge == "bottom") bottomPx = fr.Bottom;
        double destPx = edge == "top" ? topPx : bottomPx - hPx;

        // yerine yaylanarak otur, sonra gerçek yerleşimi uygula
        snapping = true;
        snapFrom = curPx;
        snapTo = destPx;
        snapStart = DateTime.Now;
        snapDone = () =>
        {
            snapping = false;
            pill.Effect = savedPillFx;
            island.Effect = savedIslandFx;
            morph.Effect = savedMorphFx;
            Lift(false);
            if (edge != current) MoveEdge(edge);
            else SetPos();   // aynı kenar: yerine otur (şeritte alanı yeniden ayır)
        };
        CompositionTarget.Rendering += OnSnapFrame;
    }

    // ───────────────────────── düzenleme modu ─────────────────────────

    public bool EditMode => editMode;

    public void SetEditMode(bool on)
    {
        editMode = on;
        foreach (var s in slots) s.SetEdit(on);
    }

    public void RemoveWidget(string id)
    {
        S.Widgets.Remove(id);
        S.Save();
        Build();
        PickerWindow.Current?.RefreshMarks();
    }

    public void Rebuild() => Build();

    Slot? dragging;

    void OnStripDown(object sender, MouseButtonEventArgs e)
    {
        if (!editMode) return;
        var src = e.OriginalSource as DependencyObject;
        if (Ui.Ancestor<RemoveBadge>(src) != null) return;
        dragging = Ui.Ancestor<Slot>(src);
        if (dragging == null) return;
        dragging.Opacity = 0.6;
        strip.CaptureMouse();
        e.Handled = true;
    }

    void OnStripMove(object sender, MouseEventArgs e)
    {
        if (dragging == null) return;
        double x = e.GetPosition(strip).X;
        int target = 0;
        foreach (var s in slots)
        {
            if (s == dragging) continue;
            var center = s.TranslatePoint(new Point(s.ActualWidth / 2, 0), strip).X;
            if (x > center) target++;
        }
        int current = slots.IndexOf(dragging);
        if (target == current) return;
        slots.RemoveAt(current);
        slots.Insert(target, dragging);
        strip.Children.Remove(dragging);
        strip.Children.Insert(target, dragging);
    }

    void OnStripUp(object sender, MouseButtonEventArgs e)
    {
        if (dragging == null) return;
        dragging.Opacity = 1;
        dragging = null;
        strip.ReleaseMouseCapture();
        S.Widgets = slots.Select(s => s.Widget.Id).ToList();
        S.Save();
        e.Handled = true;
    }
}

/// <summary>Dock'taki bir widget yuvası: widget + düzenleme modunda "kaldır" rozeti.</summary>
class Slot : Grid
{
    public Widget Widget { get; }
    readonly RemoveBadge badge;

    public Slot(Widget w, DockWindow dock)
    {
        Widget = w;
        Background = Brushes.Transparent;
        double m = Math.Round(Math.Clamp(App.Settings.Spacing, 0, 12) * App.Settings.Scale);
        Margin = new Thickness(m, 0, m, 0);
        Children.Add(w);
        badge = new RemoveBadge();
        badge.MouseLeftButtonUp += (_, e) => { e.Handled = true; dock.RemoveWidget(w.Id); };
        Children.Add(badge);
    }

    public void SetEdit(bool on)
    {
        badge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Widget.IsHitTestVisible = !on;
        Cursor = on ? Cursors.SizeWE : null;
    }
}

class RemoveBadge : Border
{
    public RemoveBadge()
    {
        Width = Height = 18;
        CornerRadius = new CornerRadius(9);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, -2, -4, 0);
        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        BorderBrush = Brushes.White;
        BorderThickness = new Thickness(1.5);
        Cursor = Cursors.Hand;
        Visibility = Visibility.Collapsed;
        Child = new Border { Width = 8, Height = 2, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Panel.SetZIndex(this, 10);
    }
}
