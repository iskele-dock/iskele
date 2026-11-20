// [Iskele] App-owned paths and logging; no experimental directory or periodic capture logs.
namespace LiquidGlass.Services
{
    public static class GlassRuntime
    {
        public static string DataDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Iskele");
    }
    internal static class AppDataPaths
    {
        public static string Root => GlassRuntime.DataDirectory;
        public static string CrashLog => Path.Combine(Root, "glass-errors.log");
        public static void EnsureRoot() => Directory.CreateDirectory(Root);
    }
}
namespace LiquidGlass.Graphics
{
    internal static class DemoCapture { public static void Log(string line) { } }
}
