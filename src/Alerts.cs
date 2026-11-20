namespace Iskele;
partial class Settings
{
    public bool QuietHours { get; set; }
    public int QuietStart { get; set; } = 22;
    public int QuietEnd { get; set; } = 8;
    public string AlertSound { get; set; } = "none";
    public List<string> SentAlerts { get; set; } = [];
}
static class Alerts
{
    static Window? current;
    public static bool IsQuiet(DateTime now, bool enabled, int start, int end) => enabled &&
        (start == end || (start < end ? now.Hour >= start && now.Hour < end : now.Hour >= start || now.Hour < end));
    public static bool Send(string key, string title, string message, DateTime now)
    {
        var s = App.Settings;
        if (current != null || s.SentAlerts.Contains(key) || IsQuiet(now, s.QuietHours, s.QuietStart, s.QuietEnd)) return false;
        s.SentAlerts.Add(key);
        if (s.SentAlerts.Count > 500) s.SentAlerts.RemoveRange(0, s.SentAlerts.Count - 500);
        s.Save();
        current?.Close();
        var text = Ui.Text(message, 13, "Fg"); text.TextWrapping = TextWrapping.Wrap;
        var body = Ui.Col(Ui.Text(title, 15, "Fg", FontWeights.Bold), text);
        var border = new Border { Padding = new Thickness(18), Child = body };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBg");
        var w = new Window { Title = title, Content = border, Width = 350, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Topmost = true };
        // Use the established panel background resource.
        border.SetResourceReference(Border.BackgroundProperty, "DockBg");
        w.SetResourceReference(Window.BackgroundProperty, "DockBg");
        FirstFrame.Guard(w, "DockBg");
        var area = SystemParameters.WorkArea;
        w.Left = area.Right - w.Width - 16; w.Top = area.Top + 16;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) => w.Close();
        w.Closed += (_, _) => { timer.Stop(); if (current == w) current = null; };
        w.MouseLeftButtonUp += (_, _) => w.Close();
        current = w; w.Show(); timer.Start();
        switch (s.AlertSound)
        {
            case "asterisk": System.Media.SystemSounds.Asterisk.Play(); break;
            case "exclamation": System.Media.SystemSounds.Exclamation.Play(); break;
        }
        return true;
    }
}
