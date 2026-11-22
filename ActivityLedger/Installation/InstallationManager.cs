using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ActivityLedger.Core;
using ActivityLedger.UI;
using Microsoft.Win32;

namespace ActivityLedger.Installation;

/// <summary>A per-user installer for the self-contained application. Journal data is never removed.</summary>
public static class InstallationManager
{
    private const string ApplicationMutex = @"Local\ActivityLedger.Desktop.v1";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ActivityLedger";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ActivityLedger");
    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Activity Ledger.lnk");

    public static string InstalledExecutable => Path.Combine(InstallDirectory, "ActivityLedger.exe");
    public static bool IsInstalled => File.Exists(InstalledExecutable);

    public static void Install()
    {
        try
        {
            RequireWindows();
            var action = IsInstalled ? "Update" : "Install";
            if (MessageBox.Show(action + " Activity Ledger for your Windows account?\n\n" + InstallDirectory +
                    "\n\nRecords foreground app/site activity for this Windows account. Recording starts automatically at sign-in and remains visible in the tray.\n\n" +
                    "You will set a management password on this machine. Cloud sync is optional and off on a new installation.\n\n" +
                    "Existing journal, settings, password and pairing are preserved during updates.", "Activity Ledger Setup",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) != DialogResult.Yes)
                return;

            // A registered installation must always have a password before it can be
            // launched or exposed in Installed Apps. Corrupt stores fail closed.
            var password = OpenPasswordStore();
            if (password.IsConfigured)
            {
                if (!PasswordDialog.ConfirmPassword(null, password, "install or update Activity Ledger")) return;
            }
            else if (!PasswordDialog.CreatePassword(null, password)) return;

            if (!WaitForTrackerToExit()) return;

            var source = Environment.ProcessPath ?? throw new InvalidOperationException("The setup executable could not be located.");
            Directory.CreateDirectory(InstallDirectory);
            if (!Path.GetFullPath(source).Equals(Path.GetFullPath(InstalledExecutable), StringComparison.OrdinalIgnoreCase))
            {
                var staging = InstalledExecutable + ".new";
                try
                {
                    File.Copy(source, staging, true);
                    File.Move(staging, InstalledExecutable, true);
                }
                finally
                {
                    try { if (File.Exists(staging)) File.Delete(staging); } catch (IOException) { }
                }
            }
            WriteUninstallRegistration();
            TryCreateShortcut();

            Process.Start(new ProcessStartInfo(InstalledExecutable) { UseShellExecute = true, WorkingDirectory = InstallDirectory });
        }
        catch (Exception error)
        {
            MessageBox.Show("Setup could not finish:\n\n" + error.Message +
                "\n\nYour journal and password have been preserved.", "Activity Ledger Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void Uninstall()
    {
        try
        {
            RequireWindows();
            if (!IsInstalled)
            {
                MessageBox.Show("Activity Ledger is not installed for this Windows account. Your journal data has not been changed.",
                    "Activity Ledger removal", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var password = OpenPasswordStore();
            if (!password.IsConfigured)
            {
                MessageBox.Show("Password setup is incomplete. Open Activity Ledger and create your password before uninstalling.",
                    "Activity Ledger removal", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!PasswordDialog.ConfirmPassword(null, password, "uninstall Activity Ledger")) return;
            if (!WaitForTrackerToExit()) return;
            if (MessageBox.Show("Uninstall Activity Ledger?\n\nThe application and its Windows startup entry will be removed. " +
                    "Your journal, settings, and password will remain in:\n" + SettingsStore.DataDirectory,
                    "Activity Ledger removal", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            var scriptPath = Path.Combine(Path.GetTempPath(), "ActivityLedger-remove-" + Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(scriptPath, UninstallCleanup.Script.ReplaceLineEndings("\r\n"), Encoding.ASCII);
            object? previousStartup;
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                previousStartup = run?.GetValue("ActivityLedger");
                run?.DeleteValue("ActivityLedger", false);
            }

            try
            {
                using var registryRoot = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true);
                registryRoot?.DeleteSubKeyTree("ActivityLedger", false);
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);

                // The installed executable is this process during a normal uninstall.
                // A visible, bounded cleanup waits for us to exit before deleting it.
                // Paths travel in environment variables, never as shell code.
                var cleanup = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
                {
                    Arguments = UninstallCleanup.Arguments,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetTempPath()
                };
                foreach (var pair in UninstallCleanup.EnvironmentFor(InstalledExecutable, InstallDirectory, scriptPath,
                             Environment.ProcessId, Environment.SystemDirectory))
                    cleanup.Environment[pair.Key] = pair.Value;
                if (Process.Start(cleanup) is null) throw new IOException("Windows could not start application cleanup.");
            }
            catch
            {
                // If cleanup cannot start, leave a usable Installed Apps entry so
                // the user can retry rather than orphaning the executable.
                WriteUninstallRegistration();
                if (previousStartup is not null)
                {
                    using var run = Registry.CurrentUser.CreateSubKey(RunKey);
                    run.SetValue("ActivityLedger", previousStartup);
                }
                TryCreateShortcut();
                try { File.Delete(scriptPath); } catch (IOException) { }
                throw;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show("Removal could not finish:\n\n" + error.Message +
                "\n\nYour journal and password have been preserved.", "Activity Ledger removal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static PasswordStore OpenPasswordStore() => new(Path.Combine(SettingsStore.DataDirectory, "password.json"));

    private static bool WaitForTrackerToExit()
    {
        while (IsTrackerRunning())
        {
            if (MessageBox.Show("Activity Ledger is still running.\n\nOpen its system tray menu, choose Quit, and enter your password. " +
                    "Then click Retry to continue.", "Close Activity Ledger", MessageBoxButtons.RetryCancel,
                    MessageBoxIcon.Information) != DialogResult.Retry)
                return false;
        }
        return true;
    }

    private static bool IsTrackerRunning()
    {
        if (!Mutex.TryOpenExisting(ApplicationMutex, out var mutex)) return false;
        mutex.Dispose();
        return true;
    }

    private static void WriteUninstallRegistration()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "Activity Ledger");
        key.SetValue("DisplayVersion", "2.0.0");
        key.SetValue("Publisher", "Activity Ledger");
        key.SetValue("InstallLocation", InstallDirectory);
        key.SetValue("DisplayIcon", InstalledExecutable + ",0");
        key.SetValue("UninstallString", '"' + InstalledExecutable + "\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, new FileInfo(InstalledExecutable).Length / 1024), RegistryValueKind.DWord);
    }

    private static void TryCreateShortcut()
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
            shortcut = ((dynamic)shell).CreateShortcut(ShortcutPath);
            dynamic link = shortcut;
            link.TargetPath = InstalledExecutable;
            link.WorkingDirectory = InstallDirectory;
            link.IconLocation = InstalledExecutable + ",0";
            link.Description = "Open your local Activity Ledger journal";
            link.Save();
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or IOException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // The application remains usable when a corporate policy disables WScript.
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Activity Ledger setup requires Windows.");
    }
}
