// Single resource for application, panels and settings branding.
using System.Windows.Media.Imaging;
namespace Iskele;
static class Brand
{
    static readonly BitmapImage Logo = Load();
    static BitmapImage Load()
    {
        var image = new BitmapImage(new Uri("pack://application:,,,/Iskele;component/Assets/Brand/iskele.png", UriKind.Absolute));
        image.Freeze();
        return image;
    }
    public static ImageSource Source => Logo;
    public static Image Image(double size) => new() { Source = Source, Width = size, Height = size, Stretch = Stretch.Uniform };
}
