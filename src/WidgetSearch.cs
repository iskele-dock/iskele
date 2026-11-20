using System.Globalization;
using System.Text;
namespace Iskele;
static class WidgetSearch
{
    public static readonly HashSet<string> NewIds = ["githuboverview", "whatsapp"];
    static string Normalize(string value) => string.Concat(value.Replace('ı', 'i').Replace('İ', 'I').Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
    public static bool Matches(string id, string name, string description, string category, string query)
    {
        string synonyms = id switch
        {
            "volume" => "sound ses hoparlor audio", "media" => "music muzik player sarki",
            "battery" => "pil sarj battery", "system" => "cpu ram bellek memory islemci",
            "clock" => "time saat zaman", "calendar" => "takvim calendar toplanti meeting",
            "githuboverview" => "github pr pull request bildirim stars yildiz",
            _ => ""
        };
        var haystack = Normalize(id + " " + name + " " + description + " " + category + " " + synonyms);
        return Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(haystack.Contains);
    }
}
partial class Settings { public List<string> SeenWidgets { get; set; } = []; }
