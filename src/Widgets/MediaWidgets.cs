using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace Iskele;

// ───────────────────────── Şimdi çalan ─────────────────────────

/// <summary>Windows'un medya oturumunu (Spotify, tarayıcı, medya oynatıcı…) okur ve kontrol eder.</summary>
class MediaWidget : Widget
{
    static GlobalSystemMediaTransportControlsSessionManager? manager;
    static Task? managerTask;

    GlobalSystemMediaTransportControlsSession? session;
    readonly Border cover;
    readonly TextBlock title, artist;
    readonly IconButton play;
    readonly string style;
    readonly MiniSlider progressBar = new() { Interactive = false, Height = 4 };
    readonly RingGauge progressRing = new() { Stroke = 3 };

    public MediaWidget(string style = "full")
    {
        this.style = style;
        double c = H - F(10);
        cover = new Border { Width = c, Height = c, CornerRadius = new CornerRadius(c / 2), ClipToBounds = true };
        cover.SetResourceReference(BackgroundProperty, "Track");
        cover.Child = Ui.Glyph("", F(14), "FgDim");
        title = Ui.Text("Çalan bir şey yok", F(12), "Fg", FontWeights.Bold);
        artist = Ui.Text("", F(10), "FgDim");
        var text = Ui.Col(title, artist);
        text.Width = F(100);
        text.Margin = new Thickness(F(8), 0, F(4), 0);

        var prev = new IconButton("", F(11), F(26));
        play = new IconButton("", F(12), F(28));
        var next = new IconButton("", F(11), F(26));
        prev.Click += () => _ = SkipTrack(session, false);
        next.Click += () => _ = SkipTrack(session, true);
        play.Click += () => _ = session?.TryTogglePlayPauseAsync();

        // kapağa ya da yazıya tıkla: büyük "şimdi çalan" kartı
        foreach (var el in new FrameworkElement[] { cover, text })
        {
            el.Cursor = Cursors.Hand;
            el.MouseLeftButtonDown += (_, e) => e.Handled = true;
            el.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (session != null) Flyout.Show(this, NowPlayingPanel.Build(session), 320); };
        }
        Padding = new Thickness(F(5), 0, F(6), 0);
        switch (style)
        {
            case "compact":
                text.Width = F(90);
                Child = Ui.Row(cover, text, play);
                break;
            case "mini":
                // ilerleme halkalı yuvarlak oynat düğmesi
                Width = H;
                Padding = new Thickness(F(3));
                var g = new Grid();
                g.Children.Add(progressRing);
                play.HorizontalAlignment = HorizontalAlignment.Center;
                g.Children.Add(play);
                Child = g;
                break;
            case "bar":
                text.Width = F(130);
                text.Margin = new Thickness(F(4), 0, F(8), 0);
                progressBar.Margin = new Thickness(0, F(4), 0, 0);
                text.Children.Add(progressBar);
                Child = Ui.Row(text, play, next);
                break;
            default:
                Child = Ui.Row(cover, text, prev, play, next);
                break;
        }
        managerTask ??= Init();
    }

    // Track changes briefly remove/recreate the Windows media session.
    internal static readonly MediaTransitionGrace Transition = new();
    internal static bool TransitionActive => Transition.Active(DateTime.UtcNow);
    internal static async Task SkipTrack(GlobalSystemMediaTransportControlsSession? target, bool next)
    {
        if(target==null)return;
        Transition.Hold(DateTime.UtcNow);
        try { if(next)await target.TrySkipNextAsync();else await target.TrySkipPreviousAsync(); }
        catch(Exception ex){App.Log(ex.Message);}
    }

    static string npTitle = "";
    static DateTime npFetched;
    static object? npSession;

    /// <summary>Balonun küçük hali için: o an çalan şarkı (widget dock'ta olmasa da çalışır).</summary>
    public static (string Title, bool Playing)? NowPlaying()
    {
        managerTask ??= Init();
        if (manager == null) return null;
        GlobalSystemMediaTransportControlsSession? s = null;
        try { s = manager.GetCurrentSession(); } catch { }
        if (s == null) return TransitionActive && npTitle.Length>0 ? (npTitle, false) : null;
        bool playing = false;
        try { playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { }
        if (!ReferenceEquals(s, npSession) || (DateTime.Now - npFetched).TotalSeconds > 4)
        {
            npSession = s;
            npFetched = DateTime.Now;
            _ = FetchTitle(s);
        }
        return (npTitle, playing);
    }

    static async Task FetchTitle(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if(TransitionActive && string.IsNullOrWhiteSpace(p.Title))return; // Retain the last island label during skips.
            npTitle = string.IsNullOrWhiteSpace(p.Artist) ? p.Title : $"{p.Title} — {p.Artist}";
        }
        catch { }
    }

    /// <summary>Şu an müzik çalan uygulamanın kimliği (ör. "Spotify.exe", "Chrome"); çalmıyorsa boş.</summary>
    public static string PlayingApp()
    {
        managerTask ??= Init();
        if (manager == null) return "";
        try
        {
            var s = manager.GetCurrentSession();
            if (s != null && s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return s.SourceAppUserModelId ?? "";
        }
        catch { }
        return "";
    }

    public static async Task<bool> PausePlaying()
    {
        managerTask ??= Init(); await managerTask;
        bool paused = false;
        if (manager == null) return false;
        foreach (var session in manager.GetSessions())
            if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                paused |= await session.TryPauseAsync();
        return paused;
    }

    static async Task Init()
    {
        try { manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch (Exception ex) { App.Log(ex); }
    }

    public override void Tick(DateTime now)
    {
        if (manager == null) return;
        GlobalSystemMediaTransportControlsSession? s = null;
        try { s = manager.GetCurrentSession(); } catch { }

        // Keep the last title, artwork, controls and slot during the six-second grace period.
        if(s==null && TransitionActive){SetIdle(false);return;}
        if (!ReferenceEquals(s, session))
        {
            if (session != null) session.MediaPropertiesChanged -= OnProps;
            session = s;
            if (session != null) session.MediaPropertiesChanged += OnProps;
            _ = LoadProps();
        }

        if (session == null)
        {
            title.Text = Localization.T("Çalan bir şey yok");
            artist.Text = "";
            play.GlyphText = "";
            SetIdle(true);
            return;
        }
        try
        {
            var status = session.GetPlaybackInfo().PlaybackStatus;
            bool playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            // duraklatılmışsa görünür kal (kaldığı yerden devam ettirilebilsin)
            SetIdle(!playing && status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
            play.GlyphText = playing ? "" : "";
            if (style is "mini" or "bar")
            {
                var tl = session.GetTimelineProperties();
                var pos = tl.Position + (playing ? DateTimeOffset.Now - tl.LastUpdatedTime : TimeSpan.Zero);
                double frac = tl.EndTime.TotalSeconds > 0 ? Math.Clamp(pos.TotalSeconds / tl.EndTime.TotalSeconds, 0, 1) : 0;
                progressBar.Value = frac;
                progressRing.Value = frac;
            }
        }
        catch { }
    }

    /// <summary>Müzik çalmıyorken widget'ı dock'ta gizle (seçicideki önizlemeye dokunma).</summary>
    internal void SetIdle(bool idle)
    {
        var slot = Ui.Ancestor<Slot>(this);
        if (slot == null) return;
        var v = idle && !TransitionActive && App.Settings.HideIdleMedia && !(App.Dock?.EditMode ?? false) ? Visibility.Collapsed : Visibility.Visible;
        if (slot.Visibility != v) slot.Visibility = v;
    }

    void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => _ = LoadProps());

    async Task LoadProps()
    {
        var s = session;
        if (s == null) { SetCover(null); return; }
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(s, session)) return;
            if(TransitionActive && string.IsNullOrWhiteSpace(p.Title))return; // Loading is not an empty player.
            title.Text = string.IsNullOrWhiteSpace(p.Title) ? "Bilinmeyen" : p.Title;
            artist.Text = string.IsNullOrWhiteSpace(p.Artist) ? AppName(s.SourceAppUserModelId) : p.Artist;
            ToolTip = $"{title.Text}\n{artist.Text}";
            ImageSource? img = null;
            if (p.Thumbnail != null)
            {
                using var ras = await p.Thumbnail.OpenReadAsync();
                using var stream = ras.AsStream();
                var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 96;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                img = bmp;
            }
            SetCover(img);
            MediaAccent.Update(img);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static string AppName(string aumid)
    {
        var n = aumid.Split('!')[0];
        n = Path.GetFileNameWithoutExtension(n);
        return n.Length > 0 ? char.ToUpper(n[0]) + n[1..] : "";
    }

    void SetCover(ImageSource? img)
    {
        if (img == null)
        {
            cover.Background = null;
            cover.SetResourceReference(BackgroundProperty, "Track");
            cover.Child = Ui.Glyph("", F(14), "FgDim");
        }
        else
        {
            cover.Child = null;
            cover.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        }
    }

    public override void Detach()
    {
        if (session != null) session.MediaPropertiesChanged -= OnProps;
        session = null;
    }
}

// ───────────────────────── Ses ─────────────────────────

class VolumeWidget : Widget
{
    readonly TextBlock name, pct, icon;
    readonly MiniSlider slider;
    bool dragging;
    int n;

    public VolumeWidget()
    {
        icon = Ui.Glyph("", F(11), "FgDim");
        icon.Margin = new Thickness(0, 0, F(5), 0);
        var iconHost = new Border { Child = icon, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        iconHost.MouseLeftButtonDown += (_, e) => e.Handled = true;
        iconHost.MouseLeftButtonUp += (_, e) => { e.Handled = true; Audio.ToggleMute(); Tick(DateTime.Now); };
        name = Ui.Text("Ses", F(11), "Fg", FontWeights.Bold);
        pct = Ui.Text("", F(10), "FgDim", FontWeights.SemiBold);
        var head = new DockPanel();
        DockPanel.SetDock(pct, Dock.Right);
        head.Children.Add(pct);
        head.Children.Add(Ui.Row(iconHost, name));
        slider = new MiniSlider { Height = F(14), Margin = new Thickness(0, F(4), 0, 0) };
        slider.ValueChanged += v => { Audio.Volume = (float)v; pct.Text = $"%{v * 100:0}"; };
        slider.MouseLeftButtonDown += (_, _) => dragging = true;
        slider.MouseLeftButtonUp += (_, _) => dragging = false;
        var col = Ui.Col(head, slider);
        col.Width = F(118);
        Child = col;
        MouseWheel += (_, e) =>
        {
            Audio.Volume = (float)Math.Clamp(Audio.Volume + Math.Sign(e.Delta) * 0.02, 0, 1);
            e.Handled = true;
            Tick(DateTime.Now);
        };
        MouseRightButtonUp += (_, e) => { e.Handled = true; Flyout.Show(this, SoundPanel.Build(), 340); };
        name.Cursor = Cursors.Hand;
        name.MouseLeftButtonDown += (_, e) => e.Handled = true;
        name.MouseLeftButtonUp += (_, e) => { e.Handled = true; Flyout.Show(this, SoundPanel.Build(), 340); };
        ToolTip = "Tekerle ses ayarla · simgeye tıkla: sessiz · ada tıkla ya da sağ tıkla: çıkışlar ve uygulama sesleri";
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        if (n++ % 5 == 0) Audio.Refresh();
        if (dragging) return;
        float v = Audio.Volume;
        bool muted = Audio.Muted;
        slider.Value = v;
        pct.Text = muted ? "sessiz" : $"%{v * 100:0}";
        icon.Text = muted ? "" : "";
        name.Text = Audio.DeviceName;
    }
}

/// <summary>Windows Core Audio (varsayılan çıkış aygıtı) için küçük COM köprüsü.</summary>
static class Audio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        int OpenPropertyStore(int access, out IPropertyStore store);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        int GetCount(out int count);
        int GetAt(int i, out PROPERTYKEY key);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float db, ref Guid ctx);
        int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        int GetMasterVolumeLevel(out float db);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
        int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        int GetChannelVolumeLevel(uint ch, out float db);
        int GetChannelVolumeLevelScalar(uint ch, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);

    static IAudioEndpointVolume? endpoint;
    static Guid ctx = Guid.Empty;
    public static string DeviceName { get; private set; } = "Ses";

    public static void Refresh()
    {
        try
        {
            var en = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            if (en.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var dev) != 0 || dev == null)
            {
                endpoint = null;
                DeviceName = "Ses aygıtı yok";
                return;
            }
            var iid = typeof(IAudioEndpointVolume).GUID;
            dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var obj);
            endpoint = (IAudioEndpointVolume)obj;

            if (dev.OpenPropertyStore(0, out var store) == 0)
            {
                var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                if (store.GetValue(ref key, out var pv) == 0)
                {
                    if (pv.vt == 31 /* VT_LPWSTR */)
                    {
                        var full = Marshal.PtrToStringUni(pv.p) ?? "Ses";
                        int i = full.IndexOf(" (", StringComparison.Ordinal);
                        DeviceName = i > 0 ? full[..i] : full;
                    }
                    PropVariantClear(ref pv);
                }
            }
        }
        catch (Exception ex) { App.Log(ex); endpoint = null; }
    }

    public static float Volume
    {
        get
        {
            if (endpoint == null) Refresh();
            float v = 0;
            try { endpoint?.GetMasterVolumeLevelScalar(out v); } catch { }
            return v;
        }
        set
        {
            if (endpoint == null) Refresh();
            try { endpoint?.SetMasterVolumeLevelScalar(Math.Clamp(value, 0, 1), ref ctx); } catch { }
        }
    }

    public static bool Muted
    {
        get
        {
            bool m = false;
            try { endpoint?.GetMute(out m); } catch { }
            return m;
        }
    }

    public static void ToggleMute()
    {
        try { endpoint?.SetMute(!Muted, ref ctx); } catch { }
    }

    // ── Mikrofon (varsayılan kayıt aygıtı)

    static IAudioEndpointVolume? Capture()
    {
        try
        {
            var en = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            if (en.GetDefaultAudioEndpoint(1 /* eCapture */, 0 /* eConsole */, out var dev) != 0 || dev == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            dev.Activate(ref iid, 23, IntPtr.Zero, out var obj);
            return (IAudioEndpointVolume)obj;
        }
        catch { return null; }
    }

    /// <summary>null: mikrofon yok.</summary>
    public static bool? MicMuted
    {
        get
        {
            var ep = Capture();
            if (ep == null) return null;
            try { ep.GetMute(out bool m); return m; } catch { return null; }
        }
    }

    public static void ToggleMic()
    {
        var ep = Capture();
        if (ep == null) return;
        try { ep.GetMute(out bool m); ep.SetMute(!m, ref ctx); } catch { }
    }
}

// Explicit clock inputs make the six-second hold and repeated skips deterministic to verify.
sealed class MediaTransitionGrace
{
    DateTime until;
    internal void Hold(DateTime now)=>until=now.AddSeconds(6);
    internal bool Active(DateTime now)=>now<until;
}
