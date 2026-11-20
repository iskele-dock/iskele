using System.Text;
namespace Iskele;

// A failed write must not truncate the last valid user-data file.
static class AtomicFile
{
    static readonly object gate = new();
    internal static void Write(string path, string text, bool backup = true, Action? beforeCommit = null)
    {
        lock (gate)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                    writer.Write(text); writer.Flush(); stream.Flush(flushToDisk: true);
                }
                beforeCommit?.Invoke();
                if (File.Exists(path)) File.Replace(temp, path, backup ? path + ".bak" : null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
