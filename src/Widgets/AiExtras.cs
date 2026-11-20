using System.Text.Json;

namespace Iskele;

/// <summary>
/// AI kullanım grafikleri (hedef 38): son 7 günün günlük çıktı token'ları. Yerel kayıtlardan, arka planda ve en fazla
/// 30 dakikada bir hesaplanır (Claude Code kayıtları büyük olabilir).
/// - Claude Code: ~/.claude/projects/**/*.jsonl (yanıt kimliğine göre tekilleştirilir)
/// - Codex: ~/.codex/sessions/**/*.jsonl; her oturum dosyasının son token_count toplamı, dosyanın gününe yazılır
/// </summary>
static class AiHistory
{
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly Cached<long[]> claude = new(TimeSpan.FromMinutes(30), () => Task.Run(ReadClaude));
    static readonly Cached<long[]> codex = new(TimeSpan.FromMinutes(30), () => Task.Run(ReadCodex));
    public static long[]? Claude { get { claude.Ensure(); return claude.Value; } }
    public static long[]? Codex { get { codex.Ensure(); return codex.Value; } }

    static int Slot(DateTime day) => 6 - (int)(DateTime.Today - day.Date).TotalDays;

    static long[] ReadClaude()
    {
        var days = new long[7];
        var dir = Path.Combine(Home, ".claude", "projects");
        if (!Directory.Exists(dir)) return days;
        var from = DateTime.Today.AddDays(-6);
        var seen = new HashSet<string>();
        foreach (var f in SafeFiles(dir, "*.jsonl").Where(f => f.LastWriteTime >= from))
        {
            try
            {
                using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (!line.Contains("\"output_tokens\"") || !line.Contains("\"assistant\"")) continue;
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    if (!r.TryGetProperty("timestamp", out var ts)) continue;
                    int slot = Slot(ts.GetDateTime().ToLocalTime());
                    if (slot is < 0 or > 6) continue;
                    var msg = r.GetProperty("message");
                    var id = msg.TryGetProperty("id", out var mid) ? mid.GetString() ?? "" : "";
                    if (id.Length > 0 && !seen.Add(id)) continue;
                    if (msg.TryGetProperty("usage", out var u) && u.TryGetProperty("output_tokens", out var ot)) days[slot] += ot.GetInt64();
                }
            }
            catch { }
        }
        return days;
    }

    static long[] ReadCodex()
    {
        var days = new long[7];
        var dir = Path.Combine(Home, ".codex", "sessions");
        if (!Directory.Exists(dir)) return days;
        var from = DateTime.Today.AddDays(-6);
        foreach (var f in SafeFiles(dir, "*.jsonl").Where(f => f.LastWriteTime >= from))
        {
            try
            {
                string? last = null;
                using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    string? line;
                    while ((line = sr.ReadLine()) != null) if (line.Contains("\"total_token_usage\"")) last = line;
                }
                if (last == null) continue;
                using var doc = JsonDocument.Parse(last);
                var tu = doc.RootElement.GetProperty("payload").GetProperty("info").GetProperty("total_token_usage");
                int slot = Slot(f.LastWriteTime);
                if (slot is >= 0 and <= 6) days[slot] += tu.GetProperty("output_tokens").GetInt64();
            }
            catch { }
        }
        return days;
    }

    /// <summary>Erişilemeyen (silinmiş, çok uzun yollu) klasörlerde durmadan dosyaları dolaş.</summary>
    public static IEnumerable<FileInfo> SafeFiles(string dir, string pattern)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        try { return new DirectoryInfo(dir).EnumerateFiles(pattern, opts).ToList(); }
        catch { return []; }
    }
}

/// <summary>
/// OpenCode kullanımı (hedef 36): ~/.local/share/opencode/storage/message/**/*.json içindeki yanıtların token'ları.
/// Yalnızca yerel dosyalar; bu sayfadaki veri biçimi OpenCode'un yerel depolamasına göredir.
/// </summary>
static class OpenCode
{
    public record Usage(long Today, long Week, string TopModel, int Messages);
    static readonly Cached<Usage?> cache = new(TimeSpan.FromMinutes(2), () => Task.Run(Read));
    public static Usage? Data { get { cache.Ensure(); return cache.Value; } }

    static Usage? Read()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = new[] { Path.Combine(home, ".local", "share", "opencode", "storage", "message"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "storage", "message") }
            .FirstOrDefault(Directory.Exists);
        if (dir == null) return null;
        long today = 0, week = 0;
        int msgs = 0;
        var byModel = new Dictionary<string, long>();
        var weekStart = DateTime.Today.AddDays(-6);
        foreach (var f in AiHistory.SafeFiles(dir, "*.json").Where(f => f.LastWriteTime >= weekStart))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f.FullName));
                var r = doc.RootElement;
                if (!r.TryGetProperty("tokens", out var tk)) continue;
                long n = (tk.TryGetProperty("input", out var i) ? i.GetInt64() : 0) + (tk.TryGetProperty("output", out var o) ? o.GetInt64() : 0);
                var when = r.TryGetProperty("time", out var tm) && tm.TryGetProperty("created", out var cr) ? DateTimeOffset.FromUnixTimeMilliseconds(cr.GetInt64()).LocalDateTime : f.LastWriteTime;
                if (when < weekStart) continue;
                week += n;
                if (when >= DateTime.Today) today += n;
                msgs++;
                var model = r.TryGetProperty("modelID", out var m) ? m.GetString() ?? "?" : "?";
                byModel[model] = byModel.GetValueOrDefault(model) + n;
            }
            catch { }
        }
        return new Usage(today, week, byModel.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? "", msgs);
    }
}

class OpenCodeWidget : Widget
{
    readonly TextBlock title, sub;
    public OpenCodeWidget()
    {
        title = Ui.Text("", F(12), "Fg", FontWeights.Bold);
        sub = Ui.Text("", F(9.5), "FgDim");
        var col = Ui.Col(title, sub);
        col.MinWidth = F(110);
        col.Margin = new Thickness(F(8), 0, 0, 0);
        Child = Ui.Row(ProviderIcons.Create("OpenCode",H*.66),col); // Original OpenCode mark.
        OnClick(() => Flyout.Show(this, AiPanel.Build(), 360));
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        var d = OpenCode.Data;
        if (d == null) { title.Text = "OpenCode"; sub.Text = Localization.T("kayıt bulunamadı"); return; }
        title.Text = Localization.F("Bugün {0}", AiLimits.Num(d.Today));
        sub.Text = Localization.F("Hafta {0} · {1}", AiLimits.Num(d.Week), d.TopModel);
    }
}
