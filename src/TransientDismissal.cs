// Outside-click dismissal also works when a non-activating dock owns the menu.
using System.Runtime.InteropServices;
namespace Iskele;
static class TransientDismissal
{
    sealed record Watch(Func<Point,bool> Contains,Action Close);
    static readonly Dictionary<object,Watch> watches=[];
    delegate IntPtr MouseProc(int code,IntPtr message,IntPtr data);
    static readonly MouseProc callback=OnMouse;
    static IntPtr hook;
    static bool installed;
    public static void Install()
    {
        if(installed)return;installed=true;
        EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.OpenedEvent,new RoutedEventHandler((s,_)=>
        {
            var menu=(ContextMenu)s;menu.StaysOpen=false;
            Add(menu,p=>ContainsMenu(menu,p),()=>menu.IsOpen=false);
        }));
        EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.ClosedEvent,new RoutedEventHandler((s,_)=>Remove(s)));
    }
    public static void Add(object key,Func<Point,bool> contains,Action close)
    {
        watches[key]=new(contains,close);
        if(hook==IntPtr.Zero)hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
    }
    public static void Remove(object key)
    {
        watches.Remove(key);
        if(watches.Count==0&&hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}
    }
    internal static bool InBounds(Point p,Rect bounds)=>bounds.Contains(p);
    public static bool Contains(FrameworkElement element,Point p)
    {
        try
        {
            if(!element.IsVisible||PresentationSource.FromVisual(element)==null)return false;
            return InBounds(p,new Rect(element.PointToScreen(new Point()),element.PointToScreen(new Point(element.ActualWidth,element.ActualHeight))));
        }
        catch(InvalidOperationException){return false;}
    }
    static bool ContainsMenu(ContextMenu menu,Point p)
    {
        if(Contains(menu,p))return true;
        bool Submenu(MenuItem item)
        {
            if(!item.IsSubmenuOpen)return false;
            if(item.Template?.FindName("PART_Popup",item) is Popup {Child:FrameworkElement child}&&Contains(child,p))return true;
            return item.Items.OfType<MenuItem>().Any(Submenu);
        }
        return menu.Items.OfType<MenuItem>().Any(Submenu);
    }
    static IntPtr OnMouse(int code,IntPtr message,IntPtr data)
    {
        if(code>=0&&(int)message is 0x201 or 0x204 or 0x207 or 0x20B)
        {
            var point=Marshal.PtrToStructure<POINT>(data);
            // Never consume the click or activate the dock. Defer closure until input can proceed.
            DispatchOutside(new Point(point.X,point.Y));
        }
        return CallNextHookEx(hook,code,message,data);
    }
    internal static void DispatchOutside(Point point)
    {
        var outside=watches.Where(kv=>!kv.Value.Contains(point)).Select(kv=>kv.Key).ToArray();
        Application.Current?.Dispatcher.BeginInvoke(()=>
        {foreach(var key in outside)if(watches.TryGetValue(key,out var watch))watch.Close();},DispatcherPriority.Input);
    }
    [DllImport("user32.dll")]static extern IntPtr SetWindowsHookEx(int id,MouseProc proc,IntPtr mod,uint thread);
    [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr h,int code,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string? module);
}
