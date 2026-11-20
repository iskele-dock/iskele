using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Security.Credentials;

namespace Iskele;

/// <summary>Açılır listelerde ortak satır görünümü.</summary>
static class PanelRows
{
    public static Border Row(UIElement icon, string title, string sub, UIElement? right, Action? click)
    {
        var d = new DockPanel();
        if (right != null) { DockPanel.SetDock(right, Dock.Right); d.Children.Add(right); }
        var ic = new Border { Width = 32, Child = icon };
        DockPanel.SetDock(ic, Dock.Left);
        d.Children.Add(ic);
        // Rows carry SSIDs, device names and notes: preserve them verbatim.
        var t = Ui.RawText(title, 13, "Fg", FontWeights.SemiBold);
        var s = Ui.RawText(sub, 11, "FgDim");
        d.Children.Add(sub.Length > 0 ? Ui.Col(t, s) : t);
        var b = new Border { Child = d, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent };
        if (click != null)
        {
            b.Cursor = Cursors.Hand;
            b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "Track");
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (_, e) => { if (!e.Handled) { e.Handled = true; click(); } };
        }
        return b;
    }

    public static TextBlock Link(string text, Action a)
    {
        var t = Ui.Text(text, 12, "Accent", FontWeights.SemiBold);
        t.Cursor = Cursors.Hand;
        t.Margin = new Thickness(4, 10, 0, 0);
        t.MouseLeftButtonUp += (_, _) => a();
        return t;
    }

    public static (DockPanel Header, Action<bool> SetOn) Header(string title, Func<Task> toggle)
    {
        var d = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var sw = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), Cursor = Cursors.Hand, Padding = new Thickness(3) };
        var knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
        sw.Child = knob;
        void SetOn(bool on)
        {
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            if (on) sw.SetResourceReference(Border.BackgroundProperty, "Accent"); else sw.SetResourceReference(Border.BackgroundProperty, "Track");
        }
        sw.MouseLeftButtonUp += async (_, _) => await toggle();
        DockPanel.SetDock(sw, Dock.Right);
        d.Children.Add(sw);
        d.Children.Add(Ui.Text(title, 16, "Fg", FontWeights.Bold));
        return (d, SetOn);
    }
}

// ═════════════════════════ Wi‑Fi ═════════════════════════

/// <summary>Çevredeki Wi‑Fi ağları: bağlan / şifreyle bağlan / bağlantıyı kes. Ayarlar'a gitmeden.</summary>
class WifiPanel : StackPanel
{
    readonly StackPanel list = new();
    readonly TextBlock status = Ui.Text("", 11.5, "FgDim");
    readonly Action<bool> setOn;
    WiFiAdapter? adapter;
    Radio? radio;

    public WifiPanel()
    {
        var (header, set) = PanelRows.Header("Wi‑Fi", ToggleRadio);
        setOn = set;
        Children.Add(header);
        Children.Add(new ScrollViewer { Content = list, Height = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(4, 8, 0, 0);
        Children.Add(status);
        Children.Add(PanelRows.Link("Ağ ve İnternet ayarları", () => { Flyout.CloseCurrent(); App.Open("ms-settings:network-wifi"); }));
        _ = Load(scan: true);
    }

    async Task ToggleRadio()
    {
        try
        {
            radio ??= (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            if (radio == null) return;
            await radio.SetStateAsync(radio.State == RadioState.On ? RadioState.Off : RadioState.On);
            await Task.Delay(radio.State == RadioState.On ? 2500 : 300);
            await Load(scan: radio.State == RadioState.On);
        }
        catch (Exception ex) { status.Text = "Wi‑Fi açılıp kapatılamadı: " + ex.Message; }
    }

    async Task Load(bool scan)
    {
        list.Children.Clear();
        list.Children.Add(Ui.Text("Ağlar taranıyor…", 12.5, "FgDim"));
        try
        {
            radio ??= (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            setOn(radio?.State == RadioState.On);
            if (radio?.State == RadioState.Off)
            {
                list.Children.Clear();
                list.Children.Add(Ui.Text("Wi‑Fi kapalı.", 12.5, "FgDim"));
                return;
            }
            if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed)
            {
                list.Children.Clear();
                list.Children.Add(Ui.Text("Windows Wi‑Fi erişimine izin vermedi.", 12.5, "FgDim"));
                return;
            }
            adapter ??= (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault();
            if (adapter == null)
            {
                list.Children.Clear();
                list.Children.Add(Ui.Text("Wi‑Fi bağdaştırıcısı bulunamadı.", 12.5, "FgDim"));
                return;
            }
            if (scan) await adapter.ScanAsync();
            Fill(await CurrentSsid());
        }
        catch (Exception ex)
        {
            list.Children.Clear();
            list.Children.Add(Ui.Text("Ağlar okunamadı.", 12.5, "FgDim"));
            App.Log(ex);
        }
    }

    async Task<string?> CurrentSsid()
    {
        try
        {
            var p = await adapter!.NetworkAdapter.GetConnectedProfileAsync();
            if (p == null) return null;
            return p.IsWlanConnectionProfile ? p.WlanConnectionProfileDetails.GetConnectedSsid() : p.ProfileName;
        }
        catch { return null; }
    }

    static string Bars(byte b) => b switch { >= 4 => "", 3 => "", 2 => "", _ => "" };

    static bool Secured(WiFiAvailableNetwork n) =>
        n.SecuritySettings.NetworkAuthenticationType is not (Windows.Networking.Connectivity.NetworkAuthenticationType.Open80211 or Windows.Networking.Connectivity.NetworkAuthenticationType.None);

    void Fill(string? current)
    {
        list.Children.Clear();
        var nets = adapter!.NetworkReport.AvailableNetworks
            .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
            .GroupBy(n => n.Ssid).Select(g => g.OrderByDescending(n => n.SignalBars).First())
            .OrderByDescending(n => n.Ssid == current).ThenByDescending(n => n.SignalBars)
            .ToList();

        if (nets.Count == 0)
        {
            list.Children.Add(Ui.Text("Ağ bulunamadı. Windows 11'de ağ adlarını görmek için " +
                "Ayarlar → Gizlilik → Konum → \"Masaüstü uygulamalarının konumunuza erişmesine izin ver\" açık olmalı.", 12, "FgDim"));
            ((TextBlock)list.Children[0]).TextWrapping = TextWrapping.Wrap;
            list.Children.Add(PanelRows.Link("Konum izinlerini aç", () => App.Open("ms-settings:privacy-location")));
            return;
        }

        foreach (var n in nets)
        {
            var net = n;
            bool connected = net.Ssid == current;
            bool secured = Secured(net);
            var icon = Ui.Glyph(Bars(net.SignalBars), 16, connected ? "Accent" : "Fg");
            UIElement? right = null;
            if (connected)
            {
                var off = Ui.Text("Bağlantıyı kes", 11.5, "Accent", FontWeights.SemiBold);
                off.Cursor = Cursors.Hand;
                off.MouseLeftButtonUp += async (_, e) => { e.Handled = true; adapter!.Disconnect(); status.Text = "Bağlantı kesildi."; await Task.Delay(800); await Load(scan: false); };
                right = off;
            }
            else if (secured) right = Ui.Glyph("", 11, "FgDim");

            var sub = connected ? (NetState.Read().Internet ? "Bağlı · internet var" : "Bağlı · internet yok") : secured ? "Güvenli" : "Açık ağ";
            var row = PanelRows.Row(icon, net.Ssid, sub, right, connected ? null : () => _ = Connect(net, secured));
            list.Children.Add(row);
        }
    }

    async Task Connect(WiFiAvailableNetwork net, bool secured, string? password = null)
    {
        status.Text = $"{net.Ssid} ağına bağlanılıyor…";
        try
        {
            var r = password == null
                ? await adapter!.ConnectAsync(net, WiFiReconnectionKind.Automatic)
                : await adapter!.ConnectAsync(net, WiFiReconnectionKind.Automatic, new PasswordCredential { Password = password });
            if (r.ConnectionStatus == WiFiConnectionStatus.Success)
            {
                status.Text = $"{net.Ssid} ağına bağlandı.";
                await Load(scan: false);
                return;
            }
            if (secured && (password == null || r.ConnectionStatus == WiFiConnectionStatus.InvalidCredential))
            {
                status.Text = password == null ? $"{net.Ssid} için şifre gerekli." : "Şifre yanlış, tekrar dene.";
                AskPassword(net);
                return;
            }
            status.Text = r.ConnectionStatus switch
            {
                WiFiConnectionStatus.NetworkNotAvailable => "Ağ artık erişilebilir değil.",
                WiFiConnectionStatus.Timeout => "Bağlantı zaman aşımına uğradı.",
                WiFiConnectionStatus.AccessRevoked => "Erişim reddedildi.",
                _ => "Bağlanılamadı.",
            };
        }
        catch (Exception ex) { status.Text = "Bağlanılamadı: " + ex.Message; }
    }

    void AskPassword(WiFiAvailableNetwork net)
    {
        // eski şifre kutusunu kaldır
        foreach (var old in list.Children.OfType<Border>().Where(b => b.Tag as string == "pw").ToList()) list.Children.Remove(old);
        var pw = new PasswordBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        pw.SetResourceReference(Control.ForegroundProperty, "Fg");
        pw.SetResourceReference(PasswordBox.CaretBrushProperty, "Fg");
        var go = new IconButton("", 12, 28, accent: true) { ToolTip = "Bağlan" };
        var d = new DockPanel();
        DockPanel.SetDock(go, Dock.Right);
        d.Children.Add(go);
        d.Children.Add(pw);
        var box = new Border { Child = d, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 4, 4), Margin = new Thickness(32, 2, 0, 6), Tag = "pw" };
        box.SetResourceReference(Border.BackgroundProperty, "Track");
        void Go() { if (pw.Password.Length > 0) _ = Connect(net, true, pw.Password); }
        go.Click += Go;
        pw.KeyDown += (_, e) => { if (e.Key == Key.Enter) Go(); };
        int idx = list.Children.Cast<UIElement>().ToList().FindIndex(c => c is Border b && b.Child is DockPanel dp && dp.Children.OfType<FrameworkElement>().Any(x => x is TextBlock tb && tb.Text == net.Ssid || (x is StackPanel sp && sp.Children.OfType<TextBlock>().Any(t => t.Text == net.Ssid))));
        list.Children.Insert(idx >= 0 ? idx + 1 : list.Children.Count, box);
        pw.Focus();
    }
}

// ═════════════════════════ Bluetooth ═════════════════════════

/// <summary>Eşleşmiş Bluetooth cihazları; ses cihazlarına (kulaklık, hoparlör) tek tıkla bağlan / bağlantıyı kes.</summary>
class BluetoothPanel : StackPanel
{
    readonly StackPanel list = new();
    readonly TextBlock status = Ui.Text("", 11.5, "FgDim");
    readonly Action<bool> setOn;
    Radio? radio;

    record Dev(string Id, string Name, bool Connected, bool Audio, bool Le, string Kind);

    Dictionary<string, int> batteries = [];

    /// <summary>Cihaz pil yüzdeleri (DEVPKEY_Bluetooth_Battery). Ad → yüzde; kulaklıklarda "… Hands-Free" alt aygıtında durur.</summary>
    static async Task<Dictionary<string, int>> Batteries()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        const string key = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
        try
        {
            var all = await DeviceInformation.FindAllAsync("", [key], DeviceInformationKind.Device);
            foreach (var d in all)
                if (d.Properties.TryGetValue(key, out var v) && v is byte b && !string.IsNullOrEmpty(d.Name))
                    result[d.Name] = b;
        }
        catch (Exception ex) { App.Log(ex.Message); }
        return result;
    }

    public BluetoothPanel()
    {
        var (header, set) = PanelRows.Header("Bluetooth", ToggleRadio);
        setOn = set;
        Children.Add(header);
        Children.Add(new ScrollViewer { Content = list, Height = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(4, 8, 0, 0);
        Children.Add(status);
        Children.Add(PanelRows.Link("Yeni cihaz eşleştir…", () => { Flyout.CloseCurrent(); App.Open("ms-settings:bluetooth"); }));
        _ = Load();
    }

    async Task ToggleRadio()
    {
        try
        {
            radio ??= (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (radio == null) return;
            await radio.SetStateAsync(radio.State == RadioState.On ? RadioState.Off : RadioState.On);
            await Task.Delay(800);
            await Load();
        }
        catch (Exception ex) { status.Text = "Bluetooth açılıp kapatılamadı: " + ex.Message; }
    }

    async Task Load()
    {
        list.Children.Clear();
        list.Children.Add(Ui.Text("Cihazlar okunuyor…", 12.5, "FgDim"));
        try
        {
            radio ??= (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            setOn(radio?.State == RadioState.On);
            if (radio?.State == RadioState.Off)
            {
                list.Children.Clear();
                list.Children.Add(Ui.Text("Bluetooth kapalı.", 12.5, "FgDim"));
                return;
            }
            var devs = new List<Dev>();
            foreach (var info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)))
            {
                try
                {
                    using var d = await BluetoothDevice.FromIdAsync(info.Id);
                    if (d == null) continue;
                    var major = d.ClassOfDevice.MajorClass;
                    bool audio = major == BluetoothMajorClass.AudioVideo;
                    devs.Add(new Dev(info.Id, d.Name, d.ConnectionStatus == BluetoothConnectionStatus.Connected, audio, false, major.ToString()));
                }
                catch { }
            }
            foreach (var info in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)))
            {
                try
                {
                    using var d = await BluetoothLEDevice.FromIdAsync(info.Id);
                    if (d == null || devs.Any(x => x.Name == d.Name)) continue;
                    devs.Add(new Dev(info.Id, d.Name, d.ConnectionStatus == BluetoothConnectionStatus.Connected, false, true, "LE"));
                }
                catch { }
            }
            batteries = await Batteries();
            Fill(devs);
        }
        catch (Exception ex)
        {
            list.Children.Clear();
            list.Children.Add(Ui.Text("Cihazlar okunamadı.", 12.5, "FgDim"));
            App.Log(ex);
        }
    }

    static string Glyph(Dev d) => d.Kind switch
    {
        "AudioVideo" => "",   // kulaklık
        "Peripheral" => "",   // fare / klavye
        "Phone" => "",
        "Computer" => "",
        _ => "",
    };

    void Fill(List<Dev> devs)
    {
        list.Children.Clear();
        if (devs.Count == 0)
        {
            list.Children.Add(Ui.Text("Eşleşmiş cihaz yok.", 12.5, "FgDim"));
            return;
        }
        foreach (var d in devs.OrderByDescending(x => x.Connected).ThenByDescending(x => x.Audio).ThenBy(x => x.Name))
        {
            var dev = d;
            var icon = Ui.Glyph(Glyph(dev), 16, dev.Connected ? "Accent" : "Fg");
            string sub = dev.Connected ? "Bağlı" : "Bağlı değil";
            if (batteries.FirstOrDefault(b => b.Key.StartsWith(dev.Name, StringComparison.OrdinalIgnoreCase)) is { Key: not null } bat) sub += $" · pil %{bat.Value}";
            UIElement? right = null;
            if (dev.Audio)
            {
                var act = Ui.Text(dev.Connected ? "Bağlantıyı kes" : "Bağlan", 11.5, "Accent", FontWeights.SemiBold);
                right = act;
            }
            Action? click = dev.Audio ? () => _ = ToggleAudio(dev) : () => _ = Wake(dev);
            list.Children.Add(PanelRows.Row(icon, dev.Name, sub, right, click));
        }
    }

    async Task ToggleAudio(Dev dev)
    {
        status.Text = dev.Connected ? $"{dev.Name} bağlantısı kesiliyor…" : $"{dev.Name} bağlanıyor…";
        bool ok = await Task.Run(() => BtAudio.Send(dev.Name, connect: !dev.Connected));
        if (!ok)
        {
            status.Text = $"{dev.Name} için ses sürücüsü bulunamadı. Cihazın açık ve yakında olduğundan emin ol.";
            return;
        }
        // bağlantı birkaç saniye sürebilir
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(1000);
            try
            {
                using var d = await BluetoothDevice.FromIdAsync(dev.Id);
                bool now = d?.ConnectionStatus == BluetoothConnectionStatus.Connected;
                if (now != dev.Connected) { status.Text = now ? $"{dev.Name} bağlandı." : $"{dev.Name} bağlantısı kesildi."; break; }
            }
            catch { }
        }
        await Load();
    }

    /// <summary>Ses dışı cihazlar (fare, klavye) genelde kendileri bağlanır; servisleri sorgulamak bağlantıyı tetikleyebilir.</summary>
    async Task Wake(Dev dev)
    {
        if (dev.Connected) { status.Text = $"{dev.Name} zaten bağlı."; return; }
        status.Text = $"{dev.Name} uyandırılıyor… (cihazı açık tut)";
        try
        {
            if (dev.Le)
            {
                using var d = await BluetoothLEDevice.FromIdAsync(dev.Id);
                if (d != null) await d.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            }
            else
            {
                using var d = await BluetoothDevice.FromIdAsync(dev.Id);
                if (d != null) await d.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
            }
        }
        catch { }
        await Task.Delay(1500);
        await Load();
    }
}

/// <summary>
/// Bluetooth ses cihazına bağlan / bağlantıyı kes. Windows'un Bluetooth ses sürücüsüne
/// KSPROPERTY_ONESHOT_RECONNECT / DISCONNECT gönderir (Ayarlar'daki "Bağlan" düğmesinin yaptığı).
/// </summary>
static class BtAudio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        int GetCount(out uint count);
        int Item(uint i, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        int OpenPropertyStore(int access, out IPropertyStore store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        int GetCount(out int count);
        int GetAt(int i, out PROPERTYKEY key);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p; public IntPtr p2; }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceTopology
    {
        int GetConnectorCount(out uint count);
        int GetConnector(uint i, out IConnector connector);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IConnector
    {
        int GetType(out int type);
        int GetDataFlow(out int flow);
        int ConnectTo(IntPtr other);
        int Disconnect();
        int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        int GetConnectedTo(out IntPtr other);
        int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [StructLayout(LayoutKind.Sequential)] struct KSPROPERTY { public Guid Set; public int Id; public int Flags; }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KSPROPERTY property, int propertyLength, IntPtr data, int dataLength, out int bytesReturned);
    }

    static readonly Guid KSPROPSETID_BtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");

    static string Prop(IMMDevice d, Guid fmt, int pid)
    {
        try
        {
            if (d.OpenPropertyStore(0, out var store) != 0) return "";
            var key = new PROPERTYKEY { fmtid = fmt, pid = pid };
            if (store.GetValue(ref key, out var pv) != 0) return "";
            string s = pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) ?? "" : "";
            PropVariantClear(ref pv);
            return s;
        }
        catch { return ""; }
    }

    /// <summary>Adı eşleşen Bluetooth ses aygıtına bağlan/kes komutu gönder. Komut gönderilebildiyse true.</summary>
    public static bool Send(string deviceName, bool connect)
    {
        var en = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
        bool sent = false;
        foreach (int flow in new[] { 0 /* render */, 1 /* capture */ })
        {
            if (en.EnumAudioEndpoints(flow, 0xF /* tüm durumlar, bağlı olmayanlar dahil */, out var col) != 0) continue;
            col.GetCount(out uint n);
            for (uint i = 0; i < n && !sent; i++)
            {
                try
                {
                    col.Item(i, out var dev);
                    var ifName = Prop(dev, new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);   // PKEY_DeviceInterface_FriendlyName
                    var name = Prop(dev, new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);   // PKEY_Device_FriendlyName
                    if (!ifName.Equals(deviceName, StringComparison.OrdinalIgnoreCase) && !name.Contains(deviceName, StringComparison.OrdinalIgnoreCase)) continue;

                    var iid = typeof(IDeviceTopology).GUID;
                    if (dev.Activate(ref iid, 23, IntPtr.Zero, out var topoObj) != 0) continue;
                    var topo = (IDeviceTopology)topoObj;
                    if (topo.GetConnector(0, out var conn) != 0) continue;
                    if (conn.GetDeviceIdConnectedTo(out var filterId) != 0) continue;
                    if (en.GetDevice(filterId, out var filter) != 0) continue;
                    var ksIid = typeof(IKsControl).GUID;
                    if (filter.Activate(ref ksIid, 23, IntPtr.Zero, out var ksObj) != 0) continue;
                    var ks = (IKsControl)ksObj;
                    var prop = new KSPROPERTY { Set = KSPROPSETID_BtAudio, Id = connect ? 0 : 1, Flags = 1 /* KSPROPERTY_TYPE_GET */ };
                    int hr = ks.KsProperty(ref prop, Marshal.SizeOf<KSPROPERTY>(), IntPtr.Zero, 0, out _);
                    App.Debug($"bt {(connect ? "baglan" : "kes")} '{deviceName}' -> hr=0x{hr:X8}");
                    sent = hr >= 0 || hr == unchecked((int)0x80070490);   // ELEMENT_NOT_FOUND bazı sürücülerde başarıyla birlikte döner
                }
                catch (Exception ex) { App.Log(ex.Message); }
            }
            if (sent) break;
        }
        return sent;
    }
}
