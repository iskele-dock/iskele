using System.Text.Json;
namespace Iskele;

// Regression cases from the internal code review; no real taskbar or network calls.
static class ReviewRegressionTests
{
    public static void Run(string output, Action<bool, string> check)
    {
        var root = Path.Combine(output, "fault-cases"); Directory.CreateDirectory(root);
        var file = Path.Combine(root, "atomic.json");
        AtomicFile.Write(file, "old");
        bool fault = false;
        try { AtomicFile.Write(file, "new", beforeCommit: () => throw new IOException("Injected interrupted write")); }
        catch (IOException) { fault = true; }
        check(fault && File.ReadAllText(file) == "old" && !Directory.GetFiles(root, "*.tmp-*").Any(), "Interrupted write preserves previous file and removes temporary data");
        AtomicFile.Write(file, "new");
        check(File.ReadAllText(file) == "new" && File.ReadAllText(file + ".bak") == "old", "Atomic replacement retains last committed backup");

        var settingsFile = Path.Combine(root, "settings.json");
        AtomicFile.Write(settingsFile, JsonSerializer.Serialize(new Settings { Note = "surviving note" }));
        AtomicFile.Write(settingsFile, JsonSerializer.Serialize(new Settings { Note = "newer note" }));
        File.WriteAllText(settingsFile, "{broken-json");
        var recovered = Settings.LoadFrom(settingsFile);
        check(recovered.Note == "surviving note" && Settings.LoadFrom(settingsFile).Note == "surviving note", "Broken settings recover valid backup and repair main file");
        check(Directory.GetFiles(root, "settings.json.corrupt-*").Any(), "Broken original is preserved for recovery");

        var nullFile = Path.Combine(root, "nulls.json");
        var properties = typeof(Settings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanWrite && (p.PropertyType == typeof(string) || typeof(System.Collections.IList).IsAssignableFrom(p.PropertyType))).ToArray();
        File.WriteAllText(nullFile, "{" + string.Join(",", properties.Select(p => "\"" + p.Name + "\":null")) + "}");
        var normalized = Settings.LoadFrom(nullFile);
        check(properties.Where(p => p.Name != "Pins").All(p => p.GetValue(normalized) != null), "Every explicit-null collection/string normalized before UI access");
        check(normalized.Pins == null, "Null Pins still requests first-run taskbar import");

        File.WriteAllText(nullFile, "{\"NoteList\":[null,{\"Text\":null}],\"Groups\":[{\"Id\":null,\"Name\":null,\"Items\":null}],\"FuturePreference\":42}");
        var nested = Settings.LoadFrom(nullFile);
        check(nested.NoteList.Count == 1 && nested.NoteList[0].Text == "" && nested.Groups[0].Items.Count == 0, "Null nested notes and group items normalized");
        check(JsonSerializer.Serialize(nested).Contains("FuturePreference"), "Unknown preference fields survive a load/save roundtrip");
        check(!TaskbarGuard.TryParseArea("0,broken", out _) && !TaskbarGuard.TryParseArea("2,2,1,1", out _), "Corrupt work area rejected without parse exception");
        check(TaskbarGuard.TryParseArea("-1920,0,0,1080", out var area) && area.Left == -1920, "Valid negative-origin saved work area preserved");

        string mutexName = @"Local\Iskele.Review." + Guid.NewGuid().ToString("N");
        using var ready = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => TaskbarGuard.ExecuteLocked(mutexName, TimeSpan.FromSeconds(2), () => { ready.Set(); release.Wait(TimeSpan.FromSeconds(5)); return 0; }));
        bool entered = false, timedOut = false;
        if (ready.Wait(TimeSpan.FromSeconds(3)))
        {
            try { TaskbarGuard.ExecuteLocked(mutexName, TimeSpan.FromMilliseconds(50), () => { entered = true; return 0; }); }
            catch (TimeoutException) { timedOut = true; }
        }
        release.Set(); holder.GetAwaiter().GetResult();
        check(timedOut && !entered, "Contended mutex timeout never invokes mutation callback");
        check(TaskbarGuard.ExecuteLocked(mutexName, TimeSpan.FromSeconds(1), () => true), "Mutation succeeds after mutex release");
        var owner = Path.Combine(root, "owner.txt"); File.WriteAllText(owner, "new-generation");
        bool restored = false;
        check(!TaskbarGuard.RestoreGeneration(owner, "old-generation", () => restored = true) && !restored, "Old watchdog cannot restore a newer generation");
        check(TaskbarGuard.RestoreGeneration(owner, "new-generation", () => restored = true) && restored, "Matching watchdog can restore its generation");

        // Run asynchronous cache fixtures without a WPF synchronization context or external network.
        Task.Run(async () =>
        {
            var a = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var b = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var cache = new Cached<string>(TimeSpan.FromHours(1), () => Interlocked.Increment(ref calls) == 1 ? a.Task : b.Task);
            cache.Ensure(); var first = cache.Pending!;
            cache.Invalidate(); cache.Ensure();
            a.SetResult("old A"); await first;
            check(calls == 2 && cache.Value == null, "Source change while busy immediately queues fresh load and discards A");
            b.SetResult("new B"); await cache.Pending!;
            check(cache.Value == "new B", "Queued B source becomes the only published cache value");

            var late = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool removed = false;
            var clearing = new Cached<string>(TimeSpan.FromHours(1), () => removed ? Task.FromResult("") : late.Task);
            clearing.Ensure(); var pending = clearing.Pending!;
            removed = true; clearing.Invalidate();
            late.SetResult("removed calendar events"); await pending;
            if (clearing.Pending != null) await clearing.Pending;
            check(string.IsNullOrEmpty(clearing.Value), "Cleared source remains empty after late response");
        }).GetAwaiter().GetResult();

        var start = DateTime.Today.AddDays(1);
        string stamp(DateTime date) => date.ToString("yyyyMMdd") + "T100000";
        string fixture = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nDTSTART:" + stamp(start) + "\nDTEND:" + start.ToString("yyyyMMdd") + "T110000\nRRULE:FREQ=DAILY;COUNT=5\nEXDATE:" + stamp(start.AddDays(1)) + "," + stamp(start.AddDays(2)) + "\nEXDATE:" + stamp(start.AddDays(3)) + "\nSUMMARY:Review fixture\nEND:VEVENT\nEND:VCALENDAR";
        var events = CalendarService.Parse(fixture);
        check(events.Count == 2 && events[0].Start.Date == start && events[1].Start.Date == start.AddDays(4), "All EXDATE lines and comma-separated dates excluded");
        bool held = false; int toggles = 0;
        for (int i = 0; i < 20; i++) if (KeyHook.FirstPress(ref held)) toggles++;
        held = false; if (KeyHook.FirstPress(ref held)) toggles++;
        check(toggles == 2, "Alt+T repeat toggles once until release and once on next press");

        var language = App.Settings.Language; App.Settings.Language = "en";
        check(Ui.Text("Ayarlar", 12).Text == "Settings" && Ui.RawText("Ayarlar", 12).Text == "Ayarlar", "UI label translates but matching user content remains literal");
        var row = PanelRows.Row(new Border(), "Ayarlar", "Takvim", null, null);
        check(FindText(row, "Ayarlar") && FindText(row, "Takvim"), "Note/device/calendar row content does not translate user values");
        App.Settings.Language = language;
    }
    static bool FindText(DependencyObject node, string text)
    {
        if (node is TextBlock t && t.Text == text) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (FindText(VisualTreeHelper.GetChild(node, i), text)) return true;
        return false;
    }
}
