namespace Iskele;

partial class Settings
{
    public List<Renewal> Renewals { get; set; } = [];
}
class Renewal
{
    public string Name { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today;
    public int Months { get; set; } = 1;
    public DateTime Next(DateTime from)
    {
        var date = Date.Date;
        if (Months <= 0 || date >= from.Date) return date;
        int steps = Math.Max(0, ((from.Year - date.Year) * 12 + from.Month - date.Month) / Months);
        var next = date.AddMonths(steps * Months);
        return next < from.Date ? date.AddMonths((steps + 1) * Months) : next;
    }
    public bool On(DateTime day) => Next(day) == day.Date;
}
static class RenewalEditor
{
    public static void Open()
    {
        var win = new FeaturePanel("Ödemeler ve yenilemeler", 560, 440);
        var p = new StackPanel { Margin = new Thickness(20) };
        var help = Ui.Text("Her satır: ad | tarih (yyyy-MM-dd) | tekrar (ay)\nÖrnek: Claude | 2026-12-01 | 1\nTek seferlik ödeme için ay: 0. AI planlarını da buraya ekleyin.", 12, "FgDim");
        help.TextWrapping = TextWrapping.Wrap; p.Children.Add(help);
        var input = TodosWidget.Input("Ödeme veya abonelik ekleyin", multiline: true);
        input.Box.Height = 200;
        input.Box.Text = string.Join("\n", App.Settings.Renewals.Select(r => $"{r.Name} | {r.Date:yyyy-MM-dd} | {r.Months}"));
        p.Children.Add(input.Host);
        var error = Ui.Text("", 12, "Warn"); p.Children.Add(error);
        var save = FeaturePanel.ActionButton("Kaydet");
        save.Click += (_, _) =>
        {
            var items = new List<Renewal>();
            foreach (var line in input.Box.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var v = line.Split('|', StringSplitOptions.TrimEntries);
                if (v.Length != 3 || v[0].Length == 0 || !DateTime.TryParseExact(v[1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) || !int.TryParse(v[2], out int months) || months < 0 || months > 120)
                { error.Text = "Satırı kontrol edin: " + line; return; }
                items.Add(new Renewal { Name = v[0], Date = date, Months = months });
            }
            App.Settings.Renewals = items; App.Settings.Save(); win.Close();
        };
        p.Children.Add(save); win.Host.Children.Add(p); win.Show();
    }
}
