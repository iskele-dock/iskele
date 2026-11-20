using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
namespace Iskele;

// Notification text is kept only in memory. Never open browser cookies or private databases.
static class WhatsAppNotifications
{
    public static int Count { get; private set; }
    public static string Sender { get; private set; } = "";
    public static string Status { get; private set; } = "Bildirim erişimi verilmedi";
    static bool busy;
    static DateTime last;
    public static async Task Request()
    {
        try
        {
            var permission = await UserNotificationListener.Current.RequestAccessAsync();
            Status = permission == UserNotificationListenerAccessStatus.Allowed ? "" : "Windows bildirim izni gerekli";
            if (permission == UserNotificationListenerAccessStatus.Allowed) { last = DateTime.MinValue; await Refresh(); }
        }
        catch { Status = "Bildirim yetkili paketli kurulum gerekiyor"; }
    }
    public static async Task Refresh()
    {
        if (busy || DateTime.UtcNow - last < TimeSpan.FromSeconds(15)) return;
        busy = true; last = DateTime.UtcNow;
        try
        {
            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) { Count = 0; Sender = ""; return; }
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var messages = notifications.Where(n => IsWhatsApp(n.AppInfo.AppUserModelId, n.AppInfo.DisplayInfo.DisplayName))
                .OrderByDescending(n => n.CreationTime).ToList();
            Count = messages.Count;
            Sender = messages.FirstOrDefault()?.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements().FirstOrDefault()?.Text ?? "";
            Status = "";
        }
        catch { Count = 0; Sender = ""; Status = "Bildirim erişimi kullanılamıyor"; }
        finally { busy = false; }
    }
    internal static bool IsWhatsApp(string id, string name) => id.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) || name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase);
}
class WhatsAppWidget : Widget
{
    readonly TextBlock title = Ui.Text("WhatsApp", F(12), "Fg", FontWeights.Bold);
    readonly TextBlock summary = Ui.Text("", F(10), "FgDim");
    public WhatsAppWidget()
    {
        var col = Ui.Col(title, summary); col.Width = F(190); Child = col;
        OnClick(() =>
        {
            var p = new StackPanel();
            p.Children.Add(Ui.Text("WhatsApp bildirimleri", 16, "Fg", FontWeights.Bold));
            var info = Ui.Text("Yalnızca Windows bildirim merkezinde kalan bildirimler sayılır. Sohbetlerin gerçek okunmamış toplamı değildir. Bildirim başlığı bellekte tutulur; kaydedilmez.", 12, "FgDim");
            info.TextWrapping = TextWrapping.Wrap; p.Children.Add(info);
            p.Children.Add(PanelRows.Link("Windows bildirim erişimi ver…", async () => { await WhatsAppNotifications.Request(); Tick(DateTime.Now); }));
            Flyout.Show(this, p, 360);
        });
    }
    public override void Tick(DateTime now)
    {
        _ = WhatsAppNotifications.Refresh();
        title.Text = $"WhatsApp · {WhatsAppNotifications.Count} bildirim";
        summary.Text = WhatsAppNotifications.Status.Length > 0 ? WhatsAppNotifications.Status : WhatsAppNotifications.Sender.Length > 0 ? WhatsAppNotifications.Sender : "Bildirim yok";
    }
}
