using ActivityLedger.UI;
using ActivityLedger.Installation;
using ActivityLedger.Core;

namespace ActivityLedger;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Contains("--uninstall")) { InstallationManager.Uninstall(); return; }
            if (args.Contains("--install") || SetupLaunchPolicy.IsSetupExecutable(Environment.ProcessPath))
            {
                InstallationManager.Install();
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Activity Ledger setup could not complete:\n" + ex.Message, "Activity Ledger", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        using var mutex = new Mutex(true, @"Local\ActivityLedger.Desktop.v1", out bool first);
        if (!first)
        {
            MessageBox.Show("Activity Ledger is already running. Open it from the system tray.", "Activity Ledger");
            return;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show("Activity Ledger encountered an error:\n" + e.Exception.Message, "Activity Ledger", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try { Application.Run(new MainForm(args.Contains("--start-logging"))); }
        catch (Exception ex) { MessageBox.Show("Unable to start Activity Ledger:\n" + ex.Message, "Activity Ledger", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
