using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ActivityLedger.Browser;

// For private flags, ReceivedAtUtc is the validated focus observation time when supplied.
public sealed record BrowserSnapshot(string Browser, string Url, string Title, DateTimeOffset ReceivedAtUtc, bool IsPrivate = false);
public sealed record BrowserConnectionStatus(string Browser, DateTimeOffset? LastReportUtc, string State, int Instances, bool HasCurrentSnapshot);

/// <summary>A loopback-only, token-authenticated bridge. Browser data is held only in memory.</summary>
public sealed class BrowserBridge : IDisposable
{
    private const int MaxBody = 32 * 1024;
    private readonly string _token;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _connections = new(16, 16);
    private readonly object _gate = new();
    private readonly Dictionary<string, BrowserSnapshot> _snapshots = new();
    private readonly Dictionary<string, ReportStatus> _reports = new();
    private static readonly Regex ExtensionOrigin = new("^chrome-extension://[a-p]{32}$", RegexOptions.CultureInvariant);
    private Task? _acceptTask;
    public int Port { get; }
    public string? Error { get; private set; }

    public BrowserBridge(string token, int port = 47831)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 24) throw new ArgumentException("A pairing token of at least 24 characters is required.", nameof(token));
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _token = token;
        Port = port;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Server.ExclusiveAddressUse = true;
    }

    public void Start()
    {
        if (_acceptTask != null) return;
        try
        {
            _listener.Start(16);
            _acceptTask = AcceptLoop();
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException)
        {
            Error = $"Browser bridge could not listen on 127.0.0.1:{Port}: {ex.Message}";
        }
    }

    public BrowserSnapshot? GetCurrent(string processName)
    {
        string browser = Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();
        if (browser is not ("chrome" or "msedge")) return null;
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in _snapshots.Where(p => !IsFresh(p.Value, now)).Select(p => p.Key).ToArray()) _snapshots.Remove(key);
            return _snapshots.Values.Where(s => s.Browser == browser).OrderByDescending(s => s.ReceivedAtUtc).FirstOrDefault();
        }
    }

    /// <summary>Activity delivery diagnostics only. Pairing health checks are not activity reports.</summary>
    public IReadOnlyList<BrowserConnectionStatus> GetStatuses()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            return new[] { "chrome", "msedge" }.Select(browser =>
            {
                var reports = _reports.Values.Where(report => report.Browser == browser).ToArray();
                var latest = reports.OrderByDescending(report => report.ReceivedAtUtc).FirstOrDefault();
                var current = _snapshots.Values.Where(snapshot => snapshot.Browser == browser && IsFresh(snapshot, now))
                    .OrderByDescending(snapshot => snapshot.ReceivedAtUtc).FirstOrDefault();
                string state = current is not null ? current.IsPrivate ? "private" : "active" : latest is null ? "no-report" :
                    (latest.IsPrivate ? latest.ObservedAtUtc ?? latest.ReceivedAtUtc : latest.ReceivedAtUtc) < now.AddSeconds(latest.IsPrivate ? -3 : -8) ? "stale" : latest.State;
                return new BrowserConnectionStatus(browser, latest?.ReceivedAtUtc, state, reports.Length, current is not null);
            }).ToArray();
        }
    }

    private static bool IsFresh(BrowserSnapshot snapshot, DateTimeOffset now) =>
        snapshot.ReceivedAtUtc >= now.AddSeconds(snapshot.IsPrivate ? -3 : -8);

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                if (!_connections.Wait(0)) { client.Dispose(); continue; }
                _ = HandleClient(client);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex) { Error = $"Browser bridge: {ex.Message}"; break; }
        }
    }

    private async Task HandleClient(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequest(stream, timeout.Token).ConfigureAwait(false);
                if (request == null) return;
                string? origin = request.Headers.GetValueOrDefault("Origin");
                if (origin == null || !ExtensionOrigin.IsMatch(origin))
                {
                    await Respond(stream, 403, "Forbidden", null, timeout.Token).ConfigureAwait(false);
                    return;
                }
                if (request.Headers.GetValueOrDefault("Host") != $"127.0.0.1:{Port}")
                {
                    await Respond(stream, 403, "Forbidden", null, timeout.Token).ConfigureAwait(false);
                    return;
                }
                if (request.Method == "OPTIONS")
                {
                    await Respond(stream, 204, "", origin, timeout.Token).ConfigureAwait(false);
                    return;
                }
                var authorization = request.Headers.GetValueOrDefault("Authorization") ?? "";
                var expected = Encoding.UTF8.GetBytes("Bearer " + _token);
                var actual = Encoding.UTF8.GetBytes(authorization);
                if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected))
                {
                    await Respond(stream, 401, "Pairing token does not match", origin, timeout.Token).ConfigureAwait(false);
                    return;
                }
                if (request.Method == "POST" && request.Path == "/health")
                {
                    await Respond(stream, 200, "{\"status\":\"connected\",\"app\":\"ActivityLedger\"}", origin, timeout.Token).ConfigureAwait(false);
                    return;
                }
                if (request.Method != "POST" || request.Path != "/activity")
                {
                    await Respond(stream, 404, "Not found", origin, timeout.Token).ConfigureAwait(false);
                    return;
                }
                if (!(request.Headers.GetValueOrDefault("Content-Type") ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    await Respond(stream, 415, "JSON required", origin, timeout.Token).ConfigureAwait(false);
                    return;
                }
                var payload = JsonSerializer.Deserialize<ActivityPayload>(request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (payload == null || payload.Browser is not ("chrome" or "msedge") || string.IsNullOrWhiteSpace(payload.InstanceId) || payload.InstanceId.Length > 100)
                {
                    await Respond(stream, 400, "Invalid activity", origin, timeout.Token).ConfigureAwait(false);
                    return;
                }
                lock (_gate)
                {
                    string key = origin + ":" + payload.InstanceId;
                    var received = DateTimeOffset.UtcNow;
                    if (_reports.Count >= 32 && !_reports.ContainsKey(key))
                    {
                        var oldest = _reports.MinBy(pair => pair.Value.ReceivedAtUtc).Key;
                        _reports.Remove(oldest);
                        _snapshots.Remove(oldest);
                    }
                    // Private mode is a flag only. Never parse or retain private URL/title fields,
                    // even if an old or malformed client sends them with inconsistent reasons.
                    if (payload.Incognito || payload.Reason is "private" or "private-permission-required")
                    {
                        if (payload.Incognito && payload.Focused)
                        {
                            var observed = received;
                            long receivedMs = received.ToUnixTimeMilliseconds();
                            bool fresh = payload.ObservedAtUnixMs is not { } observedMs ||
                                (observedMs >= receivedMs - 3000 && observedMs <= receivedMs + 1000);
                            if (fresh)
                            {
                                if (payload.ObservedAtUnixMs is { } supplied) observed = DateTimeOffset.FromUnixTimeMilliseconds(supplied);
                                _snapshots[key] = new BrowserSnapshot(payload.Browser, "", "", observed, IsPrivate: true);
                                _reports[key] = new ReportStatus(payload.Browser, received, "private", IsPrivate: true, ObservedAtUtc: observed);
                            }
                            else
                            {
                                _snapshots.Remove(key);
                                _reports[key] = new ReportStatus(payload.Browser, received, "private-stale");
                            }
                        }
                        else
                        {
                            _snapshots.Remove(key);
                            string state = payload.Reason == "private-permission-required" ? "private-permission-required" :
                                payload.Focused ? "private" : "unfocused";
                            _reports[key] = new ReportStatus(payload.Browser, received, state);
                        }
                    }
                    else if (!payload.Focused || !Uri.TryCreate(payload.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                    {
                        _snapshots.Remove(key);
                        string state = payload.Incognito ? "private" : payload.Reason switch
                        {
                            "unfocused" or "private" or "unsupported-page" or "no-active-tab" or "unsupported-window" => payload.Reason,
                            _ => "unfocused-or-private"
                        };
                        _reports[key] = new ReportStatus(payload.Browser, received, state);
                    }
                    else
                    {
                        // Query strings, fragments, and credentials are never needed by the desktop journal.
                        var clean = new UriBuilder(url) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri;
                        if (_snapshots.Count >= 32 && !_snapshots.ContainsKey(key))
                        {
                            var oldest = _snapshots.MinBy(p => p.Value.ReceivedAtUtc).Key;
                            _snapshots.Remove(oldest);
                        }
                        _snapshots[key] = new BrowserSnapshot(payload.Browser, clean, (payload.Title ?? "")[..Math.Min(payload.Title?.Length ?? 0, 2048)], received);
                        _reports[key] = new ReportStatus(payload.Browser, received, "active");
                    }
                }
                await Respond(stream, 200, "{\"status\":\"ok\"}", origin, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or JsonException or ArgumentException or InvalidOperationException or UriFormatException) { }
            finally { _connections.Release(); }
        }
    }

    private sealed record HttpRequest(string Method, string Path, Dictionary<string, string> Headers, byte[] Body);
    private sealed record ReportStatus(string Browser, DateTimeOffset ReceivedAtUtc, string State, bool IsPrivate = false, DateTimeOffset? ObservedAtUtc = null);
    private sealed class ActivityPayload
    {
        public string Browser { get; set; } = "";
        public string InstanceId { get; set; } = "";
        public string Url { get; set; } = "";
        public string? Title { get; set; }
        public bool Focused { get; set; }
        public bool Incognito { get; set; }
        public string Reason { get; set; } = "";
        public long? ObservedAtUnixMs { get; set; }
    }

    private static async Task<HttpRequest?> ReadRequest(NetworkStream stream, CancellationToken cancellation)
    {
        using var header = new MemoryStream();
        var one = new byte[1];
        int tail = 0;
        while (header.Length < 8192)
        {
            if (await stream.ReadAsync(one, cancellation).ConfigureAwait(false) == 0) return null;
            header.WriteByte(one[0]);
            tail = (tail << 8) | one[0];
            if (tail == 0x0d0a0d0a) break;
        }
        if (tail != 0x0d0a0d0a) return null;
        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.None);
        var first = lines[0].Split(' ');
        if (first.Length != 3 || first[2] != "HTTP/1.1") return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1).Where(l => l.Length > 0))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0 || !headers.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim())) return null;
        }
        if (headers.ContainsKey("Transfer-Encoding")) return null;
        int length = 0;
        if (headers.TryGetValue("Content-Length", out var lengthText) && (!int.TryParse(lengthText, out length) || length is < 0 or > MaxBody)) return null;
        var body = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(body.AsMemory(offset), cancellation).ConfigureAwait(false);
            if (read == 0) return null;
            offset += read;
        }
        return new HttpRequest(first[0], first[1], headers, body);
    }

    private static async Task Respond(NetworkStream stream, int code, string body, string? origin, CancellationToken cancellation)
    {
        string reason = code switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 415 => "Unsupported Media Type", _ => "Error" };
        var bytes = Encoding.UTF8.GetBytes(body);
        var cors = origin == null ? "" : $"Access-Control-Allow-Origin: {origin}\r\nVary: Origin\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: Authorization, Content-Type\r\nAccess-Control-Allow-Private-Network: true\r\nAccess-Control-Max-Age: 600\r\n";
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n{cors}\r\n");
        await stream.WriteAsync(head, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        lock (_gate) { _snapshots.Clear(); _reports.Clear(); }
        // In-flight clients have a cancellation token and release their semaphore slots.
    }
}
