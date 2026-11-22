namespace ActivityLedger.Core;

/// <summary>
/// Prevents Save As from overwriting the application's credentials, settings, history, or installation.
/// Paths are canonicalized lexically; filesystem links and junctions are not resolved. Invalid inputs
/// return true so an uncertain destination cannot bypass the export restriction.
/// </summary>
public static class ExportPathPolicy
{
    public static bool IsProtectedDestination(string destination, string dataDirectory,
        string installedExecutable, string currentExecutable)
    {
        if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(dataDirectory) ||
            string.IsNullOrWhiteSpace(installedExecutable) || string.IsNullOrWhiteSpace(currentExecutable))
            return true;
        try
        {
            var target = Canonicalize(destination);
            var data = Canonicalize(dataDirectory);
            var installed = Canonicalize(installedExecutable);
            var current = Canonicalize(currentExecutable);
            var installationDirectory = Path.GetDirectoryName(installed);
            if (string.IsNullOrEmpty(installationDirectory)) return true;

            return IsInsideOrEqual(target, data) || IsInsideOrEqual(target, Canonicalize(installationDirectory)) ||
                target.Equals(current, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or System.Security.SecurityException)
        {
            return true;
        }
    }

    private static string Canonicalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsInsideOrEqual(string target, string directory)
    {
        if (target.Equals(directory, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
        return target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
