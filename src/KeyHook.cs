using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Iskele;

/// <summary>
/// Genel klavye kısayolları (düşük seviye klavye kancası): Win+1…Win+0 dock'taki 1.–10. uygulamayı açar ya da öne
/// getirir. Windows'un kendi Win+sayı kısayolu yutulur; Win bırakılınca Başlat açılmasın diye araya etkisiz bir tuş
/// basılır.
/// </summary>
static class KeyHook
{
    delegate IntPtr Proc(int code, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vk, scan, flags, time; public UIntPtr extra; }

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, Proc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    static IntPtr hook;
    static Proc? proc;
    static readonly HashSet<uint> swallowedDown = [];

    /// <summary>Win+sayıya basılınca (0 = 10. uygulama) çağrılır; true dönerse tuş yutulur.</summary>
    public static Func<int, bool>? WinNumber;

    /// <summary>Alt+T basılınca çağrılır (hızlı yapılacak).</summary>
    public static Action? AltT;
    static bool altTDown;

    public static void Start()
    {
        if (hook != IntPtr.Zero) return;
        proc = Callback;
        using var m = Process.GetCurrentProcess().MainModule;
        hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, proc, GetModuleHandle(m?.ModuleName), 0);
        if (hook == IntPtr.Zero) App.Log("Klavye kancası kurulamadı");
    }

    public static void Stop()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
        altTDown = false; swallowedDown.Clear();
    }

    // One toggle per physical key press, regardless of auto-repeat.
    internal static bool FirstPress(ref bool held)
    {
        if (held) return false;
        held = true; return true;
    }

    static bool WinDown => (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

    static IntPtr Callback(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(l);
            int msg = (int)w;
            bool down = msg is 0x0100 or 0x0104, up = msg is 0x0101 or 0x0105;
            if (k.vk == 0x54 && down && WinDown && (GetAsyncKeyState(0x11) & 0x8000) == 0 && (GetAsyncKeyState(0x12) & 0x8000) == 0)
            {
                swallowedDown.Add(k.vk);
                App.Current.Dispatcher.BeginInvoke(AppNavigator.Open);
                Native.keybd_event(0xFF, 0, 0, UIntPtr.Zero); Native.keybd_event(0xFF, 0, 2, UIntPtr.Zero);
                return (IntPtr)1;
            }
            if (k.vk == 0x54 && up && swallowedDown.Remove(k.vk)) return (IntPtr)1;
            if (k.vk == 0x54 && up && altTDown) { altTDown = false; return (IntPtr)1; }
            // Alt+T: hızlı yapılacak (Alt basılıyken gelen tuşlar WM_SYSKEYDOWN olur)
            if (k.vk == 0x54 && App.Settings.AltTodo && AltT != null && !WinDown && (GetAsyncKeyState(0x11) & 0x8000) == 0)
            {
                if (msg == 0x0104) { if (FirstPress(ref altTDown)) App.Current.Dispatcher.BeginInvoke(AltT); return (IntPtr)1; }
            }
            if (k.vk is >= 0x30 and <= 0x39)
            {
                if (down && WinDown && App.Settings.WinNumbers)
                {
                    int index = k.vk == 0x30 ? 9 : (int)k.vk - 0x31;
                    bool handled = false;
                    try { handled = WinNumber?.Invoke(index) ?? false; } catch (Exception ex) { App.Log(ex); }
                    if (handled)
                    {
                        swallowedDown.Add(k.vk);
                        // Win bırakılınca Başlat menüsü açılmasın: araya etkisiz bir tuş koy
                        Native.keybd_event(0xFF, 0, 0, UIntPtr.Zero);
                        Native.keybd_event(0xFF, 0, 2, UIntPtr.Zero);
                        return (IntPtr)1;
                    }
                }
                if (up && swallowedDown.Remove(k.vk)) return (IntPtr)1;
            }
        }
        return CallNextHookEx(hook, code, w, l);
    }
}
