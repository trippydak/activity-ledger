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
const string token = "website-pipeline-checks-pairing-token";
using var bridge = new BrowserBridge(token, port);
bridge.Start();
if (bridge.Error is not null) throw new Exception(bridge.Error);
using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.DefaultRequestHeaders.Add("Origin", "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
async Task<string> Post(string route, object payload)
{
    using var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    using var response = await http.PostAsync(route, body);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync();
}

var health = await Post("/health", new { });
Check(health.Contains("connected") && bridge.GetCurrent("msedge.exe") is null &&
    bridge.GetStatuses().All(status => status.State == "no-report"),
    "pairing health alone does not manufacture website activity");

await Post("/activity", new
{
    browser = "msedge", instanceId = "edge-profile", focused = true, incognito = false,
    url = "https://user:secret@example.com/planning?token=secret#private-fragment", title = "Planning"
});
var snapshot = bridge.GetCurrent("msedge.exe");
Check(snapshot is { Url: "https://example.com/planning", Title: "Planning" } &&
    bridge.GetStatuses().Single(status => status.Browser == "msedge").State == "active",
    "authenticated Edge delivery removes credentials, query and fragment in the bridge");

// SQLite stores milliseconds; align the simulated observation clock to that precision.
var start = DateTimeOffset.FromUnixTimeMilliseconds(snapshot!.ReceivedAtUtc.ToUnixTimeMilliseconds());
var native = new WindowSample(start, "msedge.exe", @"C:\Program Files\Microsoft\Edge\msedge.exe",
    "Planning and 2 more pages - Personal - Microsoft Edge", 123, 42, 0, false);
var settings = new AppSettings { CaptureWindowTitles = true, CaptureFullUrls = true };
var website = ActivityClassifier.Classify(native, snapshot, settings, 999);
Check(website is { Kind: "website", Domain: "example.com", Url: "https://example.com/planning", Title: "Planning" },
    "grouped Edge caption becomes a website through the real bridge and classifier");

var folder = Path.Combine(Path.GetTempPath(), "ActivityLedger-website-pipeline-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var database = Path.Combine(folder, "activity.db");
try
{
    using (var store = new ActivityStore(database))
    {
        var tracker = new ActivitySessionTracker(store.Upsert);
        tracker.Observe(website, start);
        tracker.Observe(ActivityClassifier.Classify(native with { CapturedAtUtc = start.AddSeconds(5) }, snapshot, settings, 999), start.AddSeconds(5));
        Check(store.Query(start, start.AddSeconds(11), kind: "website").Single().DurationSeconds == 5,
            "matched website time reaches a checkpointed SQLite session");

        var changedNative = native with { CapturedAtUtc = start.AddSeconds(6), WindowTitle = "Unrelated new tab - Microsoft Edge" };
        var fallback = ActivityClassifier.Classify(changedNative, bridge.GetCurrent("msedge.exe"), settings, 999);
        Check(fallback is { Kind: "app", ProcessName: "msedge.exe", Title: "", Url: "", Domain: "" },
            "a changed native tab cannot reuse a delayed website report or expose its native title");
        tracker.Observe(fallback, start.AddSeconds(6));

        await Post("/activity", new
        {
            browser = "msedge", instanceId = "edge-profile", focused = false, incognito = true,
            reason = "private", url = "", title = ""
        });
        Check(bridge.GetCurrent("msedge.exe") is null &&
            bridge.GetStatuses().Single(status => status.Browser == "msedge").State == "unfocused",
            "an inactive private report clears the previously delivered Edge website");
        var privateSample = ActivityClassifier.Classify(native with
        {
            CapturedAtUtc = start.AddSeconds(7), WindowTitle = "Sensitive private tab - InPrivate - Microsoft Edge"
        }, bridge.GetCurrent("msedge.exe"), settings, 999);
        Check(privateSample is { Kind: "app", Title: "", Url: "", Domain: "" },
            "an inactive private report never contributes a website or private native title");
        tracker.Observe(privateSample, start.AddSeconds(7));

        await Post("/activity", new
        {
            browser = "chrome", instanceId = "chrome-profile", focused = true, incognito = false,
            url = "https://wrong-browser.example/", title = "Planning"
        });
        var chrome = bridge.GetCurrent("chrome.exe");
        Check(chrome is not null && bridge.GetCurrent("msedge.exe") is null &&
            ActivityClassifier.Classify(native with { CapturedAtUtc = start.AddSeconds(7) }, chrome, settings, 999) is { Kind: "app", Title: "" },
            "Chrome activity cannot become Edge website time even when a title matches");

        var staleSource = ActivityClassifier.Classify(native with { CapturedAtUtc = start.AddSeconds(9) }, snapshot, settings, 999);
        Check(staleSource is { Kind: "app", Title: "", Url: "" },
            "an older source snapshot cannot be classified after its freshness deadline");
        tracker.Observe(staleSource, start.AddSeconds(9));
        tracker.Stop(start.AddSeconds(10));

        var timeline = store.Query(start, start.AddSeconds(11));
        Check(timeline.Count == 2 && timeline.Single(item => item.Kind == "website").DurationSeconds == 6 &&
            timeline.Single(item => item.Kind == "app").DurationSeconds == 4,
            "website and fallback app sessions partition time without persisting private or wrong-browser websites");
        Check(store.Summarize(start, start.AddSeconds(11)).Single(item => item.Kind == "website") is { Label: "example.com", Seconds: 6 },
            "saved website domain and duration reach the summary");
    }
    using (var reopened = new ActivityStore(database))
    {
        var saved = reopened.Query(start, start.AddSeconds(11), kind: "website").Single();
        Check(saved is { ProcessName: "msedge.exe", Domain: "example.com", Url: "https://example.com/planning" } &&
            saved.DurationSeconds == 6,
            "website identity, sanitized URL and measured duration survive reopening the database");
    }
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(folder, true);
}

Console.WriteLine($"All {passed} website-pipeline checks passed.");
