namespace Iskele;
partial class Settings { public bool PauseBeforeMeetings { get; set; }
    public bool RenewalAlerts { get; set; }
    public bool AiLimitAlerts { get; set; }
    public int AiAlertPercent { get; set; } = 90; }
static class Automations
{
    static DispatcherTimer? timer;
    static bool busy;
    static readonly HashSet<string> paused = [];
    public static void Start()
    {
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += async (_, _) => await Tick(DateTime.Now);
        timer.Start();
    }
    public static void Stop() => timer?.Stop();
    public static bool MeetingDue(CalEvent e, DateTime now) => !e.AllDay && e.Start > now && e.Start <= now.AddMinutes(2);
    public static async Task Tick(DateTime now)
    {
        if (busy) return;
        busy = true;
        try
        {
            if (App.Settings.RenewalAlerts)
                foreach (var renewal in App.Settings.Renewals)
                {
                    var next = renewal.Next(now.Date);
                    if (next >= now.Date && next <= now.Date.AddDays(3))
                        Alerts.Send("renewal:" + renewal.Name + ":" + next.ToString("yyyy-MM-dd"), "Yenileme hatırlatıcısı", $"{renewal.Name} · {next:dd.MM.yyyy} ({(next - now.Date).Days} gün kaldı)", now);
                }
            if (App.Settings.AiLimitAlerts)
                foreach (var usage in new[] { AiLimits.Claude, AiLimits.Codex })
                    if (usage?.Session is { } limit && limit.ResetsAt > now && limit.Percent >= App.Settings.AiAlertPercent && now - usage.UpdatedAt < TimeSpan.FromMinutes(5))
                        Alerts.Send("ai:" + usage.Tool + ":" + limit.ResetsAt.Value.ToString("O"), "AI kullanım uyarısı", $"{usage.Tool} oturum limitinin %{limit.Percent:0} kadarı kullanıldı.", now);
            if (App.Settings.PauseBeforeMeetings)
            {
                CalendarService.Ensure();
                foreach (var e in CalendarService.Events.Where(e => MeetingDue(e, now)))
                {
                    string key = e.Start.ToString("O") + ":" + e.Title;
                    if (paused.Contains(key)) continue;
                    if (await MediaWidget.PausePlaying()) paused.Add(key);
                }
                if (paused.Count > 500) paused.Clear();
            }
        }
        catch (Exception ex) { App.Log(ex); }
        finally { busy = false; }
    }
}
