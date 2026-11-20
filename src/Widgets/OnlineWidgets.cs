using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Iskele;

/// <summary>Paylaşılan HTTP istemcisi. Yalnızca dock'ta olan widget'lar istek atar.</summary>
static class Net
{
    public static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Iskele/0.2");
        return c;
    }
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
}

/// <summary>Belirli aralıklarla veri çeken, sonucu önbellekte tutan basit yardımcı.</summary>
class Cached<T>
{
    readonly TimeSpan every;
    readonly Func<Task<T>> fetch;
    DateTime at = DateTime.MinValue;
    bool busy, pendingForce;
    int generation;
    public T? Value { get; private set; }
    public string Error { get; private set; } = "";
    internal Task? Pending { get; private set; }
    public Cached(TimeSpan every, Func<Task<T>> fetch) { this.every = every; this.fetch = fetch; }
    // In-flight results from an obsolete source must never repopulate the cache.
    public void Invalidate()
    {
        generation++; Value = default; Error = ""; at = DateTime.MinValue;
        if (busy) pendingForce = true;
    }
    public void Ensure(bool force = false)
    {
        if (busy) { if (force) pendingForce = true; return; }
        if (!force && DateTime.Now - at < every) return;
        busy = true; at = DateTime.Now;
        Pending = Run(generation);
    }
    async Task Run(int requestGeneration)
    {
        try
        {
            var value = await fetch();
            if (requestGeneration == generation) { Value = value; Error = ""; }
        }
        catch (Exception ex)
        {
            if (requestGeneration == generation) { Error = "Bağlantı yok"; App.Log(ex.Message); at = DateTime.Now - every + TimeSpan.FromMinutes(1); }
        }
        finally
        {
            busy = false;
            if (pendingForce) { pendingForce = false; Ensure(force: true); }
        }
    }
}

// ═════════════════════════ Hava durumu çeşitleri ═════════════════════════

abstract class WeatherBase : Widget
{
    protected static TextBlock Icon(double size)
    {
        var t = new TextBlock { FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        return t;
    }
    protected WeatherBase() { OnClick(() => Flyout.Show(this, WeatherWidget.Details(), 340)); }
    public override void Tick(DateTime now) { WeatherService.Ensure(); Paint(now); }
    protected abstract void Paint(DateTime now);
}

class WeatherDetailsWidget : WeatherBase
{
    readonly TextBlock a, b;
    public WeatherDetailsWidget()
    {
        a = Ui.Text("", F(11), "Fg", FontWeights.SemiBold);
        b = Ui.Text("", F(11), "FgDim", FontWeights.SemiBold);
        Child = Ui.Col(a, b);
        Tick(DateTime.Now);
    }
    protected override void Paint(DateTime now)
    {
        if (WeatherService.Temp == null) { a.Text = WeatherService.Error.Length > 0 ? WeatherService.Error : "Yükleniyor…"; return; }
        a.Text = $"💨 {WeatherService.Wind:0} km/sa   💧 %{WeatherService.Humidity:0}";
        b.Text = $"Yağış %{WeatherService.RainChance ?? 0} · {WeatherService.Low:0}°/{WeatherService.High:0}° · his {WeatherService.Feels:0}°";
    }
}

class ForecastWidget : WeatherBase
{
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    public ForecastWidget() { Child = row; Tick(DateTime.Now); }
    protected override void Paint(DateTime now)
    {
        row.Children.Clear();
        foreach (var (day, code, hi, lo) in WeatherService.Days.Take(4))
        {
            var name = Ui.Text(day.Date == now.Date ? "Bugün" : day.ToString("ddd", Tr.C), F(8.5), "FgDim", FontWeights.Bold);
            var ic = Icon(F(13));
            ic.Text = WeatherService.Describe(code).Icon;
            var t = Ui.Text($"{hi:0}°", F(10.5), "Fg", FontWeights.Bold);
            name.HorizontalAlignment = ic.HorizontalAlignment = t.HorizontalAlignment = HorizontalAlignment.Center;
            var col = Ui.Col(name, ic, t);
            col.Width = F(36);
            row.Children.Add(col);
        }
        if (row.Children.Count == 0) row.Children.Add(Ui.Text("Yükleniyor…", F(11), "FgDim"));
    }
}

class HourlyWidget : WeatherBase
{
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    public HourlyWidget() { Child = row; Tick(DateTime.Now); }
    protected override void Paint(DateTime now)
    {
        row.Children.Clear();
        foreach (var (time, temp, code) in WeatherService.Hours.Where(h => h.Time >= now.AddHours(-1)).Take(6))
        {
            var name = Ui.Text(time.ToString("HH"), F(8.5), "FgDim", FontWeights.Bold);
            var ic = Icon(F(12));
            ic.Text = WeatherService.Describe(code).Icon;
            var t = Ui.Text($"{temp:0}°", F(10.5), "Fg", FontWeights.Bold);
            name.HorizontalAlignment = ic.HorizontalAlignment = t.HorizontalAlignment = HorizontalAlignment.Center;
            var col = Ui.Col(name, ic, t);
            col.Width = F(30);
            row.Children.Add(col);
        }
        if (row.Children.Count == 0) row.Children.Add(Ui.Text("Yükleniyor…", F(11), "FgDim"));
    }
}

class SunWidget : WeatherBase
{
    readonly TextBlock rise, set;
    public SunWidget()
    {
        rise = Ui.Text("", F(12), "Fg", FontWeights.Bold);
        set = Ui.Text("", F(12), "Fg", FontWeights.Bold);
        var r = Ui.Row(Ui.Text("🌅", F(14)), Pad(rise));
        var s = Ui.Row(Ui.Text("🌇", F(14)), Pad(set));
        Child = Ui.Col(r, s);
        Tick(DateTime.Now);
    }
    static FrameworkElement Pad(FrameworkElement e) { e.Margin = new Thickness(6, 0, 0, 0); return e; }
    protected override void Paint(DateTime now)
    {
        rise.Text = WeatherService.Sunrise?.ToString("HH:mm") ?? "—";
        set.Text = WeatherService.Sunset?.ToString("HH:mm") ?? "—";
    }
}

class WeatherStatusWidget : WeatherBase
{
    readonly TextBlock text, place;
    public WeatherStatusWidget()
    {
        text = Ui.Text("", F(13), "Fg", FontWeights.Bold);
        place = Ui.Text("", F(9.5), "FgDim");
        Child = Ui.Col(text, place);
        Tick(DateTime.Now);
    }
    protected override void Paint(DateTime now)
    {
        text.Text = WeatherService.Temp is double t ? $"{WeatherService.Describe(WeatherService.Code).Text}, {t:0}°" : "Yükleniyor…";
        place.Text = WeatherService.Place.Length > 0 ? WeatherService.Place : App.Settings.WeatherCity;
    }
}

// ═════════════════════════ Döviz ═════════════════════════

class CurrencyWidget : Widget
{
    static readonly Dictionary<string, Cached<Dictionary<string, double>>> rates = [];
    readonly TextBlock amount, result, from, to;

    public CurrencyWidget()
    {
        amount = Ui.Text("", F(10), "FgDim", FontWeights.SemiBold);
        result = Ui.Text("", F(15), "Fg", FontWeights.Bold);
        from = Pill();
        to = Pill();
        var swap = Ui.Glyph("", F(10), "FgDim");
        var codes = Ui.Col(Wrap(from), swap, Wrap(to));
        codes.Margin = new Thickness(F(10), 0, 0, 0);
        var col = Ui.Col(amount, result);
        col.MinWidth = F(80);
        Child = Ui.Row(col, codes);
        Padding = new Thickness(F(10), F(2), F(8), F(2));
        OnClick(Edit);
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            (App.Settings.CurrencyFrom, App.Settings.CurrencyTo) = (App.Settings.CurrencyTo, App.Settings.CurrencyFrom);
            App.Settings.Save();
            Tick(DateTime.Now);
        };
        ToolTip = "Tıkla: tutar ve para birimi · sağ tık: yer değiştir";
        Tick(DateTime.Now);
    }

    static TextBlock Pill() => Ui.Text("", F(8.5), "Fg", FontWeights.Bold);
    static Border Wrap(TextBlock t)
    {
        var b = new Border { Child = t, CornerRadius = new CornerRadius(F(6)), Padding = new Thickness(F(5), 0, F(5), 0), HorizontalAlignment = HorizontalAlignment.Center };
        b.SetResourceReference(BackgroundProperty, "Track");
        return b;
    }

    static Cached<Dictionary<string, double>> Rates(string baseCode)
    {
        if (!rates.TryGetValue(baseCode, out var c))
        {
            c = new Cached<Dictionary<string, double>>(TimeSpan.FromHours(1), async () =>
            {
                var doc = JsonDocument.Parse(await Net.Http.GetStringAsync($"https://open.er-api.com/v6/latest/{baseCode}"));
                return doc.RootElement.GetProperty("rates").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
            });
            rates[baseCode] = c;
        }
        return c;
    }

    public override void Tick(DateTime now)
    {
        var s = App.Settings;
        var r = Rates(s.CurrencyFrom);
        r.Ensure();
        from.Text = s.CurrencyFrom;
        to.Text = s.CurrencyTo;
        amount.Text = s.CurrencyAmount.ToString("N2", Tr.C) + " " + s.CurrencyFrom;
        result.Text = r.Value != null && r.Value.TryGetValue(s.CurrencyTo, out var rate)
            ? (s.CurrencyAmount * rate).ToString("N2", Tr.C)
            : (r.Error.Length > 0 ? r.Error : "…");
    }

    void Edit()
    {
        var s = App.Settings;
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("Döviz çevirici", 16, "Fg", FontWeights.Bold));
        var amt = TodosWidget.Input("Tutar");
        amt.Box.Text = s.CurrencyAmount.ToString(Tr.C);
        var f = TodosWidget.Input("Kaynak (ör. USD)");
        f.Box.Text = s.CurrencyFrom;
        var t = TodosWidget.Input("Hedef (ör. TRY)");
        t.Box.Text = s.CurrencyTo;
        foreach (var x in new[] { amt.Host, f.Host, t.Host }) { x.Margin = new Thickness(0, 8, 0, 0); panel.Children.Add(x); }
        var note = Ui.Text("Kurlar: open.er-api.com (saatte bir)", 11, "FgDim");
        note.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(note);
        var fl = Flyout.Show(this, panel, 300);
        if (fl != null) fl.Closing2 += () =>
        {
            if (double.TryParse(amt.Box.Text, NumberStyles.Any, Tr.C, out var a)) s.CurrencyAmount = a;
            if (f.Box.Text.Trim().Length == 3) s.CurrencyFrom = f.Box.Text.Trim().ToUpperInvariant();
            if (t.Box.Text.Trim().Length == 3) s.CurrencyTo = t.Box.Text.Trim().ToUpperInvariant();
            s.Save();
            Tick(DateTime.Now);
        };
        amt.Box.Focus();
        amt.Box.SelectAll();
    }
}

// ═════════════════════════ Kripto / borsa ═════════════════════════

record Quote(string Symbol, double Price, double ChangePct, double[] Series);

static class Markets
{
    static readonly Dictionary<string, Cached<Quote>> cache = [];

    public static Cached<Quote> Crypto(string pair) => Get("c:" + pair, TimeSpan.FromSeconds(30), async () =>
    {
        var doc = JsonDocument.Parse(await Net.Http.GetStringAsync($"https://api.binance.com/api/v3/ticker/24hr?symbol={pair}"));
        var r = doc.RootElement;
        double price = double.Parse(r.GetProperty("lastPrice").GetString()!, Net.Inv);
        double pct = double.Parse(r.GetProperty("priceChangePercent").GetString()!, Net.Inv);
        return new Quote(pair, price, pct, []);
    });

    public static Cached<Quote> Stock(string symbol) => Get("s:" + symbol, TimeSpan.FromMinutes(1), async () =>
    {
        var doc = JsonDocument.Parse(await Net.Http.GetStringAsync(
            $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=5m"));
        var res = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
        var meta = res.GetProperty("meta");
        double price = meta.GetProperty("regularMarketPrice").GetDouble();
        double prev = meta.TryGetProperty("chartPreviousClose", out var pc) ? pc.GetDouble() : price;
        double[] series = [];
        try
        {
            series = res.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close").EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetDouble()).ToArray();
        }
        catch { }
        return new Quote(symbol, price, prev == 0 ? 0 : (price - prev) / prev * 100, series);
    });

    static Cached<Quote> Get(string key, TimeSpan every, Func<Task<Quote>> f)
    {
        if (!cache.TryGetValue(key, out var c)) cache[key] = c = new Cached<Quote>(every, f);
        return c;
    }

    public static string Price(double p) => p >= 1000 ? p.ToString("N0", Tr.C) : p >= 1 ? p.ToString("N2", Tr.C) : p.ToString("0.####", Tr.C);
}

/// <summary>Birkaç sembolü alt alta gösteren liste (kripto ya da borsa).</summary>
class QuoteListWidget : Widget
{
    readonly bool crypto;
    readonly StackPanel list = new() { VerticalAlignment = VerticalAlignment.Center };

    public QuoteListWidget(bool crypto)
    {
        this.crypto = crypto;
        list.Width = F(150);
        Child = list;
        ToolTip = crypto ? "Binance · semboller Ayarlar'dan" : "Yahoo Finance · semboller Ayarlar'dan";
        OnClick(SettingsWindow.Open);
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        var symbols = (crypto ? App.Settings.Crypto : App.Settings.Stocks).Take(3).ToList();
        list.Children.Clear();
        foreach (var sym in symbols)
        {
            var c = crypto ? Markets.Crypto(sym) : Markets.Stock(sym);
            c.Ensure();
            var name = crypto ? sym.Replace("USDT", "") : sym.Replace(".IS", "");
            var row = new DockPanel();
            if (c.Value is Quote q)
            {
                var pct = Ui.Text($"{(q.ChangePct >= 0 ? "▲" : "▼")}{Math.Abs(q.ChangePct):0.0}%", F(9), q.ChangePct >= 0 ? "Good" : "Warn", FontWeights.Bold);
                pct.Width = F(44);
                pct.TextAlignment = TextAlignment.Right;
                DockPanel.SetDock(pct, Dock.Right);
                row.Children.Add(pct);
                var price = Ui.Text(Markets.Price(q.Price), F(9.5), "Fg", FontWeights.SemiBold);
                DockPanel.SetDock(price, Dock.Right);
                row.Children.Add(price);
            }
            else
            {
                var e = Ui.Text(c.Error.Length > 0 ? c.Error : "…", F(9), "FgDim");
                DockPanel.SetDock(e, Dock.Right);
                row.Children.Add(e);
            }
            row.Children.Add(Ui.Text(name, F(9.5), "FgDim", FontWeights.Bold));
            list.Children.Add(row);
        }
        if (symbols.Count == 0) list.Children.Add(Ui.Text("Ayarlar'dan sembol ekle", F(10), "FgDim"));
    }
}

/// <summary>Tek sembol, gün içi çizgi grafik.</summary>
class StockChartWidget : Widget
{
    readonly TextBlock sym, price, pct;
    readonly SeriesChart chart = new();

    public StockChartWidget()
    {
        sym = Ui.Text("", F(9.5), "FgDim", FontWeights.Bold);
        price = Ui.Text("", F(14), "Fg", FontWeights.Bold);
        pct = Ui.Text("", F(9.5), "Good", FontWeights.Bold);
        var col = Ui.Col(sym, price, pct);
        col.MinWidth = F(70);
        chart.Width = F(70);
        chart.Height = H * 0.55;
        chart.Margin = new Thickness(F(8), 0, 0, 0);
        Child = Ui.Row(col, chart);
        OnClick(SettingsWindow.Open);
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        var s = App.Settings.Stocks.FirstOrDefault();
        if (s == null) { sym.Text = "Sembol yok"; return; }
        var c = Markets.Stock(s);
        c.Ensure();
        sym.Text = s;
        if (c.Value is Quote q)
        {
            price.Text = Markets.Price(q.Price);
            pct.Text = $"{(q.ChangePct >= 0 ? "+" : "")}{q.ChangePct:0.00}%";
            pct.SetResourceReference(TextBlock.ForegroundProperty, q.ChangePct >= 0 ? "Good" : "Warn");
            chart.BrushKey = q.ChangePct >= 0 ? "Good" : "Warn";
            chart.Values = q.Series;
        }
        else price.Text = c.Error.Length > 0 ? c.Error : "…";
    }
}

/// <summary>Sabit bir seriden çizgi grafik.</summary>
class SeriesChart : FrameworkElement
{
    double[] values = [];
    public string BrushKey { get; set; } = "Accent";
    public double[] Values { get => values; set { values = value; InvalidateVisual(); } }

    protected override void OnRender(DrawingContext dc)
    {
        if (values.Length < 2 || ActualWidth <= 0) return;
        double min = values.Min(), max = values.Max(), span = Math.Max(max - min, 1e-9);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int i = 0; i < values.Length; i++)
            {
                var p = new Point(i * ActualWidth / (values.Length - 1), ActualHeight - 1 - (values[i] - min) / span * (ActualHeight - 2));
                if (i == 0) c.BeginFigure(p, false, false); else c.LineTo(p, true, true);
            }
        }
        dc.DrawGeometry(null, new Pen(Ui.B(BrushKey), 1.5) { LineJoin = PenLineJoin.Round }, g);
    }
}

// ═════════════════════════ Takvim (iCal) ═════════════════════════

record CalEvent(DateTime Start, DateTime End, string Title, bool AllDay);

/// <summary>
/// Google / Outlook / iCloud takvimlerinin "gizli iCal adresi"nden etkinlikleri okur.
/// Hesap bağlantısı gerekmez; adres Ayarlar'a yapıştırılır. Tekrarlayan etkinlikler
/// (DAILY/WEEKLY/MONTHLY/YEARLY, INTERVAL, COUNT, UNTIL, BYDAY) önümüzdeki 60 gün için açılır.
/// </summary>
static class CalendarService
{
    static readonly Cached<List<CalEvent>> cache = new(TimeSpan.FromMinutes(15), Load);
    static string loadedFor = "";
    public static List<CalEvent> Events { get { Ensure(); return cache.Value ?? []; } }
    public static string Error => cache.Error;

    public static void Ensure()
    {
        var key = string.Join(" ", App.Settings.IcalUrls);
        if (key != loadedFor)
        {
            loadedFor = key;
            cache.Invalidate();
        }
        if (key.Length > 0) cache.Ensure();
    }

    static async Task<List<CalEvent>> Load()
    {
        var all = new List<CalEvent>();
        foreach (var url in App.Settings.IcalUrls.ToArray())
        {
            var u = url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[9..] : url;
            all.AddRange(Parse(await Net.Http.GetStringAsync(u)));
        }
        return all.OrderBy(e => e.Start).ToList();
    }

    internal static List<CalEvent> Parse(string ics)
    {
        var lines = ics.Replace("\r\n ", "").Replace("\r\n\t", "").Replace("\n ", "").Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var result = new List<CalEvent>();
        var from = DateTime.Today.AddDays(-1);
        var until = DateTime.Today.AddDays(60);
        Dictionary<string, string>? ev = null;
        foreach (var line in lines)
        {
            if (line == "BEGIN:VEVENT") { ev = []; continue; }
            if (line == "END:VEVENT" && ev != null)
            {
                try { Expand(ev, from, until, result); } catch { }
                ev = null;
                continue;
            }
            if (ev == null) continue;
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon];
            var name = key.Split(';')[0];
            // EXDATE is multi-valued across lines and within a line.
            if (name == "EXDATE") name += ":" + ev.Count;
            ev[name] = line[(colon + 1)..];
            ev[name + "#params"] = key;
        }
        return result;
    }

    static (DateTime When, bool AllDay) ParseDate(string value, string param)
    {
        bool utc = value.EndsWith('Z');
        value = value.TrimEnd('Z');
        if (value.Length == 8)
            return (DateTime.ParseExact(value, "yyyyMMdd", Net.Inv), true);
        var dt = DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss", Net.Inv);
        if (utc) return (DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(), false);
        var tzm = System.Text.RegularExpressions.Regex.Match(param, "TZID=([^;:]+)");
        if (tzm.Success)
        {
            try
            {
                var id = tzm.Groups[1].Value.Trim('"');
                TimeZoneInfo tz = TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win) ? TimeZoneInfo.FindSystemTimeZoneById(win) : TimeZoneInfo.FindSystemTimeZoneById(id);
                return (TimeZoneInfo.ConvertTime(dt, tz, TimeZoneInfo.Local), false);
            }
            catch { }
        }
        return (dt, false);
    }

    static void Expand(Dictionary<string, string> ev, DateTime from, DateTime until, List<CalEvent> result)
    {
        if (!ev.TryGetValue("DTSTART", out var ds)) return;
        var (start, allDay) = ParseDate(ds, ev.GetValueOrDefault("DTSTART#params", ""));
        var end = ev.TryGetValue("DTEND", out var de) ? ParseDate(de, ev.GetValueOrDefault("DTEND#params", "")).When : start.AddHours(allDay ? 24 : 1);
        var dur = end - start;
        var title = (ev.GetValueOrDefault("SUMMARY") ?? "(başlıksız)").Replace("\\,", ",").Replace("\\;", ";").Replace("\\n", " ");

        if (!ev.TryGetValue("RRULE", out var rr))
        {
            if (end >= from && start <= until) result.Add(new CalEvent(start, end, title, allDay));
            return;
        }

        var rule = rr.Split(';').Select(p => p.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        string freq = rule.GetValueOrDefault("FREQ", "");
        int interval = int.TryParse(rule.GetValueOrDefault("INTERVAL"), out var iv) ? Math.Max(1, iv) : 1;
        int count = int.TryParse(rule.GetValueOrDefault("COUNT"), out var cn) ? cn : int.MaxValue;
        DateTime last = rule.TryGetValue("UNTIL", out var un) ? ParseDate(un, "").When : DateTime.MaxValue;
        var byDay = rule.TryGetValue("BYDAY", out var bd)
            ? bd.Split(',').Select(d => d[^2..] switch { "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday }).ToHashSet()
            : null;
        var exdates = ev.Where(kv => kv.Key.StartsWith("EXDATE:", StringComparison.Ordinal) && !kv.Key.EndsWith("#params", StringComparison.Ordinal))
            .SelectMany(kv => kv.Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(value =>
                ParseDate(value, ev.GetValueOrDefault(kv.Key + "#params", "")).When)).ToHashSet();

        int produced = 0;
        var cur = start;
        for (int guard = 0; guard < 4000 && cur <= until && cur <= last && produced < count; guard++)
        {
            if (freq == "WEEKLY" && byDay != null)
            {
                // haftanın günlerini tek tek dolaş
                var weekStart = cur.Date.AddDays(-(((int)cur.DayOfWeek + 6) % 7));
                for (int d = 0; d < 7 && produced < count; d++)
                {
                    var day = weekStart.AddDays(d) + start.TimeOfDay;
                    if (!byDay.Contains(day.DayOfWeek) || day < start || day > last || day > until) continue;
                    produced++;
                    if (day + dur >= from && !exdates.Contains(allDay ? day.Date : day)) result.Add(new CalEvent(day, day + dur, title, allDay));
                }
                cur = weekStart.AddDays(7 * interval) + start.TimeOfDay;
                continue;
            }
            produced++;
            if (cur + dur >= from && !exdates.Contains(allDay ? cur.Date : cur)) result.Add(new CalEvent(cur, cur + dur, title, allDay));
            cur = freq switch
            {
                "DAILY" => cur.AddDays(interval),
                "WEEKLY" => cur.AddDays(7 * interval),
                "MONTHLY" => cur.AddMonths(interval),
                "YEARLY" => cur.AddYears(interval),
                _ => DateTime.MaxValue,
            };
            if (cur == DateTime.MaxValue) break;
        }
    }

    public static CalEvent? Next(DateTime now) =>
        Events.FirstOrDefault(e => e.End > now && (!e.AllDay || e.Start.Date >= now.Date));
}

class CalendarWidget : Widget
{
    readonly TextBlock dayName, dayNum, title, when;

    public CalendarWidget()
    {
        dayName = new TextBlock { FontFamily = Ui.Font, FontSize = F(8.5), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)), HorizontalAlignment = HorizontalAlignment.Center };
        dayNum = Ui.Text("", F(16), "Fg", FontWeights.Bold);
        dayNum.HorizontalAlignment = HorizontalAlignment.Center;
        var page = new Border { Width = H * 0.78, Child = Ui.Col(dayName, dayNum), CornerRadius = new CornerRadius(F(8)) };
        page.SetResourceReference(BackgroundProperty, "Track");
        title = Ui.Text("", F(11.5), "Fg", FontWeights.Bold);
        when = Ui.Text("", F(9.5), "FgDim");
        var col = Ui.Col(title, when);
        col.Width = F(140);
        col.Margin = new Thickness(F(9), 0, 0, 0);
        Padding = new Thickness(F(5), F(4), F(10), F(4));
        Child = Ui.Row(page, col);
        OnClick(() => Flyout.Show(this, Agenda(), 340));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        CalendarService.Ensure();
        dayName.Text = now.ToString("ddd", Tr.C).ToUpper(Tr.C);
        dayNum.Text = now.Day.ToString();
        if (App.Settings.IcalUrls.Count == 0) { title.Text = "Takvim bağla"; when.Text = "Ayarlar → iCal adresi"; return; }
        var next = CalendarService.Next(now);
        if (next == null) { title.Text = "Yaklaşan etkinlik yok"; when.Text = CalendarService.Error; return; }
        title.Text = next.Title;
        when.Text = Relative(next, now);
    }

    static string Relative(CalEvent e, DateTime now)
    {
        if (e.Start <= now) return "şimdi · " + (e.AllDay ? "tüm gün" : $"{e.End:HH:mm}'e kadar");
        var d = e.Start - now;
        if (e.Start.Date == now.Date) return d.TotalMinutes < 60 ? $"{(int)d.TotalMinutes} dk sonra" : $"bugün {e.Start:HH:mm}";
        if (e.Start.Date == now.Date.AddDays(1)) return e.AllDay ? "yarın" : $"yarın {e.Start:HH:mm}";
        return e.Start.ToString(e.AllDay ? "d MMM ddd" : "d MMM ddd HH:mm", Tr.C);
    }

    static UIElement Agenda()
    {
        var p = new StackPanel();
        p.Children.Add(new MonthCalendar());
        var head = Ui.Text("Yaklaşanlar", 13, "FgDim", FontWeights.SemiBold);
        head.Margin = new Thickness(0, 12, 0, 4);
        p.Children.Add(head);
        var now = DateTime.Now;
        var list = CalendarService.Events.Where(e => e.End > now).Take(8).ToList();
        foreach (var e in list)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var t = Ui.Text(Relative(e, now), 11.5, "FgDim");
            DockPanel.SetDock(t, Dock.Right);
            row.Children.Add(t);
            var bar = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 8, 0) };
            bar.SetResourceReference(Border.BackgroundProperty, "Accent");
            row.Children.Add(bar);
            row.Children.Add(Ui.RawText(e.Title, 13, "Fg"));
            p.Children.Add(row);
        }
        if (list.Count == 0)
            p.Children.Add(Ui.Text(App.Settings.IcalUrls.Count == 0 ? "Ayarlar'a takviminin gizli iCal adresini yapıştır." : "Yaklaşan etkinlik yok.", 12, "FgDim"));
        return p;
    }
}
