using System.Text.Json;

namespace Iskele;

/// <summary>Kullanıcı ayarları; %AppData%\Iskele\settings.json içinde tutulur.</summary>
partial class Settings
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; } // Preserve fields from newer clients.
    public string Edge { get; set; } = "bottom";      // bottom | top
    public string Theme { get; set; } = "midnight";   // black | white | midnight | glass
    public string Size { get; set; } = "M";           // S | M | L
    // Approved HLSL preset; hover dwell defaults to an integer 120 ms, minimum 90 ms.
    public double GlassRefraction { get; set; } = 8;
    public double GlassBlur { get; set; } = 6;
    public double GlassTint { get; set; } = 0.12;
    public double GlassHighlight { get; set; } = 0.65;
    public double HoverResponseMs { get; set; } = InteractionTiming.DefaultDelay;
    public bool FullWidth { get; set; }
    public bool HideTaskbar { get; set; } = true;
    public bool Clock24 { get; set; } = true;
    public bool Previews { get; set; } = true;
    public bool HideIdleMedia { get; set; } = true;
    /// <summary>
    /// Görev çubuğu gizlenirken alanı: "keep" (varsayılan) ekran alanı hiç değişmez, masaüstü düzeni bozulmaz;
    /// "free" görev çubuğu otomatik gizlemeye alınır, pencereler tüm ekranı kullanır (düzen bir kez değişir).
    /// </summary>
    public string TaskbarSpace { get; set; } = "keep";
    /// <summary>Görev çubuğu gizlenince: "hidden" tamamen gizli; "autohide" imleç ekranın alt kenarına değince görünür.</summary>
    public string TaskbarMode { get; set; } = "hidden";
    public bool WinNumbers { get; set; } = true;
    public List<string> QuickToggles { get; set; } = [];   // Wi‑Fi/Bluetooth yanındaki ek hızlı ayarlar
    public bool AccentFromArt { get; set; }                // albüm kapağı rengi vurgu rengi olsun
    public string TempUnit { get; set; } = "C";
    public string TempSensor { get; set; } = "";
    public bool TrackSites { get; set; }
    public bool AltTodo { get; set; } = true;            // Alt+T ile hızlı yapılacak                   // ekran süresinde web sitelerini de say
    public List<NoteItem> NoteList { get; set; } = [];
    public List<AppGroup> Groups { get; set; } = [];
    public string IconStyle { get; set; } = "normal";    // normal | frame (macOS tarzı çerçeve)
    public bool AppCards { get; set; }                   // uygulamalar widget boyutunda kart olarak
    public double Magnify { get; set; } = 1;
    public string CardStyle { get; set; } = "auto";
    public string Displays { get; set; } = "main";        // main (bir ekran) | all (her ekranda) | follow (imleci takip et)
    public string MonitorName { get; set; } = "";         // dock'un durduğu ekran (aygıt adı)       // auto | black | white | frosted
    public double DockMaxWidth { get; set; }             // uçlarından daraltılmış dock genişliği (0 = sınırsız)             // Mac tarzı büyütme (1 = kapalı)
    public bool FixedAppArea { get; set; } // Fit the number of applications without trailing empty slots.
    public int AppSlots { get; set; }                  // ayrılan simge yeri sayısı
    public string StartImage { get; set; } = "";      // Başlat düğmesi için özel resim       // Win+1…Win+0 dock uygulamalarını açar
    public bool NamesOnHover { get; set; }             // uygulama simgesinin üstünde ad etiketi
    public string ClockClick { get; set; } = "iskele"; // saate tıklayınca: iskele takvimi | windows (bildirim/takvim paneli)   // müzik çalmıyorken "şimdi çalan" widget'ını dock'ta gizle
    public bool AutoHide { get; set; }
    public string Mode { get; set; } = "bar";          // bar (kenar şeridi) | balloon (üstte yüzen balon)
    public bool BalloonAlwaysOpen { get; set; }
    public string BalloonEdge { get; set; } = "top";   // balonun kendi konumu (şeritten bağımsız)
    public bool ShowOnDesktop { get; set; } = true;
    public bool LockDock { get; set; }
    public string Indicator { get; set; } = "dot";   // dot | bigdot | line
    public string IconSize { get; set; } = "M";      // S | M | L
    public int Spacing { get; set; } = 3;
    public int DockPadding { get; set; } = 5;
    public int EdgeDistance { get; set; } = 6;
    public string Accent { get; set; } = "#2F8CFF";
    public List<string> IcalUrls { get; set; } = [];
    public List<string> Stocks { get; set; } = ["AAPL", "MSFT", "NVDA"];
    public List<string> Crypto { get; set; } = ["BTCUSDT", "ETHUSDT"];
    public string CurrencyFrom { get; set; } = "USD";
    public string CurrencyTo { get; set; } = "TRY";
    public double CurrencyAmount { get; set; } = 100;
    public string WorldCity { get; set; } = "Tokyo Standard Time";
    public List<string> Widgets { get; set; } =
        ["battery", "media", "volume", "toggles", "apps", "screentime", "system", "clock"];

    /// <summary>Dock'a sabitlenmiş uygulamalar (.lnk veya .exe yolları). null ise ilk açılışta Windows görev çubuğundan alınır.</summary>
    public List<string>? Pins { get; set; }
    public string WeatherCity { get; set; } = "İstanbul";
    public List<TodoItem> Todos { get; set; } = [];
    public string Note { get; set; } = "";
}

class NoteItem
{
    public string Text { get; set; } = "";
    public bool Pinned { get; set; }
    public DateTime Updated { get; set; } = DateTime.Now;
    public string Title => Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "Yeni not";
}

class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

partial class Settings
{

    internal static string? DirectoryOverride; // Isolate all test logs/state from real AppData.

    public static string Dir
    {
        get
        {
            var d = DirectoryOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Iskele");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>Dock görev çubuğunun yerine sığdırılırken uygulanan ek küçültme (kaydedilmez).</summary>
    public static double Fit { get; set; } = 1;

    public double Scale => Size switch { "S" => 0.85, "L" => 1.2, _ => 1.0 } * Fit;

    public static Settings Load() => LoadFrom(FilePath);

    // Preserve corrupt input and restore the last valid backup atomically.
    internal static Settings LoadFrom(string path)
    {
        Settings? Read(string file)
        {
            if (!File.Exists(file)) return null;
            var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file)) ?? throw new JsonException("Empty settings object");
            value.Normalize(); return value;
        }
        try { if (Read(path) is { } value) return value; }
        catch (Exception ex) { App.Log(ex.Message); }
        try
        {
            if (File.Exists(path)) File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
            var backup = Read(path + ".bak");
            if (backup != null)
            {
                AtomicFile.Write(path, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }), backup: false);
                return backup;
            }
        }
        catch (Exception ex) { App.Log(ex.Message); }
        return new Settings();
    }

    // Explicit JSON null overrides property initializers; repair it before UI access.
    internal void Normalize()
    {
        // Validate persisted numeric material/motion controls before GPU/layout use.
        static double Clamp(double v,double min,double max,double fallback)=>double.IsFinite(v)?Math.Clamp(v,min,max):fallback;
        GlassRefraction=Clamp(GlassRefraction,0,18,8);GlassBlur=Clamp(GlassBlur,0,20,6);
        GlassTint=Clamp(GlassTint,0,.4,.12);GlassHighlight=Clamp(GlassHighlight,0,1.2,.65);
        HoverResponseMs=Math.Round(Clamp(HoverResponseMs,InteractionTiming.MinimumDelay,350,InteractionTiming.DefaultDelay));
        var defaults = new Settings();
        foreach (var p in typeof(Settings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!p.CanWrite) continue;
            if (p.GetValue(this) == null && (p.PropertyType == typeof(string) || typeof(System.Collections.IList).IsAssignableFrom(p.PropertyType)))
                p.SetValue(this, p.GetValue(defaults));
            if (p.GetValue(this) is System.Collections.IList list)
                for (int i = list.Count - 1; i >= 0; i--) if (list[i] == null) list.RemoveAt(i);
        }
        foreach (var note in NoteList) note.Text ??= "";
        foreach (var todo in Todos) todo.Text ??= "";
        foreach (var group in Groups)
        {
            group.Items ??= []; group.Items.RemoveAll(item => item == null);
            group.Name ??= "Grup"; group.Id ??= Guid.NewGuid().ToString("N")[..8];
        }
    }

    /// <summary>Test bayrakları (ISKELE_THEME/MODE/AUTOHIDE) açıkken ayarlar diske yazılmaz.</summary>
    public static bool NoSave { get; set; }

    public void Save()
    {
        if (NoSave) return;
        try
        {
            AtomicFile.Write(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); // atomic + backup
        }
        catch (Exception ex) { App.Log(ex); }
    }
}

/// <summary>Windows ile birlikte başlatma (HKCU Run anahtarı; yönetici izni gerekmez).</summary>
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Iskele";

    public static bool Enabled
    {
        get
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) != null;
        }
        set
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (value) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue(Name, false);
        }
    }
}
