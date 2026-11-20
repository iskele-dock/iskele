using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;

namespace Iskele;

partial class Settings
{
    /// <summary>Yeni sürümleri GitHub'dan denetle (yalnızca sürüm bilgisi sorulur, hiçbir veri gönderilmez).</summary>
    public bool CheckUpdates { get; set; } = true;
}

/// <summary>
/// Güncelleme denetimi: GitHub'daki son sürümü sorar, daha yeniyse kullanıcıya haber verir. Kullanıcı onaylarsa
/// kurulum dosyasını indirir, SHA256 ile doğrular, IskeleSetup.exe'yi "--update" kipinde başlatıp kendini kapatır;
/// kurulum programı Iskele'nin kapanmasını bekler, sessizce kurar ve Iskele'yi yeniden açar. Hiçbir şey sormadan kurulmaz.
/// </summary>
static class Updater
{
    const string LatestApi = "https://api.github.com/repos/iskele-dock/iskele/releases/latest";
    public const string Site = "https://iskele-dock.github.io";

    public record Release(Version Version, string Tag, string ExeUrl, string ShaUrl);

    public static Release? Available { get; private set; }
    public static bool Busy { get; private set; }
    public static event Action? Changed;
    static DispatcherTimer? timer;

    /// <summary>Çalışan sürüm. Test için ISKELE_FAKE_VERSION ile geçersiz kılınabilir.</summary>
    public static Version Current =>
        Version.TryParse(Environment.GetEnvironmentVariable("ISKELE_FAKE_VERSION"), out var fake) ? Norm(fake)
        : Norm(typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));

    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
    static string Api => Environment.GetEnvironmentVariable("ISKELE_UPDATE_API") is { Length: > 0 } u ? u : LatestApi;

    static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Iskele-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static void Start()
    {
        timer?.Stop();
        if (!App.Settings.CheckUpdates) return;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += async (_, _) =>
        {
            timer.Interval = TimeSpan.FromHours(12);
            if (App.Settings.CheckUpdates) await CheckAsync(manual: false);
        };
        timer.Start();
    }

    public static void Stop() => timer?.Stop();

    /// <summary>Son sürümü sorar. Hata olursa sessizce null döner (elle denetimde mesaj gösterilir).</summary>
    public static async Task<Release?> CheckAsync(bool manual)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(Api));
            var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement[0] : doc.RootElement;
            var release = Parse(root);
            Available = release != null && release.Version > Current ? release : null;
            Changed?.Invoke();
            if (Available != null && !manual)
                Alerts.Send("update-" + Available.Tag, Localization.F("Iskele {0} hazır", Available.Version.ToString(3)),
                    Localization.T("Dock'a sağ tıklayıp Güncelle'yi seçin ya da Ayarlar → Genel'e gidin."), DateTime.Now);
            if (manual)
            {
                if (Busy) return Available;
                if (Available != null) await InstallAsync();
                else MessageBox.Show(Localization.F("Iskele güncel ({0}).", Current.ToString(3)), "Iskele");
            }
            return Available;
        }
        catch (Exception ex)
        {
            App.Log("Güncelleme denetimi: " + ex.Message);
            if (manual) MessageBox.Show(Localization.T("Güncelleme denetlenemedi. İnternet bağlantınızı kontrol edin ya da yeni sürümü siteden indirin."), "Iskele");
            return null;
        }
    }

    internal static Release? Parse(JsonElement r)
    {
        if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        var tag = r.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        string exe = "", exeVersioned = "", sha = "";
        foreach (var a in r.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString() ?? "";
            if (name.Equals("IskeleSetup.exe", StringComparison.OrdinalIgnoreCase)) exe = url;
            else if (name.StartsWith("IskeleSetup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exeVersioned = url;
            else if (name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase)) sha = url;
        }
        if (exeVersioned.Length > 0) exe = exeVersioned;   // SHA dosyası sürümlü dosyayı tanımlar; ikisi aynı bayttır
        return exe.Length == 0 || sha.Length == 0 ? null : new Release(Norm(v), tag, exe, sha);
    }

    /// <summary>Kullanıcı onayıyla indir, doğrula, kurulum programını başlat ve Iskele'yi kapat.</summary>
    public static async Task InstallAsync()
    {
        var rel = Available;
        if (rel == null || Busy) return;
        Busy = true; Changed?.Invoke();   // onay penceresi açıkken ikinci bir güncelleme başlamasın
        bool started = false;
        try
        {
        if (!Uninstaller.Available)
        {
            if (MessageBox.Show(Localization.F("Iskele {0} çıktı. Bu kopya kurulum programıyla kurulmadığı için otomatik güncellenemiyor. Siteyi açayım mı?", rel.Version.ToString(3)),
                    "Iskele", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) App.Open(Site);
            return;
        }
        if (MessageBox.Show(Localization.F("Iskele {0} yüklensin mi?", rel.Version.ToString(3)) + "\n\n" + Localization.T("Iskele birkaç saniyeliğine kapanıp yeniden açılacak; ayarlarınız korunur."),
                "Iskele", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes) return;

        started = true;
        var progress = ProgressWindow.Show(Localization.F("Iskele {0} indiriliyor…", rel.Version.ToString(3)));
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Iskele-update");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"IskeleSetup-{rel.Version.ToString(3)}.exe");
            var expected = (await Http.GetStringAsync(rel.ShaUrl)).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];

            using (var resp = await Http.GetAsync(rel.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? 0, done = 0;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(file);
                var buf = new byte[81920]; int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n)); done += n;
                    if (total > 0) progress.Set((double)done / total);
                }
            }

            string actual;
            await using (var fs = File.OpenRead(file)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                throw new InvalidOperationException(Localization.T("İndirilen dosya doğrulanamadı (SHA256 eşleşmedi); hiçbir şey kurulmadı."));
            }

            progress.Text(Localization.T("Kuruluyor… Iskele birazdan yeniden açılacak."));
            Process.Start(new ProcessStartInfo(file, $"--silent --update {Environment.ProcessId}") { UseShellExecute = false, WorkingDirectory = dir });
            await Task.Delay(400);
            App.Quit();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            progress.Close();
            started = false;
            MessageBox.Show(Localization.T("Güncelleme yapılamadı:") + " " + ex.Message + "\n\n" + Localization.T("Yeni sürümü siteden indirip kurabilirsiniz."), "Iskele");
        }
        }
        finally
        {
            // Kurulum programı başlatıldıysa uygulama kapanıyor; aksi halde kilidi bırak.
            if (!started) { Busy = false; Changed?.Invoke(); }
        }
    }

    /// <summary>İndirme ilerlemesi için küçük pencere.</summary>
    sealed class ProgressWindow
    {
        readonly Window w; readonly TextBlock label; readonly ProgressBar bar;
        ProgressWindow(string text)
        {
            label = Ui.Text(text, 13.5, "Fg", FontWeights.SemiBold, localize: false);
            bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 12, 0, 0) };
            bar.SetResourceReference(Control.ForegroundProperty, "Accent");
            var border = new Border { Padding = new Thickness(20), Child = Ui.Col(label, bar) };
            border.SetResourceReference(Border.BackgroundProperty, "DockBg");
            w = new Window { Title = "Iskele", Content = border, Width = 360, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen };
            w.SetResourceReference(Window.BackgroundProperty, "DockBg");
        }
        public static ProgressWindow Show(string text) { var p = new ProgressWindow(text); p.w.Show(); return p; }
        public void Set(double v) => w.Dispatcher.BeginInvoke(() => bar.Value = v);
        public void Text(string t) { label.Text = t; bar.IsIndeterminate = true; }
        public void Close() => w.Close();
    }
}
