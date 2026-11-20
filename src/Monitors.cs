using System.Runtime.InteropServices;

namespace Iskele;

/// <summary>Bağlı ekranlar: tutamaç, tam alan, çalışma alanı, birincil mi, aygıt adı (hedef 22).</summary>
static class Monitors
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    delegate bool EnumProc(IntPtr h, IntPtr dc, ref RECT r, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumProc cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFOEX mi);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);

    public record Info(IntPtr Handle, RECT Bounds, RECT Work, bool Primary, string Name);

    public static Info? Get(IntPtr h)
    {
        if (h == IntPtr.Zero) return null;
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return GetMonitorInfo(h, ref mi) ? new Info(h, mi.rcMonitor, mi.rcWork, (mi.dwFlags & 1) != 0, mi.szDevice) : null;
    }

    public static List<Info> All()
    {
        var list = new List<Info>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) => { if (Get(h) is { } i) list.Add(i); return true; }, IntPtr.Zero);
        return list;
    }

    public static Info Primary => All().FirstOrDefault(m => m.Primary) ?? All().First();

    public static Info? At(POINT p) => Get(MonitorFromPoint(p, 2 /* NEAREST */));
}
