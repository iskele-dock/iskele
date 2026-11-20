using System.Diagnostics;
namespace Iskele;
static class Performance
{
    static DispatcherTimer? timer;
    static TimeSpan lastCpu;
    static DateTime lastAt;
    public static double CpuPercent { get; private set; }
    public static double MemoryMb { get; private set; }
    public static void Start()
    {
        using var p = Process.GetCurrentProcess(); lastCpu = p.TotalProcessorTime; lastAt = DateTime.UtcNow;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) => Sample(); timer.Start();
    }
    public static void Stop() => timer?.Stop();
    public static void Sample()
    {
        using var p = Process.GetCurrentProcess(); var now = DateTime.UtcNow;
        var elapsed = (now - lastAt).TotalSeconds;
        CpuPercent = elapsed > 0 ? Math.Max(0, (p.TotalProcessorTime - lastCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100) : 0;
        MemoryMb = p.WorkingSet64 / 1048576d; lastCpu = p.TotalProcessorTime; lastAt = now;
        IconCache.Trim(); Thumbs.Trim();
    }
    public static bool ShouldTick(bool force, bool visible, bool collapsed, bool inViewport, bool essential) => force || essential || (visible && !collapsed && inViewport);
    public static void Open()
    {
        Sample();
        var w = new FeaturePanel("Iskele performansı", 500, 280);
        var p = new StackPanel { Margin = new Thickness(20) };
        var text = Ui.Text($"CPU: %{CpuPercent:0.00} · RAM: {MemoryMb:0} MB\nHedef: boşta CPU <%0,5, çalışma kümesi <350 MB.\nÖnceki sürümün 5 saniyelik örneği: %0,21 / 310 MB.\nÖlçümler sisteme ve widget seçimine göre değişir.\nGizli kartlar dondurulur; simge önbellekleri sınırlandırılır.", 13, "Fg");
        text.TextWrapping = TextWrapping.Wrap; p.Children.Add(text); w.Host.Children.Add(p); w.Show();
    }
}
