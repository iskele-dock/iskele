namespace Iskele;
class AppNavigator : FeaturePanel
{
    static AppNavigator? current;
    readonly List<List<WinInfo>> groups;
    readonly ListBox apps = new() { MinHeight = 100, MaxHeight = 280, BorderThickness = new Thickness(0) };
    readonly TextBlock detail = Ui.Text("", 12, "FgDim");
    int windowIndex;
    public static void Open()
    {
        if (current != null) { current.Move(1); current.Activate(); return; }
        WindowTracker.Refresh();
        if (WindowTracker.Windows.Count == 0) return;
        current = new AppNavigator(); current.Closed += (_, _) => current = null;
        current.Show(); current.Activate(); current.apps.Focus();
    }
    AppNavigator() : base("Uygulamalar · Win+T", 560, 420)
    {
        groups = WindowTracker.Windows.GroupBy(w => w.Exe.Length > 0 ? w.Exe : w.Hwnd.ToString()).Select(g => g.ToList()).ToList();
        var p = new StackPanel { Margin = new Thickness(20) };
        p.Children.Add(Ui.Text("← → / ↑ ↓: uygulama · Tab: pencere · Enter: aç · Esc: kapat", 12, "FgDim"));
        apps.SetResourceReference(Control.BackgroundProperty, "Track"); apps.SetResourceReference(Control.ForegroundProperty, "Fg");
        foreach (var group in groups) apps.Items.Add(new ListBoxItem { Content = group[0].Exe.Length > 0 ? Path.GetFileNameWithoutExtension(group[0].Exe) : group[0].Title, Padding = new Thickness(10, 6, 10, 6) });
        apps.SelectionChanged += (_, _) => { windowIndex = 0; Paint(); };
        apps.SelectedIndex = 0; p.Children.Add(apps);
        detail.TextWrapping = TextWrapping.Wrap; detail.Margin = new Thickness(0, 12, 0, 0); p.Children.Add(detail);
        Host.Children.Add(p); Paint();
        PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Left: case Key.Up: Move(-1); break;
                case Key.Right: case Key.Down: Move(1); break;
                case Key.Tab: windowIndex = Wrap(windowIndex + ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1), groups[apps.SelectedIndex].Count); Paint(); break;
                case Key.Enter:
                    var target = groups[apps.SelectedIndex][windowIndex]; Close();
                    if (Native.IsWindow(target.Hwnd)) WindowTracker.Activate(target.Hwnd); break;
                case Key.Escape: Close(); break;
                default: return;
            }
            e.Handled = true;
        };
        Deactivated += (_, _) => { if (IsVisible) Close(); };
    }
    internal static int Wrap(int index, int count) => count <= 0 ? 0 : (index % count + count) % count;
    void Move(int delta) { apps.SelectedIndex = Wrap(apps.SelectedIndex + delta, groups.Count); apps.ScrollIntoView(apps.SelectedItem); }
    void Paint()
    {
        if (apps.SelectedIndex < 0) return;
        var group = groups[apps.SelectedIndex]; detail.Text = $"{windowIndex + 1}/{group.Count} · {group[windowIndex].Title}";
    }
}
