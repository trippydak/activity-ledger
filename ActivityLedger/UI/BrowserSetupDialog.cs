using System.Diagnostics;
using System.Reflection;
using ActivityLedger.Core;

namespace ActivityLedger.UI;

public sealed class BrowserSetupDialog : Form
{
    private readonly AppSettings settings;
    private readonly string extensionFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ActivityLedger", "browser-extension");

    public BrowserSetupDialog(AppSettings settings, Func<BrowserDiagnostics>? diagnostics = null)
    {
        this.settings = settings;
        Text = "Activity Ledger · Browser setup";
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 247, 251);
        ClientSize = new Size(680, 565);
        MinimumSize = new Size(630, 565);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.Controls.Add(new Label { Text = "Connect Chrome or Edge\nActive website tracking stays on this computer.", Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 34, 59), ForeColor = Color.White, Padding = new Padding(24, 15, 10, 0), Font = new Font("Segoe UI", 12) }, 0, 0);
        var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24, 15, 20, 8) };
        void TextLine(string text) => body.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(605, 0), Margin = new Padding(0, 0, 0, 13) });
        TextLine("1. Extract the bundled extension, then open your browser's extensions page.");
        var folders = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        var openFolder = new Button { Text = "Extract and open extension folder", AutoSize = true, Height = 32 };
        openFolder.Click += (_, _) => Run(() => { ExtractExtension(); Process.Start(new ProcessStartInfo(extensionFolder) { UseShellExecute = true }); });
        var copyChrome = new Button { Text = "Copy Chrome setup URL", AutoSize = true, Height = 32 };
        copyChrome.Click += (_, _) => Run(() => Clipboard.SetText("chrome://extensions"));
        var copyEdge = new Button { Text = "Copy Edge setup URL", AutoSize = true, Height = 32 };
        copyEdge.Click += (_, _) => Run(() => Clipboard.SetText("edge://extensions"));
        folders.Controls.Add(openFolder);
        folders.Controls.Add(copyChrome);
        folders.Controls.Add(copyEdge);
        folders.MaximumSize = new Size(610, 100);
        body.Controls.Add(folders);
        TextLine("2. Paste the setup URL into your browser. Turn on Developer mode, choose Load unpacked, and select the browser-extension folder opened above.");
        TextLine("3. Open the extension's Details → Extension options. Paste the connection token, select Chrome or Edge, and use port " + settings.BridgePort + ". Choose Save and test connection.");
        var copyToken = new Button { Text = "Copy connection token", AutoSize = true, Height = 34, BackColor = Color.FromArgb(29, 79, 216), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 0, 15) };
        copyToken.Click += (_, _) => Run(() => { Clipboard.SetText(settings.BridgeToken); MessageBox.Show(this, "Connection token copied. Paste it into the extension options. Keep it private.", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information); });
        body.Controls.Add(copyToken);
        TextLine("4. Recording starts automatically in the desktop app. Keep a normal website active for ten seconds, then select Websites and today's dates. Install the extension separately in each browser profile you use.");
        TextLine("Already installed? Extract the extension files again to update the bundled folder, then open edge://extensions or chrome://extensions and click Reload on Activity Ledger. Existing pairing settings are preserved when the folder stays the same.");
        TextLine("Private-mode flags: after updating and Reloading the companion, open its Details and turn on Allow in InPrivate (Edge) or Allow in Incognito (Chrome). This must be enabled separately in each profile. Private pages are never sent; the companion reports only that a private window is active.");
        if (diagnostics is not null)
        {
            var status = new Button { Text = "Check website tracking", AutoSize = true, Height = 34, Margin = new Padding(0, 0, 0, 15) };
            status.Click += (_, _) => { using var dialog = new BrowserStatusDialog(diagnostics); dialog.ShowDialog(this); };
            body.Controls.Add(status);
        }
        TextLine("Visit an InPrivate/Incognito window, then choose Timeline → Private browsing in the tracker to see its start time, end time and duration. Without the private-window permission, browser app usage may still appear but reliable private-mode flags are unavailable. Other browsers appear as app usage only.");
        layout.Controls.Add(body, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 10, 16, 0), BackColor = Color.White };
        var done = new Button { Text = "Done", AutoSize = true, Height = 32, DialogResult = DialogResult.OK };
        actions.Controls.Add(done);
        layout.Controls.Add(actions, 0, 2);
        Controls.Add(layout);
        AcceptButton = done;
        CancelButton = done;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Browser setup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExtractExtension()
    {
        const string prefix = "browser-extension/";
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames().Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        if (resources.Length == 0) throw new InvalidOperationException("The bundled browser extension was not found. Use browser-extension from the download ZIP.");
        Directory.CreateDirectory(extensionFolder);
        foreach (var resource in resources)
        {
            var relative = resource[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(extensionFolder, relative));
            if (!target.StartsWith(Path.GetFullPath(extensionFolder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid bundled extension path.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = assembly.GetManifestResourceStream(resource)!;
            using var output = File.Create(target);
            input.CopyTo(output);
        }
    }
}
