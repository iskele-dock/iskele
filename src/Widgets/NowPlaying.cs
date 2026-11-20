using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace Iskele;

/// <summary>Büyük "şimdi çalan" kartı (hedef 60): büyük kapak, sürüklenebilir ilerleme, 10 sn ileri/geri, kontroller.</summary>
static class NowPlayingPanel
{
    public static UIElement Build(GlobalSystemMediaTransportControlsSession s)
    {
        var p = new StackPanel();
        var cover = new Border { Width = 292, Height = 292, CornerRadius = new CornerRadius(14), ClipToBounds = true };
        cover.SetResourceReference(Border.BackgroundProperty, "Track");
        cover.Child = Ui.Glyph("", 48, "FgDim");
        var title = Ui.Text("", 17, "Fg", FontWeights.Bold);
        var artist = Ui.Text("", 13, "FgDim");
        title.Margin = new Thickness(0, 14, 0, 0);
        var seek = new MiniSlider { Height = 14, Margin = new Thickness(0, 12, 0, 2) };
        var times = new DockPanel();
        var tPos = Ui.Text("0:00", 11, "FgDim"); var tLen = Ui.Text("0:00", 11, "FgDim");
        DockPanel.SetDock(tLen, Dock.Right);
        times.Children.Add(tLen); times.Children.Add(tPos);

        var back = new IconButton("", 15, 40) { ToolTip = "10 sn geri" };
        var prev = new IconButton("", 17, 44);
        var play = new IconButton("", 20, 56, accent: true);
        var next = new IconButton("", 17, 44);
        var fwd = new IconButton("", 15, 40) { ToolTip = "10 sn ileri" };
        var row = Ui.Row(back, prev, play, next, fwd);
        row.HorizontalAlignment = HorizontalAlignment.Center;
        row.Margin = new Thickness(0, 8, 0, 0);
        foreach (var b in new[] { back, prev, play, next, fwd }) b.Margin = new Thickness(6, 0, 6, 0);

        p.Children.Add(cover); p.Children.Add(title); p.Children.Add(artist); p.Children.Add(seek); p.Children.Add(times); p.Children.Add(row);

        bool dragging = false;
        TimeSpan length = TimeSpan.Zero;
        static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        TimeSpan Position()
        {
            var tl = s.GetTimelineProperties();
            bool playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var pos = tl.Position + (playing ? DateTimeOffset.Now - tl.LastUpdatedTime : TimeSpan.Zero);
            length = tl.EndTime;
            return pos < TimeSpan.Zero ? TimeSpan.Zero : pos > length && length > TimeSpan.Zero ? length : pos;
        }

        void Paint()
        {
            try
            {
                var pos = Position();
                bool playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                play.GlyphText = playing ? "" : "";
                if (!dragging && length.TotalSeconds > 0) seek.Value = pos.TotalSeconds / length.TotalSeconds;
                tPos.Text = Fmt(dragging ? TimeSpan.FromSeconds(seek.Value * length.TotalSeconds) : pos);
                tLen.Text = Fmt(length);
            }
            catch { }
        }

        async Task Seek(TimeSpan to)
        {
            try { await s.TryChangePlaybackPositionAsync(Math.Max(0, (long)to.TotalMilliseconds) * 10_000); } catch { }
        }

        seek.MouseLeftButtonDown += (_, _) => dragging = true;
        seek.ValueChanged += _ => { if (dragging) tPos.Text = Fmt(TimeSpan.FromSeconds(seek.Value * length.TotalSeconds)); };
        seek.MouseLeftButtonUp += async (_, _) => { dragging = false; await Seek(TimeSpan.FromSeconds(seek.Value * length.TotalSeconds)); };
        back.Click += () => _ = Seek(Position() - TimeSpan.FromSeconds(10));
        fwd.Click += () => _ = Seek(Position() + TimeSpan.FromSeconds(10));
        prev.Click += () => _ = MediaWidget.SkipTrack(s, false);
        next.Click += () => _ = MediaWidget.SkipTrack(s, true);
        play.Click += () => _ = s.TryTogglePlayPauseAsync();

        async Task LoadProps()
        {
            try
            {
                var mp = await s.TryGetMediaPropertiesAsync();
                title.Text = string.IsNullOrWhiteSpace(mp.Title) ? "Bilinmeyen" : mp.Title;
                artist.Text = mp.Artist ?? "";
                if (mp.Thumbnail != null)
                {
                    using var ras = await mp.Thumbnail.OpenReadAsync();
                    var ms = new MemoryStream();
                    await ras.AsStream().CopyToAsync(ms);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.DecodePixelWidth = 600; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
                    cover.Child = null;
                    cover.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                }
            }
            catch (Exception ex) { App.Log(ex.Message); }
        }

        void OnProps(GlobalSystemMediaTransportControlsSession ss, MediaPropertiesChangedEventArgs ea) => p.Dispatcher.BeginInvoke(() => { _ = LoadProps(); });
        s.MediaPropertiesChanged += OnProps;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => Paint(), p.Dispatcher);
        p.Unloaded += (_, _) => { timer.Stop(); s.MediaPropertiesChanged -= OnProps; };
        _ = LoadProps();
        Paint();
        return p;
    }
}

/// <summary>Albüm kapağı rengi vurgu rengi olsun (hedef 59, isteğe bağlı). Kalıcı vurgu ayarına dokunmaz.</summary>
static class MediaAccent
{
    public static void Update(ImageSource? img)
    {
        if (!App.Settings.AccentFromArt) return;
        if (img is not BitmapSource src) { Theme.Apply(App.Settings.Theme); return; }
        var c = Dominant(src);
        if (c != null) Theme.SetAccent(c.Value);
    }

    /// <summary>Doygun ve çok koyu/açık olmayan piksellerin ortalaması; uygun renk yoksa null.</summary>
    static Color? Dominant(BitmapSource src)
    {
        var bmp = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = bmp.PixelWidth, h = bmp.PixelHeight, stride = w * 4;
        var px = new byte[h * stride];
        bmp.CopyPixels(px, stride, 0);
        double r = 0, g = 0, b = 0, wsum = 0;
        for (int i = 0; i < px.Length; i += 16)
        {
            double B = px[i], G = px[i + 1], R = px[i + 2];
            double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
            double sat = max == 0 ? 0 : (max - min) / max;
            if (max < 50 || max > 245 && sat < 0.1) continue;
            double weight = sat * sat + 0.02;
            r += R * weight; g += G * weight; b += B * weight; wsum += weight;
        }
        if (wsum < 1) return null;
        var col = Color.FromRgb((byte)(r / wsum), (byte)(g / wsum), (byte)(b / wsum));
        // okunur kalsın: çok koyuysa biraz aç
        double lum = 0.299 * col.R + 0.587 * col.G + 0.114 * col.B;
        if (lum < 90) col = Color.FromRgb((byte)Math.Min(255, col.R + 60), (byte)Math.Min(255, col.G + 60), (byte)Math.Min(255, col.B + 60));
        return col;
    }
}
