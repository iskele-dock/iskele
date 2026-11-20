using System.Windows.Media.Imaging;
namespace Iskele;
static class FeatureTests
{
    public static int Run(string output)
    {
        output = Path.GetFullPath(output); // Relative CLI paths must produce valid Steam fixtures too.
        Directory.CreateDirectory(output);
        Settings.DirectoryOverride = Path.Combine(output, "isolated-appdata");
        var results = new List<string>();
        void Check(bool pass, string name) { results.Add((pass ? "PASS " : "FAIL ") + name); }
        try
        {
            Settings.NoSave = true;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.LoadStyles(); Theme.Apply("midnight");
            ReviewRegressionTests.Run(output, Check);
            Check(WidgetSearch.Matches("clock", "Saat", "", "", "time"), "English search synonym");
            Check(WidgetSearch.Matches("x", "İşlemci ölçümü", "", "", "islemci olcumu"), "Turkish accent and multiword search");
            Check(!WidgetSearch.Matches("clock", "Saat", "", "", "volume"), "Unmatched query");
            var now = new DateTime(2026, 11, 17, 12, 0, 0);
            Check(Alerts.IsQuiet(now.AddHours(11), true, 22, 8), "Quiet overnight start");
            Check(Alerts.IsQuiet(now.AddHours(-5), true, 22, 8), "Quiet overnight morning");
            Check(!Alerts.IsQuiet(now, true, 22, 8), "Quiet daytime exclusion");
            Check(!Alerts.IsQuiet(now, false, 0, 0), "Quiet disabled");
            Check(Alerts.IsQuiet(now, true, 8, 18), "Quiet same-day range");
            Check(!Alerts.IsQuiet(now.Date.AddHours(8), true, 22, 8), "Quiet end exclusive");
            var renewal = new Renewal { Date = new DateTime(2026, 1, 31), Months = 1 };
            Check(renewal.Next(new DateTime(2026, 2, 1)) == new DateTime(2026, 2, 28), "Month-end renewal");
            Check(renewal.Next(new DateTime(2026, 3, 1)) == new DateTime(2026, 3, 31), "Month-end anchor preserved");
            Check(new Renewal { Date = now.Date.AddDays(-1), Months = 0 }.Next(now) < now.Date, "One-time past payment not repeated");
            Check(Automations.MeetingDue(new(now.AddMinutes(1), now.AddHours(1), "Test", false), now), "Meeting due within lead window");
            Check(!Automations.MeetingDue(new(now.AddMinutes(3), now.AddHours(1), "Test", false), now), "Meeting too early");
            Check(!Automations.MeetingDue(new(now.AddMinutes(1), now.AddHours(1), "Test", true), now), "All-day meeting excluded");
            var order = new List<int>(); DockRecovery.RecoverCore(() => order.Add(1), () => order.Add(2), () => order.Add(3));
            Check(order.SequenceEqual(new[] { 1, 2, 3 }), "Recovery refreshes displays windows then layout");
            var icon = Path.Combine(output, "game.ico"); File.WriteAllText(icon, "fixture");
            var shortcut = Path.Combine(output, "game.url");
            File.WriteAllText(shortcut, "[InternetShortcut]\nURL=steam://rungameid/123\nIconFile=" + icon);
            Check(SteamIcons.Resolve(shortcut) == icon, "Steam shortcut explicit game icon");
            File.WriteAllText(shortcut, "URL=https://example.com\nIconFile=" + icon);
            Check(SteamIcons.Resolve(shortcut) == null, "Non-Steam URL unchanged");
            var redacted = Diagnostics.Redact("token=private https://example.com/private");
            Check(!redacted.Contains("private"), "Diagnostic URL and token redaction");
            var json = System.Text.Json.JsonSerializer.Serialize(new Settings { Renewals = [renewal], QuietHours = true });
            Check(System.Text.Json.JsonSerializer.Deserialize<Settings>(json)?.Renewals.Count == 1, "Settings renewal persistence roundtrip");
            Check(AppNavigator.Wrap(-1, 3) == 2 && AppNavigator.Wrap(3, 3) == 0, "Application keyboard wrap");
            Check(!Performance.ShouldTick(false, false, false, true, false), "Hidden widgets suspended");
            Check(!Performance.ShouldTick(false, true, true, true, false), "Collapsed balloon widgets suspended");
            Check(!Performance.ShouldTick(false, true, false, false, false), "Offscreen widgets suspended");
            Check(Performance.ShouldTick(false, false, true, false, true), "Countdown timers remain active");
            Check(Performance.ShouldTick(true, false, true, false, false), "Forced initial refresh preserved");
            Check(WhatsAppNotifications.IsWhatsApp("5319275A.WhatsAppDesktop_abc", "Chat"), "WhatsApp identity filter");
            Check(!WhatsAppNotifications.IsWhatsApp("Other", "Mail"), "Other notifications excluded");
            foreach (var dpi in new[] { 96, 144, 192 })
            {
                var bounds = new RECT { Left = -1920, Top = -200, Right = 0, Bottom = 880 };
                var top = DockLayout.Edge(bounds, 60 * dpi / 96, true);
                var bottom = DockLayout.Edge(bounds, 60 * dpi / 96, false);
                Check(top.Left == -1920 && top.Top == -200 && top.Bottom - top.Top == 60 * dpi / 96, $"Second monitor negative origin / top / {dpi} DPI");
                Check(bottom.Bottom == 880 && bottom.Right == 0 && bottom.Bottom - bottom.Top == 60 * dpi / 96, $"Second monitor bottom / {dpi} DPI");
            }
            Check(DockLayout.Edge(new RECT { Bottom = 10 }, 100, false).Top == 0, "Small display thickness clamped");
            Check(!Performance.ShouldTick(false, false, false, false, false), "Fullscreen-hidden content suspended");
            App.Settings.Language = "en";
            Check(Localization.T("Ayarlar") == "Settings" && Localization.T("unknown-user-text") == "unknown-user-text", "English lookup and user text fallback");
            Check(Tr.C.Name == "en-US", "English date culture");
            SettingsWindow.Open(); Snapshot(app.Windows.Cast<Window>().Last(), Path.Combine(output, "settings-en.png"));
            App.Settings.Language = "tr";
            Check(Localization.T("Ayarlar") == "Ayarlar" && Tr.C.Name == "tr-TR", "Turkish lookup restored");
            RenderDockScenes(output, Check);
            Check(AppsWidget.MagnifyWeight(0, 100) == 1 && AppsWidget.MagnifyWeight(100, 100) == 0, "Hover wave bounded peak and support");
            Check(Math.Abs(AppsWidget.MagnifyWeight(-40, 100) - AppsWidget.MagnifyWeight(40, 100)) < 0.00001, "Hover neighbors symmetric");
            var sixty = 1d; var oneTwenty = 1d;
            for (int i = 0; i < 12; i++) sixty = AppsWidget.MagnifyApproach(sixty, 1.6, 1d / 60, .065);
            for (int i = 0; i < 24; i++) oneTwenty = AppsWidget.MagnifyApproach(oneTwenty, 1.6, 1d / 120, .065);
            Check(Math.Abs(sixty - oneTwenty) < .00001 && sixty < 1.6, "Hover frame-rate independence without overshoot");
            RenderHoverScene(output, Check);
            RenewalEditor.Open(); Snapshot(app.Windows.Cast<Window>().Last(), Path.Combine(output, "renewals.png"));
            Diagnostics.Open(); Snapshot(app.Windows.Cast<Window>().Last(), Path.Combine(output, "diagnostics.png"));
            SettingsWindow.Open();
            var settings = app.Windows.Cast<Window>().Last();
            settings.UpdateLayout();
            var scroll = FindScroll(settings); scroll?.ScrollToBottom(); settings.UpdateLayout();
            Snapshot(settings, Path.Combine(output, "settings.png"));
            foreach (Window w in app.Windows.Cast<Window>().ToArray()) w.Close();
            Check(true, "New feature windows render without exception");
            UiIntegrationChecks.Run(output,Check); // User-requested material, inline configuration, menus and spacing.
        }
        catch (Exception ex) { results.Add("FAIL " + ex); }
        File.WriteAllLines(Path.Combine(output, "results.txt"), results);
        return results.Any(r => r.StartsWith("FAIL")) ? 1 : 0;
    }
    static void RenderDockScenes(string output, Action<bool, string> check)
    {
        var s = App.Settings;
        s.HideTaskbar = false; s.Widgets = ["clock", "system", "timer"];
        foreach (var mode in new[] { "bar", "balloon" })
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            s.Mode = mode; s.BalloonAlwaysOpen = true;
            var dock = new DockWindow(Monitors.Primary.Handle);
            var content = (FrameworkElement)dock.Content;
            var size = new Size(1100, 160);
            content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1100 * dpi / 96, 160 * dpi / 96, dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, $"dock-{mode}-{dpi}.png"))) encoder.Save(stream);
            check(content.ActualWidth > 0 && content.ActualHeight > 0, $"Offline dock render {mode} {dpi} DPI");
            dock.Close();
        }
        s.Mode = "bar";
    }
    // Isolated visual coverage: source opacity, return-to-rest and both edge anchors.
    static void RenderHoverScene(string output, Action<bool, string> check)
    {
        var s = App.Settings; s.Size = "S"; s.Mode = "balloon"; s.Magnify = 1.6;
        s.Widgets = ["apps"]; s.Pins = [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "calc.exe"), Environment.ProcessPath!];
        foreach (string edge in new[] { "top", "bottom" })
        {
            s.BalloonEdge = edge; s.BalloonAlwaysOpen = true;
            var dock = new DockWindow(Monitors.Primary.Handle);
            var content = (FrameworkElement)dock.Content;
            var size = new Size(900, 180); content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
            // Compare the real dock before/after swapping icons to hover copies at scale 1.
            byte[] Pixels(){content.UpdateLayout();var image=new RenderTargetBitmap(900,180,96,96,PixelFormats.Pbgra32);image.Render(content);var bytes=new byte[900*180*4];image.CopyPixels(bytes,900*4,0);return bytes;}
            void SavePixels(byte[] data,string name){var bitmap=BitmapSource.Create(900,180,96,96,PixelFormats.Pbgra32,null,data,900*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name));encoder.Save(stream);}
            var rest=Pixels();SavePixels(rest,$"rest-{edge}.png");
            var state=AppsWidget.PreviewMagnify(dock,.48,0);
            var swapped=Pixels();SavePixels(swapped,$"swapped-{edge}.png");
            // Allow minor antialiasing from drawing on another visual; a shifted icon changes far more pixels.
            int changed=Enumerable.Range(0,900*180).Count(i=>Enumerable.Range(0,4).Any(c=>Math.Abs(rest[i*4+c]-swapped[i*4+c])>16));
            check(changed<150,$"Actual dock hover copy keeps pixel position at unit scale / {edge}");
            File.WriteAllText(Path.Combine(output,$"hover-swap-{edge}.json"),System.Text.Json.JsonSerializer.Serialize(new{significantlyChangedPixels=changed,allowed=149}));
            state = AppsWidget.PreviewMagnify(dock, .48, 20);

            check(state.Copies > 0 && state.Copies == state.Hidden && state.Peak > 1.1, $"One visible icon per source / {edge}");
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(900, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, $"hover-{edge}.png"))) encoder.Save(stream);
            state = AppsWidget.PreviewMagnify(dock, .48, 60, leave: true);
            check(state.Copies == 0 && state.Hidden == 0, $"Hover exit removes copies and restores source / {edge}");
            dock.Close();
        }
        s.BalloonEdge = "top";
    }
    static ScrollViewer? FindScroll(DependencyObject node)
    {
        if (node is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindScroll(VisualTreeHelper.GetChild(node, i)) is { } child) return child;
        return null;
    }
    static void Snapshot(Window w, string path)
    {
        w.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(w);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
        w.Close();
    }
}
