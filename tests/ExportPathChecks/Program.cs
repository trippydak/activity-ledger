using ActivityLedger.Core;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

var folder = Path.Combine(Path.GetTempPath(), "ActivityLedger-export-checks-" + Guid.NewGuid().ToString("N"));
var data = Path.Combine(folder, "ActivityLedger");
var installed = Path.Combine(folder, "Programs", "ActivityLedger", "ActivityLedger.exe");
var current = Path.Combine(folder, "Downloads", "ActivityLedger.exe");
bool IsProtected(string target) => ExportPathPolicy.IsProtectedDestination(target, data, installed, current);

Check(IsProtected(data), "data directory itself is protected");
Check(IsProtected(data + Path.DirectorySeparatorChar), "data directory with trailing separator is protected");
foreach (var file in new[] { "password.json", "settings.json", "activity.db", "activity.db-wal", "activity.db-shm" })
    Check(IsProtected(Path.Combine(data, file)), "protects " + file);
Check(IsProtected(Path.Combine(data, "config", "nested", "credentials.json")), "all nested data files are protected");
Check(IsProtected(Path.Combine(data, "nested", "..", "password.json")), "dot segments cannot bypass data protection");
Check(IsProtected(Path.Combine(data, ".", "password.json")), "current-directory segments cannot bypass protection");
Check(IsProtected(Path.Combine(data.ToUpperInvariant(), "PASSWORD.JSON")), "comparison uses Windows case-insensitive rules");

var installedDirectory = Path.GetDirectoryName(installed)!;
Check(IsProtected(installedDirectory), "installation directory itself is protected");
Check(IsProtected(installed), "installed executable is protected");
Check(IsProtected(Path.Combine(installedDirectory, "settings.json")), "other installation files are protected");
Check(IsProtected(Path.Combine(installedDirectory, "browser-extension", "manifest.json")), "nested installation resources are protected");
Check(IsProtected(Path.Combine(installedDirectory, "sub", "..", "ActivityLedger.exe")), "dot segments cannot bypass installation protection");
Check(IsProtected(current.ToUpperInvariant()), "portable current executable is protected regardless of case");

Check(!IsProtected(Path.Combine(folder, "ActivityLedger-export", "history.csv")), "data-prefix sibling folder remains allowed");
Check(!IsProtected(Path.Combine(folder, "Programs", "ActivityLedger-export", "history.csv")), "installation-prefix sibling folder remains allowed");
Check(!IsProtected(Path.Combine(Path.GetDirectoryName(current)!, "history.csv")), "normal file beside portable executable remains allowed");
Check(!IsProtected(Path.Combine(folder, "Documents", "history.csv")), "ordinary user export destination remains allowed");
Check(!IsProtected(Path.Combine(data, "..", "history.csv")), "canonical path outside protected data remains allowed");
Check(!IsProtected(Path.Combine(folder, "ActivityLedger.exe.csv")), "unrelated file sharing the executable name prefix remains allowed");

Check(IsProtected(""), "empty destination fails closed");
Check(IsProtected("bad\0path"), "invalid destination fails closed");
Check(ExportPathPolicy.IsProtectedDestination(Path.Combine(folder, "history.csv"), "", installed, current), "invalid data directory fails closed");
Check(ExportPathPolicy.IsProtectedDestination(Path.Combine(folder, "history.csv"), data, "bad\0path", current), "invalid installed executable fails closed");
Check(ExportPathPolicy.IsProtectedDestination(Path.Combine(folder, "history.csv"), data, installed, ""), "invalid current executable fails closed");

// Filesystem roots must retain their separator when forming the ancestor boundary.
var root = Path.GetPathRoot(folder)!;
Check(ExportPathPolicy.IsProtectedDestination(Path.Combine(folder, "history.csv"), root, installed, current), "root directory ancestry is handled correctly");

Console.WriteLine($"All {passed} export-path checks passed.");
