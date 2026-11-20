using System.Runtime.InteropServices;

namespace Iskele;

/// <summary>
/// Windows görev çubuğunu gizler ve geri getirir. Windows dosyalarına dokunmaz.
///
/// İki yöntem:
/// - keepSpace = true (varsayılan, şerit modu): yalnızca görev çubuğu penceresi gizlenir; ayırdığı ekran alanı
///   aynen kalır ve dock tam o alana oturur. Çalışma alanı hiç değişmediği için masaüstü simgeleri ve pencereler
///   yerinden oynamaz.
/// - keepSpace = false (balon / üst kenar / otomatik gizle): görev çubuğu otomatik gizlemeye alınır ve alanı boşalır.
///   Bu çalışma alanını değiştirir.
///
/// Güvence: değişiklikten ÖNCE özgün durum bir dosyaya yazılır; uygulama çökse bile bir sonraki açılışta
/// (ya da bekçi süreçle, ya da "Iskele.exe --restore" ile) özgün durum geri yüklenir.
/// </summary>
static class TaskbarGuard
{
    static string StateFile => Path.Combine(Settings.Dir, "taskbar-state.txt");

    static IEnumerable<IntPtr> TrayWindows()
    {
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        var h = IntPtr.Zero;
        while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            yield return h;
    }

    static APPBARDATA Data(IntPtr tray) =>
        new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = tray };

    public static bool IsHiddenByUs => File.Exists(StateFile);

    static int OriginalState()
    {
        if (File.Exists(StateFile) && int.TryParse(File.ReadAllText(StateFile).Trim(), out int s)) return s;
        return -1;
    }

    /// <summary>Görev çubuğunun ekrandaki yeri (fiziksel piksel) ve kenarı. Otomatik gizlemedeyse null.</summary>
    public static (RECT Rect, uint Edge)? Position()
    {
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return null;
        var d = Data(tray);
        if (((int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref d) & Native.ABS_AUTOHIDE) != 0) return null;
        if (Native.SHAppBarMessage(5 /* ABM_GETTASKBARPOS */, ref d) == IntPtr.Zero) return null;
        return (d.rc, d.uEdge);
    }

    /// <summary>
    /// Gizle/geri yükle işlemleri tüm Iskele süreçleri (ana süreç, bekçi, yeni açılan kopya) arasında sıraya girer.
    /// Yoksa eski kopyanın bekçisi geri yüklemeyi bitirmeden yeni kopya "otomatik gizle açık" durumunu özgün ayar
    /// sanıp kaydedebiliyor ve görev çubuğu kalıcı olarak otomatik gizlemede kalıyordu.
    /// </summary>
    // Never execute a taskbar mutation without ownership of the interprocess lock.
    static T Locked<T>(Func<T> f) => ExecuteLocked(@"Local\Iskele.TaskbarGuard", TimeSpan.FromSeconds(10), f);
    internal static T ExecuteLocked<T>(string name, TimeSpan timeout, Func<T> f)
    {
        using var m = new Mutex(false, name);
        bool got = false;
        try
        {
            try { got = m.WaitOne(timeout); } catch (AbandonedMutexException) { got = true; }
            if (!got) throw new TimeoutException("Taskbar state is busy; no changes were made.");
            return f();
        }
        finally { if (got) m.ReleaseMutex(); }
    }

    static string OwnerFile => Path.Combine(Settings.Dir, "taskbar-owner.txt");
    // Ownership check and restore are one locked operation, including late ProcessExit.
    public static void RestoreOwned(string generation) => Locked(() =>
        RestoreGeneration(OwnerFile, generation, () => RestoreCore(false)));
    internal static bool RestoreGeneration(string ownerFile, string generation, Action restore)
    {
        var owner = File.Exists(ownerFile) ? File.ReadAllText(ownerFile).Trim() : "";
        if (owner != generation || (generation.Length == 0 && owner.Length > 0)) return false;
        restore(); return true;
    }

    public static void Hide(bool keepSpace) => Locked(() => { HideCore(keepSpace); return 0; });

    public static void Restore(bool force = false) => Locked(() => { RestoreCore(force); return 0; });

    static string AreaFile => Path.Combine(Settings.Dir, "workarea-state.txt");

    /// <summary>
    /// Görev çubuğunun bıraktığı alanı uygulamalara ver, ama masaüstüne HABER VERMEDEN: çalışma alanı sessizce
    /// (SPIF_SENDCHANGE olmadan) tam ekran yapılır. Büyütülen pencereler yeni alanı kullanır; masaüstü simge
    /// ızgarası değişikliği duymadığı için simgeler yerinden oynamaz. Zaten büyütülmüş pencereler aşağı uzatılır.
    /// </summary>
    public static void UseFullArea(bool on) => Locked(() => { UseFullAreaCore(on); return 0; });

    static void UseFullAreaCore(bool on)
    {
        var cur = new RECT();
        if (!Native.SystemParametersInfo(0x0030, 0, ref cur, 0)) return;
        int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
        if (on)
        {
            var full = new RECT { Left = 0, Top = 0, Right = sw, Bottom = sh };
            if (cur.Left == full.Left && cur.Top == full.Top && cur.Right == full.Right && cur.Bottom == full.Bottom) return;
            if (!File.Exists(AreaFile)) AtomicFile.Write(AreaFile, $"{cur.Left},{cur.Top},{cur.Right},{cur.Bottom}", backup: false);
            Native.SystemParametersInfo(0x002F /* SPI_SETWORKAREA */, 0, ref full, 0);
            StretchMaximized(cur, full);
        }
        else
        {
            if (!File.Exists(AreaFile)) return;
            // A damaged work-area file must not abort taskbar visibility recovery.
            if (!TryParseArea(File.ReadAllText(AreaFile), out var old)) { App.Log("Invalid saved work area; taskbar visibility will still be restored."); return; }
            Native.SystemParametersInfo(0x002F, 0, ref old, 0);
            StretchMaximized(cur, old);
            File.Delete(AreaFile);
        }
    }

    internal static bool TryParseArea(string text, out RECT area)
    {
        area = default; var fields = text.Split(',');
        if (fields.Length != 4 || !int.TryParse(fields[0], out int left) || !int.TryParse(fields[1], out int top) ||
            !int.TryParse(fields[2], out int right) || !int.TryParse(fields[3], out int bottom) || right <= left || bottom <= top) return false;
        area = new RECT { Left = left, Top = top, Right = right, Bottom = bottom }; return true;
    }

    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);

    static void StretchMaximized(RECT from, RECT to)
    {
        int dTop = to.Top - from.Top, dBottom = to.Bottom - from.Bottom;
        if (dTop == 0 && dBottom == 0) return;
        Native.EnumWindows((h, _) =>
        {
            if (Native.IsWindowVisible(h) && IsZoomed(h) && Native.GetWindowRect(h, out var r))
                Native.SetWindowPos(h, IntPtr.Zero, r.Left, r.Top + dTop, r.Right - r.Left, (r.Bottom - r.Top) - dTop + dBottom, 0x0004 | 0x0010 /* NOZORDER|NOACTIVATE */);
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>Gizli görev çubuğunu geçici olarak göster / yeniden gizle (ayarlara ve çalışma alanına dokunmaz).</summary>
    public static void Reveal(bool show)
    {
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;
        if (show)
        {
            Native.ShowWindow(tray, Native.SW_SHOWNA);
            Native.SetWindowPos(tray, (IntPtr)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE|NOMOVE|NOACTIVATE */);
        }
        else Native.ShowWindow(tray, Native.SW_HIDE);
    }

    static void HideCore(bool keepSpace)
    {
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;

        var d = Data(tray);
        int current = (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref d);
        int original = OriginalState();
        if (original < 0)
        {
            original = current;
            AtomicFile.Write(StateFile, original.ToString(), backup: false);   // önce kaydet, sonra değiştir
        }

        if (!File.Exists(OwnerFile) || File.ReadAllText(OwnerFile) != Program.SessionGeneration)
            AtomicFile.Write(OwnerFile, Program.SessionGeneration, backup: false);

        // istenen otomatik gizleme durumu: alanı koru → kullanıcının özgün ayarı; alanı boşalt → otomatik gizle
        int wanted = keepSpace ? original : (original | Native.ABS_AUTOHIDE);
        if (current != wanted)
        {
            d.lParam = (IntPtr)wanted;
            // çalışma alanı değişecek: masaüstü simgelerini yerinde tut
            DesktopIcons.Preserve(() => Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d));
        }

        foreach (var h in TrayWindows())
            if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE);
    }

    /// <param name="force">Durum dosyası olmasa da görev çubuğu pencerelerini göster.</param>
    static void RestoreCore(bool force)
    {
        try
        {
            try { UseFullAreaCore(false); } catch (Exception ex) { App.Log(ex); } // Always continue to show trays.
            bool had = File.Exists(StateFile);
            if (!had && !force) return;

            var tray = Native.FindWindow("Shell_TrayWnd", null);
            int original = OriginalState();
            if (had && tray != IntPtr.Zero && original >= 0)
            {
                var d = Data(tray);
                // yalnızca gerçekten değiştirdiysek geri yaz (gereksiz yere çalışma alanını oynatma)
                if ((int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref d) != original)
                {
                    d.lParam = (IntPtr)original;
                    DesktopIcons.Preserve(() => Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d));
                }
            }

            foreach (var h in TrayWindows())
                Native.ShowWindow(h, Native.SW_SHOWNA);

            if (had) File.Delete(StateFile);
            if (File.Exists(OwnerFile)) File.Delete(OwnerFile);
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
