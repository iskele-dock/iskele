using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
namespace Iskele;
static class Diagnostics
{
    public static string Redact(string text)
    {
        text = text.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"https?://[^\s\""<>]+", "[URL gizlendi]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?i)(authorization|token|api[_-]?key|password|secret)\s*[:=]\s*[^\s,;]+", "$1=[gizlendi]");
        return text;
    }
    public static string Report(bool includeLog)
    {
        var text = $"Iskele {typeof(App).Assembly.GetName().Version}\n{DateTime.Now:O}\n" +
            $"OS: {RuntimeInformation.OSDescription}\nRuntime: {RuntimeInformation.FrameworkDescription}\n" +
            $"Mimari: {RuntimeInformation.ProcessArchitecture}\nEkran sayısı: {Monitors.All().Count}\n" +
            $"Dock modu: {App.Settings.Mode}\nWidget sayısı: {App.Settings.Widgets.Count}\n";
        if (includeLog)
        {
            var file = Path.Combine(Settings.Dir, "log.txt");
            if (File.Exists(file)) text += "\nSon günlük satırları (göndermeden önce inceleyin):\n" + string.Join("\n", File.ReadLines(file).TakeLast(100));
        }
        return Redact(text);
    }
    public static void Open()
    {
        var w = new FeaturePanel("Geri bildirim ve hata raporu", 620, 540);
        var p = new StackPanel { Margin = new Thickness(20) };
        var include = new CheckBox { Content = "Son 100 günlük satırını ekle (kişisel bilgi içerebilir)", Margin = new Thickness(0, 0, 0, 12) };
        include.SetResourceReference(Control.ForegroundProperty, "Fg");
        p.Children.Add(include);
        var preview = new TextBox { Text = Report(false), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 270, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        preview.SetResourceReference(Control.BackgroundProperty, "Track");
        preview.SetResourceReference(Control.ForegroundProperty, "Fg");
        preview.FontSize = 12; preview.Padding = new Thickness(10); preview.BorderThickness = new Thickness(0);
        p.Children.Add(preview);
        include.Checked += (_, _) => preview.Text = Report(true);
        include.Unchecked += (_, _) => preview.Text = Report(false);
        var status = Ui.Text("Rapor yalnızca bilgisayarınıza kaydedilir.", 12, "FgDim"); p.Children.Add(status);
        var save = FeaturePanel.ActionButton("Raporu kaydet…");
        save.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "Iskele-rapor.txt", Filter = "Metin dosyası|*.txt" };
            if (dialog.ShowDialog(w) != true) return;
            try { File.WriteAllText(dialog.FileName, preview.Text); status.Text = "Rapor kaydedildi."; }
            catch (Exception ex) { status.Text = "Kaydedilemedi: " + ex.Message; }
        };
        p.Children.Add(save); w.Host.Children.Add(p); w.Show();
    }
}
