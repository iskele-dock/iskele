using System.Net.Http;
using System.Text.Json;

namespace Iskele;

/// <summary>Open‑Meteo'dan (anahtarsız, ücretsiz) hava durumu. Yalnızca widget dock'taysa istek atılır.</summary>
static class WeatherService
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static DateTime fetched = DateTime.MinValue;
    static string fetchedCity = "";
    static bool busy;

    public static double? Temp { get; private set; }
    public static double? High { get; private set; }
    public static double? Low { get; private set; }
    public static int Code { get; private set; }
    public static string Place { get; private set; } = "";
    public static string Error { get; private set; } = "";
    public static (DateTime Day, int Code, double Hi, double Lo)[] Days { get; private set; } = [];
    public static (DateTime Time, double Temp, int Code)[] Hours { get; private set; } = [];
    public static double? Wind { get; private set; }
    public static double? Humidity { get; private set; }
    public static double? Feels { get; private set; }
    public static int? RainChance { get; private set; }
    public static DateTime? Sunrise { get; private set; }
    public static DateTime? Sunset { get; private set; }

    public static void Ensure()
    {
        var city = App.Settings.WeatherCity.Trim();
        if (busy || (city == fetchedCity && DateTime.Now - fetched < TimeSpan.FromMinutes(15))) return;
        _ = Fetch(city);
    }

    static async Task Fetch(string city)
    {
        busy = true;
        try
        {
            var geo = JsonDocument.Parse(await http.GetStringAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?count=1&language=tr&name={Uri.EscapeDataString(city)}"));
            if (!geo.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0)
            {
                Error = $"'{city}' bulunamadı";
                fetchedCity = city;
                fetched = DateTime.Now;
                return;
            }
            var r = res[0];
            double lat = r.GetProperty("latitude").GetDouble(), lon = r.GetProperty("longitude").GetDouble();
            Place = r.GetProperty("name").GetString() ?? city;

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var wx = JsonDocument.Parse(await http.GetStringAsync(
                $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(inv)}&longitude={lon.ToString(inv)}" +
                "&current=temperature_2m,weather_code,wind_speed_10m,relative_humidity_2m,apparent_temperature" +
                "&hourly=temperature_2m,weather_code&forecast_hours=12" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max&timezone=auto&forecast_days=4"));
            var cur = wx.RootElement.GetProperty("current");
            Temp = cur.GetProperty("temperature_2m").GetDouble();
            Code = cur.GetProperty("weather_code").GetInt32();
            Wind = cur.GetProperty("wind_speed_10m").GetDouble();
            Humidity = cur.GetProperty("relative_humidity_2m").GetDouble();
            Feels = cur.GetProperty("apparent_temperature").GetDouble();
            var hourly = wx.RootElement.GetProperty("hourly");
            var ht = hourly.GetProperty("time").EnumerateArray().Select(t => DateTime.Parse(t.GetString()!, inv)).ToArray();
            var htemp = hourly.GetProperty("temperature_2m").EnumerateArray().Select(t => t.GetDouble()).ToArray();
            var hcode = hourly.GetProperty("weather_code").EnumerateArray().Select(t => t.GetInt32()).ToArray();
            Hours = ht.Select((t, i) => (t, htemp[i], hcode[i])).ToArray();
            var daily = wx.RootElement.GetProperty("daily");
            Sunrise = DateTime.Parse(daily.GetProperty("sunrise")[0].GetString()!, inv);
            Sunset = DateTime.Parse(daily.GetProperty("sunset")[0].GetString()!, inv);
            var rain = daily.GetProperty("precipitation_probability_max")[0];
            RainChance = rain.ValueKind == JsonValueKind.Number ? rain.GetInt32() : null;
            var times = daily.GetProperty("time").EnumerateArray().Select(t => DateTime.Parse(t.GetString()!, inv)).ToArray();
            var codes = daily.GetProperty("weather_code").EnumerateArray().Select(t => t.GetInt32()).ToArray();
            var his = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(t => t.GetDouble()).ToArray();
            var los = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(t => t.GetDouble()).ToArray();
            Days = times.Select((t, i) => (t, codes[i], his[i], los[i])).ToArray();
            High = his.FirstOrDefault();
            Low = los.FirstOrDefault();
            Error = "";
            fetchedCity = city;
            fetched = DateTime.Now;
        }
        catch (Exception ex)
        {
            Error = "Bağlantı yok";
            fetched = DateTime.Now.AddMinutes(-13);   // 2 dk sonra yeniden dene
            fetchedCity = city;
            App.Log(ex.Message);
        }
        finally { busy = false; }
    }

    static readonly Dictionary<string, string> ConditionsEn = new()
    {
        ["Açık"] = "Clear", ["Az bulutlu"] = "Mostly clear", ["Parçalı bulutlu"] = "Partly cloudy", ["Kapalı"] = "Overcast",
        ["Sisli"] = "Foggy", ["Çiseliyor"] = "Drizzle", ["Yağmurlu"] = "Rain", ["Karlı"] = "Snow", ["Sağanak"] = "Showers",
        ["Kar sağanağı"] = "Snow showers", ["Fırtına"] = "Storm",
    };
    // "Açık"/"Kapalı" genel sözlükte Aç/Kapat anlamında; hava durumu kendi tablosunu kullanır.
    public static (string Icon, string Text) Describe(int code)
    {
        var (icon, text) = DescribeTr(code);
        return (icon, Localization.En && ConditionsEn.TryGetValue(text, out var en) ? en : text);
    }

    static (string Icon, string Text) DescribeTr(int code) => code switch
    {
        0 => ("☀", "Açık"),
        1 => ("🌤", "Az bulutlu"),
        2 => ("⛅", "Parçalı bulutlu"),
        3 => ("☁", "Kapalı"),
        45 or 48 => ("🌫", "Sisli"),
        >= 51 and <= 57 => ("🌦", "Çiseliyor"),
        >= 61 and <= 67 => ("🌧", "Yağmurlu"),
        >= 71 and <= 77 => ("❄", "Karlı"),
        >= 80 and <= 82 => ("🌧", "Sağanak"),
        85 or 86 => ("🌨", "Kar sağanağı"),
        >= 95 => ("⛈", "Fırtına"),
        _ => ("☁", "—"),
    };
}

class WeatherWidget : Widget
{
    readonly TextBlock icon, temp, status, range;

    public WeatherWidget()
    {
        icon = new TextBlock { FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = F(22), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, F(8), 0) };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        temp = Ui.Text("—", F(18), "Fg", FontWeights.Bold);
        temp.Margin = new Thickness(0, 0, F(10), 0);
        status = Ui.Text("", F(10.5), "Fg", FontWeights.SemiBold);
        range = Ui.Text("", F(9.5), "FgDim");
        var col = Ui.Col(status, range);
        col.MaxWidth = F(110);
        Child = Ui.Row(icon, temp, col);
        OnClick(ShowDetails);
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        WeatherService.Ensure();
        if (WeatherService.Temp is double t)
        {
            var (ic, text) = WeatherService.Describe(WeatherService.Code);
            icon.Text = ic;
            temp.Text = $"{t:0}°";
            status.Text = text;
            range.Text = Localization.F("Y:{0:0}° D:{1:0}°", WeatherService.High, WeatherService.Low);
            ToolTip = $"{WeatherService.Place}: {t:0}°, {text}";
        }
        else
        {
            icon.Text = "☁";
            status.Text = WeatherService.Error.Length > 0 ? WeatherService.Error : "Yükleniyor…";
            range.Text = App.Settings.WeatherCity;
        }
    }

    void ShowDetails() => Flyout.Show(this, Details(), 340);

    public static UIElement Details()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text(WeatherService.Place.Length > 0 ? WeatherService.Place : App.Settings.WeatherCity, 16, "Fg", FontWeights.Bold));
        if (WeatherService.Temp is double t)
            panel.Children.Add(Ui.Text($"{t:0}° · {WeatherService.Describe(WeatherService.Code).Text}", 13, "FgDim"));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = Math.Max(1, WeatherService.Days.Length), Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (day, code, hi, lo) in WeatherService.Days)
        {
            var name = Ui.Text(day.Date == DateTime.Today ? "Bugün" : day.ToString("ddd", Tr.C), 12, "FgDim", FontWeights.SemiBold);
            var ic = new TextBlock { Text = WeatherService.Describe(code).Icon, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
            ic.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            var hl = Ui.Text($"{hi:0}° / {lo:0}°", 12, "Fg", FontWeights.SemiBold);
            name.HorizontalAlignment = hl.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(Ui.Col(name, ic, hl));
        }
        panel.Children.Add(grid);

        if (WeatherService.Hours.Length > 0)
        {
            var hours = new UniformGrid { Columns = 6, Margin = new Thickness(0, 14, 0, 0) };
            foreach (var (time, temp, code) in WeatherService.Hours.Where(h => h.Time >= DateTime.Now.AddHours(-1)).Take(6))
            {
                var h = Ui.Text(time.ToString("HH:mm"), 11, "FgDim");
                var ic = new TextBlock { Text = WeatherService.Describe(code).Icon, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center };
                ic.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                var tt = Ui.Text($"{temp:0}°", 12, "Fg", FontWeights.SemiBold);
                h.HorizontalAlignment = tt.HorizontalAlignment = HorizontalAlignment.Center;
                hours.Children.Add(Ui.Col(h, ic, tt));
            }
            panel.Children.Add(hours);
        }
        if (WeatherService.Wind != null)
        {
            var extra = Ui.Text(Localization.F("Rüzgâr {0:0} km/sa · Nem %{1:0} · Hissedilen {2:0}° · Gün doğumu {3:HH:mm} · batımı {4:HH:mm}",
                WeatherService.Wind, WeatherService.Humidity, WeatherService.Feels, WeatherService.Sunrise, WeatherService.Sunset), 11.5, "FgDim");
            extra.TextWrapping = TextWrapping.Wrap;
            extra.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(extra);
        }
        var note = Ui.Text("Şehri Ayarlar'dan değiştirebilirsin · Open‑Meteo", 11, "FgDim");
        note.Margin = new Thickness(0, 12, 0, 0);
        panel.Children.Add(note);
        return panel;
    }
}
