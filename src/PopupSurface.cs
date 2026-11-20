// Shared popup surface: optical glass behind crisp, unfiltered controls.
using LiquidGlass.Controls;
using LiquidGlass.Services;
using System.Windows.Media.Effects;
namespace Iskele;
sealed class PopupSurface : Grid
{
    readonly Border frame, shadow;
    readonly LiquidGlassSurface lens;
    WallpaperService? wallpaper;
    LiveBackdrop? live;
    readonly DispatcherTimer timer;
    string monitorName="";
    Window? owner;
    internal bool OwnsBackdropResources=>wallpaper!=null||live!=null;
    readonly SolidColorBrush veil=new(Color.FromArgb(160,255,255,255));
    bool? optical;
    internal bool HasOpticalBackdrop=>lens.Backdrop!=null;
    internal bool UsesOpticalGlass=>App.Settings.Theme=="iosglass"&&GlassBackdrop.Supported;
    public PopupSurface(UIElement content,double radius,Thickness padding)
    {
        Margin=new Thickness(18);
        shadow=new Border{CornerRadius=new CornerRadius(radius),Background=Ui.B("MenuBg"),IsHitTestVisible=false,
            Effect=new DropShadowEffect{BlurRadius=22,ShadowDepth=4,Opacity=.22,Color=Colors.Black}};
        lens=new LiquidGlassSurface{CornerRadius=radius,ShadowPadding=0,Shadow=0,BezelWidth=10,ChromaticAberration=.12,IsHitTestVisible=false};
        frame=new Border{CornerRadius=new CornerRadius(radius),BorderThickness=new Thickness(1),Padding=padding,Child=content};
        frame.SetResourceReference(Border.BorderBrushProperty,"DockBorder");
        var opticalLayer=new Grid{IsHitTestVisible=false};opticalLayer.Children.Add(lens);
        opticalLayer.SizeChanged+=(_,_)=>opticalLayer.Clip=new RectangleGeometry(new Rect(0,0,opticalLayer.ActualWidth,opticalLayer.ActualHeight),radius,radius);
        Children.Add(shadow);Children.Add(opticalLayer);Children.Add(frame);
        // İlk kare doğru görünsün: tema/cam kararı pencere ekrana çıkmadan verilir. Aksi halde boş (gri) mercek
        // ve beyaz alt katman bir kare boyunca görünüp "beyaz parlama" yapıyordu.
        ApplyMode(UsesOpticalGlass);
        UseLayoutRounding=true;SnapsToDevicePixels=true;
        TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,_)=>UpdateBackdrop();
        Loaded+=(_,_)=>
        {
            owner=Window.GetWindow(this);if(owner!=null)owner.Closed+=OwnerClosed;
            timer.Start();Dispatcher.BeginInvoke(()=>{if(IsLoaded)UpdateBackdrop();},DispatcherPriority.Background);
        };
        Unloaded+=(_,_)=>Stop();
    }
    void OwnerClosed(object? sender,EventArgs e)=>Stop();
    void Stop(){timer.Stop();if(owner!=null)owner.Closed-=OwnerClosed;owner=null;Release();}
    void UpdateBackdrop()
    {
        if(!IsVisible||PresentationSource.FromVisual(this)==null)return;
        bool wanted=UsesOpticalGlass;
        if(optical!=wanted){if(!wanted)Release();ApplyMode(wanted);}
        if(!wanted)return;
        // A readable veil sits above the optics; only the backdrop receives the shader.
        var s=App.Settings;lens.Refraction=s.GlassRefraction;lens.Highlight=s.GlassHighlight;
        lens.Saturation=1.08;lens.Tint=Color.FromArgb((byte)Math.Round(s.GlassTint*255),255,255,255);
        var a=PointToScreen(new Point());var b=PointToScreen(new Point(ActualWidth,ActualHeight));
        if(b.X-a.X<4||b.Y-a.Y<4)return;
        var monitor=Monitors.At(new POINT{X=(int)((a.X+b.X)/2),Y=(int)((a.Y+b.Y)/2)})??Monitors.Primary;
        if(wallpaper==null||monitorName!=monitor.Name)
        {
            Release();monitorName=monitor.Name;var bounds=monitor.Bounds;
            GlassRuntime.DataDirectory=Settings.Dir;
            wallpaper=new WallpaperService(()=>LiquidGlass.Interop.User32.GetMonitorInfoAt((bounds.Left+bounds.Right)/2,(bounds.Top+bounds.Bottom)/2),monitor.Primary);
            live=new LiveBackdrop(wallpaper,Dispatcher);wallpaper.Changed+=BackdropChanged;live.Changed+=BackdropChanged;
            _=RefreshWallpaper();live.SetEnabled(true);
        }
        live!.SetBlur(s.GlassBlur);
        live.SetRegion(new Int32Rect((int)a.X,(int)a.Y,Math.Max(1,(int)Math.Ceiling(b.X-a.X)),Math.Max(1,(int)Math.Ceiling(b.Y-a.Y))));
    }
    /// <summary>Cam modunda mercek ve beyaz örtü yalnızca arka plan görüntüsü hazırsa gösterilir;
    /// o ana kadar temanın düz menü rengi kullanılır.</summary>
    void ApplyMode(bool wanted)
    {
        optical=wanted;
        bool ready=wanted&&lens.Backdrop!=null;
        lens.Visibility=ready?Visibility.Visible:Visibility.Collapsed;
        if(ready){frame.Background=veil;shadow.Background=Brushes.White;}
        else{frame.SetResourceReference(Border.BackgroundProperty,"MenuBg");shadow.SetResourceReference(Border.BackgroundProperty,"MenuBg");}
    }
    async Task RefreshWallpaper()
    {
        try{if(wallpaper is {} source)await source.RefreshAsync(App.Settings.GlassBlur);}
        catch(Exception ex){App.Log("Panel camı: "+ex.Message);}
    }
    void BackdropChanged(object? sender,EventArgs e)
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(()=>BackdropChanged(sender,e));return;}
        if(wallpaper==null)return;
        lens.Backdrop=live?.Image??wallpaper.Backdrop;
        lens.BackdropOrigin=live?.Image!=null?live.Origin:wallpaper.MonitorOrigin;
        if(optical==true)ApplyMode(true);
    }
    void Release()
    {
        if(live!=null){live.Changed-=BackdropChanged;live.Dispose();live=null;}
        if(wallpaper!=null)wallpaper.Changed-=BackdropChanged;
        wallpaper=null;lens.Backdrop=null;monitorName="";
        if(optical==true)ApplyMode(true);
    }
}
