// Approved optical renderer, independent of the laboratory and FX package.
using System.Windows.Interop;
using LiquidGlass.Controls;
using LiquidGlass.Services;
using Microsoft.Win32;
namespace Iskele;
sealed class GlassBackdrop
{
    AcrylicGlassBackdrop? acrylic;
    Window? host;
    Canvas? scene;
    LiquidGlassSurface? surface;
    WallpaperService? wallpaper;
    LiveBackdrop? live;
    IntPtr hwnd;
    RECT last;
    RECT lastHost;
    int lastRadius;
    internal int NativePlacements {get;private set;}
    internal int SurfaceResizes {get;private set;}
    string monitorName="";
    bool closed,liquid;
    DateTime nextZOrder;
    readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;
    internal bool IsLiquid=>liquid;
    internal bool HasBackdrop=>surface?.Backdrop!=null;
    internal bool HasLiveBackdrop=>live?.Image!=null;
    internal static bool Supported=>(RenderCapability.Tier>>16)>=2&&RenderCapability.IsPixelShaderVersionSupported(3,0);
    public GlassBackdrop()
    {
        GlassRuntime.DataDirectory=Settings.Dir;
        SystemEvents.UserPreferenceChanged+=PreferencesChanged;
        SystemEvents.DisplaySettingsChanged+=DisplaysChanged;
    }
    void PreferencesChanged(object sender,UserPreferenceChangedEventArgs e)
    {
        if(e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General or UserPreferenceCategory.Color)
            dispatcher.BeginInvoke(()=>{if(!closed)_=RefreshWallpaper();});
    }
    void DisplaysChanged(object? sender,EventArgs e)=>dispatcher.BeginInvoke(()=>{if(!closed){monitorName="";last=default;}});
    public void ApplyTint()
    {
        bool wanted=App.Settings.Theme=="iosglass"&&Supported;
        if((host!=null||acrylic!=null)&&wanted!=liquid)Release();
        acrylic?.ApplyTint();
        if(surface==null)return;
        var s=App.Settings;
        surface.Refraction=s.GlassRefraction;surface.BezelWidth=12;surface.ChromaticAberration=0.15;
        surface.Highlight=s.GlassHighlight;surface.Saturation=1.08;surface.Shadow=0;
        surface.Tint=Color.FromArgb((byte)Math.Round(s.GlassTint*255),255,255,255);
        live?.SetBlur(s.GlassBlur);
        if(wallpaper!=null)_=wallpaper.SetBlurAsync(s.GlassBlur);
    }
    void Create(Monitors.Info monitor)
    {
        liquid=App.Settings.Theme=="iosglass"&&Supported;
        if(!liquid){acrylic=new AcrylicGlassBackdrop();return;}
        host=new Window{Title="Iskele · Cam",WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,
            ShowInTaskbar=false,ShowActivated=false,Topmost=true,ResizeMode=ResizeMode.NoResize,IsHitTestVisible=false};
        surface=new LiquidGlassSurface{IsHitTestVisible=false,ShadowPadding=0};
        scene=new Canvas{IsHitTestVisible=false};scene.Children.Add(surface);host.Content=scene;
        host.SourceInitialized+=(_,_)=>
        {
            hwnd=new WindowInteropHelper(host).Handle;
            long ex=(long)Native.GetWindowLongPtr(hwnd,Native.GWL_EXSTYLE);
            Native.SetWindowLongPtr(hwnd,Native.GWL_EXSTYLE,(IntPtr)(ex|0x08000000|0x20|0x80));
            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled)=>
            {if(m==0x84){handled=true;return (IntPtr)(-1);}return IntPtr.Zero;});
        };
        var b=monitor.Bounds;
        wallpaper=new WallpaperService(()=>LiquidGlass.Interop.User32.GetMonitorInfoAt((b.Left+b.Right)/2,(b.Top+b.Bottom)/2),monitor.Primary);
        live=new LiveBackdrop(wallpaper,dispatcher);
        wallpaper.Changed+=BackdropChanged;live.Changed+=BackdropChanged;
        ApplyTint();_=RefreshWallpaper();live.SetEnabled(true);
    }
    async Task RefreshWallpaper()
    {
        try{if(wallpaper is {} source)await source.RefreshAsync(App.Settings.GlassBlur);}
        catch(Exception ex){App.Log("Cam arka planı: "+ex.Message);}
    }
    void BackdropChanged(object? sender,EventArgs e)
    {
        // Wallpaper blur can complete without a WPF synchronization context (tests/startup).
        if(!dispatcher.CheckAccess()){dispatcher.BeginInvoke(()=>BackdropChanged(sender,e));return;}
        if(closed||surface==null||wallpaper==null)return;
        surface.Backdrop=live?.Image??wallpaper.Backdrop;
        surface.BackdropOrigin=live?.Image!=null?live.Origin:wallpaper.MonitorOrigin;
    }
    public void Place(RECT rect,int radius,IntPtr dock)
    {
        if(closed)return;
        int w=rect.Right-rect.Left,h=rect.Bottom-rect.Top;
        if(w<=4||h<=4){host?.Hide();live?.SetRegion(Int32Rect.Empty);acrylic?.Place(default,0,dock);return;}
        try
        {
            bool moved=last.Left!=rect.Left||last.Top!=rect.Top||last.Right!=rect.Right||last.Bottom!=rect.Bottom;
            if(!moved&&radius==lastRadius&&host?.IsVisible==true&&DateTime.UtcNow<nextZOrder)return;
            var monitor=Monitors.At(new POINT{X=(rect.Left+rect.Right)/2,Y=(rect.Top+rect.Bottom)/2})??Monitors.Primary;
            if(monitorName!=monitor.Name){Release();monitorName=monitor.Name;moved=true;}
            if(host==null&&acrylic==null)Create(monitor);
            if(acrylic!=null){acrylic.Place(rect,radius,dock);last=rect;return;}
            if(host==null||surface==null)return;
            var dpi=Math.Max(96,Native.GetDpiForWindow(dock))/96.0;
            // Stable native render target and capture buffer throughout the balloon morph.
            // Only the lightweight surface inside this canvas changes its size/offset per frame.
            if(!Native.GetWindowRect(dock,out var envelope)||envelope.Right<=envelope.Left||envelope.Bottom<=envelope.Top)envelope=rect;
            bool hostMoved=lastHost.Left!=envelope.Left||lastHost.Top!=envelope.Top||lastHost.Right!=envelope.Right||lastHost.Bottom!=envelope.Bottom||!host.IsVisible;
            surface.CornerRadius=radius/dpi;
            if(hostMoved)
            {
                host.Left=envelope.Left/dpi;host.Top=envelope.Top/dpi;host.Width=(envelope.Right-envelope.Left)/dpi;host.Height=(envelope.Bottom-envelope.Top)/dpi;
                if(!host.IsVisible)host.Show();
                live?.SetRegion(new Int32Rect(envelope.Left,envelope.Top,envelope.Right-envelope.Left,envelope.Bottom-envelope.Top));
                surface.Width=(envelope.Right-envelope.Left)/dpi;surface.Height=(envelope.Bottom-envelope.Top)/dpi;SurfaceResizes++;
                lastHost=envelope;
            }
            if(moved||hostMoved||radius!=lastRadius)
            {
                surface.ShapeCenter=new Point((rect.Left-envelope.Left+w/2.0)/dpi,(rect.Top-envelope.Top+h/2.0)/dpi);
                surface.ShapeSize=new Size(w/dpi,h/dpi);
                last=rect;lastRadius=radius;
            }
            if(hwnd!=IntPtr.Zero&&(hostMoved||DateTime.UtcNow>=nextZOrder))
            {
                uint flags=hostMoved?0x0010u:0x0013u;
                Native.SetWindowPos(hwnd,dock,envelope.Left,envelope.Top,envelope.Right-envelope.Left,envelope.Bottom-envelope.Top,flags);
                NativePlacements++;nextZOrder=DateTime.UtcNow.AddSeconds(1);
            }
        }
        catch(Exception ex)
        {
            App.Log("HLSL cam yerine Acrylic: "+ex.Message);
            Release();liquid=false;acrylic=new AcrylicGlassBackdrop();acrylic.Place(rect,radius,dock);
        }
    }
    void Release()
    {
        acrylic?.Close();acrylic=null;
        if(live!=null){live.Changed-=BackdropChanged;live.Dispose();live=null;}
        if(wallpaper!=null)wallpaper.Changed-=BackdropChanged;
        wallpaper=null;surface=null;scene=null;host?.Close();host=null;hwnd=IntPtr.Zero;last=default;lastHost=default;
    }
    public void Close()
    {
        if(closed)return;closed=true;
        SystemEvents.UserPreferenceChanged-=PreferencesChanged;SystemEvents.DisplaySettingsChanged-=DisplaysChanged;
        Release();
    }
}
