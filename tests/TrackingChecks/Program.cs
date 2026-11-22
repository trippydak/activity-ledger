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

var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
var settings = new AppSettings { CaptureWindowTitles = true };
var edge = new WindowSample(now, "msedge.exe", @"C:\Program Files\Microsoft\Edge\msedge.exe",
    "Example - Microsoft Edge", 123, 42, 0, false);
var snapshot = new BrowserSnapshot("msedge", "https://user:secret@example.com/private/path?token=secret#fragment", "Example", now);
ActivitySample? Classify(WindowSample raw, BrowserSnapshot? browser = null) => ActivityClassifier.Classify(raw, browser, settings, 99);

var website = Classify(edge, snapshot);
Check(website is { Kind: "website", Domain: "example.com", ProcessName: "msedge.exe", Title: "Example" },
    "matching Edge caption produces a website observation");
Check(website!.Url == "https://example.com", "domain-only mode strips path, credentials, query and fragment");
settings.CaptureFullUrls = true;
Check(Classify(edge, snapshot)!.Url == "https://example.com/private/path", "path mode still strips credentials, query and fragment");
settings.CaptureWindowTitles = false;
Check(Classify(edge, snapshot)!.Title == "", "website titles are omitted when title capture is disabled");
settings.CaptureWindowTitles = true;

Check(Classify(edge with { WindowTitle = "Another Tab - Microsoft Edge" }, snapshot) is { Kind: "app", Title: "", Domain: "", Url: "" },
    "mismatched caption falls back to app without leaking native browser title");
Check(Classify(edge, snapshot with { Browser = "chrome" }) is { Kind: "app", Title: "" },
    "another browser's snapshot cannot attribute website time");
Check(Classify(edge, snapshot with { ReceivedAtUtc = now.AddSeconds(-8.001) }) is { Kind: "app" },
    "expired browser snapshots cannot attribute website time");
Check(Classify(edge, snapshot with { ReceivedAtUtc = now.AddSeconds(-8) }) is { Kind: "website" },
    "snapshot at the eight-second freshness boundary is accepted");
Check(Classify(edge, snapshot with { ReceivedAtUtc = now.AddMilliseconds(100) }) is { Kind: "website" },
    "heartbeat arriving just after native sample is accepted");
Check(Classify(edge, snapshot with { ReceivedAtUtc = now.AddHours(1) }) is { Kind: "app" },
    "implausibly future snapshot is rejected");
Check(Classify(edge, snapshot with { Url = "file:///private/document.html" }) is { Kind: "app", Url: "" },
    "unsupported URL schemes cannot become website history");
Check(Classify(edge, snapshot with { Url = "not a URL" }) is { Kind: "app" }, "malformed URLs fall back to app history");

settings.ExcludedDomains = ["example.com"];
Check(Classify(edge, snapshot) is null, "excluded website becomes a recording gap rather than browser app time");
Check(Classify(edge, snapshot with { Url = "https://sub.example.com/path" }) is null, "excluded site also covers subdomains");
settings.ExcludedDomains.Clear();
settings.ExcludedProcesses = ["MSEDGE"];
Check(Classify(edge, snapshot) is null, "process exclusions apply to website observations");
settings.ExcludedProcesses.Clear();

var app = edge with { ProcessName = "notepad.exe", WindowTitle = "Private document", ProcessId = 18 };
Check(Classify(app) is { Kind: "app", Title: "Private document" }, "ordinary applications retain opted-in titles");
Check(Classify(app, snapshot) is { Kind: "app", Title: "Private document" }, "browser snapshots cannot attach to ordinary applications");
settings.CaptureWindowTitles = false;
Check(Classify(app) is { Title: "" }, "ordinary application titles are omitted when disabled");
settings.CaptureWindowTitles = true;
settings.ExcludedProcesses = ["notepad.exe"];
Check(Classify(app) is null, "excluded application produces no observation");
settings.ExcludedProcesses.Clear();
Check(Classify(app with { SessionLocked = true }) is null, "locked sessions never record active app time");
Check(Classify(app with { ProcessName = "" }) is null, "missing foreground process produces no observation");
Check(Classify(app with { IdleSeconds = settings.IdleThresholdSeconds }) is { Kind: "idle", ProcessName: "", Title: "Idle" },
    "idle threshold produces unattributed idle time");

var own = app with { ProcessId = 99, WindowTitle = "Password: never record this" };
Check(Classify(own) is { Kind: "app", ProcessName: "ActivityLedger.exe", Title: "Activity Ledger" },
    "own windows use a constant title instead of password-dialog metadata");
Check(Classify(own with { IdleSeconds = settings.IdleThresholdSeconds }) is { Kind: "idle" },
    "idle state takes precedence over the logger's own window");
settings.CaptureWindowTitles = false;
Check(Classify(own) is { Title: "" }, "own constant title is also omitted when capture is disabled");
settings.ExcludedProcesses = ["ActivityLedger.exe"];
Check(Classify(own) is null, "explicit self-exclusion is respected");
settings.ExcludedProcesses.Clear();
settings.CaptureWindowTitles = true;

foreach (var process in new[] { "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "iexplore.exe" })
    Check(Classify(app with { ProcessName = process, WindowTitle = "Private browsing title" }) is { Kind: "app", Title: "" },
        process + " native titles are never captured without a matching supported extension snapshot");

var privateFlag = snapshot with
{
    IsPrivate = true, Url = "https://private-secret.example/never-store?token=secret", Title = "Never store this private title"
};
var privateNative = edge with { WindowTitle = "A different sensitive private caption - InPrivate - Microsoft Edge" };
Check(Classify(privateNative, privateFlag) is
    { Kind: "private", ProcessName: "msedge.exe", Title: "Private browsing (Edge InPrivate)", Url: "", Domain: "" },
    "explicit private mode records only a safe Edge label without inspecting private content");
settings.CaptureWindowTitles = false;
Check(Classify(privateNative, privateFlag) is { Kind: "private", Title: "Private browsing (Edge InPrivate)" },
    "private mode label remains available when page title capture is disabled");
settings.CaptureWindowTitles = true;
Check(Classify(privateNative with { ProcessName = "chrome.exe" }, privateFlag with { Browser = "chrome" }) is
    { Kind: "private", Title: "Private browsing (Chrome Incognito)", Url: "", Domain: "" },
    "Chrome private mode uses its own safe label");
Check(Classify(privateNative, privateFlag with { Browser = "chrome" }) is { Kind: "app", Title: "" },
    "wrong browser private flags cannot attribute private mode");
Check(Classify(privateNative, privateFlag with { ReceivedAtUtc = now.AddSeconds(-3) }) is { Kind: "private" },
    "private mode at three-second freshness boundary is accepted");
Check(Classify(privateNative, privateFlag with { ReceivedAtUtc = now.AddSeconds(-3.001) }) is { Kind: "app", Title: "" },
    "private mode expires sooner than normal website snapshots");
Check(Classify(edge, privateFlag with { ReceivedAtUtc = now.AddSeconds(-4), Title = "Example", Url = "https://secret.example/" }) is
    { Kind: "app", Title: "", Url: "", Domain: "" },
    "expired private flag never falls through to ordinary website processing");
Check(Classify(privateNative, privateFlag with { ReceivedAtUtc = now.AddSeconds(1.001) }) is { Kind: "app", Title: "" },
    "implausibly future private flag is rejected");
settings.ExcludedDomains = ["private-secret.example"];
Check(Classify(privateNative, privateFlag) is { Kind: "private" },
    "domain exclusions do not inspect private URLs or suppress a content-free mode flag");
settings.ExcludedDomains.Clear();
settings.ExcludedProcesses = ["MSEDGE.exe"];
Check(Classify(privateNative, privateFlag) is null, "excluded browser process also excludes its private mode flag");
settings.ExcludedProcesses.Clear();
Check(Classify(privateNative with { SessionLocked = true }, privateFlag) is null, "locked session takes precedence over private mode");
Check(Classify(privateNative with { IdleSeconds = settings.IdleThresholdSeconds }, privateFlag) is { Kind: "idle" },
    "idle time takes precedence over private mode");
Check(Classify(privateNative with { ProcessName = "" }, privateFlag) is null, "private flag requires a foreground process");
Check(Classify(privateNative with { ProcessId = 99 }, privateFlag) is { Kind: "app", Title: "Activity Ledger" },
    "the logger's own window takes precedence over an unrelated private flag");

Console.WriteLine($"All {passed} tracking checks passed.");
