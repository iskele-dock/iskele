namespace Iskele;
static class DockLayout
{
    // Pixel geometry shared by real window placement and the offline regression suite.
    public static RECT Edge(RECT bounds, int thickness, bool top)
    {
        var height = Math.Max(0, bounds.Bottom - bounds.Top);
        thickness = Math.Clamp(thickness, 0, height);
        return new RECT { Left = bounds.Left, Right = bounds.Right,
            Top = top ? bounds.Top : bounds.Bottom - thickness,
            Bottom = top ? bounds.Top + thickness : bounds.Bottom };
    }
}
