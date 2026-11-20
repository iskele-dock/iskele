namespace Iskele;

/// <summary>
/// Herhangi bir uygulama, dosya ya da klasörün tek başına widget'ı (14. hedef). Kimliği "launch:&lt;yol&gt;".
/// Simge (fotoğraf/videoda küçük resim) + ad; tıklayınca açar. Klasöre dosya sürüklenirse içine kopyalar.
/// </summary>
class LaunchWidget : Widget
{
    const string Prefix = "launch:";
    readonly string path;

    public static bool IsLaunch(string id) => id.StartsWith(Prefix, StringComparison.Ordinal);
    public static string PathOf(string id) => id[Prefix.Length..];
    public static string IdFor(string path) => Prefix + path;
    public static string NameOf(string id)
    {
        var p = PathOf(id);
        var n = Path.GetFileNameWithoutExtension(p.TrimEnd('\\'));
        return n.Length > 0 ? n : p;
    }

    public LaunchWidget(string path)
    {
        this.path = path;
        bool exists = File.Exists(path) || Directory.Exists(path);
        double s = H * 0.62;
        UIElement icon = Thumbs.IsMedia(path) && Thumbs.Get(path, 128) is { } th
            ? Thumbs.Rounded(th, s)
            : IconCache.Get(path) is { } ic ? new Image { Source = ic, Width = s, Height = s } : Ui.Glyph("", F(16));
        var name = Ui.RawText(NameOf(IdFor(path)), F(11.5), exists ? "Fg" : "FgDim", FontWeights.SemiBold);
        name.MaxWidth = F(110);
        name.Margin = new Thickness(F(8), 0, 0, 0);
        Padding = new Thickness(F(6), 0, F(12), 0);
        Child = Ui.Row(icon, name);
        ToolTip = exists ? path : path + " (bulunamadı)";
        OnClick(() => { if (File.Exists(path) || Directory.Exists(path)) App.Open(path); });

        if (Directory.Exists(path))
        {
            AllowDrop = true;
            Drop += (_, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
                foreach (var f in files)
                {
                    try
                    {
                        var dest = Path.Combine(path, Path.GetFileName(f.TrimEnd('\\')));
                        if (File.Exists(f) && !File.Exists(dest)) File.Copy(f, dest);
                    }
                    catch (Exception ex) { App.Log(ex.Message); }
                }
                e.Handled = true;
            };
        }
    }
}
