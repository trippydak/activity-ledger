using System.Security.Cryptography;
using System.Text.Json;

namespace ActivityLedger.Core;

public sealed record ActivityRecord(
    string Id,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Kind,
    string ProcessName,
    string Title,
    string Url,
    string Domain)
{
    public double DurationSeconds => Math.Max(0, (EndUtc - StartUtc).TotalSeconds);
}

/// <summary>One foreground observation. Return null to the tracker when collection is paused or excluded.</summary>
public sealed record ActivitySample(
    string Kind,
    string ProcessName,
    string Title = "",
    string Url = "",
    string Domain = "");

public sealed record ActivitySummary(string Label, string Kind, double Seconds, int Sessions);

public sealed class AppSettings
{
    public int IdleThresholdSeconds { get; set; } = 120;
    /// <summary>Zero retains records indefinitely.</summary>
    public int RetentionDays { get; set; } = 90;
    public bool CaptureWindowTitles { get; set; }
    /// <summary>False saves only the site's origin. Query strings and fragments are never retained.</summary>
    public bool CaptureFullUrls { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool WelcomeCompleted { get; set; }
    public List<string> ExcludedProcesses { get; set; } = [];
    public List<string> ExcludedDomains { get; set; } = [];
    public string BridgeToken { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public int BridgePort { get; set; } = 47831;
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ActivityLedger");

    public AppSettings Load()
    {
        if (!File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new();
            Normalize(settings);
            return settings;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // Do not silently discard privacy exclusions or rotate an existing pairing token.
            throw new InvalidDataException("Saved settings could not be read. Restore or rename " + path +
                " before starting Activity Ledger. Your activity history has been preserved.", error);
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, path, true);
    }

    private static void Normalize(AppSettings settings)
    {
        settings.IdleThresholdSeconds = Math.Clamp(settings.IdleThresholdSeconds, 15, 3600);
        settings.RetentionDays = Math.Clamp(settings.RetentionDays, 0, 36500);
        settings.ExcludedProcesses ??= [];
        settings.ExcludedDomains ??= [];
        if (settings.BridgePort < 1024 || settings.BridgePort > 65535) settings.BridgePort = 47831;
        if (string.IsNullOrWhiteSpace(settings.BridgeToken) || settings.BridgeToken.Length < 32)
            settings.BridgeToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }
}

public static class PrivacyRules
{
    public static bool IsExcluded(ActivitySample sample, AppSettings settings)
    {
        var process = NormalizeProcess(sample.ProcessName);
        if (settings.ExcludedProcesses.Any(value => NormalizeProcess(value) is { Length: > 0 } excluded &&
                process.Equals(excluded, StringComparison.OrdinalIgnoreCase))) return true;
        var domain = NormalizeDomain(sample.Domain);
        return domain.Length > 0 && settings.ExcludedDomains.Any(value =>
        {
            var excluded = NormalizeDomain(value);
            return excluded.Length > 0 && (domain.Equals(excluded, StringComparison.OrdinalIgnoreCase) ||
                domain.EndsWith("." + excluded, StringComparison.OrdinalIgnoreCase));
        });
    }

    public static string NormalizeProcess(string process)
    {
        var value = Path.GetFileName(process.Trim().Replace('\\', '/'));
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;
    }

    public static string NormalizeDomain(string domain)
    {
        var value = domain.Trim().TrimStart('.');
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)) value = uri.Host;
        return value.TrimEnd('.').ToLowerInvariant();
    }
}
