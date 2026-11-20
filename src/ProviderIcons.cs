// Provider-owned SVG silhouettes, drawn natively at their original aspect ratio.
using System.Globalization;
using System.Xml.Linq;
namespace Iskele;
static class ProviderIcons
{
    public static FrameworkElement Create(string provider,double size)
    {
        string file=provider switch{"Claude"=>"claude","Gemini"=>"gemini","OpenCode"=>"opencode",_=>"openai"};
        using var stream=typeof(ProviderIcons).Assembly.GetManifestResourceStream($"Iskele.Assets.Providers.{file}.svg")??throw new InvalidOperationException("Provider icon is missing: "+file);
        var svg=XDocument.Load(stream).Root!;
        var numbers=svg.Attribute("viewBox")!.Value.Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray();
        var drawing=new DrawingGroup();
        // Transparent viewbox preserves the official asset's clear space.
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(numbers[0],numbers[1],numbers[2],numbers[3]))));
        foreach(var path in svg.Descendants().Where(e=>e.Name.LocalName=="path"))
        {
            string fill=path.Attribute("fill")?.Value??"currentColor";
            Brush brush;
            if(fill.StartsWith("url(",StringComparison.Ordinal))
            {
                var gradient=svg.Descendants().First(e=>e.Name.LocalName=="radialGradient");
                var radial=new RadialGradientBrush{MappingMode=BrushMappingMode.Absolute,Center=new Point(0,0),GradientOrigin=new Point(0,0),RadiusX=1,RadiusY=1};
                foreach(var stop in gradient.Elements())radial.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stop.Attribute("stop-color")!.Value),double.Parse(stop.Attribute("offset")!.Value,CultureInfo.InvariantCulture)));
                var v=System.Text.RegularExpressions.Regex.Matches(gradient.Attribute("gradientTransform")!.Value,@"[-+]?\d*\.?\d+").Select(m=>double.Parse(m.Value,CultureInfo.InvariantCulture)).ToArray();
                var transform=new TransformGroup();transform.Children.Add(new ScaleTransform(v[3],v[4]));transform.Children.Add(new RotateTransform(v[2]));transform.Children.Add(new TranslateTransform(v[0],v[1]));radial.Transform=transform;brush=radial;
            }
            else brush=fill=="currentColor"?Ui.B("Fg"):(Brush)new BrushConverter().ConvertFromString(fill)!;
            if(path.Attribute("fill-opacity") is {} opacity){brush=brush.Clone();brush.Opacity=double.Parse(opacity.Value,CultureInfo.InvariantCulture);}
            drawing.Children.Add(new GeometryDrawing(brush,null,Geometry.Parse("F1 "+path.Attribute("d")!.Value)));
        }
        var image=new System.Windows.Controls.Image{Source=new DrawingImage(drawing),Width=size,Height=size,Stretch=Stretch.Uniform,ToolTip=provider};
        System.Windows.Automation.AutomationProperties.SetName(image,provider+" logosu");return image;
    }
}
