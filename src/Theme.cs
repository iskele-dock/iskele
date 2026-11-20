using System.Windows.Markup;

namespace Iskele;

/// <summary>Temalar: fırça kaynakları uygulama genelinde değiştirilir, arayüz DynamicResource ile izler.</summary>
static class Theme
{
    public static readonly (string Hex, string Name)[] Accents =
    [
        ("#2F8CFF", "Mavi"), ("#8B5CF6", "Mor"), ("#EC4899", "Pembe"), ("#FF453A", "Kırmızı"),
        ("#FF9F0A", "Turuncu"), ("#FFD60A", "Sarı"), ("#30D158", "Yeşil"), ("#8E8E93", "Grafit"),
    ];

    public static readonly (string Id, string Name)[] Names =
        [("black", "Siyah"), ("white", "Beyaz"), ("midnight", "Gece"), ("glass", "Cam"), ("iosglass", "iOS Cam"), ("minimal", "Sade")];

    static readonly Dictionary<string, Dictionary<string, string>> Palettes = new()
    {
        ["black"] = new()
        {
            ["DockBg"] = "#F20D0D0F", ["DockBorder"] = "#26FFFFFF", ["CardBg"] = "#FF1B1B1E", ["CardBorder"] = "#14FFFFFF",
            ["Fg"] = "#FFFFFFFF", ["FgDim"] = "#99FFFFFF", ["Track"] = "#2EFFFFFF", ["MenuBg"] = "#FF161618",
        },
        ["white"] = new()
        {
            ["DockBg"] = "#F5EEEEF1", ["DockBorder"] = "#FFFFFFFF", ["CardBg"] = "#FFFFFFFF", ["CardBorder"] = "#12000000",
            ["Fg"] = "#FF111114", ["FgDim"] = "#8C000000", ["Track"] = "#1F000000", ["MenuBg"] = "#FFF7F7F9",
        },
        ["midnight"] = new()
        {
            ["DockBg"] = "#F20A0F1E", ["DockBorder"] = "#338FA8FF", ["CardBg"] = "#FF131A2E", ["CardBorder"] = "#1F8FA8FF",
            ["Fg"] = "#FFEEF2FF", ["FgDim"] = "#99C7D2FE", ["Track"] = "#33A5B4FF", ["MenuBg"] = "#FF0F1526",
        },
        ["glass"] = new()
        {
            // gerçek cam: arkadaki bulanık pencere görünsün diye dock neredeyse saydam, kenarda parlak ışık çizgisi
            ["DockBg"] = "#14FFFFFF", ["DockBorder"] = "#80FFFFFF", ["CardBg"] = "#1FFFFFFF", ["CardBorder"] = "#38FFFFFF",
            ["Fg"] = "#FFFFFFFF", ["FgDim"] = "#C8FFFFFF", ["Track"] = "#33FFFFFF", ["MenuBg"] = "#F21C2026",
        },
        ["iosglass"] = new()
        {
            // Approved HLSL surface carries the optics; keep labels/icons crisp.
            ["DockBg"] = "#03FFFFFF", ["DockBorder"] = "#30FFFFFF", ["CardBg"] = "#18FFFFFF", ["CardBorder"] = "#20FFFFFF",
            ["Fg"] = "#FF1C1C1E", ["FgDim"] = "#A6000000", ["Track"] = "#26000000", ["MenuBg"] = "#F7F5F5F7",
        },
        ["minimal"] = new()
        {
            // sade: kart arka planı ve çerçeve yok, yalnızca içerik
            ["DockBg"] = "#E6101014", ["DockBorder"] = "#1AFFFFFF", ["CardBg"] = "#00000000", ["CardBorder"] = "#00000000",
            ["Fg"] = "#FFF2F2F5", ["FgDim"] = "#8CFFFFFF", ["Track"] = "#26FFFFFF", ["MenuBg"] = "#FF16161A",
        },
    };

    public static void Apply(string id)
    {
        if (!Palettes.TryGetValue(id, out var p)) p = Palettes["midnight"];
        var res = Application.Current.Resources;
        foreach (var (key, hex) in p) res[key] = Brush(hex);
        string accent = App.Settings.Accent;
        try { ColorConverter.ConvertFromString(accent); } catch { accent = "#2F8CFF"; }
        res["Accent"] = Brush(accent);
        res["Good"] = Brush("#FF34C759");
        res["Warn"] = Brush("#FFFF453A");
        // panel pencereleri için yazı renginden türeyen ince tonlar (açık ve koyu temada aynı oranda)
        var fg = ((SolidColorBrush)res["Fg"]).Color;
        res["Subtle"] = Alpha(fg, 0x0C);     // grup kutusu / kenar çubuğu zemini
        res["Hover"] = Alpha(fg, 0x14);      // üzerine gelme
        res["Hairline"] = Alpha(fg, 0x1A);   // ayırıcı çizgi
        SetAccentSoft(((SolidColorBrush)res["Accent"]).Color);
    }

    static SolidColorBrush Alpha(Color c, byte a) { var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze(); return b; }
    static void SetAccentSoft(Color c) => Application.Current.Resources["AccentSoft"] = Alpha(c, 0x33);

    /// <summary>
    /// Kart stili (hedef 88, "Clear"): widget kartları dock'un temasından bağımsız siyah, beyaz ya da buzlu olsun.
    /// Kartın kendi kaynakları ezilir; yazı rengi de kartla uyumlu olur.
    /// </summary>
    public static void ApplyCardStyle(FrameworkElement card, string style)
    {
        var map = style switch
        {
            "black" => new Dictionary<string, string> { ["CardBg"] = "#F2121214", ["CardBorder"] = "#1FFFFFFF", ["Fg"] = "#FFFFFFFF", ["FgDim"] = "#A6FFFFFF", ["Track"] = "#33FFFFFF" },
            "white" => new() { ["CardBg"] = "#F7FFFFFF", ["CardBorder"] = "#14000000", ["Fg"] = "#FF111114", ["FgDim"] = "#8C000000", ["Track"] = "#1F000000" },
            "frosted" => new() { ["CardBg"] = "#4DFFFFFF", ["CardBorder"] = "#66FFFFFF", ["Fg"] = "#FFFFFFFF", ["FgDim"] = "#D9FFFFFF", ["Track"] = "#40FFFFFF" },
            _ => null,
        };
        card.Resources.Clear();
        if (map != null) foreach (var (k, v) in map) card.Resources[k] = Brush(v);
    }

    /// <summary>Vurgu rengini geçici olarak değiştir (ör. albüm kapağı rengi); ayarlara yazılmaz.</summary>
    public static void SetAccent(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        Application.Current.Resources["Accent"] = b;
        SetAccentSoft(c);
    }

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    /// <summary>Menü ve ipucu stilleri (varsayılan WPF görünümü yerine temaya uyan yuvarlak köşeli görünüm).</summary>
    public static void LoadStyles()
    {
        TransientDismissal.Install(); // Covers every ContextMenu, including widget-owned menus.
        const string xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="ContextMenu">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="HasDropShadow" Value="False"/>
    <Setter Property="FontFamily" Value="Segoe UI Variable Text, Segoe UI"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Foreground" Value="{DynamicResource Fg}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ContextMenu">
          <Border Background="{DynamicResource MenuBg}" BorderBrush="{DynamicResource DockBorder}"
                  BorderThickness="1" CornerRadius="10" Padding="5" MinWidth="170">
            <StackPanel IsItemsHost="True"/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <!-- Native menu semantics: checks and nested groups remain fully interactive. -->
  <Style TargetType="MenuItem">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="Template"><Setter.Value>
      <ControlTemplate TargetType="MenuItem">
        <Grid>
          <Border x:Name="b" Background="Transparent" CornerRadius="6" Padding="10,7">
            <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="20"/><ColumnDefinition/><ColumnDefinition Width="18"/></Grid.ColumnDefinitions>
              <TextBlock x:Name="check" Text="✓" Visibility="Collapsed"/>
              <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="False"/>
              <TextBlock x:Name="arrow" Grid.Column="2" Text="›" HorizontalAlignment="Right" Visibility="Collapsed"/>
            </Grid>
          </Border>
          <Popup x:Name="PART_Popup" Placement="Right" HorizontalOffset="-2" AllowsTransparency="True" Focusable="False" IsOpen="{Binding IsSubmenuOpen,RelativeSource={RelativeSource TemplatedParent}}">
            <Border Background="{DynamicResource MenuBg}" BorderBrush="{DynamicResource DockBorder}" BorderThickness="1" CornerRadius="10" Padding="5" MinWidth="190"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/></Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="b" Property="Background" Value="{DynamicResource Track}"/></Trigger>
          <Trigger Property="HasItems" Value="True"><Setter TargetName="arrow" Property="Visibility" Value="Visible"/></Trigger>
          <Trigger Property="IsChecked" Value="True"><Setter TargetName="check" Property="Visibility" Value="Visible"/></Trigger>
          <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Separator">
          <Border Height="1" Margin="8,4" Background="{DynamicResource Track}"/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="ToolTip">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="HasDropShadow" Value="False"/>
    <Setter Property="FontFamily" Value="Segoe UI Variable Text, Segoe UI"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="{DynamicResource Fg}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToolTip">
          <Border Background="{DynamicResource MenuBg}" BorderBrush="{DynamicResource DockBorder}"
                  BorderThickness="1" CornerRadius="7" Padding="9,5">
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- İnce, oksuz, yuvarlak kaydırma çubuğu (tüm pencereler; dikey ve yatay) -->
  <ControlTemplate x:Key="SlimThumb" TargetType="Thumb">
    <Border x:Name="b" CornerRadius="3" Background="{DynamicResource FgDim}" Opacity="0.4"/>
    <ControlTemplate.Triggers>
      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Opacity" Value="0.7"/></Trigger>
      <Trigger Property="IsDragging" Value="True"><Setter TargetName="b" Property="Opacity" Value="0.85"/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <Style TargetType="ScrollBar">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Width" Value="10"/>
    <Setter Property="MinWidth" Value="10"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ScrollBar">
          <Grid Background="Transparent">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
              <Track.Thumb><Thumb Template="{StaticResource SlimThumb}" Width="5" Margin="0,4" HorizontalAlignment="Center"/></Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property="Orientation" Value="Horizontal">
        <Setter Property="Width" Value="Auto"/>
        <Setter Property="MinWidth" Value="0"/>
        <Setter Property="Height" Value="10"/>
        <Setter Property="MinHeight" Value="10"/>
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="ScrollBar">
              <Grid Background="Transparent">
                <Track x:Name="PART_Track" IsDirectionReversed="False">
                  <Track.Thumb><Thumb Template="{StaticResource SlimThumb}" Height="5" Margin="4,0" VerticalAlignment="Center"/></Track.Thumb>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>
""";
        Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
    }
}
