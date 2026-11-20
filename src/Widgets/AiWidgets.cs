using System.Diagnostics;
using System.Text.Json;

namespace Iskele;

record LimitWindow(double Percent, DateTime? ResetsAt);
record AiUsage(string Tool, LimitWindow? Session, LimitWindow? Week, string Detail, DateTime UpdatedAt)
{
    public string L1 { get; init; } = "Oturum";
    public string L2 { get; init; } = "Hafta";
    public string Plan { get; init; } = "";
    public string Extra { get; init; } = "";
    public List<(string Name, double Pct)> Breakdown { get; init; } = [];
}

/// <summary>
/// AI araçlarının plan limitleri. Hepsi bu bilgisayardaki yerel dosyalardan okunur; hiçbir yere istek atılmaz.
/// - Claude: Claude Code durum satırının yazdığı ~/.claude/statusline-last.json (rate_limits)
///           + ~/.claude/projects/**/*.jsonl içinden bugünkü token kullanımı
/// - Codex : ~/.codex/sessions/**/*.jsonl içindeki son "token_count" olayı (rate_limits)
/// </summary>
static class AiLimits
{
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly Cached<AiUsage?> claude = new(TimeSpan.FromSeconds(30), () => Task.Run(ReadClaudeAny));
    static readonly Cached<AiUsage?> gemini = new(TimeSpan.FromSeconds(30), () => Task.Run(ReadGemini));
    public static AiUsage? Gemini { get { gemini.Ensure(); return gemini.Value; } }
    static readonly Cached<AiUsage?> codex = new(TimeSpan.FromSeconds(30), () => Task.Run(ReadCodex));
    static readonly Cached<ClaudeTokens> tokens = new(TimeSpan.FromMinutes(3), () => Task.Run(ReadTokens));

    public static AiUsage? Claude { get { claude.Ensure(); return claude.Value; } }
    public static AiUsage? Codex { get { codex.Ensure(); return codex.Value; } }
    public static ClaudeTokens? Tokens { get { tokens.Ensure(); return tokens.Value; } }

    static DateTime FromUnix(long s) => DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime;

    // ── Claude: önce Claude Code'un kendi oturum anahtarıyla Anthropic'in kullanım ucu (salt okunur, en fazla 3 dk'da bir);
    //    olmazsa durum satırının yazdığı dosya. Anahtar yalnızca Anthropic'e gönderilir, hiçbir yere kaydedilmez.
    static DateTime apiAt = DateTime.MinValue;
    static AiUsage? apiLast;

    static AiUsage? ReadClaudeAny()
    {
        if ((DateTime.Now - apiAt).TotalMinutes >= 3)
        {
            apiAt = DateTime.Now;
            try { apiLast = ReadClaudeApi() ?? apiLast; } catch (Exception ex) { App.Log("Claude kullanım ucu: " + ex.Message); }
        }
        var sl = ReadClaude();
        if (apiLast != null && (sl == null || apiLast.UpdatedAt >= sl.UpdatedAt.AddMinutes(-5)))
            return apiLast with { Detail = sl?.Detail is { Length: > 0 } d ? d : apiLast.Detail };
        return sl ?? apiLast;
    }

    static AiUsage? ReadClaudeApi()
    {
        var path = Path.Combine(Home, ".claude", ".credentials.json");
        if (!File.Exists(path)) return null;
        using var cred = JsonDocument.Parse(File.ReadAllText(path));
        if (!cred.RootElement.TryGetProperty("claudeAiOauth", out var o)) return null;
        var token = o.GetProperty("accessToken").GetString();
        if (o.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number &&
            DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64()) < DateTimeOffset.Now) return null;   // süresi dolmuş; Claude Code yeniler
        string plan = o.TryGetProperty("subscriptionType", out var st) ? st.GetString() ?? "" : "";

        var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        req.Headers.Authorization = new("Bearer", token);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        using var resp = Net.Http.Send(req);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
        var r = doc.RootElement;
        LimitWindow? Win(string name)
        {
            if (!r.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
            double p = w.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() : 0;
            DateTime? reset = w.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.String ? DateTime.Parse(ra.GetString()!).ToLocalTime() : null;
            return new LimitWindow(p, reset);
        }
        string extra = "";
        if (r.TryGetProperty("extra_usage", out var ex) && ex.ValueKind == JsonValueKind.Object)
            extra = ex.TryGetProperty("is_enabled", out var en) && en.ValueKind == JsonValueKind.True ? "Ek kullanım açık" : "Ek kullanım kapalı";
        var bd = new List<(string, double)>();
        if (r.TryGetProperty("seven_day_breakdown", out var b) && b.ValueKind == JsonValueKind.Object && b.TryGetProperty("rows", out var rows))
            foreach (var row in rows.EnumerateArray())
                if (row.TryGetProperty("percent", out var pc) && pc.ValueKind == JsonValueKind.Number && pc.GetDouble() > 0)
                    bd.Add((row.GetProperty("display_name").GetString() ?? "", pc.GetDouble()));
        string planName = plan.Length > 0 ? "Claude " + char.ToUpper(plan[0]) + plan[1..] : "";
        return new AiUsage("Claude", Win("five_hour"), Win("seven_day"), planName, DateTime.Now) { Plan = planName, Extra = extra, Breakdown = bd };
    }

    // ── Gemini (Antigravity CLI): durum satırı betiğinin (quota.ps1) kaydettiği son kota verisi
    static AiUsage? ReadGemini()
    {
        var path = Path.Combine(Home, ".gemini", "antigravity-cli", "statusline-last.json");
        if (!File.Exists(path)) return null;
        var at = File.GetLastWriteTime(path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        var wins = new List<(string Name, LimitWindow W)>();
        if (r.TryGetProperty("quota", out var q) && q.ValueKind == JsonValueKind.Object)
            foreach (var p in q.EnumerateObject())
            {
                if (!p.Value.TryGetProperty("remaining_fraction", out var rf) || rf.ValueKind != JsonValueKind.Number) continue;
                DateTime? reset = p.Value.TryGetProperty("reset_in_seconds", out var rs) && rs.ValueKind == JsonValueKind.Number ? at.AddSeconds(rs.GetDouble()) : null;
                double used = (1 - rf.GetDouble()) * 100;
                if (reset != null && reset < DateTime.Now) used = 0;
                wins.Add((p.Name, new LimitWindow(used, reset)));
            }
        if (wins.Count == 0) return null;
        string model = r.TryGetProperty("model", out var m) && m.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "";
        static string Short(string n) => n.Length > 7 ? n[..7] : n;
        return new AiUsage("Gemini", wins[0].W, wins.Count > 1 ? wins[1].W : null, model, at)
        { L1 = Short(wins[0].Name), L2 = wins.Count > 1 ? Short(wins[1].Name) : "" };
    }

    static AiUsage? ReadClaude()
    {
        var path = Path.Combine(Home, ".claude", "statusline-last.json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        LimitWindow? Win(string name)
        {
            if (!r.TryGetProperty("rate_limits", out var rl) || !rl.TryGetProperty(name, out var w)) return null;
            double p = w.TryGetProperty("used_percentage", out var up) && up.ValueKind == JsonValueKind.Number ? up.GetDouble() : 0;
            DateTime? reset = w.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.Number ? FromUnix(ra.GetInt64()) : null;
            // pencere sıfırlandıysa eski yüzdeyi gösterme
            if (reset != null && reset < DateTime.Now) p = 0;
            return new LimitWindow(p, reset);
        }
        string model = r.TryGetProperty("model", out var m) && m.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "";
        return new AiUsage("Claude", Win("five_hour"), Win("seven_day"), model, File.GetLastWriteTime(path));
    }

    static AiUsage? ReadCodex()
    {
        var dir = Path.Combine(Home, ".codex", "sessions");
        if (!Directory.Exists(dir)) return null;
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTime).Take(5))
        {
            string? last = null;
            using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                string? line;
                while ((line = sr.ReadLine()) != null)
                    if (line.Contains("\"rate_limits\"") && line.Contains("token_count")) last = line;
            }
            if (last == null) continue;
            using var doc = JsonDocument.Parse(last);
            var rl = doc.RootElement.GetProperty("payload").GetProperty("rate_limits");
            LimitWindow? Win(string name)
            {
                if (!rl.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
                double p = w.GetProperty("used_percent").GetDouble();
                DateTime? reset = w.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.Number ? FromUnix(ra.GetInt64()) : null;
                if (reset != null && reset < DateTime.Now) p = 0;
                return new LimitWindow(p, reset);
            }
            string plan = rl.TryGetProperty("plan_type", out var pt) && pt.ValueKind == JsonValueKind.String ? pt.GetString()! : "";
            return new AiUsage("Codex", Win("primary"), Win("secondary"), plan.Length > 0 ? char.ToUpper(plan[0]) + plan[1..] : "", f.LastWriteTime);
        }
        return null;
    }

    public record ClaudeTokens(long Output, long Input, long CacheRead, int Messages, string TopModel, Dictionary<string, long> ByModel);

    /// <summary>Bugün Claude Code ile yapılan istekler (yerel kayıtlardan).</summary>
    static ClaudeTokens ReadTokens()
    {
        var dir = Path.Combine(Home, ".claude", "projects");
        long outT = 0, inT = 0, cache = 0;
        int msgs = 0;
        var byModel = new Dictionary<string, long>();
        var seen = new HashSet<string>();
        var today = DateTime.Today;
        if (Directory.Exists(dir))
        {
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).Where(f => f.LastWriteTime >= today))
            {
                try
                {
                    using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (!line.Contains("\"usage\"") || !line.Contains("\"assistant\"")) continue;
                        using var doc = JsonDocument.Parse(line);
                        var r = doc.RootElement;
                        if (!r.TryGetProperty("timestamp", out var ts) || ts.GetDateTime().ToLocalTime() < today) continue;
                        var msg = r.GetProperty("message");
                        // aynı yanıt birden çok satıra bölünebilir: mesaj kimliğiyle tekilleştir
                        var id = msg.TryGetProperty("id", out var mid) ? mid.GetString() ?? "" : "";
                        if (id.Length > 0 && !seen.Add(id)) continue;
                        var u = msg.GetProperty("usage");
                        long o = u.TryGetProperty("output_tokens", out var ot) ? ot.GetInt64() : 0;
                        outT += o;
                        inT += u.TryGetProperty("input_tokens", out var it) ? it.GetInt64() : 0;
                        cache += u.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt64() : 0;
                        msgs++;
                        var model = msg.TryGetProperty("model", out var md) ? md.GetString() ?? "?" : "?";
                        byModel[model] = byModel.GetValueOrDefault(model) + o;
                    }
                }
                catch { }
            }
        }
        var top = byModel.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? "";
        return new ClaudeTokens(outT, inT, cache, msgs, Pretty(top), byModel);
    }

    public static string Pretty(string model)
    {
        if (model.Length == 0) return "";
        var parts = model.Replace("claude-", "").Split('-');
        return parts.Length >= 3 ? $"{char.ToUpper(parts[0][0]) + parts[0][1..]} {parts[1]}.{parts[2]}" : model;
    }

    public static string Until(DateTime? t)
    {
        if (t == null) return "";
        var d = t.Value - DateTime.Now;
        if (d <= TimeSpan.Zero) return Localization.T("sıfırlandı");
        var (dd, hh, mm) = Localization.En ? ("d", "h", "m") : ("g", "sa", "dk");
        if (d.TotalHours >= 24) return $"{(int)d.TotalDays}{dd} {d.Hours}{hh}";
        return d.TotalHours >= 1 ? $"{(int)d.TotalHours}{hh} {d.Minutes}{mm}" : $"{d.Minutes}{mm}";
    }

    public static string Pct(double p) => Localization.En ? $"{p:0}%" : $"%{p:0}";
    public static string Num(long n) => n >= 1_000_000 ? $"{n / 1e6:0.0}M" : n >= 1000 ? $"{n / 1e3:0.0}k" : n.ToString();

    public static string LevelKey(double p) => p >= 85 ? "Warn" : p >= 60 ? "Accent" : "Good";
}

/// <summary>Bir AI aracının limit kartı: oturum ve hafta çubukları + sıfırlanma.</summary>
class AiLimitWidget : Widget
{
    readonly string tool;
    readonly Color color;
    readonly string glyph;
    readonly LimitRow session, week;
    readonly TextBlock foot;

    class LimitRow : DockPanel
    {
        readonly MiniSlider bar = new() { Interactive = false, Height = 4 };
        readonly TextBlock pct, reset, l;
        public string Label { set => l.Text = Localization.T(value); }
        public LimitRow(string label, double s)
        {
            l = Ui.Text(label, Math.Round(9 * s, 1), "Fg", FontWeights.Bold);
            l.Width = Math.Round(40 * s);
            pct = Ui.Text("", Math.Round(9 * s, 1), "Fg", FontWeights.Bold);
            pct.Width = Math.Round(30 * s);
            pct.TextAlignment = TextAlignment.Right;
            reset = Ui.Text("", Math.Round(8.5 * s, 1), "FgDim");
            reset.Width = Math.Round(44 * s);
            reset.TextAlignment = TextAlignment.Right;
            SetDock(l, Dock.Left);
            SetDock(pct, Dock.Right);
            SetDock(reset, Dock.Right);
            Children.Add(l);
            Children.Add(pct);
            Children.Add(reset);
            bar.VerticalAlignment = VerticalAlignment.Center;
            bar.Margin = new Thickness(0, 0, Math.Round(4 * s), 0);
            Children.Add(bar);
        }
        public void Set(LimitWindow? w)
        {
            bar.Value = (w?.Percent ?? 0) / 100;
            bar.FillKey = AiLimits.LevelKey(w?.Percent ?? 0);
            bar.InvalidateVisual();
            pct.Text = w == null ? "—" : AiLimits.Pct(w.Percent);
            reset.Text = AiLimits.Until(w?.ResetsAt);
        }
    }

    public AiLimitWidget(string tool)
    {
        this.tool = tool;
        (color, glyph) = tool switch { "Claude" => (Color.FromRgb(0xD9, 0x77, 0x57), "\uE945"), "Gemini" => (Color.FromRgb(0x42, 0x85, 0xF4), "\uE9D9"), _ => (Color.FromRgb(0x10, 0xA3, 0x7F), "\uE943") };
        session = new LimitRow("Oturum", S);
        week = new LimitRow("Hafta", S);
        foot = Ui.Text("", F(8.5), "FgDim");
        var col = Ui.Col(session, week, foot);
        col.Width = F(170);
        col.Margin = new Thickness(F(8), 0, 0, 0);
        Padding = new Thickness(F(6), 0, F(10), 0);
        Child = Ui.Row(ProviderIcons.Create(tool,H*.66), col); // Original provider mark, not a generic glyph.
        OnClick(() => Flyout.Show(this, AiPanel.Build(), 360));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        var u = tool switch { "Claude" => AiLimits.Claude, "Gemini" => AiLimits.Gemini, _ => AiLimits.Codex };
        if (u == null)
        {
            session.Set(null);
            week.Set(null);
            foot.Text = tool switch { "Claude" => "Claude Code girişi bulunamadı", "Gemini" => "Antigravity CLI verisi yok", _ => "Codex oturumu bulunamadı" };
            return;
        }
        session.Label = u.L1;
        week.Label = u.L2;
        session.Set(u.Session);
        week.Set(u.Week);
        string extra = tool == "Claude" && AiLimits.Tokens is { } t && t.TopModel.Length > 0 ? $"{(u.Plan.Length > 0 ? u.Plan.Replace("Claude ", "") + " · " : "")}{t.TopModel} · {(Localization.En ? "today" : "bugün")} {AiLimits.Num(t.Output)}" : u.Detail;
        foot.Text = $"{tool} · {extra}";
        ToolTip = Localization.F("{0} — son güncelleme {1:HH:mm}", tool, u.UpdatedAt);
    }
}

/// <summary>İki aracın 5 saatlik limiti tek kartta.</summary>
class AiLimitsWidget : Widget
{
    readonly List<(string Tool, RingGauge Ring, TextBlock Pct, TextBlock Reset)> items = [];

    public AiLimitsWidget()
    {
        var row = Ui.Row();
        foreach (var (tool, c) in new[] { ("Claude", Color.FromRgb(0xD9, 0x77, 0x57)), ("Codex", Color.FromRgb(0x10, 0xA3, 0x7F)) })
        {
            double r = H - F(12);
            var ring = new RingGauge { Stroke = 3.5 };
            var pct = Ui.Text("", F(10), "Fg", FontWeights.Bold);
            pct.HorizontalAlignment = HorizontalAlignment.Center;
            var g = new Grid { Width = r, Height = r };
            g.Children.Add(ring);
            g.Children.Add(pct);
            var name = new TextBlock { Text = tool, FontFamily = Ui.Font, FontSize = F(9.5), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(c) };
            var reset = Ui.Text("", F(9), "FgDim");
            var col = Ui.Col(name, reset);
            col.Margin = new Thickness(F(6), 0, F(10), 0);
            row.Children.Add(Ui.Row(ProviderIcons.Create(tool,F(20)),g, col)); // Identify each combined limit with its real logo.
            items.Add((tool, ring, pct, reset));
        }
        Child = row;
        OnClick(() => Flyout.Show(this, AiPanel.Build(), 360));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        foreach (var (tool, ring, pct, reset) in items)
        {
            var u = tool == "Claude" ? AiLimits.Claude : AiLimits.Codex;
            var s = u?.Session;
            ring.Value = (s?.Percent ?? 0) / 100;
            pct.Text = s == null ? "—" : $"{s.Percent:0}";
            reset.Text = s?.ResetsAt != null ? "⟳ " + AiLimits.Until(s.ResetsAt) : "veri yok";
        }
    }
}

static class AiPanel
{
    public static UIElement Build()
    {
        var p = new StackPanel();
        p.Children.Add(Ui.Text("AI kullanım limitleri", 16, "Fg", FontWeights.Bold));
        foreach (var u in new[] { AiLimits.Claude, AiLimits.Codex, AiLimits.Gemini })
        {
            if (u == null) continue;
            var h = Ui.Text($"{u.Tool}  ·  {u.Detail}", 13, "Fg", FontWeights.SemiBold);
            h.Margin = new Thickness(0, 14, 0, 4);
            p.Children.Add(h);
            if (u.Extra.Length > 0) p.Children.Add(Ui.Text(u.Extra, 11.5, "FgDim"));
            var labels = u.Tool == "Gemini" ? (u.L1, u.L2) : ("5 saatlik oturum", "Haftalık");
            foreach (var (label, w) in new[] { (labels.Item1, u.Session), (labels.Item2, u.Week) })
            {
                if (w == null) continue;
                var d = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
                var right = Ui.Text(Localization.F("{0} · {1} sonra sıfırlanır", AiLimits.Pct(w.Percent), AiLimits.Until(w.ResetsAt)), 12, "FgDim");
                DockPanel.SetDock(right, Dock.Right);
                d.Children.Add(right);
                d.Children.Add(Ui.Text(label, 12.5, "Fg"));
                p.Children.Add(d);
                p.Children.Add(new MiniSlider { Interactive = false, Height = 6, Value = w.Percent / 100, FillKey = AiLimits.LevelKey(w.Percent), Margin = new Thickness(0, 4, 0, 2) });
            }
            if (u.Breakdown.Count > 0)
                p.Children.Add(Ui.Text(Localization.T("Bu hafta:") + " " + string.Join(" · ", u.Breakdown.Select(b => $"{b.Name} {AiLimits.Pct(b.Pct)}")), 11.5, "FgDim"));
            var upd = Ui.Text(Localization.F("Son güncelleme: {0:HH:mm}", u.UpdatedAt), 11, "FgDim");
            p.Children.Add(upd);
        }
        if (AiLimits.Tokens is { Messages: > 0 } t)
        {
            var h = Ui.Text("Claude Code · bugün", 13, "Fg", FontWeights.SemiBold);
            h.Margin = new Thickness(0, 16, 0, 4);
            p.Children.Add(h);
            p.Children.Add(Ui.Text(Localization.F("{0} yanıt · {1} çıktı · {2} girdi · {3} önbellek okuma", t.Messages, AiLimits.Num(t.Output), AiLimits.Num(t.Input), AiLimits.Num(t.CacheRead)), 12, "FgDim"));
            foreach (var (m, o) in t.ByModel.OrderByDescending(kv => kv.Value).Take(4))
                p.Children.Add(Ui.Text(Localization.F("{0}: {1} çıktı", AiLimits.Pretty(m), AiLimits.Num(o)), 12, "FgDim"));
        }
        foreach (var (name, days) in new[] { ("Claude Code", AiHistory.Claude), ("Codex", AiHistory.Codex) })
        {
            if (days == null || days.All(d => d == 0)) continue;
            var h = Ui.Text($"{name} · son 7 gün (çıktı token)", 13, "Fg", FontWeights.SemiBold);
            h.Margin = new Thickness(0, 16, 0, 4);
            p.Children.Add(h);
            p.Children.Add(new MiniBars { Height = 54, Values = days.Select(d => (double)d).ToArray() });
            var axis = new UniformGrid { Columns = 7 };
            for (int i = 6; i >= 0; i--)
            {
                var day = DateTime.Today.AddDays(-i);
                var tt = Ui.Text(i == 0 ? "bugün" : day.ToString("ddd", Tr.C), 10, "FgDim");
                tt.HorizontalAlignment = HorizontalAlignment.Center;
                tt.ToolTip = AiLimits.Num(days[6 - i]);
                axis.Children.Add(tt);
            }
            p.Children.Add(axis);
        }
        if (OpenCode.Data is { } oc && oc.Week > 0)
        {
            var h = Ui.Text("OpenCode", 13, "Fg", FontWeights.SemiBold);
            h.Margin = new Thickness(0, 16, 0, 4);
            p.Children.Add(h);
            p.Children.Add(Ui.Text($"Bugün {AiLimits.Num(oc.Today)} · bu hafta {AiLimits.Num(oc.Week)} token · {oc.TopModel}", 12, "FgDim"));
        }
        if (AiLimits.Claude == null && AiLimits.Codex == null)
            p.Children.Add(Ui.Text("Claude Code ya da Codex verisi bulunamadı.", 12, "FgDim"));
        var note = Ui.Text("Veriler bu bilgisayardaki yerel dosyalardan okunur; Claude limitleri Claude Code girişinle yalnızca Anthropic'e sorulur.", 11, "FgDim");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 14, 0, 0);
        p.Children.Add(note);
        return p;
    }
}

// ═════════════════════════ GitHub ═════════════════════════

/// <summary>GitHub CLI (gh) girişinin anahtarıyla bildirimler, PR'lar ve katkı ızgarası. Anahtar yalnızca bellekte tutulur.</summary>
static class GitHub
{
    static string? token;
    static bool tokenTried;

    public record Info(string Login, int Unread, int OpenPrs, int Stars, int[] Days, int Streak, int TotalYear);
    static readonly Cached<Info?> cache = new(TimeSpan.FromMinutes(5), Load);
    public static Info? Data { get { cache.Ensure(); return cache.Value; } }
    public static string Error => cache.Error.Length > 0 ? cache.Error : token == null && tokenTried ? "gh girişi yok" : "";

    static string? Token()
    {
        if (token != null || tokenTried) return token;
        tokenTried = true;
        foreach (var exe in new[] { @"C:\Program Files\GitHub CLI\gh.exe", "gh" })
        {
            try
            {
                var psi = new ProcessStartInfo(exe, "auth token") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi)!;
                var t = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                if (p.ExitCode == 0 && t.Length > 10) { token = t; break; }
            }
            catch { }
        }
        return token;
    }

    static async Task<Info?> Load()
    {
        var t = await Task.Run(Token);
        if (t == null) return null;

        var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.github.com/notifications?per_page=50");
        req.Headers.Authorization = new("Bearer", t);
        var notif = JsonDocument.Parse(await (await Net.Http.SendAsync(req)).Content.ReadAsStringAsync());
        int unread = notif.RootElement.ValueKind == JsonValueKind.Array ? notif.RootElement.GetArrayLength() : 0;

        const string q = "{\"query\":\"query{viewer{login pullRequests(states:OPEN){totalCount} repositories(ownerAffiliations:OWNER,first:100){nodes{stargazerCount}} contributionsCollection{contributionCalendar{totalContributions weeks{contributionDays{contributionCount date}}}}}}\"}";
        var gq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.github.com/graphql") { Content = new System.Net.Http.StringContent(q) };
        gq.Headers.Authorization = new("Bearer", t);
        var g = JsonDocument.Parse(await (await Net.Http.SendAsync(gq)).Content.ReadAsStringAsync());
        var v = g.RootElement.GetProperty("data").GetProperty("viewer");
        var cal = v.GetProperty("contributionsCollection").GetProperty("contributionCalendar");
        var days = cal.GetProperty("weeks").EnumerateArray()
            .SelectMany(w => w.GetProperty("contributionDays").EnumerateArray())
            .Select(d => d.GetProperty("contributionCount").GetInt32()).ToArray();
        int streak = 0;
        for (int i = days.Length - 1; i >= 0; i--)
        {
            if (days[i] > 0) streak++;
            else if (i == days.Length - 1) continue;   // bugün henüz katkı yoksa seriyi bozma
            else break;
        }
        int stars = v.GetProperty("repositories").GetProperty("nodes").EnumerateArray().Sum(n => n.GetProperty("stargazerCount").GetInt32());
        return new Info(v.GetProperty("login").GetString() ?? "", unread, v.GetProperty("pullRequests").GetProperty("totalCount").GetInt32(),
            stars, days, streak, cal.GetProperty("totalContributions").GetInt32());
    }
}

class GitHubWidget : Widget
{
    readonly TextBlock title, sub;
    public GitHubWidget(bool overview = false)
    {
        title = Ui.Text("", F(12), "Fg", FontWeights.Bold);
        sub = Ui.Text("", F(9.5), "FgDim");
        var col = Ui.Col(title, sub);
        col.MinWidth = F(110);
        col.Margin = new Thickness(F(8), 0, 0, 0);
        Child = Ui.Row(Chip("\uE943", Color.FromRgb(0x24, 0x29, 0x2F), H * 0.62), col);
        OnClick(() => { if (overview) Flyout.Show(this, Overview(), 320); else App.Open("https://github.com/notifications"); });
        Tick(DateTime.Now);
    }
    static UIElement Overview()
    {
        var p = new StackPanel();
        var d = GitHub.Data;
        p.Children.Add(Ui.Text(d == null ? "GitHub" : "@" + d.Login, 16, "Fg", FontWeights.Bold));
        if (d == null) p.Children.Add(Ui.Text(GitHub.Error.Length > 0 ? GitHub.Error + " · gh auth login ile bağlanın" : "Yükleniyor; kısa süre sonra tekrar açın.", 12, "FgDim"));
        else
        {
            p.Children.Add(PanelRows.Link($"{d.Unread} okunmamış bildirim", () => App.Open("https://github.com/notifications")));
            p.Children.Add(PanelRows.Link($"{d.OpenPrs} açık pull request", () => App.Open("https://github.com/pulls")));
            p.Children.Add(PanelRows.Link($"{d.Stars} depo yıldızı", () => App.Open("https://github.com/" + d.Login + "?tab=repositories")));
        }
        return p;
    }

    public override void Tick(DateTime now)
    {
        var d = GitHub.Data;
        if (d == null) { title.Text = "GitHub"; sub.Text = GitHub.Error.Length > 0 ? GitHub.Error : "yükleniyor…"; return; }
        title.Text = d.Unread == 0 ? "Bildirim yok" : $"{d.Unread} bildirim";
        sub.Text = $"{d.OpenPrs} açık PR · ★ {d.Stars}";
        ToolTip = "@" + d.Login;
    }
}

class GitHubActivityWidget : Widget
{
    readonly ContributionGrid grid = new();
    readonly TextBlock streak, total;
    public GitHubActivityWidget()
    {
        grid.Height = H - F(14);
        grid.Width = F(130);
        streak = Ui.Text("", F(12), "Fg", FontWeights.Bold);
        total = Ui.Text("", F(9), "FgDim");
        var col = Ui.Col(streak, total);
        col.Margin = new Thickness(F(8), 0, 0, 0);
        Child = Ui.Row(grid, col);
        OnClick(() => { if (GitHub.Data is { } d) App.Open("https://github.com/" + d.Login); });
        Tick(DateTime.Now);
    }
    public override void Tick(DateTime now)
    {
        var d = GitHub.Data;
        if (d == null) { streak.Text = "GitHub"; total.Text = GitHub.Error; return; }
        grid.Days = d.Days;
        streak.Text = $"🔥 {d.Streak} gün";
        total.Text = $"{d.TotalYear} katkı / yıl";
    }
}

/// <summary>Katkı ızgarası: son ~18 hafta, 7 satır.</summary>
class ContributionGrid : FrameworkElement
{
    int[] days = [];
    public int[] Days { get => days; set { days = value; InvalidateVisual(); } }

    protected override void OnRender(DrawingContext dc)
    {
        if (days.Length == 0 || ActualHeight <= 0) return;
        double cell = ActualHeight / 7;
        int weeks = (int)(ActualWidth / cell);
        int start = Math.Max(0, days.Length - weeks * 7);
        // haftanın gününe hizala (GitHub haftaları pazar başlar)
        start -= start % 7;
        int max = Math.Max(1, days.Skip(start).DefaultIfEmpty(1).Max());
        var accent = ((SolidColorBrush)Ui.B("Good")).Color;
        var track = Ui.B("Track");
        for (int i = start; i < days.Length; i++)
        {
            int col = (i - start) / 7, row = (i - start) % 7;
            var r = new Rect(col * cell + 0.75, row * cell + 0.75, cell - 1.5, cell - 1.5);
            Brush b = days[i] == 0 ? track : new SolidColorBrush(Color.FromArgb((byte)(80 + 175 * Math.Min(1, days[i] / (double)max)), accent.R, accent.G, accent.B));
            dc.DrawRoundedRectangle(b, null, r, 1.5, 1.5);
        }
    }
}
