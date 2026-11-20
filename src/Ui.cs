using System.Windows.Media.Effects;

namespace Iskele;

/// <summary>Ortak arayüz yardımcıları ve küçük özel denetimler.</summary>
static class Ui
{
    public static readonly FontFamily Font = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static Brush B(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>Segoe UI Variable'ın küçük boyutlar için çizilmiş optik sürümü (Display sürümü 18 px altında silik).</summary>
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    /// <summary>Penceredeki küçük yazıları Text optik boyutuna geçir (dock'a uygulanmaz; orada genişlikler sabit).</summary>
    public static void UseTextOpticalSize(DependencyObject root)
    {
        if (root is TextBlock t && t.FontSize < 18 && Equals(t.FontFamily, Font)) t.FontFamily = TextFont;
        else if (root is Control c && c.FontSize < 18 && Equals(c.FontFamily, Font)) c.FontFamily = TextFont;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++) UseTextOpticalSize(VisualTreeHelper.GetChild(root, i));
    }

    public static TextBlock Text(string s, double size, string brush = "Fg", FontWeight? weight = null, bool localize = true)
    {
        var t = new TextBlock
        {
            Text = localize ? Localization.T(s) : s, FontSize = size, FontFamily = Font, FontWeight = weight ?? FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    // User/account/file content is never a localization key.
    public static TextBlock RawText(string s, double size, string brush = "Fg", FontWeight? weight = null)
        => Text(s, size, brush, weight, localize: false);

    public static TextBlock Glyph(string g, double size, string brush = "Fg")
    {
        var t = new TextBlock
        {
            Text = g, FontSize = size, FontFamily = IconFont,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static StackPanel Col(params UIElement[] children)
    {
        var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static T? Ancestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>Seçenek şeridi (ayarlar penceresi için).</summary>
    public static FrameworkElement Segmented(string[] options, int current, Action<int> onChange)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var shell = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(3), Child = row, HorizontalAlignment = HorizontalAlignment.Right };
        shell.SetResourceReference(Border.BackgroundProperty, "Track");
        var cells = new List<(Border b, TextBlock t)>();

        void Paint(int sel)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (i == sel)
                {
                    cells[i].b.SetResourceReference(Border.BackgroundProperty, "Accent");
                    cells[i].t.Foreground = Brushes.White;
                }
                else
                {
                    cells[i].b.Background = Brushes.Transparent;
                    cells[i].t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                }
            }
        }

        for (int i = 0; i < options.Length; i++)
        {
            int idx = i;
            var t = Text(options[i], 12.5, "Fg", FontWeights.SemiBold);
            var b = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(13, 5, 13, 5), Child = t, Cursor = Cursors.Hand };
            b.MouseLeftButtonDown += (_, e) => { e.Handled = true; Paint(idx); onChange(idx); };
            cells.Add((b, t));
            row.Children.Add(b);
        }
        Paint(current);
        return shell;
    }
}

/// <summary>Yuvarlak simge düğmesi.</summary>
class IconButton : Border
{
    readonly TextBlock glyph;
    public event Action? Click;

    public IconButton(string g, double size = 13, double box = 28, bool accent = false)
    {
        Width = Height = box;
        CornerRadius = new CornerRadius(box / 2);
        Cursor = Cursors.Hand;
        VerticalAlignment = VerticalAlignment.Center;
        glyph = Ui.Glyph(g, size);
        Child = glyph;
        if (accent)
        {
            SetResourceReference(BackgroundProperty, "Accent");
            glyph.Foreground = Brushes.White;
        }
        else Background = Brushes.Transparent;

        MouseEnter += (_, _) => Opacity = 0.7;
        MouseLeave += (_, _) => Opacity = 1;
        MouseLeftButtonDown += (_, e) => e.Handled = true;
        MouseLeftButtonUp += (_, e) => { e.Handled = true; Click?.Invoke(); };
    }

    public string GlyphText { set => glyph.Text = value; }
}

/// <summary>Halka gösterge (0..1).</summary>
class RingGauge : FrameworkElement
{
    double value;
    public double Value
    {
        get => value;
        set { this.value = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }
    public double Stroke { get; set; } = 3.5;

    protected override void OnRender(DrawingContext dc)
    {
        double r = Math.Min(ActualWidth, ActualHeight) / 2 - Stroke / 2;
        if (r <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(Ui.B("Track"), Stroke), c, r, r);
        if (value <= 0.001) return;

        var pen = new Pen(Ui.B("Accent"), Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (value >= 0.999) { dc.DrawEllipse(null, pen, c, r, r); return; }

        double a = value * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, value > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>Yatay doluluk çubuğu / kaydırıcı (0..1). Interactive ise sürüklenebilir.</summary>
class MiniSlider : FrameworkElement
{
    double value;
    public bool Interactive { get; set; } = true;
    public string FillKey { get; set; } = "Accent";
    public event Action<double>? ValueChanged;

    public double Value
    {
        get => value;
        set { this.value = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }

    public MiniSlider() { Height = 12; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // tıklanabilir alan
        double th = Interactive ? 6 : h;
        double y = (h - th) / 2;
        dc.DrawRoundedRectangle(Ui.B("Track"), null, new Rect(0, y, w, th), th / 2, th / 2);
        double fw = Math.Max(th, w * value);
        if (value > 0.001) dc.DrawRoundedRectangle(Ui.B(FillKey), null, new Rect(0, y, fw, th), th / 2, th / 2);
        if (Interactive)
        {
            double x = Math.Clamp(w * value, h / 2, w - h / 2);
            dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 1), new Point(x, h / 2), h / 2 - 0.5, h / 2 - 0.5);
        }
    }

    void Set(MouseEventArgs e)
    {
        Value = e.GetPosition(this).X / Math.Max(1, ActualWidth);
        ValueChanged?.Invoke(value);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!Interactive) return;
        e.Handled = true;
        CaptureMouse();
        Set(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Interactive && IsMouseCaptured) Set(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) { ReleaseMouseCapture(); e.Handled = true; }
    }
}

/// <summary>Widget seçici ve ayarlar için ortak çerçevesiz pencere.</summary>
class PanelWindow : Window
{
    protected readonly Grid Body = new();

    protected PanelWindow(string title, double width, double height)
    {
        Title = Localization.T(title);
        Icon = Brand.Source; // Shared application icon.
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Ui.Font;
        Width = width;
        if (height > 0) Height = height; else SizeToContent = SizeToContent.Height;

        // Settings and utility panels share the same material as dock flyouts.
        Content = new PopupSurface(Body,18,new Thickness(0));

        // net yazı: piksele oturan glifler ve yerleşim; küçük yazılarda "Variable Text" optik boyutu
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Loaded += (_, _) => Ui.UseTextOpticalSize(this);

        // Sürükleme ancak fare basılıyken birkaç piksel hareket edince başlar. Önceden basar basmaz DragMove()
        // çağrılıyordu; DragMove fareyi yakalayıp bırakma olayını yuttuğu için MouseLeftButtonUp ile çalışan
        // bağlantılar, renk noktaları ve çipler (ör. Ayarlar'da) hiç tepki vermiyordu.
        Point? press = null;
        MouseLeftButtonDown += (_, e) => press = e.GetPosition(this);
        MouseLeftButtonUp += (_, _) => press = null;
        PreviewMouseMove += (_, e) =>
        {
            if (press is not { } p0 || e.LeftButton != MouseButtonState.Pressed || Mouse.Captured != null) { if (e.LeftButton != MouseButtonState.Pressed) press = null; return; }
            var d = e.GetPosition(this) - p0;
            if (Math.Abs(d.X) < 4 && Math.Abs(d.Y) < 4) return;
            press = null;
            try { DragMove(); } catch { }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}

/// <summary>
/// Saydam olmayan (katmansız) pencerelerde Windows, WPF ilk kareyi çizmeden önce varsayılan beyaz zemini
/// bir an gösterebilir. Pencere ilk çizim bitene kadar DWM ile gizlenir (cloak) ve arka plan temaya boyanır.
/// </summary>
static class FirstFrame
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    const int DWMWA_CLOAK = 13;

    public static void Guard(Window w, string backgroundKey = "MenuBg")
    {
        if (w.AllowsTransparency) return;
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target
                && w.TryFindResource(backgroundKey) is SolidColorBrush brush)
                target.BackgroundColor = brush.Color;
            int on = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref on, 4);
            void Reveal()
            {
                int off = 0;
                DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref off, 4);
            }
            bool shown = false;
            w.ContentRendered += (_, _) => { if (!shown) { shown = true; w.Dispatcher.BeginInvoke(Reveal, DispatcherPriority.Render); } };
            // Güvenlik: içerik bir sebeple çizilmezse pencere görünmez kalmasın.
            var fallback = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            fallback.Tick += (_, _) => { fallback.Stop(); if (!shown) { shown = true; Reveal(); } };
            fallback.Start();
        };
    }
}
