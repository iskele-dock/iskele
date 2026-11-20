using Microsoft.Win32;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace Iskele;

/// <summary>Hızlı ayarlardaki yuvarlak tek dokunuşluk düğme.</summary>
class QuickButton : Border
{
    readonly TextBlock glyph;
    readonly Func<bool?> state;

    public QuickButton(string g, string tip, double size, Func<bool?> state, Func<Task> act)
    {
        this.state = state;
        Width = Height = size;
        CornerRadius = new CornerRadius(size / 2);
        BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand;
        ToolTip = tip;
        glyph = Ui.Glyph(g, Math.Round(size * 0.42));
        Child = glyph;
        MouseLeftButtonDown += (_, e) => e.Handled = true;
        MouseLeftButtonUp += async (_, e) => { e.Handled = true; try { await act(); } catch (Exception ex) { App.Log(ex.Message); } Refresh(); };
        Refresh();
    }

    public void Refresh()
    {
        bool? on = state();
        if (on == true)
        {
            var a = ((SolidColorBrush)Ui.B("Accent")).Color;
            Background = new SolidColorBrush(Color.FromArgb(46, a.R, a.G, a.B));
            SetResourceReference(BorderBrushProperty, "Accent");
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        }
        else
        {
            SetResourceReference(BackgroundProperty, "Track");
            BorderBrush = Brushes.Transparent;
            glyph.SetResourceReference(TextBlock.ForegroundProperty, on == null ? "FgDim" : "Fg");
        }
    }
}

/// <summary>
/// Hedef 52: Wi‑Fi/Bluetooth dışındaki hızlı ayarlar. Her biri Windows'un kendi ayarını değiştirir; doğrudan
/// değiştirilemeyenlerde (enerji tasarrufu, Ethernet) durum gösterilir, tıklayınca ilgili Ayarlar sayfası açılır.
/// </summary>
static class ExtraToggles
{
    public static readonly (string Id, string Name)[] All =
    [
        ("airplane", "Uçak modu"), ("hotspot", "Mobil erişim noktası"), ("ethernet", "Ethernet"),
        ("energy", "Enerji tasarrufu"), ("dnd", "Rahatsız etmeyin"), ("mic", "Mikrofon"),
        ("camera", "Kamera"), ("awake", "Uyanık tut"),
    ];

    public static QuickButton? Create(string id, double size) => id switch
    {
        "airplane" => new("", "Uçak modu", size, () => Airplane, ToggleAirplane),
        "hotspot" => new("", "Mobil erişim noktası", size, () => Hotspot, ToggleHotspot),
        "ethernet" => new("", "Ethernet (tıkla: ayarlar)", size, () => Ethernet, () => { App.Open("ms-settings:network-ethernet"); return Task.CompletedTask; }),
        "energy" => new("", "Enerji tasarrufu (tıkla: ayarlar)", size, () => EnergySaver, () => { App.Open("ms-settings:batterysaver"); return Task.CompletedTask; }),
        "dnd" => new("", "Rahatsız etmeyin", size, () => DoNotDisturb.On, () => { DoNotDisturb.Toggle(); return Task.CompletedTask; }),
        "mic" => new("", "Mikrofon (vurgulu: açık)", size, () => Audio.MicMuted is bool m ? !m : null, () => { Audio.ToggleMic(); return Task.CompletedTask; }),
        "camera" => new("", "Kamera erişimi", size, () => Camera, () => { Camera = !(Camera ?? true); return Task.CompletedTask; }),
        "awake" => new("", "Uyanık tut", size, () => SystemActions.KeepAwake, () => { SystemActions.ToggleAwake(); return Task.CompletedTask; }),
        _ => null,
    };

    // ── uçak modu: tüm radyoları kapat; kapatırken açık olanları hatırla, geri açarken onları aç
    static readonly List<RadioKind> wereOn = [];
    static bool? airplane;
    static bool? Airplane => airplane;

    static async Task ToggleAirplane()
    {
        var radios = await Radio.GetRadiosAsync();
        bool turnOn = !(airplane ?? radios.All(r => r.State != RadioState.On));
        if (turnOn)
        {
            wereOn.Clear();
            foreach (var r in radios.Where(r => r.State == RadioState.On)) { wereOn.Add(r.Kind); await r.SetStateAsync(RadioState.Off); }
        }
        else
        {
            foreach (var r in radios.Where(r => wereOn.Count == 0 ? r.Kind is RadioKind.WiFi or RadioKind.Bluetooth : wereOn.Contains(r.Kind)))
                await r.SetStateAsync(RadioState.On);
        }
        airplane = turnOn;
    }

    // ── mobil erişim noktası (paylaşılan internet)
    static bool? Hotspot
    {
        get
        {
            try
            {
                var p = NetworkInformation.GetInternetConnectionProfile();
                if (p == null) return null;
                return NetworkOperatorTetheringManager.CreateFromConnectionProfile(p).TetheringOperationalState == TetheringOperationalState.On;
            }
            catch { return null; }
        }
    }

    static async Task ToggleHotspot()
    {
        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            if (p == null) { App.Open("ms-settings:network-mobilehotspot"); return; }
            var m = NetworkOperatorTetheringManager.CreateFromConnectionProfile(p);
            var r = m.TetheringOperationalState == TetheringOperationalState.On ? await m.StopTetheringAsync() : await m.StartTetheringAsync();
            if (r.Status != TetheringOperationStatus.Success) App.Open("ms-settings:network-mobilehotspot");
        }
        catch { App.Open("ms-settings:network-mobilehotspot"); }
    }

    // ── Ethernet: kablo takılı ve bağlı mı (yalnızca durum)
    static bool? Ethernet
    {
        get
        {
            var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet && !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)).ToList();
            if (nics.Count == 0) return null;
            return nics.Any(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);
        }
    }

    // ── enerji tasarrufu: Windows'un pil tasarrufu bayrağı (yalnızca durum)
    static bool? EnergySaver => Native.GetSystemPowerStatus(out var s) ? s.SystemStatusFlag == 1 : null;

    // ── kamera: uygulamaların kameraya erişim izni (Ayarlar → Gizlilik → Kamera ile aynı anahtar)
    const string CamKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    static bool? Camera
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(CamKey);
            return k?.GetValue("Value") is string v ? v != "Deny" : null;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(CamKey);
            k.SetValue("Value", value == true ? "Allow" : "Deny");
        }
    }
}
