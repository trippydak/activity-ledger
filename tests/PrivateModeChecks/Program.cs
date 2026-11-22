using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ActivityLedger.Browser;
using ActivityLedger.Core;
using ActivityLedger.Native;
using ActivityLedger.Tracking;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

var probe = new TcpListener(IPAddress.Loopback, 0);
probe.Start();
var port = ((IPEndPoint)probe.LocalEndpoint).Port;
probe.Stop();
using var bridge = new BrowserBridge("private-mode-pipeline-checks-pairing-token", port);
bridge.Start();
if (bridge.Error is not null) throw new Exception(bridge.Error);
using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "private-mode-pipeline-checks-pairing-token");
http.DefaultRequestHeaders.Add("Origin", "chrome-extension://bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
async Task Post(object payload)
{
    using var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    using var response = await http.PostAsync("/activity", body);
    response.EnsureSuccessStatusCode();
}

var settings = new AppSettings { CaptureWindowTitles = true, CaptureFullUrls = true };
WindowSample Native(DateTimeOffset captured, string caption, string process = "msedge.exe") =>
    new(captured, process, "", caption, 123, 42, 0, false);
ActivitySample? Classify(BrowserSnapshot snapshot, string caption, string process = "msedge.exe") =>
    ActivityClassifier.Classify(Native(snapshot.ReceivedAtUtc, caption, process), snapshot, settings, 999);

await Post(new
{
    browser = "msedge", instanceId = "edge-profile", focused = true, incognito = false,
    url = "https://public.example/planning?discard=this", title = "Planning"
});
var normalSnapshot = bridge.GetCurrent("msedge.exe")!;
var normal = Classify(normalSnapshot, "Planning and 2 more pages - Personal - Microsoft Edge");
Check(normal is { Kind: "website", Domain: "public.example", Url: "https://public.example/planning", Title: "Planning" },
    "normal Edge website starts the real bridge-to-history pipeline");

// Deliberately supply content the production extension must never send. The bridge and
// classifier must independently discard it when only a private-mode flag is requested.
const string privateUrl = "https://PRIVATE_USER_SENTINEL:PRIVATE_PASSWORD_SENTINEL@PRIVATE_DOMAIN_SENTINEL.example/PRIVATE_PATH_SENTINEL?PRIVATE_QUERY_SENTINEL#PRIVATE_FRAGMENT_SENTINEL";
const string privateTitle = "PRIVATE_EXTENSION_TITLE_SENTINEL";
const string privateCaption = "PRIVATE_NATIVE_CAPTION_SENTINEL - InPrivate - Microsoft Edge";
await Post(new
{
    browser = "msedge", instanceId = "edge-profile", focused = true, incognito = true,
    url = privateUrl, title = privateTitle
});
var privateSnapshot = bridge.GetCurrent("msedge.exe");
Check(privateSnapshot is { IsPrivate: true, Url: "", Title: "" },
    "focused private report creates a content-free mode snapshot even with malicious content fields");
var privateMode = Classify(privateSnapshot!, privateCaption);
Check(privateMode is { Kind: "private", ProcessName: "msedge.exe", Title: "Private browsing (Edge InPrivate)", Url: "", Domain: "" },
    "private mode records only browser identity and a constant safe label");
Check(Classify(privateSnapshot!, privateCaption, "chrome.exe") is { Kind: "app", Title: "", Url: "", Domain: "" },
    "private mode from Edge cannot be attributed to Chrome");
Check(ActivityClassifier.Classify(Native(privateSnapshot!.ReceivedAtUtc.AddSeconds(3.001), privateCaption),
    privateSnapshot, settings, 999) is { Kind: "app", Title: "", Url: "", Domain: "" },
    "stale private flag expires to an ordinary browser app observation without content");

await Post(new
{
    browser = "msedge", instanceId = "edge-profile", focused = false, incognito = true,
    reason = "unfocused", url = privateUrl, title = privateTitle
});
Check(bridge.GetCurrent("msedge.exe") is null, "losing private window focus clears the mode snapshot");

await Post(new
{
    browser = "msedge", instanceId = "edge-profile", focused = true, incognito = false,
    url = "https://public.example/resumed?discard=this", title = "Work resumed"
});
var resumedSnapshot = bridge.GetCurrent("msedge.exe")!;
var resumed = Classify(resumedSnapshot, "Work resumed - Microsoft Edge");
Check(resumedSnapshot is { IsPrivate: false } && resumed is { Kind: "website", Url: "https://public.example/resumed", Title: "Work resumed" },
    "a new normal report replaces private mode and resumes ordinary website history");

var folder = Path.Combine(Path.GetTempPath(), "ActivityLedger-private-mode-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var database = Path.Combine(folder, "activity.db");
var start = DateTimeOffset.FromUnixTimeMilliseconds(normalSnapshot.ReceivedAtUtc.ToUnixTimeMilliseconds());
IReadOnlyList<ActivityRecord> saved;
try
{
    using (var store = new ActivityStore(database))
    {
        var tracker = new ActivitySessionTracker(store.Upsert);
        // Translate freshly classified reports onto one deterministic observation clock. No sleeps
        // are needed to exercise session partitioning and the separate private freshness deadline.
        tracker.Observe(normal, start);
        tracker.Observe(privateMode, start.AddSeconds(2));
        tracker.Observe(resumed, start.AddSeconds(5));
        tracker.Stop(start.AddSeconds(8));
        saved = store.Query(start, start.AddSeconds(9));
        Check(saved.Count == 3 && saved.Where(item => item.Kind == "website").Sum(item => item.DurationSeconds) == 5 &&
            saved.Single(item => item.Kind == "private").DurationSeconds == 3,
            "normal to private to normal sessions partition time into five website seconds and three mode seconds");
        Check(saved.Single(item => item.Kind == "private") is
            { Title: "Private browsing (Edge InPrivate)", Url: "", Domain: "", ProcessName: "msedge.exe" },
            "SQLite stores the private label without URL, domain or page title");
        ActivityStore.ExportCsv(saved, Path.Combine(folder, "history.csv"));
        File.WriteAllText(Path.Combine(folder, "history.json"), JsonSerializer.Serialize(saved));
        Check(File.ReadAllText(Path.Combine(folder, "history.csv")).Contains("Private browsing (Edge InPrivate)") &&
            File.ReadAllText(Path.Combine(folder, "history.json")).Contains("Private browsing (Edge InPrivate)"),
            "CSV and JSON exports preserve the safe private-mode label");
    }
    using (var reopened = new ActivityStore(database))
        Check(reopened.Query(start, start.AddSeconds(9), kind: "private").Single().DurationSeconds == 3,
            "private mode duration survives database reopening");

    var sentinels = new[] { "PRIVATE_USER_SENTINEL", "PRIVATE_PASSWORD_SENTINEL", "PRIVATE_DOMAIN_SENTINEL", "PRIVATE_PATH_SENTINEL",
        "PRIVATE_QUERY_SENTINEL", "PRIVATE_FRAGMENT_SENTINEL", privateTitle, "PRIVATE_NATIVE_CAPTION_SENTINEL" };
    var serializedHistory = JsonSerializer.Serialize(saved);
    Check(sentinels.All(sentinel => !serializedHistory.Contains(sentinel, StringComparison.OrdinalIgnoreCase)),
        "no private-content sentinel is present in saved activity records");
    foreach (var name in new[] { "history.csv", "history.json" })
        Check(sentinels.All(sentinel => !File.ReadAllText(Path.Combine(folder, name)).Contains(sentinel, StringComparison.OrdinalIgnoreCase)),
            name + " contains no private URL, title, credentials or native caption");
    var databaseFiles = Directory.GetFiles(folder, "activity.db*");
    Check(databaseFiles.All(file => sentinels.All(sentinel =>
        !Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains(sentinel, StringComparison.OrdinalIgnoreCase))),
        "SQLite database and journal bytes contain no private-content sentinel");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(folder, true);
}

Console.WriteLine($"All {passed} private-mode checks passed.");
