namespace Iskele;

/// <summary>Widget seçici: soldaki kategoriler + arama, sağda canlı önizlemeler. Tıkla → dock'a ekle / çıkar.</summary>
class PickerWindow : UserControl // Embedded in Settings; this is no longer a separate window.
{
    // A small chevron, without a separate button plate, preserves the original cards.
    static readonly ControlTemplate ArrowTemplate=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
    <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleButton">
      <Border x:Name="surface" Background="Transparent" CornerRadius="6"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
      <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="{DynamicResource Track}"/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate>
    """);
    public static PickerWindow? Current { get; private set; }

    readonly Grid Body=new();
    readonly StackPanel categories=new(); // Explicit category list; no dropdown.
    readonly StackPanel content = new();
    readonly TextBlock summary;
    readonly TextBox search;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly List<Widget> previews = [];
    readonly Dictionary<string, Border> marks = [];
    string category = "";

    public static void Open() => Open(null);

    public static void Open(string? startCategory) => SettingsWindow.OpenWidgets(startCategory);

    public void SelectCategory(string? startCategory)
    {
        category=startCategory!=null&&WidgetRegistry.Categories.Contains(startCategory)?startCategory:"";
        BuildCategories();Fill();
    }

    internal PickerWindow()
    {
        Content=Body;
        // Keep the wider settings window, restoring the familiar selectable category list.
        Body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(180)});
        Body.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new DockPanel{Margin=new Thickness(14,18,12,16)};
        var input=TodosWidget.Input("Widget ara");search=input.Box;
        input.Host.Margin=new Thickness(0,0,0,14);DockPanel.SetDock(input.Host,Dock.Top);left.Children.Add(input.Host);
        search.TextChanged+=(_,_)=>Fill();
        left.Children.Add(new ScrollViewer{Content=categories,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var rail=new Border{Child=left,BorderThickness=new Thickness(0,0,1,0)};
        rail.SetResourceReference(Border.BorderBrushProperty,"Hairline");Body.Children.Add(rail);
        var right=new DockPanel{Margin=new Thickness(22,14,4,16)};Grid.SetColumn(right,1);
        var footer=new DockPanel{Margin=new Thickness(0,10,18,0)};
        summary=Ui.Text("",11.5,"FgDim");DockPanel.SetDock(summary,Dock.Right);footer.Children.Add(summary);
        footer.Children.Add(Ui.Text("Kart: ekle / çıkar · Ok: özelleştir",11.5,"FgDim"));
        DockPanel.SetDock(footer,Dock.Bottom);right.Children.Add(footer);
        right.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        Body.Children.Add(right);

        BuildCategories();
        Fill();

        timer.Tick += (_, _) =>
        {
            var now = DateTime.Now;
            foreach (var p in previews) try { p.Tick(now); } catch { }
        };
        IsVisibleChanged += (_,_) =>
        {
            if(IsVisible){Current=this;timer.Start();}else{if(Current==this)Current=null;timer.Stop();}
            App.EachDock(d=>d.SetEditMode(IsVisible));
        };
        Unloaded += (_, _) =>
        {
            timer.Stop();
            foreach (var p in previews) p.Detach();
            App.EachDock(d => d.SetEditMode(false));
            Current = null;
        };
    }

    void BuildCategories()
    {
        categories.Children.Clear();
        foreach(var id in new[]{""}.Concat(WidgetRegistry.Categories))
        {
            var label=Ui.Text(id.Length==0?"Tüm widget’lar":id,13.5,id==category?"Accent":"Fg",id==category?FontWeights.SemiBold:FontWeights.Normal);
            label.TextWrapping=TextWrapping.Wrap;label.TextTrimming=TextTrimming.None;
            var item=new Border{Child=label,Padding=new Thickness(10,9,10,9),Margin=new Thickness(0,1,0,1),CornerRadius=new CornerRadius(8),Background=Brushes.Transparent,Cursor=Cursors.Hand};
            if(id==category)item.SetResourceReference(Border.BackgroundProperty,"Track");
            item.MouseEnter+=(_,_)=>{if(id!=category)item.SetResourceReference(Border.BackgroundProperty,"Subtle");};
            item.MouseLeave+=(_,_)=>{if(id!=category)item.Background=Brushes.Transparent;};
            item.MouseLeftButtonUp+=(_,e)=>{e.Handled=true;category=id;BuildCategories();Fill();};
            categories.Children.Add(item);
        }
    }

    void Fill()
    {
        foreach (var p in previews) p.Detach();
        previews.Clear();
        marks.Clear();
        content.Children.Clear();

        var q = search.Text.Trim();
        var shown = WidgetRegistry.All
            .Where(w => category.Length == 0 || w.Category == category)
            .Where(w => WidgetSearch.Matches(w.Id, w.Name, w.Description, w.Category, q))
            .ToList();

        foreach (var group in shown.GroupBy(w => w.Category))
        {
            var title = Ui.Text(group.Key, 14, "FgDim", FontWeights.SemiBold);
            title.Margin = new Thickness(0, 6, 0, 12);
            content.Children.Add(title);
            var wrap = new WrapPanel();
            foreach (var info in group) wrap.Children.Add(Tile(info));
            content.Children.Add(wrap);
        }
        if (shown.Count == 0 && (q.Length > 0 || category != "Uygulamalar & Dosyalar")) content.Children.Add(Ui.Text("Eşleşen widget yok.", 13, "FgDim"));
        if (q.Length == 0 && (category.Length == 0 || category == "Uygulamalar & Dosyalar")) content.Children.Add(LaunchAdder());
        RefreshMarks();
    }

    /// <summary>Herhangi bir uygulamayı, dosyayı ya da klasörü dock'a kendi widget'ı olarak ekle.</summary>
    FrameworkElement LaunchAdder()
    {
        var title = Ui.Text("Kendi kısayolların", 14, "FgDim", FontWeights.SemiBold);
        title.Margin = new Thickness(0, 14, 0, 8);
        var wrap = new WrapPanel();
        foreach (var id in App.Settings.Widgets.Where(LaunchWidget.IsLaunch)) wrap.Children.Add(Tile(WidgetRegistry.Info(id)!));
        Border Button(string glyph, string text, Action a)
        {
            var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand, BorderThickness = new Thickness(1.5) };
            b.SetResourceReference(Border.BorderBrushProperty, "Track");
            b.Child = Ui.Row(Ui.Glyph(glyph, 16, "Accent"), Pad(Ui.Text(text, 13, "Fg", FontWeights.SemiBold)));
            b.MouseEnter += (_, _) => b.SetResourceReference(Border.BorderBrushProperty, "Accent");
            b.MouseLeave += (_, _) => b.SetResourceReference(Border.BorderBrushProperty, "Track");
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; a(); };
            return b;
        }
        static FrameworkElement Pad(FrameworkElement e) { e.Margin = new Thickness(8, 0, 0, 0); return e; }
        void Add(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var id = LaunchWidget.IdFor(path);
            if (!App.Settings.Widgets.Contains(id)) App.Settings.Widgets.Add(id);
            App.Settings.Save();
            App.EachDock(d => d.Rebuild());
            Fill();
        }
        wrap.Children.Add(Button("\uE8E5", "Uygulama ya da dosya ekle…", () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Dock'a eklenecek uygulama ya da dosya", Filter = "Tümü|*.*|Uygulamalar|*.exe;*.lnk" };
            var owner=Window.GetWindow(this);if(owner!=null)owner.Topmost=false;
            try {if(d.ShowDialog(owner)==true)Add(d.FileName);}finally{if(owner!=null)owner.Topmost=true;}
        }));
        wrap.Children.Add(Button("\uE8B7", "Klasör ekle…", () =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "Dock'a eklenecek klasör" };
            var owner=Window.GetWindow(this);if(owner!=null)owner.Topmost=false;
            try {if(d.ShowDialog(owner)==true)Add(d.FolderName);}finally{if(owner!=null)owner.Topmost=true;}
        }));
        var p = new StackPanel();
        p.Children.Add(title);
        p.Children.Add(wrap);
        return p;
    }

    FrameworkElement Tile(WidgetInfo info)
    {
        var preview = WidgetRegistry.Create(info.Id);
        FrameworkElement shown;
        if (preview != null)
        {
            previews.Add(preview);
            preview.IsHitTestVisible = false;
            preview.ToolTip = null;
            shown = preview;
        }
        else shown = Ui.Text(info.Name, 13, "FgDim");

        var stage = new Border
        {
            Child = shown, MaxWidth = 340, ClipToBounds = true, Padding = new Thickness(8), MinWidth = 110,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var mark = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0),
            Child = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        mark.SetResourceReference(Border.BackgroundProperty, "Accent");
        marks[info.Id] = mark;

        var g = new Grid();
        g.Children.Add(stage);
        g.Children.Add(mark);
        // Preserve the original preview card; only its top-right arrow expands settings.
        bool customizable=WidgetConfiguration.HasOptions(info.Id);
        mark.Margin=new Thickness(0,2,customizable?30:2,0);
        Border? card=null;
        var options=new Border{Visibility=Visibility.Collapsed,Padding=new Thickness(4,14,4,4),Width=360};
        options.MouseLeftButtonDown+=(_,e)=>e.Handled=true; // Editing must not toggle the containing widget card.
        options.MouseLeftButtonUp+=(_,e)=>e.Handled=true;
        var arrow=new ToggleButton{Content=Ui.Glyph("\uE70D",11),Width=26,Height=26,Background=Brushes.Transparent,BorderThickness=new Thickness(0),
            HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,1,1,0),ToolTip="Özelleştir"};
        arrow.Template=ArrowTemplate;
        System.Windows.Automation.AutomationProperties.SetName(arrow,info.Name+" özelleştir");
        void Expand(bool open)
        {
            if(open&&options.Child==null)options.Child=WidgetConfiguration.Build(info.Id);
            options.Visibility=open?Visibility.Visible:Visibility.Collapsed;
            arrow.Content=Ui.Glyph(open?"\uE70E":"\uE70D",11);
        }
        arrow.Checked+=(_,_)=>Expand(true);arrow.Unchecked+=(_,_)=>Expand(false);
        if(customizable)g.Children.Add(arrow); // No arrow without actual per-widget options.

        bool isNew = WidgetSearch.NewIds.Contains(info.Id) && !App.Settings.SeenWidgets.Contains(info.Id);
        var name = Ui.Text((isNew ? "● Yeni · " : "") + info.Name, 13, "Fg", FontWeights.SemiBold);
        if (isNew) name.Foreground = Brushes.IndianRed;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        var desc = Ui.Text(info.Description, 11.5, "FgDim");
        name.Margin=new Thickness(0,7,0,4);
        desc.TextWrapping=TextWrapping.Wrap;desc.TextTrimming=TextTrimming.None;
        desc.HorizontalAlignment = HorizontalAlignment.Center;
        // Description follows the preview width instead of inflating every tile into an equal column.
        shown.Measure(new Size(double.PositiveInfinity,100));
        desc.MaxWidth=Math.Clamp(shown.DesiredSize.Width+16,120,300);

        card = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(8,8,8,10), Margin = new Thickness(0, 0, 16, 16),
            Child = Ui.Col(g, name, desc, options), Tag="widget:"+info.Id, Cursor = Cursors.Hand, BorderThickness = new Thickness(1), ToolTip = info.Description,
        };
        card.VerticalAlignment=VerticalAlignment.Top; // Opening one card must not stretch its neighbors.
        card.SetResourceReference(Border.BackgroundProperty, "DockBg");
        card.SetResourceReference(Border.BorderBrushProperty, "Track");
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "Accent");
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "Track");
        card.MouseLeftButtonUp += (_, e) => { e.Handled = true; Toggle(info.Id); };
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;   // pencereyi sürüklemeyi başlatma
        return card;
    }

    void Toggle(string id)
    {
        if (!App.Settings.SeenWidgets.Contains(id)) App.Settings.SeenWidgets.Add(id);
        var list = App.Settings.Widgets;
        if (list.Contains(id)) list.Remove(id);
        else list.Add(id);
        App.Settings.Save();
        App.EachDock(d => d.Rebuild());
        RefreshMarks();
    }

    public void RefreshMarks()
    {
        foreach (var (id, m) in marks)
            m.Visibility = App.Settings.Widgets.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
        summary.Text = Localization.F("{0} widget · {1} tanesi dock'ta", WidgetRegistry.All.Length, App.Settings.Widgets.Count);
    }
}
