namespace Iskele;

// ═════════════════════════ 42–43. Ekran süresi: gün gezinme, hafta, siteler ═════════════════════════

static class ScreenTimePanel
{
    public static UIElement Build()
    {
        var host = new ContentControl();
        void Show(DateTime day) => host.Content = Day(day, Show);
        Show(DateTime.Today);
        return host;
    }

    static UIElement Day(DateTime day, Action<DateTime> go)
    {
        var d = ScreenTime.LoadDay(day);
        var p = new StackPanel();

        // başlık: ‹ gün ›
        var prev = new IconButton("", 11);
        var next = new IconButton("", 11);
        prev.Click += () => go(day.AddDays(-1));
        next.Click += () => { if (day < DateTime.Today) go(day.AddDays(1)); };
        next.Opacity = day < DateTime.Today ? 1 : 0.3;
        var head = new DockPanel();
        DockPanel.SetDock(next, Dock.Right);
        DockPanel.SetDock(prev, Dock.Right);
        head.Children.Add(next);
        head.Children.Add(prev);
        head.Children.Add(Ui.Text(day == DateTime.Today ? "Bugün" : day == DateTime.Today.AddDays(-1) ? "Dün" : day.ToString("d MMMM dddd", Tr.C), 12, "FgDim"));
        p.Children.Add(head);
        p.Children.Add(Ui.Text(ScreenTime.Format(d.Total), 26, "Fg", FontWeights.Bold));

        // hafta: son 7 gün toplamları, seçili gün vurgulu
        var weekStart = day.AddDays(-6);
        var totals = Enumerable.Range(0, 7).Select(i => (double)ScreenTime.LoadDay(weekStart.AddDays(i)).Total).ToArray();
        var wh = Ui.Text($"Son 7 gün · ortalama {ScreenTime.Format((int)totals.Average())}", 12, "FgDim", FontWeights.SemiBold);
        wh.Margin = new Thickness(0, 12, 0, 4);
        p.Children.Add(wh);
        p.Children.Add(new MiniBars { Height = 44, Values = totals });
        var axis = new UniformGrid { Columns = 7 };
        for (int i = 0; i < 7; i++)
        {
            var dd = weekStart.AddDays(i);
            var t = Ui.Text(dd.ToString("ddd", Tr.C), 10, dd == day ? "Accent" : "FgDim", dd == day ? FontWeights.Bold : FontWeights.Normal);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Cursor = Cursors.Hand;
            t.ToolTip = ScreenTime.Format((int)totals[i]);
            t.MouseLeftButtonUp += (_, _) => go(dd);
            axis.Children.Add(t);
        }
        p.Children.Add(axis);

        // saatlik
        var hh = Ui.Text("Saat saat", 12, "FgDim", FontWeights.SemiBold);
        hh.Margin = new Thickness(0, 12, 0, 4);
        p.Children.Add(hh);
        p.Children.Add(new MiniBars { Height = 44, Values = d.Hours.Select(x => (double)x).ToArray() });

        // uygulamalar
        Rows(p, "Uygulamalar", d.Apps, 7);
        if (d.Sites.Count > 0) Rows(p, "Siteler", d.Sites, 6);
        else if (!App.Settings.TrackSites)
        {
            var hint = PanelRows.Link("Web sitelerini de say", () => { App.Settings.TrackSites = true; App.Settings.Save(); go(day); });
            p.Children.Add(hint);
        }
        if (d.Total == 0) p.Children.Add(Ui.Text("Bu gün için kayıt yok.", 12, "FgDim"));
        return p;
    }

    static void Rows(StackPanel p, string title, Dictionary<string, int> data, int take)
    {
        if (data.Count == 0) return;
        var h = Ui.Text(title, 12, "FgDim", FontWeights.SemiBold);
        h.Margin = new Thickness(0, 12, 0, 0);
        p.Children.Add(h);
        int max = data.Values.Max();
        foreach (var (name, sec) in data.OrderByDescending(kv => kv.Value).Take(take))
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var t = Ui.Text(ScreenTime.Format(sec), 12, "FgDim");
            DockPanel.SetDock(t, Dock.Right);
            row.Children.Add(t);
            row.Children.Add(Ui.RawText(name, 12.5, "Fg", FontWeights.SemiBold));
            p.Children.Add(row);
            p.Children.Add(new MiniSlider { Interactive = false, Height = 4, Value = (double)sec / max, Margin = new Thickness(0, 3, 0, 2) });
        }
    }
}

// ═════════════════════════ 41. Gün çizelgesi ═════════════════════════

/// <summary>
/// Günü tek çizgide gösterir: saat başına baskın uygulama türünün rengi, pomodoro odak turları (altta çizgi),
/// takvim etkinlikleri (üstte nokta), gün doğumu/batımı (güneş/ay işareti) ve şu anki zaman.
/// </summary>
class DayTimeline : FrameworkElement
{
    public ScreenTime.Day? Data;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || Data == null) return;
        const int from = 6, to = 24;   // 06:00–24:00
        double X(double hour) => (hour - from) / (to - from) * w;
        double barY = h * 0.38, barH = h * 0.26;
        dc.DrawRoundedRectangle(Ui.B("Track"), null, new Rect(0, barY, w, barH), barH / 2, barH / 2);

        for (int hr = from; hr < to; hr++)
        {
            var best = Data.Kinds.Select(kv => (kv.Key, Sec: kv.Value.Length == 24 ? kv.Value[hr] : 0)).OrderByDescending(x => x.Sec).FirstOrDefault();
            if (best.Sec < 60) continue;   // bir dakikadan az: boş bırak
            var c = ScreenTime.KindColors.GetValueOrDefault(best.Key, Colors.Gray);
            byte alpha = (byte)Math.Clamp(90 + best.Sec / 3600.0 * 165, 90, 255);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)), null, new Rect(X(hr) + 0.5, barY, X(hr + 1) - X(hr) - 1, barH));
        }

        // odak turları
        var focusPen = new Pen(Ui.B("Warn"), 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var f in Data.Focus)
        {
            var parts = f.Split('-');
            if (parts.Length == 2 && TimeSpan.TryParse(parts[0], out var a) && TimeSpan.TryParse(parts[1], out var b))
                dc.DrawLine(focusPen, new Point(X(a.TotalHours), barY + barH + 4), new Point(X(b.TotalHours), barY + barH + 4));
        }

        // takvim etkinlikleri
        foreach (var e in CalendarService.Events.Where(e => !e.AllDay && e.Start.Date == DateTime.Today))
            dc.DrawEllipse(Ui.B("Accent"), null, new Point(X(e.Start.TimeOfDay.TotalHours), barY - 4), 2.2, 2.2);

        // gün doğumu / batımı
        var sunPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)), 1.2);
        foreach (var t in new[] { WeatherService.Sunrise, WeatherService.Sunset })
            if (t is DateTime s && s.Date == DateTime.Today)
                dc.DrawLine(sunPen, new Point(X(s.TimeOfDay.TotalHours), barY - 2), new Point(X(s.TimeOfDay.TotalHours), barY + barH + 2));

        // şimdi
        double now = DateTime.Now.TimeOfDay.TotalHours;
        if (now >= from) dc.DrawLine(new Pen(Ui.B("Fg"), 1.5), new Point(X(now), barY - 5), new Point(X(now), barY + barH + 5));
    }
}

class DayTimelineWidget : Widget
{
    readonly DayTimeline line = new();
    readonly TextBlock title;

    public DayTimelineWidget()
    {
        title = Ui.Text("", F(9.5), "FgDim", FontWeights.SemiBold);
        line.Width = F(230);
        line.Height = H * 0.6;
        Child = Ui.Col(title, line);
        OnClick(() => Flyout.Show(this, Legend(), 340));
        Tick(DateTime.Now);
    }

    public override void Tick(DateTime now)
    {
        if (now.Second % 10 != 0 && line.Data != null) return;
        WeatherService.Ensure();
        CalendarService.Ensure();
        line.Data = ScreenTime.Today;
        line.InvalidateVisual();
        var top = ScreenTime.Today.Kinds.Select(kv => (kv.Key, kv.Value.Sum())).OrderByDescending(x => x.Item2).FirstOrDefault();
        title.Text = $"Bugün · {ScreenTime.Format(ScreenTime.Today.Total)}" + (top.Item2 > 0 ? $" · çoğu {top.Key.ToLower(Tr.C)}" : "") + (ScreenTime.Today.Focus.Count > 0 ? $" · {ScreenTime.Today.Focus.Count} odak" : "");
    }

    static UIElement Legend()
    {
        var p = new StackPanel();
        p.Children.Add(Ui.Text("Gün çizelgesi", 16, "Fg", FontWeights.Bold));
        var big = new DayTimeline { Data = ScreenTime.Today, Height = 46, Margin = new Thickness(0, 10, 0, 6) };
        p.Children.Add(big);
        var axis = new DockPanel();
        var a = Ui.Text("06", 10, "FgDim"); var b = Ui.Text("15", 10, "FgDim"); var c = Ui.Text("24", 10, "FgDim");
        DockPanel.SetDock(a, Dock.Left); DockPanel.SetDock(c, Dock.Right); b.HorizontalAlignment = HorizontalAlignment.Center;
        axis.Children.Add(a); axis.Children.Add(c); axis.Children.Add(b);
        p.Children.Add(axis);
        foreach (var (kind, color) in ScreenTime.KindColors)
        {
            int sec = ScreenTime.Today.Kinds.TryGetValue(kind, out var arr) ? arr.Sum() : 0;
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 8, 0) };
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var t = Ui.Text(ScreenTime.Format(sec), 12, "FgDim");
            DockPanel.SetDock(t, Dock.Right);
            row.Children.Add(t);
            row.Children.Add(Ui.Row(dot, Ui.Text(kind, 12.5, "Fg")));
            p.Children.Add(row);
        }
        var note = Ui.Text("Alttaki kırmızı çizgiler: pomodoro odak turları · üstteki noktalar: takvim etkinlikleri · sarı çizgiler: gün doğumu/batımı", 11, "FgDim");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 12, 0, 0);
        p.Children.Add(note);
        return p;
    }
}

// ═════════════════════════ 48. Birden çok not ═════════════════════════

static class NotesStore
{
    public static List<NoteItem> All
    {
        get
        {
            var s = App.Settings;
            if (s.NoteList.Count == 0 && s.Note.Trim().Length > 0) { s.NoteList.Add(new NoteItem { Text = s.Note }); s.Note = ""; s.Save(); }
            return s.NoteList;
        }
    }

    public static IEnumerable<NoteItem> Sorted => All.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.Updated);
}

class NotesWidget : Widget
{
    readonly TextBlock title, body;

    public NotesWidget()
    {
        title = Ui.Text("", F(12), "Fg", FontWeights.Bold);
        body = Ui.Text("", F(10), "FgDim");
        var col = Ui.Col(title, body);
        col.Width = F(130);
        col.Margin = new Thickness(F(8), 0, 0, 0);
        Child = Ui.Row(Chip("", Color.FromRgb(0xF5, 0xB8, 0x00), H * 0.62), col);
        OnClick(() => Flyout.Show(this, NotesPanel.Build(), 360));
        Notes.Changed += Paint;
        Paint();
    }

    public override void Detach() => Notes.Changed -= Paint;

    void Paint()
    {
        var n = NotesStore.Sorted.FirstOrDefault();
        if (n == null) { title.Text = "Not"; body.Text = "Yazmak için tıkla"; return; }
        var lines = n.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        title.Text = (n.Pinned ? "📌 " : "") + n.Title;
        body.Text = lines.Length > 1 ? string.Join(" ", lines.Skip(1)) : $"{NotesStore.All.Count} not";
    }
}

static class NotesPanel
{
    public static UIElement Build()
    {
        var host = new ContentControl();
        void List() => host.Content = ListView(Edit, List);
        void Edit(NoteItem n) => host.Content = Editor(n, List);
        List();
        return host;
    }

    static UIElement ListView(Action<NoteItem> edit, Action refresh)
    {
        var p = new StackPanel();
        var add = new IconButton("", 12, 28) { ToolTip = "Yeni not" };
        add.Click += () => { var n = new NoteItem(); NotesStore.All.Add(n); edit(n); };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);
        head.Children.Add(Ui.Text("Notlar", 16, "Fg", FontWeights.Bold));
        p.Children.Add(head);
        var search = TodosWidget.Input("Notlarda ara");
        p.Children.Add(search.Host);
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        p.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        void Fill()
        {
            list.Children.Clear();
            var q = search.Box.Text.Trim();
            foreach (var n in NotesStore.Sorted.Where(n => q.Length == 0 || n.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            {
                var note = n;
                var pin = new IconButton(note.Pinned ? "" : "", 11, 24) { ToolTip = note.Pinned ? "Sabitlemeyi kaldır" : "Sabitle" };
                pin.Click += () => { note.Pinned = !note.Pinned; Notes.Save(); Fill(); };
                var del = new IconButton("", 11, 24) { ToolTip = "Sil" };
                del.Click += () => { NotesStore.All.Remove(note); Notes.Save(); Fill(); };
                var right = Ui.Row(pin, del);
                var lines = note.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                list.Children.Add(PanelRows.Row(Ui.Glyph(note.Pinned ? "" : "", 14, note.Pinned ? "Accent" : "FgDim"),
                    note.Title, (lines.Length > 1 ? lines[1] + " · " : "") + note.Updated.ToString("d MMM HH:mm", Tr.C), right, () => edit(note)));
            }
            if (list.Children.Count == 0) list.Children.Add(Ui.Text(q.Length > 0 ? "Eşleşen not yok." : "Henüz not yok. + ile ekle.", 12, "FgDim"));
        }
        search.Box.TextChanged += (_, _) => Fill();
        Fill();
        return p;
    }

    static UIElement Editor(NoteItem n, Action back)
    {
        var p = new StackPanel();
        var b = new IconButton("", 12, 28) { ToolTip = "Notlara dön" };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(b, Dock.Left);
        head.Children.Add(b);
        head.Children.Add(Ui.Text(" Not", 16, "Fg", FontWeights.Bold));
        p.Children.Add(head);
        var input = TodosWidget.Input("İlk satır başlık olur…", multiline: true);
        input.Box.Height = 240;
        input.Box.Text = n.Text;
        input.Box.TextChanged += (_, _) => { n.Text = input.Box.Text; n.Updated = DateTime.Now; };
        p.Children.Add(input.Host);
        void Done()
        {
            if (n.Text.Trim().Length == 0) NotesStore.All.Remove(n);
            Notes.Save();
            back();
        }
        b.Click += Done;
        p.Unloaded += (_, _) => { if (n.Text.Trim().Length == 0) NotesStore.All.Remove(n); Notes.Save(); };
        p.Loaded += (_, _) => { input.Box.Focus(); input.Box.CaretIndex = input.Box.Text.Length; };
        return p;
    }
}

// ═════════════════════════ 47. Alt+T: her yerden hızlı yapılacak ═════════════════════════

class QuickTodoWindow : PanelWindow
{
    static QuickTodoWindow? current;

    public static void Toggle()
    {
        if (current != null) { current.Close(); return; }
        current = new QuickTodoWindow();
        current.Closed += (_, _) => current = null;
        current.Show();
        current.Activate();
    }

    QuickTodoWindow() : base("Yapılacaklar", 420, 0)
    {
        var p = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
        p.Children.Add(Ui.Text("Hızlı yapılacak · Alt+T", 12, "FgDim"));
        var input = TodosWidget.Input("Yeni iş yaz, Enter'a bas (Esc: kapat)");
        input.Host.Margin = new Thickness(0, 8, 0, 8);
        p.Children.Add(input.Host);
        var list = new StackPanel();
        p.Children.Add(list);

        void Fill()
        {
            list.Children.Clear();
            foreach (var t in App.Settings.Todos.Where(t => !t.Done).Take(8).ToList())
            {
                var item = t;
                var check = new IconButton("", 14, 26);
                check.Click += () => { item.Done = true; Notes.Save(); Fill(); };
                var tx = Ui.RawText(item.Text, 13, "Fg");
                tx.Margin = new Thickness(6, 0, 0, 0);
                list.Children.Add(Ui.Row(check, tx));
            }
            if (list.Children.Count == 0) list.Children.Add(Ui.Text("Açık iş yok.", 12, "FgDim"));
        }
        input.Box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Box.Text)) return;
            App.Settings.Todos.Add(new TodoItem { Text = input.Box.Text.Trim() });
            input.Box.Clear();
            Notes.Save();
            Fill();
        };
        Fill();
        Body.Children.Add(p);
        Deactivated += (_, _) => { if (IsVisible) Close(); };
        Loaded += (_, _) => input.Box.Focus();
    }
}
