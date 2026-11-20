using System.Diagnostics;

namespace Iskele;

/// <summary>
/// Ayarlar → Genel → "Iskele'yi kaldır". Kurulum klasöründeki IskeleSetup.exe'yi kaldırma kipinde başlatır,
/// sonra Iskele'yi normal yoldan kapatır (görev çubuğu geri gelir). Silme işini kurulum programı yapar;
/// Windows Ayarlar → Uygulamalar ile aynı yoldur.
/// </summary>
static class Uninstaller
{
    static string Dir => AppContext.BaseDirectory;
    static string SetupExe => Path.Combine(Dir, "IskeleSetup.exe");

    /// <summary>Yalnızca kurulum programıyla kurulmuş kopyalarda (işaret dosyası + setup yanında) etkin.</summary>
    public static bool Available => File.Exists(SetupExe) && File.Exists(Path.Combine(Dir, ".iskele-install"));

    public static void Run()
    {
        var answer = MessageBox.Show(
            Localization.T("Iskele bu bilgisayardan kaldırılsın mı?\n\nIskele kapanacak ve Windows görev çubuğu geri gelecek. Kişisel ayarlarınızı silip silmemeyi bir sonraki adımda seçebilirsiniz."),
            "Iskele", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            Process.Start(new ProcessStartInfo(SetupExe, "--uninstall --confirmed") { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
        }
        catch (Exception ex)
        {
            App.Log(ex);
            MessageBox.Show(Localization.T("Kaldırma başlatılamadı. Windows Ayarlar → Uygulamalar üzerinden kaldırabilirsiniz."), "Iskele");
            return;
        }
        App.Quit();
    }
}
