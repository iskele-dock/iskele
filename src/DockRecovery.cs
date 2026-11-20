using Microsoft.Win32;
namespace Iskele;

static class DockRecovery
{
    static DispatcherTimer? settle;
    public static int RecoveryCount { get; private set; }
    public static void Start()
    {
        settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        settle.Tick += (_, _) => { settle.Stop(); Recover(); };
        SystemEvents.DisplaySettingsChanged += Display;
        SystemEvents.PowerModeChanged += Power;
        SystemEvents.SessionSwitch += Session;
    }
    static void Display(object? s, EventArgs e) => Request();
    static void Power(object s, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Request(); }
    static void Session(object s, SessionSwitchEventArgs e)
    { if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon) Request(); }
    public static void Request() => Application.Current.Dispatcher.BeginInvoke(() => { settle?.Stop(); settle?.Start(); });
    public static void Recover()
    {
        RecoverCore(App.CreateDocks, WindowTracker.Refresh, () => App.EachDock(d => d.ApplySettings()));
        RecoveryCount++;
    }
    internal static void RecoverCore(Action create, Action refresh, Action apply) { create(); refresh(); apply(); }
    public static void Stop()
    {
        settle?.Stop();
        SystemEvents.DisplaySettingsChanged -= Display;
        SystemEvents.PowerModeChanged -= Power;
        SystemEvents.SessionSwitch -= Session;
    }
}
