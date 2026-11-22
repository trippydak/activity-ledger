namespace ActivityLedger.Core;

/// <summary>Recognizes distributed setup names, including browser duplicate-download suffixes.</summary>
public static class SetupLaunchPolicy
{
    public static bool IsSetupExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        const string prefix = "ActivityLedgerSetup";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return name.Length == prefix.Length || name[prefix.Length] is '-' or '_' or ' ' or '(';
    }
}
