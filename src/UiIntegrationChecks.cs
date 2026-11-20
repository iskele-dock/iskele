// Integration coverage for the approved material and requested settings/menu behavior.
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
namespace Iskele;
static class UiIntegrationChecks
{
    static void Pump(int ms)
    {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(ms)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    static IEnumerable<T> Find<T>(DependencyObject node) where T:DependencyObject
    {
        if(node is T value)yield return value;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
            foreach(var child in Find<T>(VisualTreeHelper.GetChild(node,i)))yield return child;
    }
    static void Snapshot(Window window,string file)
    {
        window.UpdateLayout();
        var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(file);encoder.Save(stream);
    }
    public static void Run(string output,Action<bool,string> check)
    {
        var s=App.Settings;
        // Boundary and repeat-click coverage without waiting or controlling a real music player.
        var grace=new MediaTransitionGrace();var moment=new DateTime(2026,11,18,12,0,0,DateTimeKind.Utc);
        grace.Hold(moment);check(grace.Active(moment.AddSeconds(5.999))&&!grace.Active(moment.AddSeconds(6)),"Track-change grace lasts six seconds and expires");
        grace.Hold(moment.AddSeconds(5));check(grace.Active(moment.AddSeconds(10.999))&&!grace.Active(moment.AddSeconds(11)),"Each repeated skip renews the full six-second grace");
        var graceDock=new DockWindow(Monitors.Primary.Handle);var media=new MediaWidget();var mediaSlot=new Slot(media,graceDock);bool hideIdle=s.HideIdleMedia;s.HideIdleMedia=true;
        MediaWidget.Transition.Hold(DateTime.UtcNow);media.SetIdle(true);check(mediaSlot.Visibility==Visibility.Visible,"An idle music slot stays visible during a track transition");
        MediaWidget.Transition.Hold(DateTime.UtcNow.AddSeconds(-7));media.SetIdle(true);check(mediaSlot.Visibility==Visibility.Collapsed,"Music slot resumes normal idle hiding after the grace expires");
        media.Detach();graceDock.Close();s.HideIdleMedia=hideIdle;

        check(InteractionTiming.DefaultDelay==120&&new Settings().HoverResponseMs==120,"Hover dwell defaults to integer 120 ms");
        s.HoverResponseMs=90;var expansion=InteractionTiming.Scale(360);s.HoverResponseMs=350;
        check(expansion==252&&InteractionTiming.Scale(360)==expansion,"Changing hover dwell does not change expansion animation duration");s.HoverResponseMs=120;
        var invalid=new Settings{GlassRefraction=999,GlassBlur=-3,GlassTint=double.NaN,HoverResponseMs=999};invalid.Normalize();
        check(invalid.GlassRefraction==18&&invalid.GlassBlur==0&&invalid.GlassTint==.12&&invalid.HoverResponseMs==350,"Persisted optical and timing values are bounded");
        invalid.HoverResponseMs=12;invalid.Normalize();check(invalid.HoverResponseMs==90,"Hover slider cannot go below 90 ms");
        invalid.HoverResponseMs=120.7;invalid.Normalize();check(invalid.HoverResponseMs==121,"Persisted millisecond values round to integers");
        var fixture=new Grid{Width=32,Height=32};fixture.Children.Add(new Border{Background=Brushes.Blue});fixture.Children.Add(new Border{Width=18,Height=4,Background=Brushes.Blue,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,-5)});
        fixture.Measure(new Size(32,32));fixture.Arrange(new Rect(0,0,32,32));fixture.UpdateLayout();
        var(copy,copyBounds)=AppsWidget.MagnifyCopy(fixture,false);copy.Measure(copyBounds.Size);copy.Arrange(new Rect(new Point(),copyBounds.Size));
        check(copyBounds.Height>32&&copy.Fill is VisualBrush{ViewboxUnits:BrushMappingMode.RelativeToBoundingBox,Stretch:Stretch.Fill},"Running underline uses normalized source bounds without rescaling the icon");
        var iconRaster=new RenderTargetBitmap(48,48,96,96,PixelFormats.Pbgra32);iconRaster.Render(copy);byte[] iconPixels=new byte[48*48*4];iconRaster.CopyPixels(iconPixels,48*4,0);
        check(Enumerable.Range(0,48).Count(x=>iconPixels[(10*48+x)*4]>200)==32,"Running icon is pixel-exact at rest, without a final enlargement");
        // Inset visible content must keep its original Y position when copied for hover.
        var inset=new Grid{Width=32,Height=48};inset.Children.Add(new Border{Width=32,Height=24,Background=Brushes.Blue,VerticalAlignment=VerticalAlignment.Center});
        inset.Measure(new Size(32,48));inset.Arrange(new Rect(0,0,32,48));inset.UpdateLayout();
        var(insetCopy,insetBounds)=AppsWidget.MagnifyCopy(inset,false);insetCopy.Measure(insetBounds.Size);insetCopy.Arrange(new Rect(new Point(),insetBounds.Size));
        byte[] Raster(Visual visual){var bitmap=new RenderTargetBitmap(48,64,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var pixels=new byte[48*64*4];bitmap.CopyPixels(pixels,48*4,0);return pixels;}
        var originalPixels=Raster(inset);var copiedPixels=Raster(insetCopy);
        check(originalPixels.SequenceEqual(copiedPixels),"Inset icon keeps its exact vertical position when hover starts");
        foreach(var provider in new[]{"Claude","Codex","Gemini","OpenCode"})
        {var logo=ProviderIcons.Create(provider,32);logo.Measure(new Size(32,32));logo.Arrange(new Rect(0,0,32,32));check(logo.DesiredSize.Width==32,provider+" original vector logo loads");}
        var logoRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(16)};
        foreach(var provider in new[]{"Claude","Codex","Gemini","OpenCode"})
        {
            var cell=Ui.Col(ProviderIcons.Create(provider,32),Ui.Text(provider,12,"Fg"));cell.Width=100;logoRow.Children.Add(cell);
        }
        var logoWindow=new Window{Width=440,Height=100,WindowStyle=WindowStyle.None,ShowInTaskbar=false,ShowActivated=false,Content=logoRow,Background=Ui.B("MenuBg")};logoWindow.Show();Snapshot(logoWindow,Path.Combine(output,"provider-icons.png"));logoWindow.Close();
        // Native overflow follows its widget and fits top/bottom/negative-origin screens.
        var trayWork=new RECT{Left=-1920,Top=-200,Right=0,Bottom=880};
        foreach(bool top in new[]{true,false})
        {
            var trayAnchor=new RECT{Left=-20,Right=0,Top=top?-200:860,Bottom=top?-180:880};
            var placed=TrayWidget.OverflowPlacement(trayAnchor,300,200,trayWork,top);
            check(placed.Left>=trayWork.Left&&placed.Right<=trayWork.Right&&placed.Top>=trayWork.Top&&placed.Bottom<=trayWork.Bottom,$"Tray overflow stays onscreen at the widget / {(top?"top":"bottom")}");
        }
        var trayOrder=new List<string>();TrayWidget.InvokeWithoutReveal(()=>trayOrder.Add("invoke"),true,()=>trayOrder.Add("keep-hidden"));
        check(trayOrder.SequenceEqual(new[]{"invoke","keep-hidden"}),"Tray invocation never asks to reveal the Windows taskbar");
        var key=new object();bool dismissed=false;
        TransientDismissal.Add(key,p=>new Rect(-100,-100,50,50).Contains(p),()=>dismissed=true);
        TransientDismissal.DispatchOutside(new Point(-80,-80));Pump(20);check(!dismissed,"Click inside negative-monitor menu stays open");
        TransientDismissal.DispatchOutside(new Point(200,200));Pump(20);check(dismissed,"Outside click dismisses non-activating menu");TransientDismissal.Remove(key);

        SettingsWindow.OpenWidgets("Hava durumu");Pump(40);
        var settings=Application.Current.Windows.OfType<SettingsWindow>().Single();
        check(PickerWindow.Current!=null&&!typeof(PickerWindow).IsSubclassOf(typeof(Window)),"Catalog is embedded in the settings window");
        var weather=Find<ToggleButton>(settings).FirstOrDefault(e=>AutomationProperties.GetName(e)=="Hava durumu özelleştir");
        check(weather!=null,"Weather has its own customization arrow");
        if(weather!=null){weather.IsChecked=true;settings.UpdateLayout();var weatherCard=Find<Border>(settings).First(b=>(string?)b.Tag=="widget:weather");check(Find<TextBox>(weatherCard).Any(t=>t.Text==s.WeatherCity),"Weather city setting opens inside its original preview card");}
        check(Application.Current.Windows.OfType<SettingsWindow>().Count()==1,"Widget customization does not create another settings window");
        Snapshot(settings,Path.Combine(output,"widgets-weather-inline.png"));
        SettingsWindow.OpenWidgets("Uygulamalar & Dosyalar");settings.UpdateLayout();
        var appsExpander=Find<ToggleButton>(settings).FirstOrDefault(e=>AutomationProperties.GetName(e)=="Uygulamalar özelleştir");
        if(appsExpander!=null){appsExpander.IsChecked=true;settings.UpdateLayout();}
        check(appsExpander?.IsChecked==true,"Application preferences expand inside the original preview card");
        Snapshot(settings,Path.Combine(output,"widgets-apps-inline.png"));
        SettingsWindow.OpenWidgets("Sistem");settings.UpdateLayout();
        check(!Find<ToggleButton>(settings).Any(e=>AutomationProperties.GetName(e)=="CPU özelleştir"),"Widgets without options have no customization arrow");
        check(Find<ToggleButton>(settings).Any(e=>AutomationProperties.GetName(e)=="Hızlı ayarlar özelleştir"),"Widgets with real preferences retain their arrow");
        Snapshot(settings,Path.Combine(output,"widgets-system-spacious.png"));
        settings.Close();

        s.FixedAppArea=false;s.Pins=[Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"notepad.exe")];s.AppSlots=0;
        var a=WidgetRegistry.Create("apps")!;a.Measure(new Size(double.PositiveInfinity,100));var natural=a.DesiredSize.Width;a.Detach();
        s.AppSlots=100;
        var b=WidgetRegistry.Create("apps")!;b.Measure(new Size(double.PositiveInfinity,100));check(Math.Abs(natural-b.DesiredSize.Width)<.01,"Applications ignore historical reserved slots in flexible mode");b.Detach();

        // No taskbar/AppBar changes: a plain test window anchors both backdrop and flyout.
        var anchor=new Border{Width=700,Height=60,Background=Brushes.Transparent};
        var window=new Window{Title="İskele · Cam doğrulama",Content=anchor,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,
            Left=100,Top=22,Width=700,Height=60,ShowInTaskbar=false,ShowActivated=false};window.Show();window.UpdateLayout();
        var hwnd=new WindowInteropHelper(window).Handle;
        var dock=new DockWindow(Monitors.Primary.Handle);
        var menu=dock.BuildMenu();menu.PlacementTarget=anchor;menu.IsOpen=true;Pump(30);
        var placement=menu.Items.OfType<MenuItem>().FirstOrDefault(i=>(string)i.Header=="Yerleşim");
        if(placement!=null)placement.IsSubmenuOpen=true;
        Pump(30);
        check(placement?.Template?.FindName("PART_Popup",placement) is Popup{IsOpen:true},"Grouped context menu renders a real submenu");
        TransientDismissal.DispatchOutside(new Point(-10000,-10000));Pump(30);
        check(!menu.IsOpen,"An outside click closes grouped context menus");dock.Close();
        s.Theme="iosglass";
        var glass=new GlassBackdrop();glass.Place(new RECT{Left=100,Top=22,Right=800,Bottom=82},24,hwnd);Pump(900);
        check(!GlassBackdrop.Supported||(glass.IsLiquid&&glass.HasBackdrop),"Approved HLSL shader has a real backdrop");
        int placements=glass.NativePlacements,resizes=glass.SurfaceResizes;var stopwatch=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<60;i++)glass.Place(new RECT{Left=100+i,Top=22,Right=800-i,Bottom=82},24,hwnd);
        stopwatch.Stop();check(!GlassBackdrop.Supported||glass.NativePlacements-placements<3,"Sixty balloon morph frames reuse the same native glass window");
        check(!GlassBackdrop.Supported||glass.SurfaceResizes==resizes,"Balloon morph uses shader uniforms with zero GPU surface resizes");
        File.WriteAllText(Path.Combine(output,"glass-morph-performance.json"),System.Text.Json.JsonSerializer.Serialize(new{frames=60,nativePlacements=glass.NativePlacements-placements,surfaceResizes=glass.SurfaceResizes-resizes,placeTimeMs=stopwatch.Elapsed.TotalMilliseconds}));
        File.WriteAllText(Path.Combine(output,"integrated-glass.json"),System.Text.Json.JsonSerializer.Serialize(new{supported=GlassBackdrop.Supported,liquid=glass.IsLiquid,backdrop=glass.HasBackdrop,live=glass.HasLiveBackdrop,windows=LiquidGlass.Services.LiveBackdrop.ActiveWindows}));
        s.Theme="glass";glass.ApplyTint();glass.Place(new RECT{Left=100,Top=22,Right=800,Bottom=82},24,hwnd);
        check(!glass.IsLiquid,"Ordinary Cam keeps the previous Acrylic renderer");
        s.Theme="iosglass";glass.ApplyTint();glass.Place(new RECT{Left=100,Top=22,Right=800,Bottom=82},24,hwnd);Pump(300);
        check(!GlassBackdrop.Supported||glass.HasBackdrop,"Switching back restores HLSL without stale capture");
        glass.Close();glass.Close();
        Theme.Apply("iosglass");
        var popup=Flyout.Show(anchor,Ui.Text("Açılır panel testi",14),240);
        check(popup!=null&&(!SystemParameters.ClientAreaAnimation||((FrameworkElement)popup.Content).Opacity<.1),"Flyout starts transparent before its first rendered frame");
        Pump(250);check(popup?.IsVisible==true,"Flyout remains open after its opening animation");
        var popupSurface=popup?.Content as PopupSurface;Pump(800);
        check(popupSurface!=null&&(!GlassBackdrop.Supported||popupSurface.HasOpticalBackdrop),"iOS flyout uses the same live HLSL material as the bar");
        check(popupSurface?.Effect==null,"Flyout text and controls have no parent shadow/blur effect");
        Flyout.CloseCurrent();check(popupSurface?.OwnsBackdropResources==false,"Closing a flyout releases its capture resources");
        var sound=Flyout.Show(anchor,SoundPanel.Build(),340);Pump(900);
        if(sound!=null)Snapshot(sound,Path.Combine(output,"sound-ios-glass.png"));
        check(sound?.Content is PopupSurface,"Sound controls share the optical popup shell");Flyout.CloseCurrent();
        SettingsWindow.Open();Pump(900);var opticalSettings=Application.Current.Windows.OfType<SettingsWindow>().Single();
        check(opticalSettings.Content is PopupSurface shell&&(!GlassBackdrop.Supported||shell.HasOpticalBackdrop),"Settings and utility windows share the optical surface");Snapshot(opticalSettings,Path.Combine(output,"settings-ios-glass.png"));opticalSettings.Close();
        window.Close();s.Theme="midnight";Theme.Apply("midnight");
    }
}
