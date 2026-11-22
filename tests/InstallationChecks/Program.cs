using ActivityLedger.Installation;

var assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException("FAILED: " + message);
}
void Reject(Action action, string message)
{
    var rejected = false;
    try { action(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, message);
}

// Real Windows account paths can contain spaces, non-Latin letters and shell
// metacharacters. They must remain data rather than becoming command source.
const string directory = @"C:\Users\A & B ! %PATH% 日本語\AppData\Local\Programs\ActivityLedger";
const string executable = directory + @"\ActivityLedger.exe";
const string script = @"C:\Users\A & B ! %PATH% 日本語\Temp\remove.cmd";
var environment = UninstallCleanup.EnvironmentFor(executable, directory, script, 12345, @"C:\Windows\System32");
Check(environment["AL_REMOVE_EXE"] == executable, "Executable path retained verbatim in environment");
Check(environment["AL_REMOVE_DIR"] == directory, "Directory path retained verbatim in environment");
Check(environment["AL_REMOVE_SCRIPT"] == script, "Script path retained verbatim in environment");
Check(environment["AL_PARENT_PID"] == "12345", "PID serialized as invariant decimal");
Check(!UninstallCleanup.Arguments.Contains(directory), "Account path never interpolated into command line");
Check(!UninstallCleanup.Script.Contains(directory), "Account path never interpolated into script source");
Check(UninstallCleanup.Arguments.Contains("/v:off"), "Delayed shell expansion disabled before script launch");
Check(UninstallCleanup.Script.Contains("setlocal DisableDelayedExpansion"), "Delayed expansion remains disabled in script");
Check(UninstallCleanup.Script.Contains("del /f /q \"%AL_REMOVE_EXE%\""), "Deletion names only the exact executable");
Check(UninstallCleanup.Script.Contains("rmdir \"%AL_REMOVE_DIR%\""), "Folder removal is nonrecursive");
Check(!UninstallCleanup.Script.Contains("rmdir /s", StringComparison.OrdinalIgnoreCase), "No recursive data removal");
Check(!UninstallCleanup.Script.Contains("password", StringComparison.OrdinalIgnoreCase) ||
    !UninstallCleanup.Script.Contains("password.json", StringComparison.OrdinalIgnoreCase), "Password file is not a cleanup target");
Check(!UninstallCleanup.Script.Contains("call ", StringComparison.OrdinalIgnoreCase), "No CALL double expansion of paths");
Check(UninstallCleanup.Script.Contains("GEQ 15"), "Locked-file retry is bounded");
Reject(() => UninstallCleanup.EnvironmentFor(executable, directory, script, 0, @"C:\Windows"), "Invalid PID rejected");
Reject(() => UninstallCleanup.EnvironmentFor("bad\"path", directory, script, 1, @"C:\Windows"), "Quote injection rejected");
Reject(() => UninstallCleanup.EnvironmentFor("bad\r\npath", directory, script, 1, @"C:\Windows"), "Newline injection rejected");
Reject(() => UninstallCleanup.EnvironmentFor(executable, "", script, 1, @"C:\Windows"), "Empty removal directory rejected");

Console.WriteLine($"Installation checks passed: {assertions} assertions.");
