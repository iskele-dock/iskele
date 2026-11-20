using System.Windows.Automation;

namespace Iskele;

/// <summary>
/// Sistem tepsisi (hedef 27). Windows 11'de uygulama tepsi simgeleri (OneDrive, Discord…) görev çubuğunun "gizli simgeler"
/// panelinde durur ve resimlerine dışarıdan erişilemez. Bu widget Windows'un kendi tepsi panelini açar: simgeler, sağ tık
/// ve çift tık görev çubuğundakiyle birebir aynı çalışır. Gizli görev çubuğu panel için görünür yapılmaz.
/// </summary>
class TrayWidget : Widget
{
    /// <summary>Tepsi paneli açıkken dock küçülmesin; görev çubuğu gizli kalır.</summary>
    public static bool Open_ { get; private set; }
    static DispatcherTimer? watch;
    static bool opening;
    static InvisibleWindow? staging;
    static bool stagedOpening;
    static readonly object dismissalKey=new(); // Native overflow participates in outside-click dismissal.

    public TrayWidget()
    {
        Width = H;
        Padding = new Thickness(0);
        Child = Ui.Glyph("", F(15));
        ToolTip = "Sistem tepsisi (gizli simgeler)";
        OnClick(()=>Open(this));
    }

    // Defer Explorer invocation until the widget's mouse-up has finished.
    public static void Open(FrameworkElement? anchor=null)
    {
        RECT placement;
        if(anchor!=null&&PresentationSource.FromVisual(anchor)!=null)
        {
            var a=anchor.PointToScreen(new Point());var b=anchor.PointToScreen(new Point(anchor.ActualWidth,anchor.ActualHeight));
            placement=new RECT{Left=(int)a.X,Top=(int)a.Y,Right=(int)b.X,Bottom=(int)b.Y};
        }
        else{Native.GetCursorPos(out var cursor);placement=new RECT{Left=cursor.X,Right=cursor.X+1,Top=cursor.Y,Bottom=cursor.Y+1};}
        Application.Current.Dispatcher.BeginInvoke(()=>OpenCore(placement),DispatcherPriority.Background);
    }
    static async void OpenCore(RECT anchor)
    {
        if(opening)return;
        opening=true;
        try
        {
            var tray=Native.FindWindow("Shell_TrayWnd",null);
            if(tray==IntPtr.Zero)return;
            var existing=OverflowWindow();
            if(Open_&&existing!=IntPtr.Zero&&Native.IsWindowVisible(existing)){Native.ShowWindow(existing,Native.SW_HIDE);Restore();return;}
            bool hidden=!Native.IsWindowVisible(tray);Open_=true;
            watch?.Stop();
            // Explorer may animate at its own anchor. Keep that intermediate position invisible.
            staging?.Dispose();staging=InvisibleWindow.TryCreate(existing);stagedOpening=staging!=null;
            try
            {
                // Temporarily expose Explorer's provider at zero opacity, outside the WPF dispatcher.
                await Task.Run(()=>InvokeExplorer(tray,hidden));

            }
            catch(Exception ex){App.Log("tepsi düğmesi: "+ex.Message);Restore();return;}
            if(staging!=null&&SystemParameters.ClientAreaAnimation)await Task.Delay(180);
            Open_=true;
            var opened=DateTime.UtcNow;bool positioned=false;
            void Update()
            {
                var overflow=OverflowWindow();
                if(overflow!=IntPtr.Zero&&(!positioned||Native.IsWindowVisible(overflow)))
                {
                    // A hidden Explorer taskbar can leave the overflow offscreen or behind the dock.
                    if(!positioned)positioned=PlaceOverflow(overflow,anchor);
                }
                if(hidden&&Native.IsWindowVisible(tray))TaskbarGuard.Reveal(false);
                bool visible=overflow!=IntPtr.Zero&&Native.IsWindowVisible(overflow);
                if(!visible&&(DateTime.UtcNow-opened).TotalMilliseconds>1000)
                {
                    if(!positioned)App.Log("tepsi: gizli simgeler paneli açılamadı");
                    Restore();
                }
            }
            Update();
            watch=new DispatcherTimer(TimeSpan.FromMilliseconds(100),DispatcherPriority.Background,(_,_)=>Update(),Application.Current.Dispatcher);
        }
        catch(Exception ex){App.Log("tepsi: "+ex.Message);Restore();}
        finally{opening=false;}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool SetLayeredWindowAttributes(IntPtr window,uint key,byte alpha,uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool GetLayeredWindowAttributes(IntPtr window,out uint key,out byte alpha,out uint flags);
    // Restore the exact original layered style/attributes after positioning the popup.
    sealed class InvisibleWindow : IDisposable
    {
        readonly IntPtr window;
        readonly long style;
        readonly bool layered;
        readonly uint key,flags;
        readonly byte alpha;
        bool disposed;
        InvisibleWindow(IntPtr window,long style,bool layered,uint key,byte alpha,uint flags)
        {this.window=window;this.style=style;this.layered=layered;this.key=key;this.alpha=alpha;this.flags=flags;}
        internal static InvisibleWindow? TryCreate(IntPtr window)
        {
            if(window==IntPtr.Zero)return null;
            long style=(long)Native.GetWindowLongPtr(window,Native.GWL_EXSTYLE);bool layered=(style&0x80000)!=0;
            uint key=0,flags=2;byte alpha=255;
            if(layered&&!GetLayeredWindowAttributes(window,out key,out alpha,out flags))return null;
            Native.SetWindowLongPtr(window,Native.GWL_EXSTYLE,(IntPtr)(style|0x80000));
            if(!SetLayeredWindowAttributes(window,0,0,2)){Native.SetWindowLongPtr(window,Native.GWL_EXSTYLE,(IntPtr)style);return null;}
            return new InvisibleWindow(window,style,layered,key,alpha,flags);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(layered)SetLayeredWindowAttributes(window,key,alpha,flags);
            Native.SetWindowLongPtr(window,Native.GWL_EXSTYLE,(IntPtr)style);
        }
    }

    static void InvokeExplorer(IntPtr tray,bool hidden)
    {
        long originalStyle=(long)Native.GetWindowLongPtr(tray,Native.GWL_EXSTYLE);
        bool wasLayered=(originalStyle&0x80000)!=0;
        uint originalKey=0,originalFlags=2;byte originalAlpha=255;
        if(hidden&&wasLayered&&!GetLayeredWindowAttributes(tray,out originalKey,out originalAlpha,out originalFlags))
            throw new InvalidOperationException("Görev çubuğunun görünüm durumu okunamadı.");
        try
        {
            if(hidden)
            {
                Native.SetWindowLongPtr(tray,Native.GWL_EXSTYLE,(IntPtr)(originalStyle|0x80000));
                if(!SetLayeredWindowAttributes(tray,0,0,2))throw new InvalidOperationException("Görev çubuğu görünmeden tepsi sağlayıcısı hazırlanamadı.");
                Native.ShowWindow(tray,Native.SW_SHOWNA);
            }
            for(int attempt=0;attempt<10;attempt++)
            {
                var root=AutomationElement.FromHandle(tray);
                var candidates=root.FindAll(TreeScope.Descendants,new OrCondition(
                    new PropertyCondition(AutomationElement.ClassNameProperty,"SystemTray.OverflowIcon"),
                    new PropertyCondition(AutomationElement.AutomationIdProperty,"SystemTrayChevron"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button))).Cast<AutomationElement>();
                foreach(var candidate in candidates)
                {
                    var info=candidate.Current;
                    if(info.ClassName!="SystemTray.OverflowIcon"&&info.AutomationId!="SystemTrayChevron"&&!info.Name.Contains("gizli",StringComparison.OrdinalIgnoreCase)&&!info.Name.Contains("hidden",StringComparison.OrdinalIgnoreCase))continue;
                    if(candidate.TryGetCurrentPattern(InvokePattern.Pattern,out var pattern)&&pattern is InvokePattern invoke){invoke.Invoke();return;}
                }
                Thread.Sleep(50);
            }
            throw new InvalidOperationException("Explorer tepsi düğmesi bulunamadı.");
        }
        finally
        {
            if(hidden)
            {
                Native.ShowWindow(tray,Native.SW_HIDE);
                if(wasLayered)SetLayeredWindowAttributes(tray,originalKey,originalAlpha,originalFlags);
                Native.SetWindowLongPtr(tray,Native.GWL_EXSTYLE,(IntPtr)originalStyle);
            }
        }
    }

    static bool PlaceOverflow(IntPtr overflow,RECT anchor)
    {
        if(!Native.GetWindowRect(overflow,out var bounds))return false;
        int width=bounds.Right-bounds.Left,height=bounds.Bottom-bounds.Top;
        if(width<=4||height<=4)return false;
        var monitor=Monitors.At(new POINT{X=(anchor.Left+anchor.Right)/2,Y=(anchor.Top+anchor.Bottom)/2})??Monitors.Primary;
        var placed=OverflowPlacement(anchor,width,height,monitor.Work,DockWindow.IsTop);
        Native.SetWindowPos(overflow,(IntPtr)(-1),placed.Left,placed.Top,width,height,0x0010|0x0040); // TOPMOST, NOACTIVATE, SHOWWINDOW
        staging?.Dispose();staging=null; // Reveal once, after final placement and native opening motion.
        Native.SetForegroundWindow(overflow);
        TransientDismissal.Add(dismissalKey,p=>Native.GetWindowRect(overflow,out var r)&&p.X>=r.Left&&p.X<r.Right&&p.Y>=r.Top&&p.Y<r.Bottom,
            ()=>{Native.ShowWindow(overflow,Native.SW_HIDE);Restore();});
        return Native.IsWindowVisible(overflow);
    }
    internal static RECT OverflowPlacement(RECT anchor,int width,int height,RECT work,bool top)
    {
        int x=Math.Clamp((anchor.Left+anchor.Right-width)/2,work.Left,Math.Max(work.Left,work.Right-width));
        int y=top?anchor.Bottom+8:anchor.Top-height-8;
        y=Math.Clamp(y,work.Top,Math.Max(work.Top,work.Bottom-height));
        return new RECT{Left=x,Top=y,Right=x+width,Bottom=y+height};
    }
    static IntPtr OverflowWindow()
    {
        var handle=Native.FindWindow("TopLevelWindowForOverflowXamlIsland",null);
        return handle!=IntPtr.Zero?handle:Native.FindWindow("NotifyIconOverflowWindow",null);
    }

    internal static void InvokeWithoutReveal(Action invoke,bool hidden,Action keepHidden)
    {
        try{invoke();}finally{if(hidden)keepHidden();} // Keep hidden even if Explorer rejects the invocation.
    }

    // Local native smoke check; never reveal the taskbar and close the opened panel afterwards.
    internal static int Probe(string directory)
    {
        Settings.DirectoryOverride=Path.Combine(Path.GetFullPath(directory),"isolated-appdata");
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var taskbar=Native.FindWindow("Shell_TrayWnd",null);
        long taskbarStyleBefore=(long)Native.GetWindowLongPtr(taskbar,Native.GWL_EXSTYLE);
        bool hiddenBefore=!Native.IsWindowVisible(taskbar);
        Open();
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(900)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
        var overflow=OverflowWindow();
        bool panel=overflow!=IntPtr.Zero&&Native.IsWindowVisible(overflow);
        Native.GetWindowRect(overflow,out var panelRect);
        var panelMonitor=Monitors.At(new POINT{X=(panelRect.Left+panelRect.Right)/2,Y=(panelRect.Top+panelRect.Bottom)/2})??Monitors.Primary;
        bool onScreen=panelRect.Left>=panelMonitor.Work.Left&&panelRect.Top>=panelMonitor.Work.Top&&panelRect.Right<=panelMonitor.Work.Right&&panelRect.Bottom<=panelMonitor.Work.Bottom;
        bool foreground=Native.GetForegroundWindow()==overflow;
        bool topmost=((long)Native.GetWindowLongPtr(overflow,Native.GWL_EXSTYLE)&8)!=0;
        int buttons=0;
        try{buttons=AutomationElement.FromHandle(overflow).FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button)).Count;}catch{}

        int visibleButtons=0;try{visibleButtons=AutomationElement.FromHandle(overflow).FindAll(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.IsOffscreenProperty,false))).Count;}catch{}
        bool paintable=((long)Native.GetWindowLongPtr(overflow,Native.GWL_EXSTYLE)&0x80000)==0 || (GetLayeredWindowAttributes(overflow,out _,out var alpha,out _)&&alpha>0);
        bool taskbarStyleRestored=(long)Native.GetWindowLongPtr(taskbar,Native.GWL_EXSTYLE)==taskbarStyleBefore;
        bool hiddenAfter=!Native.IsWindowVisible(taskbar);
        if(panel)Native.ShowWindow(overflow,Native.SW_HIDE);watch?.Stop();Restore();
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"tray-probe.json"),System.Text.Json.JsonSerializer.Serialize(new{hiddenBefore,hiddenAfter,panel,onScreen,foreground,topmost,buttons,visibleButtons,stagedOpening,paintable,taskbarStyleRestored,left=panelRect.Left,top=panelRect.Top,right=panelRect.Right,bottom=panelRect.Bottom}));
        app.Shutdown();return panel&&onScreen&&topmost&&visibleButtons>0&&paintable&&taskbarStyleRestored&&(!hiddenBefore||hiddenAfter)?0:1;
    }

    static void Restore()
    {
        staging?.Dispose();staging=null;
        watch?.Stop();TransientDismissal.Remove(dismissalKey);
        if (!Open_) return;
        Open_ = false;
        if (App.Settings.HideTaskbar) TaskbarGuard.Reveal(false);
    }
}
