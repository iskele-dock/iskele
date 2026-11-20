using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Iskele;

/// <summary>
/// Ses kartı (hedef 54, 55, 61): iOS Kontrol Merkezi tarzı büyük ana ses kaydırıcısı, çıkış aygıtları (tıkla → varsayılan
/// yap) ve uygulama başına ses düzeyleri. Windows Core Audio + IPolicyConfig.
/// </summary>
static class AudioDevices
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { int GetCount(out uint n); int Item(uint i, out IMMDevice d); }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        int OpenPropertyStore(int access, out IPropertyStore store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore { int GetCount(out int n); int GetAt(int i, out PROPERTYKEY k); int GetValue(ref PROPERTYKEY k, out PROPVARIANT v); }

    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p; public IntPtr p2; }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        int GetMixFormat(IntPtr a, IntPtr b);
        int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        int ResetDeviceFormat(IntPtr a);
        int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        int SetProcessingPeriod(IntPtr a, IntPtr b);
        int GetShareMode(IntPtr a, IntPtr b);
        int SetShareMode(IntPtr a, IntPtr b);
        int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        int SetEndpointVisibility(IntPtr a, int b);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        int GetAudioSessionControl(IntPtr a, int b, out IntPtr c);
        int GetSimpleAudioVolume(IntPtr a, int b, out IntPtr c);
        int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator { int GetCount(out int n); int GetSession(int i, out IAudioSessionControl2 s); }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName(IntPtr a, IntPtr b);
        int GetIconPath(IntPtr a);
        int SetIconPath(IntPtr a, IntPtr b);
        int GetGroupingParam(IntPtr a);
        int SetGroupingParam(IntPtr a, IntPtr b);
        int RegisterAudioSessionNotification(IntPtr a);
        int UnregisterAudioSessionNotification(IntPtr a);
        int GetSessionIdentifier(IntPtr a);
        int GetSessionInstanceIdentifier(IntPtr a);
        int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

    /// <summary>Uygulama oturumunun kanal ses düzeyleri (bu Windows sürümünde ISimpleAudioVolume desteklenmiyor).</summary>
    [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IChannelAudioVolume
    {
        int GetChannelCount(out uint n);
        int SetChannelVolume(uint i, float level, ref Guid ctx);
        int GetChannelVolume(uint i, out float level);
    }

    static Guid ctx = Guid.Empty;

    static string Name(IMMDevice d)
    {
        try
        {
            if (d.OpenPropertyStore(0, out var s) != 0) return "";
            var k = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
            if (s.GetValue(ref k, out var v) != 0) return "";
            var n = v.vt == 31 ? Marshal.PtrToStringUni(v.p) ?? "" : "";
            PropVariantClear(ref v);
            return n;
        }
        catch { return ""; }
    }

    public record Output(string Id, string Name, bool Default);

    public static List<Output> Outputs()
    {
        var list = new List<Output>();
        try
        {
            var en = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            string def = "";
            if (en.GetDefaultAudioEndpoint(0, 1, out var dd) == 0) dd.GetId(out def);
            en.EnumAudioEndpoints(0, 1 /* ACTIVE */, out var col);
            col.GetCount(out uint n);
            for (uint i = 0; i < n; i++)
            {
                col.Item(i, out var d);
                d.GetId(out var id);
                list.Add(new Output(id, Name(d), id == def));
            }
        }
        catch (Exception ex) { App.Log(ex.Message); }
        return list;
    }

    /// <summary>Varsayılan çıkış aygıtını değiştir (konsol, multimedya ve iletişim rolleri).</summary>
    public static void SetDefault(string id)
    {
        try
        {
            var pc = (IPolicyConfig)new PolicyConfigCo();
            for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);
            Audio.Refresh();
        }
        catch (Exception ex) { App.Log(ex.Message); }
    }

    internal sealed class AppVolume
    {
        public string Name = "";
        public string Exe = "";
        internal IChannelAudioVolume Vol = null!;
        static readonly Dictionary<string, float> beforeMute = [];
        float Get() { Vol.GetChannelCount(out var n); float max = 0; for (uint i = 0; i < n; i++) { Vol.GetChannelVolume(i, out var v); max = Math.Max(max, v); } return max; }
        void Set(float v) { Vol.GetChannelCount(out var n); for (uint i = 0; i < n; i++) Vol.SetChannelVolume(i, v, ref ctx); }
        // Ses oturumları çok iş parçacıklı (MTA) nesneler; WPF iş parçacığından değil, arka plan iş parçacığından çağrılır.
        public float Level
        {
            get => Mta(Get);
            set { var v = Math.Clamp(value, 0, 1); Mta(() => { Set(v); return 0; }); }
        }
        public bool Muted
        {
            get => Mta(Get) < 0.001f;
            set => Mta(() =>
            {
                var key = Exe.Length > 0 ? Exe : Name;
                if (value) { beforeMute[key] = Get(); Set(0); }
                else Set(beforeMute.TryGetValue(key, out var v) && v > 0 ? v : 1);
                return 0;
            });
        }
    }

    /// <summary>Varsayılan çıkıştaki uygulama ses oturumları (aynı programın oturumları birleştirilmez, ilki gösterilir).</summary>
    static T Mta<T>(Func<T> f) => Task.Run(f).GetAwaiter().GetResult();

    internal static List<AppVolume> Apps() => Mta(AppsCore);

    static List<AppVolume> AppsCore()
    {
        var list = new List<AppVolume>();
        try
        {
            var en = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            if (en.GetDefaultAudioEndpoint(0, 1, out var dev) != 0) return list;
            var iid = typeof(IAudioSessionManager2).GUID;
            dev.Activate(ref iid, 23, IntPtr.Zero, out var o);
            ((IAudioSessionManager2)o).GetSessionEnumerator(out var se);
            se.GetCount(out int n);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int failed = 0;
            for (int i = 0; i < n; i++)
            {
                try
                {
                    se.GetSession(i, out var s);
                    if (s is not IChannelAudioVolume vol) { failed++; continue; }
                    if (s.IsSystemSoundsSession() == 0) { list.Insert(0, new AppVolume { Name = "Sistem sesleri", Vol = vol }); continue; }
                    s.GetState(out int state);
                    if (state == 2 /* expired */) continue;
                    s.GetProcessId(out uint pid);
                    string exe = Native.ProcessPath(pid);
                    if (exe.Length == 0 || !seen.Add(exe)) continue;
                    list.Add(new AppVolume { Name = ScreenTime.FriendlyName(exe), Exe = exe, Vol = vol });
                }
                catch (Exception ex) { failed++; App.Debug("ses oturumu: " + ex.Message); }
            }
            App.Debug($"ses oturumlari: {n} toplam, {list.Count} listelendi, {failed} okunamadi");
        }
        catch (Exception ex) { App.Log(ex.Message); }
        return list;
    }
}

static class SoundPanel
{
    public static UIElement Build()
    {
        var p = new StackPanel();
        p.Children.Add(Ui.Text("Ses", 16, "Fg", FontWeights.Bold));

        // ana ses: büyük, iOS tarzı kaydırıcı
        var big = new MiniSlider { Height = 30, Value = Audio.Volume, Margin = new Thickness(0, 10, 0, 4) };
        var pct = Ui.Text($"%{Audio.Volume * 100:0}", 12, "FgDim");
        big.ValueChanged += v => { Audio.Volume = (float)v; pct.Text = $"%{v * 100:0}"; };
        var head = new DockPanel();
        DockPanel.SetDock(pct, Dock.Right);
        head.Children.Add(pct);
        head.Children.Add(Ui.RawText(Audio.DeviceName, 12.5, "Fg", FontWeights.SemiBold));
        p.Children.Add(head);
        p.Children.Add(big);

        // çıkışlar
        var oh = Ui.Text("Çıkış", 12, "FgDim", FontWeights.SemiBold);
        oh.Margin = new Thickness(0, 12, 0, 4);
        p.Children.Add(oh);
        foreach (var o in AudioDevices.Outputs())
        {
            var dev = o;
            var check = o.Default ? Ui.Glyph("", 13, "Accent") : null;
            var icon = Ui.Glyph(o.Name.Contains("Kulak", StringComparison.OrdinalIgnoreCase) || o.Name.Contains("Head", StringComparison.OrdinalIgnoreCase) || o.Name.Contains("Buds", StringComparison.OrdinalIgnoreCase) ? "" : "", 15, o.Default ? "Accent" : "Fg");
            int i = o.Name.IndexOf(" (", StringComparison.Ordinal);
            var name = i > 0 ? o.Name[..i] : o.Name;
            var sub = i > 0 ? o.Name[(i + 2)..].TrimEnd(')') : "";
            p.Children.Add(PanelRows.Row(icon, name, sub, check, o.Default ? null : () =>
            {
                AudioDevices.SetDefault(dev.Id);
                Flyout.CloseCurrent();
            }));
        }

        // uygulamalar
        var apps = AudioDevices.Apps();
        if (apps.Count > 0)
        {
            var ah = Ui.Text("Uygulamalar", 12, "FgDim", FontWeights.SemiBold);
            ah.Margin = new Thickness(0, 12, 0, 4);
            p.Children.Add(ah);
            foreach (var a in apps)
            {
                var app = a;
                UIElement ic = app.Exe.Length > 0 && IconCache.Get(app.Exe) is { } img
                    ? new Image { Source = img, Width = 20, Height = 20 }
                    : Ui.Glyph("", 14, "FgDim");
                var mute = new IconButton(app.Muted ? "" : "", 12, 26);
                var s = new MiniSlider { Height = 16, Value = app.Level, Margin = new Thickness(8, 0, 6, 0), Width = 150 };
                s.ValueChanged += v => app.Level = (float)v;
                mute.Click += () => { app.Muted = !app.Muted; mute.GlyphText = app.Muted ? "" : ""; };
                var name = Ui.RawText(app.Name, 12, "Fg");
                name.Width = 110;
                var row = Ui.Row(new Border { Width = 26, Child = ic }, name, s, mute);
                row.Margin = new Thickness(0, 3, 0, 3);
                p.Children.Add(row);
            }
        }

        var more = PanelRows.Link("Ses ayarları", () => { Flyout.CloseCurrent(); App.Open("ms-settings:sound"); });
        p.Children.Add(more);
        return p;
    }
}
