namespace Iskele;

/// <summary>Ayarlar: görünüm, dock, uygulamalar, widget verileri, genel. Değişiklikler anında uygulanır.</summary>
class SettingsWindow : PanelWindow
{
    static SettingsWindow? current;

    public static void Open()
    {
        if (current != null) { current.Activate(); return; }
        current = new SettingsWindow();
        current.Closed += (_, _) => current = null;
        current.Show();
        current.Activate();
    }

    static Settings S => App.Settings;
    // Catalog navigation uses this settings window, including dock/context-menu entry points.
    PickerWindow? widgetCatalog;
    readonly ContentControl widgetHost=new(){Height=520};
    PickerWindow EnsureCatalog()
    {
        if(widgetCatalog==null){widgetCatalog=new PickerWindow();widgetHost.Content=widgetCatalog;}
        return widgetCatalog;
    }
    Action<string>? navigate;
    public static void OpenWidgets(string? category=null)
    {
        Open();current!.navigate?.Invoke("Widget’lar");current.EnsureCatalog().SelectCategory(category);
    }

    internal static void Apply()
    {
        S.Save();
        App.CreateDocks();
        App.EachDock(d => d.ApplySettings());
    }

    SettingsWindow() : base("Ayarlar", Math.Min(1220,SystemParameters.WorkArea.Width-40), Math.Min(820,SystemParameters.WorkArea.Height-40))
    {
        widgetHost.Height=Math.Max(380,Height-220); // More vertical room while respecting the monitor.
        ShowInTaskbar=true; // Settings remains discoverable with Alt+Tab while the dock itself stays unobtrusive.
        // ayar satırları sırayla buraya eklenir; Section(...) işaretleri bölümleri ayırır, BuildLayout sayfalara dağıtır
        var p = new StackPanel();

        // ── Görünüm
        p.Children.Add(Section("Görünüm"));
        int themeIdx = Array.FindIndex(Theme.Names, t => t.Id == S.Theme);
        p.Children.Add(Row("Tema", "Dock ve kartların renkleri",
            Ui.Segmented(Theme.Names.Select(t => t.Name).ToArray(), Math.Max(0, themeIdx), i => { S.Theme = Theme.Names[i].Id; Apply(); })));
        var glassOptions=new Expander{Header=Localization.T("iOS Cam özelleştirme"),Margin=new Thickness(0,4,0,4)};
        glassOptions.SetResourceReference(ForegroundProperty,"Fg");
        glassOptions.Content=Ui.Col(
            SliderRow("Işık kırılması","Camın kenarındaki mercek etkisi",S.GlassRefraction,0,18,v=>S.GlassRefraction=v),
            SliderRow("Bulanıklık","Camın arkasındaki görüntü",S.GlassBlur,0,20,v=>S.GlassBlur=v),
            SliderRow("Beyaz cam tonu","Camın açıklığı",S.GlassTint,0,.4,v=>S.GlassTint=v),
            SliderRow("Kenar ışığı","Cam kenarının parlaklığı",S.GlassHighlight,0,1.2,v=>S.GlassHighlight=v));
        p.Children.Add(glassOptions);
        p.Children.Add(Row("Vurgu rengi", "Düğmeler, halkalar, göstergeler", AccentPicker()));
        p.Children.Add(Row("Boyut", "Kartların büyüklüğü",
            Ui.Segmented(["S", "M", "L"], S.Size switch { "S" => 0, "L" => 2, _ => 1 }, i => { S.Size = new[] { "S", "M", "L" }[i]; Apply(); })));
        p.Children.Add(Row("Biçim", "Ortada hap şeklinde ya da tam genişlik",
            Ui.Segmented(["Hap", "Tam genişlik"], S.FullWidth ? 1 : 0, i => { S.FullWidth = i == 1; Apply(); })));
        p.Children.Add(Row("Aralık", "Widget'lar arasındaki boşluk",
            Ui.Segmented(["Dar", "Normal", "Geniş"], S.Spacing <= 1 ? 0 : S.Spacing >= 6 ? 2 : 1, i => { S.Spacing = new[] { 1, 3, 6 }[i]; Apply(); })));
        p.Children.Add(Row("İç boşluk", "Dock kenarı ile kartlar arası",
            Ui.Segmented(["Az", "Normal", "Çok"], S.DockPadding <= 3 ? 0 : S.DockPadding >= 8 ? 2 : 1, i => { S.DockPadding = new[] { 3, 5, 8 }[i]; Apply(); })));

        // ── Dock
        p.Children.Add(Section("Dock"));
        p.Children.Add(Row("Yerleşim", "Şerit: ekran kenarında satır · Balon: en üstte ortada yüzen, üzerine gelince açılan ada",
            Ui.Segmented(["Şerit", "Balon"], DockWindow.Balloon ? 1 : 0, i => { S.Mode = i == 1 ? "balloon" : "bar"; Apply(); })));
        p.Children.Add(Row("Balon", "Küçük haldeyken saat ve çalan şarkıyı gösterir",
            Ui.Segmented(["Üzerine gelince aç", "Hep açık"], S.BalloonAlwaysOpen ? 1 : 0, i => { S.BalloonAlwaysOpen = i == 1; Apply(); })));
        p.Children.Add(Row("Şerit konumu", "Şerit ekranın hangi kenarında dursun",
            Ui.Segmented(["Alt", "Üst"], S.Edge == "top" ? 1 : 0, i => { S.Edge = i == 1 ? "top" : "bottom"; Apply(); })));
        p.Children.Add(Row("Balon konumu", "Balon ekranın üstünde mi altında mı yüzsün",
            Ui.Segmented(["Alt", "Üst"], S.BalloonEdge == "top" ? 1 : 0, i => { S.BalloonEdge = i == 1 ? "top" : "bottom"; Apply(); })));
        p.Children.Add(Row("Kenardan uzaklık", "Dock'u ekran kenarından kaldırır (hap biçiminde)",
            Ui.Segmented(["Yok", "Az", "Çok"], S.EdgeDistance <= 0 ? 0 : S.EdgeDistance >= 12 ? 2 : 1, i => { S.EdgeDistance = new[] { 0, 6, 14 }[i]; Apply(); })));
        p.Children.Add(Row("Otomatik gizle", "Çalışırken kenara kayar; imleç kenara değince gelir",
            Switch(S.AutoHide, false, i => { S.AutoHide = i == 1; Apply(); })));
        p.Children.Add(Row("Masaüstünde göster", "Otomatik gizlemede, boş masaüstündeyken dock açık kalır",
            Switch(S.ShowOnDesktop, true, i => { S.ShowOnDesktop = i == 0; S.Save(); })));
        p.Children.Add(Row("Dock'u kilitle", "Sürükleyerek kenar değiştirmeyi engeller",
            Switch(S.LockDock, false, i => { S.LockDock = i == 1; S.Save(); })));
        p.Children.Add(Row("Windows görev çubuğu", "Iskele açıkken gizlenir; kapatınca geri gelir",
            Ui.Segmented(["Gizle", "Göster"], S.HideTaskbar ? 0 : 1, i => { S.HideTaskbar = i == 0; Apply(); })));
        p.Children.Add(Row("Gizli görev çubuğu", "Tamamen gizli ya da imleç ekranın alt kenarına değince görünsün",
            Ui.Segmented(["Gizli", "Otomatik gizle"], S.TaskbarMode == "autohide" ? 1 : 0, i => { S.TaskbarMode = i == 1 ? "autohide" : "hidden"; Apply(); })));
        p.Children.Add(Row("Görev çubuğu alanı", "Koru: ekran alanı değişmez, masaüstü simgeleri ve pencereler yerinden oynamaz · Kullan: pencereler tüm ekranı kaplar (düzen bir kez değişir)",
            Ui.Segmented(["Koru", "Kullan"], S.TaskbarSpace == "free" ? 1 : 0, i => { S.TaskbarSpace = i == 1 ? "free" : "keep"; Apply(); })));

        p.Children.Add(Row("Ekranlar", "Ana ekran: tek dock · Her ekran: her monitörde bir dock · Takip et: imleç bir ekranın kenarında durunca dock oraya geçer",
            Ui.Segmented(["Ana ekran", "Her ekran", "Takip et"], S.Displays switch { "all" => 1, "follow" => 2, _ => 0 }, i => { S.Displays = new[] { "main", "all", "follow" }[i]; Apply(); })));
        p.Children.Add(SliderRow("Bar tepki süresi", "Balona geldikten sonra açılmaya başlama gecikmesi · varsayılan 120 ms",S.HoverResponseMs,90,350,v=>S.HoverResponseMs=v,"ms"));

        // Per-widget preferences live in the embedded widget catalog.
        p.Children.Add(Section("Widget’lar"));
        widgetHost.IsVisibleChanged+=(_,_)=>{if(widgetHost.IsVisible)EnsureCatalog();}; // No hidden widget previews on unrelated settings pages.
        p.Children.Add(widgetHost);

        p.Children.Add(Section("Uyarılar ve otomasyonlar"));
        p.Children.Add(Row("Yenileme hatırlatıcıları", "Kaydettiğiniz AI planları ve abonelikler için 3 gün önce",
            Switch(S.RenewalAlerts, false, i => { S.RenewalAlerts = i == 1; S.Save(); })));

        p.Children.Add(Row("Sessiz saatler", "Bu aralıkta Iskele bildirim ve ses göstermez",
            Switch(S.QuietHours, false, i => { S.QuietHours = i == 1; S.Save(); })));
        p.Children.Add(TextRow("Sessiz başlangıç", "Saat: 0–23", S.QuietStart.ToString(), v => { if (int.TryParse(v, out int h) && h >= 0 && h <= 23) S.QuietStart = h; }));
        p.Children.Add(TextRow("Sessiz bitiş", "Saat: 0–23; aynı saat seçilirse tüm gün sessiz", S.QuietEnd.ToString(), v => { if (int.TryParse(v, out int h) && h >= 0 && h <= 23) S.QuietEnd = h; }));
        p.Children.Add(Row("Uyarı sesi", "Windows sistem sesleri",
            Ui.Segmented(["Sessiz", "Bilgi", "Uyarı"], S.AlertSound == "asterisk" ? 1 : S.AlertSound == "exclamation" ? 2 : 0, i => { S.AlertSound = new[] { "none", "asterisk", "exclamation" }[i]; S.Save(); })));

        p.Children.Add(PanelRows.Link("Geri bildirim ve hata raporu…", Diagnostics.Open));

        p.Children.Add(PanelRows.Link("CPU ve bellek ölçümü…", Performance.Open));

        // ── Genel
        p.Children.Add(Section("Genel"));
        p.Children.Add(Row("Arayüz dili", "Türkçe veya İngilizce; mevcut pencereler yeniden açılır",
            Ui.Segmented(["Türkçe", "English (beta)"], S.Language == "en" ? 1 : 0, i =>
            {
                S.Language = i == 1 ? "en" : "tr"; Apply();
                current?.Close(); Open();
            })));

        p.Children.Add(Row("Windows ile başlat", "Oturum açınca Iskele kendiliğinden açılır",
            Switch(Startup.Enabled, true, i => { try { Startup.Enabled = i == 0; } catch (Exception ex) { App.Log(ex); } })));

        var dataLink = PanelRows.Link("Klasörü aç", () => App.Open(Settings.Dir));
        p.Children.Add(Row("Veri klasörü", "Ayarlar, notlar ve ekran süresi yalnızca bu bilgisayarda tutulur", dataLink));

        p.Children.Add(Row("Iskele'yi kaldır", Uninstaller.Available
                ? "Iskele kapanır, görev çubuğu geri gelir ve program bu bilgisayardan silinir"
                : "Bu kopya kurulum programıyla kurulmamış; klasörü silmeniz yeterli",
            PanelRows.Link(Uninstaller.Available ? "Kaldır…" : "Klasörü göster",
                () => { if (Uninstaller.Available) Uninstaller.Run(); else App.Open(AppContext.BaseDirectory); })));


        BuildLayout(p);
    }

    // ───────── yerleşim: macOS Sistem Ayarları / Windows 11 tarzı kenar çubuğu + gruplanmış satırlar ─────────

    /// <summary>Bölüm işareti; BuildLayout bunu görünce yeni sayfa açar.</summary>
    sealed class SectionMark : FrameworkElement { public string Id = ""; }

    static FrameworkElement Section(string title) => new SectionMark { Id = title };

    /// <summary>Kenar çubuğundaki bölüm simgesi ve rengi (Segoe Fluent Icons).</summary>
    static (string Glyph, string Color) SectionIcon(string id) => id switch
    {
        "Görünüm" => ("", "#FFAF52DE"),
        "Dock" => ("", "#FF0A84FF"),
        "Uygulamalar" => ("", "#FFFF9500"),
        "Widget’lar" => ("", "#FF30B158"),
        "Uyarılar ve otomasyonlar" => ("", "#FFFF3B30"),
        _ => ("", "#FF8E8E93"),
    };

    sealed class Page
    {
        public string Id = "";
        public readonly StackPanel Root = new() { Margin = new Thickness(0, 0, 0, 26) };
        public readonly List<(Border Wrap, string Text)> Items = [];
        public Border Nav = null!;
        public Action<bool> Select = _ => { };
    }

    void BuildLayout(StackPanel source)
    {
        var pages = new List<Page>();
        Page? page = null;
        StackPanel? rows = null;
        foreach (var item in source.Children.Cast<UIElement>().ToList())
        {
            source.Children.Remove(item);
            if (item is SectionMark mark)
            {
                page = new Page { Id = mark.Id };
                var title = Ui.Text(mark.Id, 26, "Fg", FontWeights.SemiBold);
                title.Margin = new Thickness(2, 0, 0, 16);
                page.Root.Children.Add(title);
                rows = new StackPanel();
                page.Root.Children.Add(Group(rows));
                pages.Add(page);
                continue;
            }
            if (page == null || rows == null) continue;
            if (item is FrameworkElement fe) { fe.Margin = new Thickness(0); }
            var wrap = new Border { Child = item, Padding = ReferenceEquals(item,widgetHost) ? new Thickness(0) : new Thickness(18, 13, 16, 13), BorderThickness = new Thickness(0, rows.Children.Count == 0 ? 0 : 1, 0, 0) };
            wrap.SetResourceReference(Border.BorderBrushProperty, "Hairline");
            rows.Children.Add(wrap);
            page.Items.Add((wrap, SearchText(item)));
        }

        // ── sağ: sayfa içeriği
        var content = new StackPanel { Margin = new Thickness(34, 16, 28, 16), MaxWidth = 920 };
        foreach (var pg in pages) content.Children.Add(pg.Root);
        var empty = Ui.Text("Sonuç yok", 14, "FgDim");
        empty.Margin = new Thickness(2, 30, 0, 0);
        empty.Visibility = Visibility.Collapsed;
        content.Children.Add(empty);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Margin = new Thickness(0, 14, 6, 14) };

        var close = new IconButton("", 11, 30) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 12, 0) };
        close.Click += Close;
        var right = new Grid();
        right.Children.Add(scroll);
        right.Children.Add(close);

        // ── sol: kenar çubuğu
        var side = new StackPanel { Margin = new Thickness(14, 20, 14, 14) };
        // Approved rounded wordmark logo replaces the settings placeholder.
        var logo = Brand.Image(44);
        var name = Ui.Col(Ui.Text("Ayarlar", 16, "Fg", FontWeights.SemiBold),
            Ui.Text($"Iskele {typeof(App).Assembly.GetName().Version?.ToString(3)}", 11.5, "FgDim", localize: false));
        name.Margin = new Thickness(10, 0, 0, 0);
        var brand = Ui.Row(logo, name);
        brand.Margin = new Thickness(6, 0, 0, 16);
        side.Children.Add(brand);

        var search = TodosWidget.Input("Ayarlarda ara");
        search.Host.Margin = new Thickness(0, 0, 0, 12);
        side.Children.Add(search.Host);

        int selected = 0;
        void Show()
        {
            string q = search.Box.Text.Trim();
            bool any = false;
            for (int i = 0; i < pages.Count; i++)
            {
                var pg = pages[i];
                if (q.Length == 0)
                {
                    pg.Root.Visibility = i == selected ? Visibility.Visible : Visibility.Collapsed;
                    foreach (var (w, _) in pg.Items) w.Visibility = Visibility.Visible;
                    pg.Select(i == selected);
                    any = true;
                }
                else
                {
                    bool hit = false;
                    foreach (var (w, text) in pg.Items)
                    {
                        bool m = Tr.C.CompareInfo.IndexOf(text, q, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;
                        w.Visibility = m ? Visibility.Visible : Visibility.Collapsed;
                        hit |= m;
                    }
                    pg.Root.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
                    pg.Select(false);
                    any |= hit;
                }
                // görünen ilk satırın üst çizgisi olmasın
                bool first = true;
                foreach (var (w, _) in pg.Items)
                {
                    if (w.Visibility != Visibility.Visible) continue;
                    w.BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0);
                    first = false;
                }
            }
            empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            scroll.ScrollToTop();
        }
        search.Box.TextChanged += (_, _) => Show();

        for (int i = 0; i < pages.Count; i++)
        {
            int idx = i;
            var pg = pages[i];
            var (glyph, color) = SectionIcon(pg.Id);
            var tile = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var label = Ui.Text(pg.Id, 13.5, "Fg");
            label.TextWrapping=TextWrapping.Wrap;label.TextTrimming=TextTrimming.None;label.MaxWidth=126; // Keep full section names visible.
            label.Margin = new Thickness(11, 0, 0, 0);
            var nav = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Child = Ui.Row(tile, label), Background = Brushes.Transparent };
            bool isSel = false;
            pg.Select = on =>
            {
                isSel = on;
                label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                if (on) nav.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                else nav.Background = Brushes.Transparent;
            };
            nav.MouseEnter += (_, _) => { if (!isSel) nav.SetResourceReference(Border.BackgroundProperty, "Hover"); };
            nav.MouseLeave += (_, _) => { if (!isSel) nav.Background = Brushes.Transparent; };
            nav.MouseLeftButtonDown += (_, e) => { e.Handled = true; selected = idx; if (search.Box.Text.Length > 0) search.Box.Text = ""; else Show(); };
            pg.Nav = nav;
            side.Children.Add(nav);
        }

        var sideHost = new Border { CornerRadius = new CornerRadius(17, 0, 0, 17), BorderThickness = new Thickness(0, 0, 1, 0), Child = side };
        sideHost.SetResourceReference(Border.BackgroundProperty, "Subtle");
        sideHost.SetResourceReference(Border.BorderBrushProperty, "Hairline");

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1);
        layout.Children.Add(sideHost);
        layout.Children.Add(right);
        Body.Children.Add(layout);
        navigate=id=>{var index=pages.FindIndex(pg=>pg.Id==id);if(index<0)return;selected=index;search.Box.Text="";Show();};
        Show();
    }

    // Application options now appear under the Applications widget's arrow.
    internal static FrameworkElement ApplicationOptions()
    {
        var p=new StackPanel();
        p.Children.Add(Row("Simge boyutu", "Dock'taki uygulama simgeleri",
            Ui.Segmented(["Küçük", "Orta", "Büyük"], S.IconSize switch { "S" => 0, "L" => 2, _ => 1 }, i => { S.IconSize = new[] { "S", "M", "L" }[i]; Apply(); })));
        p.Children.Add(Row("Açık uygulama göstergesi", "Açık uygulamaların altındaki işaret",
            Ui.Segmented(["Nokta", "Büyük nokta", "Çizgi"], S.Indicator switch { "bigdot" => 1, "line" => 2, _ => 0 }, i => { S.Indicator = new[] { "dot", "bigdot", "line" }[i]; Apply(); })));
        p.Children.Add(Row("Büyütme", "Mac tarzı: simgelerin üzerine gelince büyür",
            Ui.Segmented(["Kapalı", "Az", "Orta", "Çok"], S.Magnify < 1.2 ? 0 : S.Magnify < 1.5 ? 1 : S.Magnify < 1.8 ? 2 : 3, i => { S.Magnify = new[] { 1.0, 1.35, 1.6, 1.9 }[i]; Apply(); })));
        p.Children.Add(Row("Simge stili", "Çerçeve: macOS tarzı yuvarlatılmış kare; tek renkli simgeler dock'un rengini alır",
            Ui.Segmented(["Normal", "Çerçeve"], S.IconStyle == "frame" ? 1 : 0, i => { S.IconStyle = i == 1 ? "frame" : "normal"; Apply(); })));
        p.Children.Add(Row("Uygulama kartları", "Uygulamalar yalnızca simge ya da widget boyutunda kart olarak",
            Ui.Segmented(["Simge", "Kart"], S.AppCards ? 1 : 0, i => { S.AppCards = i == 1; S.AppSlots = 0; Apply(); })));
        p.Children.Add(Row("Kart stili", "Widget kartları temadan bağımsız siyah, beyaz ya da buzlu",
            Ui.Segmented(["Tema", "Siyah", "Beyaz", "Buzlu"], S.CardStyle switch { "black" => 1, "white" => 2, "frosted" => 3, _ => 0 }, i => { S.CardStyle = new[] { "auto", "black", "white", "frosted" }[i]; Apply(); })));
        p.Children.Add(Row("Uygulama alanı", "Sabit: yeni bir uygulama açılınca diğer widget'lar ve balon kaymaz (alan en fazla görülen simge kadar ayrılır) · Esnek: alan simge sayısına göre büyüyüp küçülür",
            Ui.Segmented(["Sabit", "Esnek"], S.FixedAppArea ? 0 : 1, i => { S.FixedAppArea = i == 0; S.AppSlots = 0; Apply(); })));
        p.Children.Add(Row("Başlat resmi", "Başlat düğmesinde Windows logosu yerine kendi resmin", StartImagePicker()));
        p.Children.Add(Row("Win+1…Win+0", "Dock'taki 1.–10. uygulamayı klavyeyle aç ya da öne getir",
            Switch(S.WinNumbers, true, i => { S.WinNumbers = i == 0; S.Save(); })));
        p.Children.Add(Row("Ad etiketi", "Simgenin üzerine gelince uygulamanın adı görünsün",
            Switch(S.NamesOnHover, false, i => { S.NamesOnHover = i == 1; Apply(); })));
        p.Children.Add(Row("Pencere önizlemeleri", "Simgenin üzerinde durunca açık pencereleri canlı göster",
            Switch(S.Previews, true, i => { S.Previews = i == 0; if (!S.Previews) WindowPreview.HideNow(); Apply(); })));

        return p;
    }

    internal static FrameworkElement SliderRow(string title,string description,double value,double min,double max,Action<double> set,string suffix="")
    {
        var label=Ui.Text($"{value:0.##} {suffix}",12,"FgDim",localize:false);
        var slider=new Slider{Minimum=min,Maximum=max,Value=value,Width=160,VerticalAlignment=VerticalAlignment.Center};
        if(suffix=="ms"){slider.IsSnapToTickEnabled=true;slider.TickFrequency=1;slider.SmallChange=1;slider.LargeChange=10;}
        System.Windows.Automation.AutomationProperties.SetName(slider,title);
        var save=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};
        save.Tick+=(_,_)=>{save.Stop();S.Save();};
        slider.ValueChanged+=(_,_)=>
        {
            var selected=suffix=="ms"?Math.Round(slider.Value):slider.Value;
            set(selected);label.Text=suffix=="ms"?$"{selected:0} ms":$"{selected:0.##} {suffix}";
            App.EachDock(d=>d.RefreshMaterialAndTiming());save.Stop();save.Start();
        };
        var control=Ui.Col(slider,label);return Row(title,description,control);
    }

    /// <summary>Yuvarlatılmış, hafif zeminli ayar grubu.</summary>
    static Border Group(UIElement rows)
    {
        var g = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = rows, Margin = new Thickness(0, 0, 0, 8) };
        g.SetResourceReference(Border.BackgroundProperty, "Subtle");
        g.SetResourceReference(Border.BorderBrushProperty, "Hairline");
        return g;
    }

    /// <summary>Aramada eşleşecek metin: satırdaki bütün yazılar (başlık, açıklama, seçenekler).</summary>
    static string SearchText(DependencyObject root)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(object o)
        {
            if (o is TextBlock t) sb.Append(t.Text).Append(' ');
            if (o is FrameworkElement { ToolTip: string tip }) sb.Append(tip).Append(' ');
            if (o is DependencyObject d) foreach (var c in LogicalTreeHelper.GetChildren(d)) Walk(c);
        }
        Walk(root);
        return sb.ToString();
    }

    /// <summary>Aç/kapa anahtarı. onIsZero: eski "Açık | Kapalı" sırasındaki seçim indeksiyle uyum için.</summary>
    internal static FrameworkElement Switch(bool on, bool onIsZero, Action<int> onChange)
    {
        var knob = new System.Windows.Shapes.Ellipse { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0) };
        var move = new TranslateTransform();
        knob.RenderTransform = move;
        var track = new Border { Width = 42, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = knob, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right };
        void Paint(bool animate)
        {
            if (on)
            {
                track.SetResourceReference(Border.BackgroundProperty, "Accent");
                track.BorderBrush = Brushes.Transparent;
                knob.Fill = Brushes.White;
            }
            else
            {
                track.SetResourceReference(Border.BackgroundProperty, "Track");
                track.SetResourceReference(Border.BorderBrushProperty, "Hairline");
                knob.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "FgDim");
            }
            double x = on ? 18 : 0;
            if (animate) move.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(x, TimeSpan.FromMilliseconds(160)) { EasingFunction = new System.Windows.Media.Animation.CubicEase() });
            else move.X = x;
        }
        track.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            on = !on;
            Paint(true);
            onChange(on == !onIsZero ? 1 : 0);
        };
        Paint(false);
        return track;
    }

    internal static FrameworkElement QuickTogglePicker()
    {
        var wrap = new WrapPanel { MaxWidth = 300, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (id, name) in ExtraToggles.All)
        {
            var key = id;
            var t = Ui.Text(name, 11.5, "Fg", FontWeights.SemiBold);
            var chip = new Border { Child = t, CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(3), Cursor = Cursors.Hand };
            void Paint()
            {
                bool on = S.QuickToggles.Contains(key);
                if (on) { chip.SetResourceReference(Border.BackgroundProperty, "Accent"); t.Foreground = Brushes.White; }
                else { chip.SetResourceReference(Border.BackgroundProperty, "Track"); t.SetResourceReference(TextBlock.ForegroundProperty, "Fg"); }
            }
            chip.MouseLeftButtonUp += (_, _) =>
            {
                if (!S.QuickToggles.Remove(key)) S.QuickToggles.Add(key);
                Paint();
                Apply();
            };
            Paint();
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    internal static FrameworkElement StartImagePicker()
    {
        TextBlock Link(string text, Action a)
        {
            var t = Ui.Text(text, 12.5, "Accent", FontWeights.SemiBold);
            t.Cursor = Cursors.Hand;
            t.Margin = new Thickness(12, 0, 0, 0);
            t.MouseLeftButtonUp += (_, _) => a();
            return t;
        }
        var row = Ui.Row(
            Link("Resim seç…", () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Title = "Başlat resmi", Filter = "Resimler|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico" };
                var owner=current;if(owner!=null)owner.Topmost=false;
                try {if(d.ShowDialog(owner)==true){S.StartImage=d.FileName;Apply();}}finally{if(owner!=null)owner.Topmost=true;}
            }),
            Link("Windows logosu", () => { S.StartImage = ""; Apply(); }));
        return row;
    }

    static FrameworkElement AccentPicker()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var dots = new List<Border>();
        foreach (var (hex, name) in Theme.Accents)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var dot = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(3, 0, 3, 0),
                Background = new SolidColorBrush(c), Cursor = Cursors.Hand, ToolTip = name, BorderBrush = Brushes.White,
                BorderThickness = new Thickness(string.Equals(hex, S.Accent, StringComparison.OrdinalIgnoreCase) ? 2.5 : 0),
            };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                S.Accent = hex;
                foreach (var d in dots) d.BorderThickness = new Thickness(d == dot ? 2.5 : 0);
                Apply();
            };
            dots.Add(dot);
            row.Children.Add(dot);
        }
        return row;
    }

    internal static FrameworkElement TextRow(string title, string sub, string value, Action<string> set, double width = 220)
    {
        var input = TodosWidget.Input(title);
        input.Box.Text = value;
        input.Host.Width = width;
        void Commit()
        {
            set(input.Box.Text.Trim());
            S.Save();
            App.EachDock(d => d.Rebuild());
        }
        input.Box.LostFocus += (_, _) => Commit();
        input.Box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return Row(title, sub, input.Host);
    }

    internal static FrameworkElement Row(string title, string sub, FrameworkElement control)
    {
        var d = new DockPanel();
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(20, 0, 0, 0);
        DockPanel.SetDock(control, Dock.Right);
        d.Children.Add(control);
        var s = Ui.Text(sub, 12, "FgDim");
        s.TextWrapping = TextWrapping.Wrap;
        s.TextTrimming = TextTrimming.None;
        s.Margin = new Thickness(0, 2, 0, 0);
        s.LineHeight = 17;
        var info = Ui.Glyph("\uE946", 11, "FgDim"); info.ToolTip = Localization.T(sub); info.Margin = new Thickness(6, 1, 0, 0); info.Opacity = 0.6;
        var head = Ui.Row(Ui.Text(title, 13.5, "Fg", FontWeights.Medium), info);
        var text = Ui.Col(head, s);
        text.VerticalAlignment = VerticalAlignment.Center;
        d.Children.Add(text);
        return d;
    }
}
