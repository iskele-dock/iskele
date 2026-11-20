using System.Runtime.InteropServices;
using System.Text;

namespace Iskele;

/// <summary>
/// Masaüstü simgelerinin konumlarını kaydeder ve geri yükler. Görev çubuğu gizlenip gösterilirken ekranın
/// kullanılabilir alanı değişir ve Windows simgeleri yeniden dizer; bu sınıf simgeleri eski yerlerine koyar.
/// Masaüstü, Explorer sürecindeki bir liste görünümüdür (SysListView32); konumlar o sürecin belleği üzerinden okunur.
/// </summary>
static class DesktopIcons
{
    const uint LVM_GETITEMCOUNT = 0x1004, LVM_GETITEMPOSITION = 0x1010, LVM_SETITEMPOSITION = 0x100F, LVM_GETITEMTEXTW = 0x1073;
    const uint MEM_COMMIT = 0x1000, MEM_RELEASE = 0x8000, PAGE_READWRITE = 0x04;

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr addr, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p, IntPtr addr, UIntPtr size, uint type);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr addr, byte[] buf, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p, IntPtr addr, byte[] buf, UIntPtr size, out UIntPtr written);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    static IntPtr ListView()
    {
        IntPtr view = Native.FindWindowEx(Native.FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view == IntPtr.Zero)
        {
            var w = IntPtr.Zero;
            while ((w = Native.FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
            {
                view = Native.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero) break;
            }
        }
        return view == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    }

    /// <summary>Simgeler, liste sırasıyla (ad, konum). Okunamazsa boş liste.</summary>
    public static List<(string Name, int X, int Y)> Save()
    {
        var result = new List<(string, int, int)>();
        var lv = ListView();
        if (lv == IntPtr.Zero) return result;
        Native.GetWindowThreadProcessId(lv, out uint pid);
        var proc = OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, pid);
        if (proc == IntPtr.Zero) return result;
        var mem = VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)4096, MEM_COMMIT, PAGE_READWRITE);
        try
        {
            if (mem == IntPtr.Zero) return result;
            int count = (int)SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            IntPtr textAddr = mem + 1024, pointAddr = mem + 3072;
            for (int i = 0; i < count && i < 2000; i++)
            {
                // LVITEMW (64 bit): iSubItem @8, pszText @24, cchTextMax @32
                var item = new byte[88];
                BitConverter.GetBytes(0).CopyTo(item, 8);
                BitConverter.GetBytes((long)textAddr).CopyTo(item, 24);
                BitConverter.GetBytes(512).CopyTo(item, 32);
                WriteProcessMemory(proc, mem, item, (UIntPtr)item.Length, out _);
                int len = (int)SendMessage(lv, LVM_GETITEMTEXTW, (IntPtr)i, mem);
                var text = new byte[1024];
                ReadProcessMemory(proc, textAddr, text, (UIntPtr)text.Length, out _);
                var name = Encoding.Unicode.GetString(text, 0, Math.Clamp(len, 0, 511) * 2);

                SendMessage(lv, LVM_GETITEMPOSITION, (IntPtr)i, pointAddr);
                var pt = new byte[8];
                ReadProcessMemory(proc, pointAddr, pt, (UIntPtr)8, out _);
                result.Add((name, BitConverter.ToInt32(pt, 0), BitConverter.ToInt32(pt, 4)));
            }
        }
        catch (Exception ex) { App.Log(ex.Message); }
        finally
        {
            if (mem != IntPtr.Zero) VirtualFreeEx(proc, mem, UIntPtr.Zero, MEM_RELEASE);
            CloseHandle(proc);
        }
        return result;
    }

    const uint LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1036, LVM_GETEXTENDEDLISTVIEWSTYLE = 0x1037, LVS_EX_SNAPTOGRID = 0x00080000;

    /// <summary>
    /// Kaydedilen konumlara geri koy. Simgeler tek tek taşınırken hedef hücrede henüz taşınmamış bir simge varsa
    /// "ızgaraya hizala" taşınanı başka boş hücreye iter ve düzen karışır. Bu yüzden taşıma süresince ızgaraya
    /// hizalama kapatılır, hepsi yerleşince eski haline getirilir.
    /// </summary>
    public static int Restore(List<(string Name, int X, int Y)> saved)
    {
        if (saved.Count == 0) return 0;
        var now = Save();
        var lv = ListView();
        if (lv == IntPtr.Zero) return 0;
        uint ex = (uint)SendMessage(lv, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
        bool snap = (ex & LVS_EX_SNAPTOGRID) != 0;
        if (snap) SendMessage(lv, LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)LVS_EX_SNAPTOGRID, IntPtr.Zero);
        try { return Move(lv, now, saved); }
        finally { if (snap) SendMessage(lv, LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)LVS_EX_SNAPTOGRID, (IntPtr)LVS_EX_SNAPTOGRID); }
    }

    static int Move(IntPtr lv, List<(string Name, int X, int Y)> now, List<(string Name, int X, int Y)> saved)
    {
        // aynı adlı simgeler olabilir: her ad için kayıtlı konumları sırayla kullan
        var byName = saved.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => new Queue<(int X, int Y)>(g.Select(s => (s.X, s.Y))));
        int moved = 0;
        for (int i = 0; i < now.Count; i++)
        {
            if (!byName.TryGetValue(now[i].Name, out var q) || q.Count == 0) continue;
            var want = q.Dequeue();
            if (want.X == now[i].X && want.Y == now[i].Y) continue;
            SendMessage(lv, LVM_SETITEMPOSITION, (IntPtr)i, (IntPtr)((want.Y << 16) | (want.X & 0xFFFF)));
            moved++;
        }
        return moved;
    }

    /// <summary>Ekran alanını değiştiren bir işlemi, simgeleri yerinde tutarak çalıştır.</summary>
    public static void Preserve(Action change)
    {
        List<(string Name, int X, int Y)> before;
        try { before = Save(); } catch { before = []; }
        change();
        if (before.Count == 0) return;
        // Windows simgeleri biraz gecikmeyle yeniden dizer; iki kez geri koy.
        foreach (int wait in new[] { 600, 900 })
        {
            Thread.Sleep(wait);
            try
            {
                int n = Restore(before);
                App.Debug($"masaustu simgeleri: {n} simge eski yerine kondu");
            }
            catch (Exception ex) { App.Log(ex.Message); }
        }
    }
}
