// Iskele kurulum programı.
// Windows'un yerleşik .NET Framework derleyicisi (csc.exe, C# 5) ile derlenir; bu yüzden
// string interpolation, ?. ve benzeri yeni dil özellikleri bilerek kullanılmaz.
//
// Kullanım:
//   IskeleSetup.exe                      Kurulum penceresi
//   IskeleSetup.exe --silent             Sessiz kurulum (onay penceresi yok)
//   IskeleSetup.exe --uninstall          Kaldırma (Windows Ayarlar > Uygulamalar buradan çağırır)
//   IskeleSetup.exe --uninstall --confirmed   Iskele Ayarlar'ından çağrılır; ilk onayı atlar
//   IskeleSetup.exe --extract-test <dir> Arşivi yalnızca verilen klasöre açar (test)
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

static class Setup
{
    const string Version = "1.0.0";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Iskele";


    static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Iskele"); } }
    static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Iskele"); } }
    static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Iskele.lnk"); } }

    // Installer language is selectable; remember it for uninstall.
    static bool English = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "tr";
    static string L(string tr, string en) { return English ? en : tr; }
    static readonly System.Collections.Generic.Dictionary<string, string> Translations = new System.Collections.Generic.Dictionary<string, string>
    {
        { "Iskele · Kurulum", "Iskele · Setup" },
        { "Iskele Kurulum", "Iskele Setup" },
        { "Masaüstünüz, sizin düzeniniz.", "Your desktop, your way." },
        { "Windows 10 / 11  ·  Sürüm ", "Windows 10 / 11  ·  Version " },
        { "Iskele dock önizlemesi", "Iskele dock preview" },
        { "Iskele'yi kurun", "Install Iskele" },
        { "Windows görev çubuğunun yerine geçen, widget'lı dock.", "A widget dock that replaces the Windows taskbar." },
        { "Yalnızca bu kullanıcı için kurulur. Yönetici izni gerekmez.", "Installs for this user only. No administrator permission required." },
        { "Gerekli .NET bileşenleri pakete dahildir.", "Required .NET components are included." },
        { "Kurulum konumu", "Installation folder" },
        { "Kurulumdan sonra Iskele'yi başlat", "Launch Iskele after installation" },
        { "Açık kaynak · GPL-3.0", "Open source · GPL-3.0" },
        { "Kur / Güncelle", "Install / Update" },
        { "Iskele başlatılamadı. Başlat menüsünden tekrar deneyebilirsiniz.", "Could not launch Iskele. You can try again from the Start menu." },
        { "Kuruluyor…", "Installing…" },
        { "Iskele kuruluyor", "Installing Iskele" },
        { "Dosyalar hazırlanıyor. Lütfen pencereyi açık tutun.", "Preparing files. Please keep this window open." },
        { "Iskele kullanıma hazır", "Iskele is ready" },
        { "Kurulum tamamlandı. Iskele'yi Başlat menüsünde bulabilirsiniz.", "Installation complete. You can find Iskele in the Start menu." },
        { "Kaldırmak için: Iskele Ayarlar > Genel veya Windows Ayarlar > Uygulamalar.", "Uninstall from Iskele Settings > General or Windows Settings > Apps." },
        { "Kurulum başarıyla tamamlandı.", "Installation completed successfully." },
        { "Tamamla", "Finish" },
        { "Bir gereksinim eksik", "A requirement is missing" },
        { ".NET 9 Desktop Runtime (x64) kurulmalı.", ".NET 9 Desktop Runtime (x64) must be installed." },
        { "Runtime’ı kurduktan sonra Kur / Güncelle düğmesine yeniden basın.", "After installing the runtime, click Install / Update again." },
        { ".NET 9 Desktop Runtime bulunamadı. İndirme sayfası açılsın mı?", ".NET 9 Desktop Runtime was not found. Open the download page?" },
        { "Kurulum tamamlanamadı", "Installation could not be completed" },
        { "Sorunu giderdikten sonra yeniden deneyebilirsiniz.", "Resolve the problem, then try again." },
        { "Yeniden dene", "Try again" },
        { "Iskele şu anda açık. Önce dock'a sağ tıklayıp Çıkış yapın, sonra kurulumu tekrar deneyin.", "Iskele is running. Right-click the dock and choose Exit, then retry setup." },
        { "Iskele bu bilgisayardan kaldırılsın mı?", "Uninstall Iskele from this computer?" },
        { "Geçersiz kaldırma çağrısı.", "Invalid uninstall command." },
        { "Iskele hâlâ açık. Dock'a sağ tıklayıp Çıkış yapın, sonra kaldırmayı tekrar deneyin.", "Iskele is still running. Right-click the dock and choose Exit, then retry uninstalling." },
        { "Beklenmeyen kurulum klasörü; hiçbir şey silinmedi.", "Unexpected installation folder; nothing was deleted." },
        { "Iskele kaldırıldı.", "Iskele was uninstalled." },
        { "Kişisel ayarlarınız, notlarınız ve ekran süresi verileriniz de silinsin mi?", "Delete your personal settings, notes and screen time data as well?" },
        { "Hayır derseniz ileride yeniden kurduğunuzda kaldığınız yerden devam edersiniz.", "Choose No to keep your data for a future installation." },
        { " Kişisel veriler de silindi.", " Personal data was also deleted." },
        { " Kişisel veriler silinemedi: ", " Could not delete personal data: " },
        { " Kişisel ayarlar korundu.", " Personal settings were kept." },
        { "Geçersiz arşiv yolu.", "Invalid archive path." }
    };
    static string T(string text)
    {
        string translated;
        return English && Translations.TryGetValue(text, out translated) ? translated : text;
    }
    static void TranslateControls(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is ComboBox) continue; // Keep language selection independent of translated copy.
            string canonical = c.Text;
            foreach (var pair in Translations) if (canonical == pair.Value) { canonical = pair.Key; break; }
            c.Text = T(canonical);
            if (c.AccessibleName != null)
            {
                string name = c.AccessibleName;
                foreach (var pair in Translations) if (name == pair.Value) { name = pair.Key; break; }
                c.AccessibleName = T(name);
            }
            TranslateControls(c);
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        try { string saved = Path.Combine(Root, ".iskele-language"); if (File.Exists(saved)) English = File.ReadAllText(saved).Trim() == "en"; } catch { }
        if (Has(args, "--lang=en")) English = true;
        if (Has(args, "--lang=tr")) English = false;
        bool silent = Has(args, "--silent") || Has(args, "/S");
        try
        {
            if (args.Length == 2 && args[0] == "--extract-test") { Extract(Path.GetFullPath(args[1])); return 0; }
            Application.EnableVisualStyles();

            if (Has(args, "--uninstall")) return StartUninstall(Has(args, "--confirmed"));
            if (args.Length >= 2 && args[0] == "--remove") return Remove(args[1]);
            if (silent) { Install(); return 0; }

            ShowInstaller();
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length > 0 && args[0] == "--extract-test") { Console.Error.WriteLine(ex); return 1; }
            if (!silent) MessageBox.Show(ex.Message, T("Iskele Kurulum"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static bool Has(string[] args, string flag)
    {
        foreach (var a in args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ───────── kurulum ─────────

    // Scoped installer UI refresh; install/uninstall destinations stay unchanged.
    static Label CopyLabel(string text, int x, int y, int width, int height, float size, Color color, bool bold)
    {
        return new Label { Text = text, Left = x, Top = y, Width = width, Height = height,
            ForeColor = color, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            UseMnemonic = false };
    }

    static void ShowInstaller()
    {
        Color ink = Color.FromArgb(23, 31, 49), muted = Color.FromArgb(80, 94, 115);
        Color accent = Color.FromArgb(28, 101, 215), navy = Color.FromArgb(15, 23, 42);
        using (var form = new Form
        {
            Text = T("Iskele · Kurulum"), ClientSize = new Size(740, 552),
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false, MinimizeBox = true, BackColor = Color.White,
            Font = new Font("Segoe UI", 10f), AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96f, 96f)
        })
        {
            // Installer uses the approved logo and native EXE icon.
            form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            var header = new Panel { Dock = DockStyle.Top, Height = 168, BackColor = navy };
            header.Controls.Add(CopyLabel("Iskele", 112, 25, 232, 48, 28f, Color.White, true));
            header.Controls.Add(CopyLabel(T("Masaüstünüz, sizin düzeniniz."), 34, 96, 310, 26, 12f, Color.FromArgb(205, 217, 239), false));
            var versionLabel = CopyLabel(T("Windows 10 / 11  ·  Sürüm ") + Version, 34, 125, 310, 23, 9f, Color.FromArgb(177, 194, 220), false);
            header.Controls.Add(versionLabel);
            var preview = new PictureBox { Left = 366, Top = 64, Width = 346, Height = 86,
                SizeMode = PictureBoxSizeMode.Zoom, BackColor = navy, TabStop = false,
                AccessibleName = T("Iskele dock önizlemesi") };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("dock-preview.png"))
            {
                if (stream != null)
                using (var original = Image.FromStream(stream))
                {
                    var crop = new Bitmap(610, 110);
                    using (var g = Graphics.FromImage(crop))
                    {
                        g.Clear(navy);
                        g.DrawImage(original, new Rectangle(0, 0, 610, 110), new Rectangle(520, 130, 610, 110), GraphicsUnit.Pixel);
                    }
                    preview.Image = crop;
                }
            }
            header.Controls.Add(preview);
            using (var brandStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("iskele-brand.png"))
            using (var brandImage = Image.FromStream(brandStream))
            {
                var brand = new PictureBox { Left = 28, Top = 18, Width = 76, Height = 76, SizeMode = PictureBoxSizeMode.Zoom, TabStop = false, Image = new Bitmap(brandImage), AccessibleName = "Iskele" };
                header.Controls.Add(brand);
                form.Disposed += delegate { brand.Image.Dispose(); };
            }
            var heading = CopyLabel(T("Iskele'yi kurun"), 32, 194, 670, 36, 20f, ink, true);
            var description = CopyLabel(T("Windows görev çubuğunun yerine geçen, widget'lı dock."), 34, 238, 670, 25, 10.5f, muted, false);
            var details = CopyLabel(T("Yalnızca bu kullanıcı için kurulur. Yönetici izni gerekmez."), 34, 279, 670, 24, 10f, ink, false);
            var runtime = CopyLabel(T("Gerekli .NET bileşenleri pakete dahildir."), 34, 307, 670, 24, 10f, muted, false);
            var pathLabel = CopyLabel(T("Kurulum konumu"), 34, 351, 660, 23, 9f, muted, true);
            var path = new TextBox { Text = Root, Left = 34, Top = 379, Width = 672,
                ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White,
                ForeColor = ink, AccessibleName = T("Kurulum konumu"), TabIndex = 0 };
            var launch = new CheckBox { Text = T("Kurulumdan sonra Iskele'yi başlat"), Left = 32, Top = 418,
                Width = 460, Height = 28, Checked = true, ForeColor = ink, TabIndex = 1 };
            var progress = new ProgressBar { Left = 34, Top = 455, Width = 672, Height = 5,
                Visible = false, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24 };
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Color.FromArgb(245, 247, 251) };
            var license = new LinkLabel { Text = T("Açık kaynak · GPL-3.0"), Left = 34, Top = 28, Width = 240,
                Height = 24, LinkColor = muted, ActiveLinkColor = accent, VisitedLinkColor = muted, TabIndex = 0 };
            license.LinkClicked += delegate { OpenUrl("https://www.gnu.org/licenses/gpl-3.0.html"); };
            var button = new Button { Text = T("Kur / Güncelle"), Left = 514, Top = 16, Width = 192, Height = 44,
                BackColor = accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Cursor = Cursors.Hand, TabIndex = 1 };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(21, 80, 176);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(17, 64, 145);
            footer.Controls.AddRange(new Control[] { license, button });
            bool busy = false, complete = false;
            var language = new ComboBox { Left = 546, Top = 20, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Dil / Language", TabIndex = 0 };
            language.Items.AddRange(new object[] { "Türkçe", "English" });
            language.SelectedIndex = English ? 1 : 0;
            header.Controls.Add(language);
            language.SelectedIndexChanged += delegate
            {
                English = language.SelectedIndex == 1;
                TranslateControls(form);
                form.Text = T("Iskele · Kurulum");
                versionLabel.Text = T("Windows 10 / 11  ·  Sürüm ") + Version;
            };
            form.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            button.Click += async delegate
            {
                if (complete)
                {
                    try
                    {
                        if (launch.Checked) Process.Start(new ProcessStartInfo(Path.Combine(Root, "Iskele.exe")) { WorkingDirectory = Root });
                        form.Close();
                    }
                    catch (Exception ex) { MessageBox.Show(form, L("Iskele başlatılamadı. Başlat menüsünden tekrar deneyebilirsiniz.\n\n", "Could not launch Iskele. You can try again from the Start menu.\n\n") + ex.Message, "Iskele", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                    return;
                }
                busy = true; button.Enabled = false; launch.Enabled = false; language.Enabled = false;
                button.Text = T("Kuruluyor…"); heading.Text = T("Iskele kuruluyor");
                description.Text = T("Dosyalar hazırlanıyor. Lütfen pencereyi açık tutun.");
                progress.Visible = true;
                try
                {
                    // Preserve an STA thread for WScript.Shell shortcut creation; keep the UI responsive.
                    var completion = new System.Threading.Tasks.TaskCompletionSource<bool>();
                    var worker = new System.Threading.Thread(delegate()
                    {
                        try { Install(); completion.SetResult(true); }
                        catch (Exception ex) { completion.SetException(ex); }
                    });
                    worker.SetApartmentState(System.Threading.ApartmentState.STA);
                    worker.IsBackground = true; worker.Start();
                    await completion.Task;
                    complete = true;
                    heading.Text = T("Iskele kullanıma hazır");
                    description.Text = T("Kurulum tamamlandı. Iskele'yi Başlat menüsünde bulabilirsiniz.");
                    details.Text = T("Kaldırmak için: Iskele Ayarlar > Genel veya Windows Ayarlar > Uygulamalar.");
                    runtime.Text = T("Kurulum başarıyla tamamlandı.");
                    runtime.ForeColor = Color.FromArgb(24, 112, 65);
                    button.Text = T("Tamamla");
                }
                catch (Exception ex)
                {
                    heading.Text = T("Kurulum tamamlanamadı");
                    description.Text = T("Sorunu giderdikten sonra yeniden deneyebilirsiniz.");
                    MessageBox.Show(form, ex.Message, T("Iskele Kurulum"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    button.Text = T("Yeniden dene");
                }
                finally { busy = false; progress.Visible = false; button.Enabled = true; launch.Enabled = true; language.Enabled = true; }
            };
            form.AcceptButton = button;
            form.Controls.AddRange(new Control[] { header, heading, description, details, runtime, pathLabel, path, launch, progress, footer });
            Application.Run(form);
            if (preview.Image != null) preview.Image.Dispose();
        }
    }



    static void Install()
    {
        if (Process.GetProcessesByName("Iskele").Length > 0)
            throw new Exception(T("Iskele şu anda açık. Önce dock'a sağ tıklayıp Çıkış yapın, sonra kurulumu tekrar deneyin."));
        // Önceki bir oturumdan kalmış görev çubuğu durumu varsa, dosyaların üzerine yazmadan önce geri yükle.
        RestoreTaskbarIfNeeded(Root);
        // Self-contained payload carries its own .NET runtime.

        Extract(Root);
        File.WriteAllText(Path.Combine(Root, ".iskele-language"), English ? "en" : "tr");
        string setup = Path.Combine(Root, "IskeleSetup.exe");
        if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), setup, StringComparison.OrdinalIgnoreCase))
            File.Copy(Application.ExecutablePath, setup, true);
        File.WriteAllText(Path.Combine(Root, ".iskele-install"), "Iskele " + Version);

        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { StartMenuLink });
        link.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Path.Combine(Root, "Iskele.exe") });
        link.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Root });
        link.GetType().InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Iskele dock" });
        link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "Iskele");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "Iskele contributors");
            key.SetValue("InstallLocation", Root);
            key.SetValue("DisplayIcon", Path.Combine(Root, "Iskele.exe"));
            key.SetValue("UninstallString", "\"" + setup + "\" --uninstall");
            key.SetValue("QuietUninstallString", "\"" + setup + "\" --uninstall --confirmed");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(DirectorySize(Root) / 1024), RegistryValueKind.DWord);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        }
    }

    // ───────── kaldırma ─────────

    static int StartUninstall(bool confirmed)
    {
        if (!confirmed && MessageBox.Show(T("Iskele bu bilgisayardan kaldırılsın mı?"), "Iskele",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
        // Kurulum klasöründeki kendi dosyamızı silebilmek için geçici bir kopyadan devam ederiz.
        string copy = Path.Combine(Path.GetTempPath(), "Iskele-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(Application.ExecutablePath, copy);
        Process.Start(new ProcessStartInfo(copy, "--remove " + Process.GetCurrentProcess().Id + (English ? " --lang=en" : " --lang=tr")) { UseShellExecute = false, CreateNoWindow = true });
        return 0;
    }

    /// <summary>Iskele'nin kaydettiği görev çubuğu/çalışma alanı durumu duruyorsa "Iskele.exe --restore" ile geri yükler.</summary>
    static void RestoreTaskbarIfNeeded(string root)
    {
        try
        {
            if (!File.Exists(Path.Combine(DataDir, "taskbar-state.txt")) && !File.Exists(Path.Combine(DataDir, "workarea-state.txt"))) return;
            string exe = Path.Combine(root, "Iskele.exe");
            if (!File.Exists(exe)) return;
            using (var p = Process.Start(new ProcessStartInfo(exe, "--restore") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root }))
                if (p != null) p.WaitForExit(15000);
        }
        catch { }
    }

    static int Remove(string parentArg)
    {
        int parent;
        if (!int.TryParse(parentArg, out parent)) throw new Exception(T("Geçersiz kaldırma çağrısı."));
        try { using (var p = Process.GetProcessById(parent)) p.WaitForExit(10000); }
        catch (ArgumentException) { }

        // Ayarlar'dan kaldırıldıysa Iskele kendini kapatıyor (görev çubuğunu geri getirip); biraz bekle.
        var until = DateTime.UtcNow.AddSeconds(20);
        while (Process.GetProcessesByName("Iskele").Length > 0 && DateTime.UtcNow < until) System.Threading.Thread.Sleep(250);
        if (Process.GetProcessesByName("Iskele").Length > 0)
            throw new Exception(T("Iskele hâlâ açık. Dock'a sağ tıklayıp Çıkış yapın, sonra kaldırmayı tekrar deneyin."));

        string canonical = Path.GetFullPath(Root);
        if (!File.Exists(Path.Combine(canonical, ".iskele-install"))) throw new Exception(T("Beklenmeyen kurulum klasörü; hiçbir şey silinmedi."));
        // Iskele ve bekçisi beklenmedik biçimde kapandıysa görev çubuğu/çalışma alanı durumu geri yüklenmemiş olabilir.
        // Program ve (isteğe bağlı) veriler silinmeden önce mutlaka geri yükle; sonra bunu yapacak kimse kalmaz.
        RestoreTaskbarIfNeeded(canonical);
        Directory.Delete(canonical, true);
        if (File.Exists(StartMenuLink)) File.Delete(StartMenuLink);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
        {
            if (run != null && Convert.ToString(run.GetValue("Iskele")).IndexOf(canonical, StringComparison.OrdinalIgnoreCase) >= 0)
                run.DeleteValue("Iskele", false);
        }

        string message = T("Iskele kaldırıldı.");
        if (Directory.Exists(DataDir))
        {
            if (MessageBox.Show(L("Kişisel ayarlarınız, notlarınız ve ekran süresi verileriniz de silinsin mi?\n\n", "Delete your personal settings, notes and screen time data as well?\n\n") + DataDir +
                    L("\n\nHayır derseniz ileride yeniden kurduğunuzda kaldığınız yerden devam edersiniz.", "\n\nChoose No to keep your data for a future installation."),
                    "Iskele", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                try { Directory.Delete(DataDir, true); message += T(" Kişisel veriler de silindi."); }
                catch (Exception ex) { message += T(" Kişisel veriler silinemedi: ") + ex.Message; }
            }
            else message += T(" Kişisel ayarlar korundu.");
        }
        MessageBox.Show(message, "Iskele", MessageBoxButtons.OK, MessageBoxIcon.Information);
        ScheduleSelfDelete();
        return 0;
    }

    static void ScheduleSelfDelete()
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & del /q \"" + Application.ExecutablePath + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch { }
    }

    // ───────── yardımcılar ─────────

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    static long DirectorySize(string dir)
    {
        long total = 0;
        try { foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) total += new FileInfo(f).Length; } catch { }
        return total;
    }

    static void Extract(string target)
    {
        Directory.CreateDirectory(target);
        string prefix = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            foreach (var entry in zip.Entries)
            {
                string path = Path.GetFullPath(Path.Combine(target, entry.FullName));
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new Exception(T("Geçersiz arşiv yolu."));
                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var input = entry.Open()) using (var output = File.Create(path)) input.CopyTo(output);
            }
    }
}
