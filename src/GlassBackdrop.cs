using System.Runtime.InteropServices;

namespace Iskele;

/// <summary>
/// Gerçek cam (hedef 86–87): dock'un ARKASINDA duran, hap şeklinde kırpılmış ayrı ve katmansız bir Win32 penceresi;
/// Windows'un acrylic bulanıklığı yalnızca buna uygulanır. Önceki denemede bulanıklık dock'un kendi (katmanlı, ekran
/// genişliğindeki) penceresine verilince saydam alanlar siyaha boyanıyordu; burada dock'un penceresine dokunulmaz.
/// WPF penceresi kullanılmaz: WPF arka planı opak boyuyor, bulanıklığı örtüyordu. Pencere hiçbir şey çizmez
/// (siyah = saydam), görünen her şey Windows'un bulanıklığıdır.
/// </summary>
sealed class AcrylicGlassBackdrop // Original Acrylic remains available as Cam/fallback.
{
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State, Flags; public uint Gradient; public int Anim; }
    [StructLayout(LayoutKind.Sequential)] struct WCAD { public int Attr; public IntPtr Data; public int Size; }
    delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public int cbSize; public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WCAD d);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(int ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
    [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int i);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? n);

    static WndProc? proc;          // GC'ye karşı tut
    static bool registered;
    const string ClassName = "IskeleGlassBackdrop";

    readonly IntPtr hwnd;
    RECT last;
    int lastRadius, tick;
    bool visible;

    public AcrylicGlassBackdrop()
    {
        if (!registered)
        {
            proc = (h, m, w, l) => m == 0x0084 /* NCHITTEST */ ? (IntPtr)(-1) /* HTTRANSPARENT */ : DefWindowProc(h, m, w, l);
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = proc, hInstance = GetModuleHandle(null),
                hbrBackground = GetStockObject(4 /* BLACK_BRUSH */), lpszClassName = ClassName,
            };
            RegisterClassEx(ref wc);
            registered = true;
        }
        const int exStyle = 0x80 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */ | 0x20 /* TRANSPARENT */ | 0x08 /* TOPMOST */;
        hwnd = CreateWindowEx(exStyle, ClassName, "", 0x80000000 /* WS_POPUP */, -100, -100, 10, 10, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        int round = 2; // DWMWCP_ROUND: bulanıklığı da kesen tek köşe yuvarlatması
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, 4);
        ApplyTint();
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    public void Close()
    {
        if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
    }

    /// <summary>Bulanıklık rengi: Cam'da hafif koyu, iOS Cam'da sütlü beyaz (AABBGGRR).</summary>
    public void ApplyTint()
    {
        if (hwnd == IntPtr.Zero) return;
        uint tint = App.Settings.Theme == "iosglass" ? 0x88F7F2F2u : 0x20101010u;
        var a = new AccentPolicy { State = 4 /* ACRYLICBLURBEHIND */, Flags = 2, Gradient = tint };
        int size = Marshal.SizeOf<AccentPolicy>();
        var p = Marshal.AllocHGlobal(size);
        try { Marshal.StructureToPtr(a, p, false); var d = new WCAD { Attr = 19, Data = p, Size = size }; SetWindowCompositionAttribute(hwnd, ref d); }
        finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>Camı ekranda verilen dikdörtgene (fiziksel piksel) ve köşe yarıçapına oturt; boş dikdörtgen gizler.</summary>
    public void Place(RECT r, int radius, IntPtr dock)
    {
        if (hwnd == IntPtr.Zero) return;
        bool show = r.Right - r.Left > 4 && r.Bottom - r.Top > 4;
        if (!show)
        {
            if (visible) { Native.ShowWindow(hwnd, Native.SW_HIDE); visible = false; }
            return;
        }
        bool first = !visible;
        if (first) { Native.ShowWindow(hwnd, Native.SW_SHOWNA); visible = true; }
        bool moved = r.Left != last.Left || r.Top != last.Top || r.Right != last.Right || r.Bottom != last.Bottom || radius != lastRadius;
        if (moved || first || ++tick % 30 == 0)
        {
            // Windows 11'de acrylic bulanıklık pencere bölgesini (SetWindowRgn) yok sayıp tüm dikdörtgeni boyuyor;
            // köşelerde koyu kareler kalıyordu. Köşeleri Windows'un kendi yuvarlatması (8 px) kesiyor; dock'un köşesi
            // daha yuvarlaksa camı, 8 px'lik köşesi dock'un kavsinin içinde kalacak kadar içeri çek:
            // √2·(R−k−8) + 8 ≤ R  ⇒  k ≥ (R−8)(1−1/√2).
            int k = Inset(radius);
            int x = r.Left + k, y = r.Top + k, w = r.Right - r.Left - 2 * k, h = r.Bottom - r.Top - 2 * k;
            // dock penceresinin hemen ALTINA yerleş (hWndInsertAfter = dock)
            Native.SetWindowPos(hwnd, dock, x, y, w, h, 0x0010 /* NOACTIVATE */);
            int rr = Math.Max(0, radius - k);
            if (moved || first) SetWindowRgn(hwnd, CreateRoundRectRgn(0, 0, w + 1, h + 1, rr * 2, rr * 2), true);
            last = r;
            lastRadius = radius;
        }
    }

    const int SystemCorner = 8;   // DWMWCP_ROUND yarıçapı (96 DPI'da 8 px; ölçekte büyür, aşağıda güvenli tarafta kalınır)
    internal static int Inset(int radius) =>
        radius <= SystemCorner ? 0 : (int)Math.Ceiling((radius - SystemCorner) * (1 - 1 / Math.Sqrt(2))) + 1;
}
