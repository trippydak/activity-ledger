using ActivityLedger.Core;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

var start = new DateTimeOffset(2026, 11, 20, 23, 59, 50, TimeSpan.Zero);
var app = new ActivitySample("app", "notepad", "Document");
var site = new ActivitySample("website", "chrome", "Example", "https://example.com", "example.com");
using var memory = new ActivityStore(":memory:");
var tracker = new ActivitySessionTracker(memory.Upsert);
tracker.Observe(app, start);
tracker.Observe(app, start.AddSeconds(5));
tracker.Observe(site, start.AddSeconds(10));
tracker.Observe(site, start.AddSeconds(15));
tracker.Stop(start.AddSeconds(20));
var all = memory.Query(start, start.AddDays(1));
Check(all.Count == 2 && all.Sum(item => item.DurationSeconds) == 20, "switches partition foreground time without overlap");
var nextDay = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);
Check(memory.Query(nextDay, nextDay.AddDays(1)).Sum(item => item.DurationSeconds) == 10,
    "query clips a session crossing midnight");
Check(memory.Summarize(nextDay, nextDay.AddDays(1)).Sum(item => item.Seconds) == 10,
    "summaries use the same midnight bounds");
Check(memory.Query(start, start.AddDays(1), "example").Count == 1, "search matches domain and title");
Check(memory.Query(start, start.AddDays(1), "", "app").Count == 1, "kind filtering");

memory.DeleteAll();
tracker = new(memory.Upsert);
tracker.Observe(app, start);
tracker.Observe(app, start.AddSeconds(4));
tracker.Observe(app, start.AddHours(2));
tracker.Observe(app, start.AddHours(2).AddSeconds(3));
tracker.Stop(start.AddHours(2).AddSeconds(4));
Check(memory.Query(start, start.AddDays(1)).Sum(item => item.DurationSeconds) == 8,
    "suspend gaps are excluded rather than counted as app use");

memory.DeleteAll();
tracker = new(memory.Upsert);
tracker.Observe(app, start);
tracker.Observe(null, start.AddSeconds(5));
tracker.Observe(null, start.AddSeconds(10));
tracker.Observe(app, start.AddSeconds(15));
tracker.Stop(start.AddSeconds(20));
Check(memory.Query(start, start.AddDays(1)).Sum(item => item.DurationSeconds) == 10,
    "pause and exclusions produce gaps, not attributed time");

memory.DeleteAll();
tracker = new(memory.Upsert);
tracker.Observe(app, start);
tracker.Observe(app, start.AddSeconds(4));
tracker.Flush(start.AddHours(1));
Check(memory.Query(start, start.AddDays(1)).Single().DurationSeconds == 4,
    "flush never invents time after the last observation");
tracker.Stop(start.AddHours(1));
Check(memory.Query(start, start.AddDays(1)).Single().DurationSeconds == 4,
    "stopping after sleep preserves the last observed end");

memory.DeleteAll();
memory.Upsert(new("long", start, start.AddMinutes(1), "app", "notepad", "", "", ""));
memory.DeleteRange(start.AddSeconds(20), start.AddSeconds(40));
var preserved = memory.Query(start, start.AddDays(1));
Check(preserved.Count == 2 && preserved.Sum(item => item.DurationSeconds) == 40,
    "deleting a range preserves time before and after it");
Check(memory.Query(start.AddSeconds(20), start.AddSeconds(40)).Count == 0,
    "deleted interval has no residual sessions");
memory.DeleteBefore(start.AddSeconds(50));
Check(memory.Query(start, start.AddDays(1)).Single().DurationSeconds == 10,
    "retention trims a session that overlaps its cutoff");

var settings = new AppSettings { ExcludedProcesses = ["NOTEPAD.exe"], ExcludedDomains = ["example.com"] };
Check(PrivacyRules.IsExcluded(app, settings), "executable exclusions ignore case and .exe suffix");
Check(PrivacyRules.IsExcluded(site with { Domain = "sub.example.com" }, settings), "site exclusion includes subdomains");
Check(!PrivacyRules.IsExcluded(site with { Domain = "notexample.com", ProcessName = "chrome" }, settings),
    "site exclusion respects domain boundaries");

var folder = Path.Combine(Path.GetTempPath(), "ActivityLedger-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var settingsStore = new SettingsStore(Path.Combine(folder, "settings.json"));
    var initial = settingsStore.Load();
    settingsStore.Save(initial);
    Check(settingsStore.Load().BridgeToken == initial.BridgeToken, "bridge pairing token persists across launches");
    File.WriteAllText(Path.Combine(folder, "settings.json"), "{broken");
    var refusedBrokenSettings = false;
    try { settingsStore.Load(); } catch (InvalidDataException) { refusedBrokenSettings = true; }
    Check(refusedBrokenSettings, "corrupt settings cannot silently erase privacy exclusions");
    using (var disk = new ActivityStore(Path.Combine(folder, "history.db")))
        disk.Upsert(new("persist", start, start.AddSeconds(8), "app", "notepad", "=HYPERLINK(\"bad\")", "", ""));
    using (var disk = new ActivityStore(Path.Combine(folder, "history.db")))
    {
        var persisted = disk.Query(start, start.AddDays(1));
        Check(persisted.Single().DurationSeconds == 8, "SQLite sessions persist after reopening");
        var export = Path.Combine(folder, "export.csv");
        ActivityStore.ExportCsv(persisted, export);
        Check(File.ReadAllText(export).Contains("'=HYPERLINK"),
            "CSV sanitizes formula-like window titles");
        Check(File.ReadAllText(export).Contains("Start UTC"), "CSV includes timestamps and duration");
    }
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(folder, true);
}

Console.WriteLine($"All {passed} core checks passed.");
