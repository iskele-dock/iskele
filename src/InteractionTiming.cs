// Hover dwell is independent from the already-approved expansion/icon animation.
namespace Iskele;
static class InteractionTiming
{
    public const double DefaultDelay=120;
    public const double MinimumDelay=90;
    public static double Delay=>Math.Round(Math.Clamp(App.Settings.HoverResponseMs,MinimumDelay,350));
    public const double Factor=.7;
    public static double Scale(double milliseconds)=>Math.Round(milliseconds*Factor);
}
