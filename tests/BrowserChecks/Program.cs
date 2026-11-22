using ActivityLedger.Browser;
using System.Net;
using System.Net.Sockets;

const string secret = "test-token-123456789012345678901234";
var reservation = new TcpListener(IPAddress.Loopback, 0);
reservation.Start();
var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
reservation.Stop();
using var bridge = new BrowserBridge(secret, port);
bridge.Start();
if (bridge.Error != null) throw new Exception(bridge.Error);
using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
var origin = "chrome-extension://" + new string('a', 32);
int count = 0;
async Task Send(string method, string path, object? data, int expected, string? auth = secret, string? testOrigin = null, bool omitOrigin = false)
{
    using var request = new HttpRequestMessage(new HttpMethod(method), path);
    if (!omitOrigin) request.Headers.Add("Origin", testOrigin ?? origin);
    if (auth != null) request.Headers.Add("Authorization", "Bearer " + auth);
    if (data != null) request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(data), System.Text.Encoding.UTF8, "application/json");
    using var response = await client.SendAsync(request);
    if ((int)response.StatusCode != expected) throw new Exception($"Expected {expected}, got {response.StatusCode}");
    if (expected == 200 && !response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)) throw new Exception("Missing CORS header");
    count++;
}
async Task Report(string id, bool focused, string url, bool incognito = false, string browser = "chrome", string? reason = null) =>
    await Send("POST", "/activity", new { instanceId = id, browser, url, title = "Test", focused, incognito, reason }, 200);
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    count++;
}
BrowserConnectionStatus Status(string browser) => bridge.GetStatuses().Single(s => s.Browser == browser);

Check(bridge.GetStatuses().Count == 2, "Diagnostics must include both supported browsers before pairing");
Check(bridge.GetStatuses().All(s => s.LastReportUtc is null && s.State == "no-report" && !s.HasCurrentSnapshot && s.Instances == 0), "Initial report diagnostics must not claim a connection");

await Send("POST", "/health", null, 403, omitOrigin: true);
await Send("POST", "/health", null, 403, testOrigin: "https://malicious.example");
await Send("POST", "/health", null, 401, auth: "wrong-token");
await Send("POST", "/health", null, 401, auth: null);
await Send("GET", "/health", null, 404);
await Send("POST", "/health", new { }, 200);
await Send("OPTIONS", "/activity", null, 204, auth: null);
await Send("POST", "/activity", new { instanceId = "untrusted", browser = "chrome", focused = true, url = "https://example.com" }, 401, auth: "wrong-token");
Check(bridge.GetStatuses().All(s => s.LastReportUtc is null), "Pairing health and rejected requests must not count as activity");
await Report("a", true, "https://user:password@example.com/page?view=1#fragment");
if (bridge.GetCurrent("chrome.exe")?.Url != "https://example.com/page") throw new Exception("URL credentials/query/fragment sanitation failed"); count++;
if (bridge.GetCurrent("msedge") != null || bridge.GetCurrent("other") != null) throw new Exception("Browser attribution failed"); count++;
Check(Status("chrome").State == "active" && Status("chrome").HasCurrentSnapshot && Status("chrome").LastReportUtc.HasValue && Status("chrome").Instances == 1, "Accepted focused report must expose active Chrome diagnostics");
Check(Status("msedge").State == "no-report" && Status("msedge").LastReportUtc is null, "Chrome reporting must not claim Edge is connected");
await Report("b", false, "");
if (bridge.GetCurrent("chrome") == null) throw new Exception("Inactive profile incorrectly cleared active profile"); count++;
Check(Status("chrome").HasCurrentSnapshot && Status("chrome").State == "active" && Status("chrome").Instances == 2, "Inactive profile diagnostics must preserve another profile's active snapshot");
await Report("a", false, "", reason: "unfocused");
if (bridge.GetCurrent("chrome") != null) throw new Exception("Focus clear failed"); count++;
Check(Status("chrome").State == "unfocused" && !Status("chrome").HasCurrentSnapshot, "Focus loss must be visible in diagnostics");
await Report("a", true, "https://example.com");
// The actual private flag must override an inconsistent client-supplied reason.
await Report("a", true, "https://private-user:private-password@private.example/secret?query=private#private", incognito: true, reason: "active");
var privateSnapshot = bridge.GetCurrent("chrome");
Check(privateSnapshot is { IsPrivate: true, Url: "", Title: "" }, "Private indicators must discard all inbound URL and title data");
Check(Status("chrome").State == "private" && Status("chrome").HasCurrentSnapshot, "Focused private window must expose only a private activity indicator");
await Report("a", false, "https://private.example/secret", incognito: true, reason: "private");
Check(bridge.GetCurrent("chrome") is null && !Status("chrome").HasCurrentSnapshot, "Unfocused private window must clear its private indicator");
await Report("a", false, "", reason: "private");
Check(bridge.GetCurrent("chrome") is null, "Old private reason without a focused private flag must not fabricate private activity");
await Report("a", true, "https://private.example/should-not-be-retained", reason: "private");
Check(bridge.GetCurrent("chrome") is null && !Status("chrome").HasCurrentSnapshot, "A private reason alone must not become a private flag or an ordinary website snapshot");
await Send("POST", "/activity", new { instanceId = "a", browser = "chrome", url = "https://private.example", title = "Sensitive", focused = true, incognito = true, reason = "private", observedAtUnixMs = DateTimeOffset.UtcNow.AddSeconds(-4).ToUnixTimeMilliseconds() }, 200);
Check(bridge.GetCurrent("chrome") is null && Status("chrome").State == "private-stale", "A delayed private observation must not restart a private activity indicator");
await Send("POST", "/activity", new { instanceId = "a", browser = "chrome", url = "https://private.example", title = "Sensitive", focused = true, incognito = true, reason = "private", observedAtUnixMs = DateTimeOffset.UtcNow.AddSeconds(3).ToUnixTimeMilliseconds() }, 200);
Check(bridge.GetCurrent("chrome") is null && Status("chrome").State == "private-stale", "A future private observation must not extend the private indicator lifetime");
var recentPrivateObservation = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();
await Send("POST", "/activity", new { instanceId = "a", browser = "chrome", url = "https://private.example", title = "Sensitive", focused = true, incognito = true, reason = "private", observedAtUnixMs = recentPrivateObservation }, 200);
Check(bridge.GetCurrent("chrome") is { IsPrivate: true, Url: "", Title: "" } observedPrivate && observedPrivate.ReceivedAtUtc.ToUnixTimeMilliseconds() == recentPrivateObservation, "A fresh private observation must preserve observation time and no website data");
await Report("a", true, "https://example.com");
Check(bridge.GetCurrent("chrome") is { IsPrivate: false } ordinary && ordinary.Url.StartsWith("https://example.com"), "Returning to an ordinary site must replace the private indicator");
await Report("a", true, "chrome://settings", reason: "unsupported-page");
if (bridge.GetCurrent("chrome") != null) throw new Exception("Internal tab clear failed"); count++;
Check(Status("chrome").State == "unsupported-page" && !Status("chrome").HasCurrentSnapshot, "Internal pages must expose an unsupported-page diagnostic");
await Report("a", false, "", reason: "not-a-real-state");
Check(Status("chrome").State == "unfocused-or-private", "Unrecognized report reason must not become arbitrary diagnostic text");
await Send("POST", "/activity", new { browser = "unknown", instanceId = "a", url = "https://example.com", focused = true }, 400);
await Report("a", true, "https://example.com", browser: "msedge");
if (bridge.GetCurrent("msedge.exe") == null) throw new Exception("Edge lookup failed"); count++;
Check(Status("msedge").State == "active" && Status("msedge").HasCurrentSnapshot, "Edge diagnostics must follow actual Edge reports");
var edgeLastSeen = Status("msedge").LastReportUtc;
await Report("a-private", true, "", incognito: true, browser: "chrome", reason: "private");
Check(bridge.GetCurrent("chrome") is { IsPrivate: true }, "Private state should work without any website address");
await Task.Delay(3100);
Check(bridge.GetCurrent("chrome") is null && !Status("chrome").HasCurrentSnapshot, "Private activity indicators must expire after three seconds without a heartbeat");
Check(bridge.GetCurrent("msedge") is { IsPrivate: false }, "Ordinary website heartbeat must remain valid beyond the shorter private indicator lifetime");
await Task.Delay(5000);
if (bridge.GetCurrent("msedge") != null) throw new Exception("Expired heartbeat retained"); count++;
Check(Status("msedge").State == "stale" && !Status("msedge").HasCurrentSnapshot && Status("msedge").LastReportUtc == edgeLastSeen, "Expired reports must retain last-seen diagnostics without attributing website time");
Console.WriteLine($"Browser bridge: {count} checks passed.");
