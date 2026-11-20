using System.Diagnostics;

namespace Iskele;

static class Program
{
    // Unique owner token passed to this instance's watchdog.
    internal static readonly string SessionGeneration = Guid.NewGuid().ToString("N");
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "--feature-tests") return FeatureTests.Run(args[1]);
        if (args.Length > 1 && args[0] == "--tray-probe") return TrayWidget.Probe(args[1]); // Explicit native verification.

        // "Iskele.exe --restore": görev çubuğunu her koşulda geri getirir (acil durum).
        if (args.Length > 0 && args[0] == "--restore")
        {
            TaskbarGuard.Restore(force: true);
            return 0;
        }

        // "Iskele.exe --icons <dosya>": masaüstü simgelerinin konumlarını dosyaya yaz (test/teşhis için).
        if (args.Length > 1 && args[0] == "--icons")
        {
            File.WriteAllLines(args[1], DesktopIcons.Save().Select(i => $"{i.Name}\t{i.X}\t{i.Y}"));
            return 0;
        }

        // "Iskele.exe --icons-set <dosya>": masaüstü simgelerini dosyadaki konumlara koy (ad<TAB>x<TAB>y).
        if (args.Length > 1 && args[0] == "--icons-set")
        {
            var want = File.ReadAllLines(args[1]).Select(l => l.Split('\t')).Where(p => p.Length == 3)
                .Select(p => (p[0], int.Parse(p[1]), int.Parse(p[2]))).ToList();
            int n = DesktopIcons.Restore(want);
            File.WriteAllText(args[1] + ".sonuc", n.ToString());
            return 0;
        }

        // "Iskele.exe --dnd-test <dosya>": rahatsız etmeyin durumunu oku, değiştir, tekrar oku, eski haline getir.
        if (args.Length > 1 && args[0] == "--dnd-test")
        {
            var a = DoNotDisturb.On;
            if (a is bool b0) DoNotDisturb.Set(!b0);
            Thread.Sleep(800);
            var b = DoNotDisturb.On;
            if (a is bool b1) DoNotDisturb.Set(b1);
            Thread.Sleep(500);
            File.WriteAllText(args[1], $"once={a} degisti={b} geri={DoNotDisturb.On}");
            return 0;
        }

        // "Iskele.exe --watch <pid>": bekçi süreç. Ana süreç nasıl kapanırsa kapansın
        // (çökme, Görev Yöneticisi'nden sonlandırma) görev çubuğunu geri getirir.
        if (args.Length > 1 && args[0] == "--watch")
        {
            if (int.TryParse(args[1], out int pid))
            {
                try { using var p = Process.GetProcessById(pid); p.WaitForExit(); } catch { }
                Thread.Sleep(400);
                TaskbarGuard.RestoreOwned(args.Length > 2 ? args[2] : "");
            }
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\Iskele.Dock", out bool first);
        if (!first) return 0;

        TaskbarGuard.Restore();   // önceki oturum çöktüyse kalan durumu temizle
        new App().Run();
        return 0;
    }
}

class App : Application
{
    public static Settings Settings { get; private set; } = new();
    public static DockWindow? Dock { get; private set; }

    /// <summary>Tüm dock'lar (çoklu monitörde her ekrana bir tane). Dock, birincil olanıdır.</summary>
    public static readonly List<DockWindow> Docks = [];
    public static void EachDock(Action<DockWindow> a) { foreach (var d in Docks.ToList()) a(d); }

    /// <summary>Ayara göre dock'ları kur ya da ekran değişince yeniden düzenle (hedef 22).</summary>
    public static void CreateDocks()
    {
        var mons = Monitors.All();
        if (mons.Count == 0) return;
        var primary = mons.FirstOrDefault(m => m.Primary) ?? mons[0];
        List<Monitors.Info> want = Settings.Displays == "all"
            ? mons
            : [mons.FirstOrDefault(m => m.Name == Settings.MonitorName) ?? primary];
        // artık olmayan ekranlardaki dock'ları kapat
        foreach (var d in Docks.ToList())
            if (!want.Any(m => m.Handle == d.MonitorHandle)) { Docks.Remove(d); d.Close(); }
        foreach (var m in want)
            if (!Docks.Any(d => d.MonitorHandle == m.Handle)) { var d = new DockWindow(m.Handle); Docks.Add(d); d.Show(); }
        Docks.Sort((a, b) => b.IsPrimaryMon.CompareTo(a.IsPrimaryMon));
        Dock = Docks.FirstOrDefault();
    }

    // takip modu: imleç başka bir ekranın kenarında kısa süre durursa dock oraya geçer
    static DispatcherTimer? followTimer;
    static IntPtr followCandidate;
    static DateTime followSince;

    static void FollowTick()
    {
        if (Settings.Displays != "follow" || Dock == null) return;
        Native.GetCursorPos(out var c);
        var m = Monitors.At(c);
        if (m == null || m.Handle == Dock.MonitorHandle) { followCandidate = IntPtr.Zero; return; }
        bool top = DockWindow.IsTop;
        bool atEdge = top ? c.Y <= m.Bounds.Top + 1 : c.Y >= m.Bounds.Bottom - 2;
        if (!atEdge) { followCandidate = IntPtr.Zero; return; }
        if (followCandidate != m.Handle) { followCandidate = m.Handle; followSince = DateTime.Now; return; }
        if ((DateTime.Now - followSince).TotalMilliseconds < 350) return;
        followCandidate = IntPtr.Zero;
        Settings.MonitorName = m.Name;
        Settings.Save();
        Dock.MoveToMonitor(m.Handle);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (_, ex) => { Log(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { Log(ex.ExceptionObject); WriteCrash(ex.ExceptionObject); TaskbarGuard.RestoreOwned(Program.SessionGeneration); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TaskbarGuard.RestoreOwned(Program.SessionGeneration);
        SessionEnding += (_, _) => { ScreenTime.Save(); TaskbarGuard.RestoreOwned(Program.SessionGeneration); };

        Settings = Settings.Load();
        // Geliştirici testi: yalnızca bu çalıştırma için geçerli; açıkken ayarlar diske KAYDEDİLMEZ.
        if (Environment.GetEnvironmentVariable("ISKELE_THEME") is { Length: > 0 } testTheme) { Settings.Theme = testTheme; Settings.NoSave = true; }
        if (Environment.GetEnvironmentVariable("ISKELE_MODE") is { Length: > 0 } testMode) { Settings.Mode = testMode; Settings.NoSave = true; }
        if (Environment.GetEnvironmentVariable("ISKELE_AUTOHIDE") is { Length: > 0 } testAh) { Settings.AutoHide = testAh == "1"; Settings.NoSave = true; }
        if (Environment.GetEnvironmentVariable("ISKELE_HIDETB") is { Length: > 0 } testTb) { Settings.HideTaskbar = testTb == "1"; Settings.NoSave = true; }
        if (Environment.GetEnvironmentVariable("ISKELE_TBMODE") is { Length: > 0 } testTbm) { Settings.TaskbarMode = testTbm; Settings.NoSave = true; }
        if (Environment.GetEnvironmentVariable("ISKELE_ADDWIDGET") is { Length: > 0 } testW) { Settings.Widgets.AddRange(testW.Split('|')); Settings.NoSave = true; }
        if (Environment.GetEnvironmentVariable("ISKELE_TOGGLES") is { Length: > 0 } testTg) { Settings.QuickToggles = testTg.Split('|').ToList(); Settings.NoSave = true; }
        // Geliştirici testi: ISKELE_SET="Magnify=1.6;IconStyle=frame" (yalnızca bu çalıştırma, kaydedilmez)
        if (Environment.GetEnvironmentVariable("ISKELE_SET") is { Length: > 0 } sets)
        {
            foreach (var pair in sets.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                var prop = typeof(Settings).GetProperty(kv[0]);
                if (prop == null || kv.Length < 2) continue;
                try { prop.SetValue(Settings, Convert.ChangeType(kv[1], prop.PropertyType, System.Globalization.CultureInfo.InvariantCulture)); } catch (Exception ex) { Log(ex.Message); }
            }
            Settings.NoSave = true;
        }
        if (Environment.GetEnvironmentVariable("ISKELE_STARTIMG") is { Length: > 0 } testSi) { Settings.StartImage = testSi; Settings.NoSave = true; }
        if (Environment.GetEnvironmentVariable("ISKELE_BALLOONEDGE") is { Length: > 0 } testBe) { Settings.BalloonEdge = testBe; Settings.NoSave = true; }
        Theme.LoadStyles();
        Theme.Apply(Settings.Theme);
        StartWatchdog();
        ScreenTime.Start();
        WindowTracker.Start();
        KeyHook.WinNumber = AppsWidget.ActivateIndex;
        KeyHook.AltT = QuickTodoWindow.Toggle;
        KeyHook.Start();
        Exit += (_, _) => KeyHook.Stop();

        CreateDocks();
        followTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => FollowTick(), Dispatcher);
        followTimer.Start();
        DockRecovery.Start();
        Automations.Start();
        Updater.Start();
        Performance.Start();

        // Geliştirici testi: ISKELE_OPEN=settings | picker
        var open = Environment.GetEnvironmentVariable("ISKELE_OPEN") ?? "";
        if (open == "settings") SettingsWindow.Open();
        else if (open is "sound" or "storage" or "ai" or "screentime" or "notes")
        {
            Dock.Dispatcher.BeginInvoke(() => Flyout.Show(Dock, open switch
            {
                "sound" => SoundPanel.Build(), "storage" => StorageHistory.Panel(), "ai" => AiPanel.Build(),
                "screentime" => ScreenTimePanel.Build(), _ => NotesPanel.Build(),
            }, 360), DispatcherPriority.ApplicationIdle);
        }
        else if (open is "wifi" or "bluetooth")
        {
            // Geliştirici testi: ilk widget'ın yanında Wi‑Fi / Bluetooth listesini aç
            Dock.Dispatcher.BeginInvoke(() => Flyout.Show(Dock, open == "wifi" ? new WifiPanel() : new BluetoothPanel(), 340), DispatcherPriority.ApplicationIdle);
        }
        else if (open.StartsWith("picker")) PickerWindow.Open(open.Contains(':') ? open[(open.IndexOf(':') + 1)..] : null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Performance.Stop();
        Automations.Stop();
        DockRecovery.Stop();
        ScreenTime.Save();
        TaskbarGuard.RestoreOwned(Program.SessionGeneration);
        base.OnExit(e);
    }

    public static void Quit()
    {
        try { EachDock(d => d.Close()); } catch (Exception ex) { Log(ex); }
        TaskbarGuard.RestoreOwned(Program.SessionGeneration);
        ScreenTime.Save();
        Current.Shutdown();
    }

    static void StartWatchdog()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo(exe, $"--watch {Environment.ProcessId} {Program.SessionGeneration}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex) { Log(ex); }
    }

    public static void Log(object? o)
    {
        try { File.AppendAllText(Path.Combine(Settings.Dir, "log.txt"), $"{DateTime.Now:s} {o}{Environment.NewLine}"); }
        catch { }
    }

    static void WriteCrash(object? error)
    {
        try { File.WriteAllText(Path.Combine(Settings.Dir, "crash.txt"), Diagnostics.Redact($"{DateTime.Now:O}\n{error}")); } catch { }
    }

    static readonly bool debug = Environment.GetEnvironmentVariable("ISKELE_DEBUG") == "1";

    /// <summary>Yalnızca ISKELE_DEBUG=1 iken günlüğe yazar.</summary>
    public static void Debug(string msg)
    {
        if (debug) Log("[debug] " + msg);
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log(ex); }
    }
}
