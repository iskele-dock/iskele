namespace Iskele;
class FeaturePanel : PanelWindow
{
    public Grid Host { get; } = new();
    public FeaturePanel(string title, double width, double height) : base(title, width, height)
    {
        var layout = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(20, 14, 16, 0) };
        var close = new IconButton("\uE711", 11, 28); close.Click += Close;
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(Ui.Text(title, 17, "Fg", FontWeights.Bold));
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header); layout.Children.Add(Host); Body.Children.Add(layout);
    }
    public static Button ActionButton(string label)
    {
        var b = new Button { Content = Localization.T(label), Height = 34, Padding = new Thickness(14, 4, 14, 4), FontSize = 13,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(0, 12, 0, 0) };
        b.SetResourceReference(Control.BackgroundProperty, "Accent"); b.Foreground = Brushes.White; return b;
    }
}
