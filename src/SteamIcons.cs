namespace Iskele;

static class SteamIcons
{
    // Steam internet shortcuts carry an explicit per-game IconFile, unlike generic URL icons.
    public static string? Resolve(string path)
    {
        if (!path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        if (!lines.Any(l => l.Trim().StartsWith("URL=steam://", StringComparison.OrdinalIgnoreCase))) return null;
        var icon = lines.FirstOrDefault(l => l.Trim().StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase));
        if (icon == null) return null;
        var file = Environment.ExpandEnvironmentVariables(icon[(icon.IndexOf('=') + 1)..].Trim().Trim('"'));
        if (!Path.IsPathRooted(file)) file = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, file));
        return File.Exists(file) ? file : null;
    }
}
