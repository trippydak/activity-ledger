namespace ActivityLedger.UI;

/// <summary>Display-safe browser diagnostics. Callers must omit secrets, full URLs, and raw window titles.</summary>
public sealed record BrowserDiagnostics(
    string DesktopState,
    string ForegroundApplication,
    string EdgeConnection,
    string ChromeConnection,
    string ActiveWebsite,
    string MatchingState,
    string Details);

/// <summary>Read-only browser connection troubleshooting; refreshes without changing recording state.</summary>
public sealed class BrowserStatusDialog : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 33, 61);
    private readonly Func<BrowserDiagnostics> read;
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1000 };
    private readonly Dictionary<string, Label> values = new();
    private readonly Label updated = new() { AutoSize = true, ForeColor = Color.FromArgb(99, 113, 137), Margin = new Padding(0, 12, 14, 0) };
    private BrowserDiagnostics? latest;

    public BrowserStatusDialog(Func<BrowserDiagnostics> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        this.read = read;
        Text = "Activity Ledger · Browser status";
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 247, 251);
        ClientSize = new Size(700, 600);
        MinimumSize = new Size(600, 520);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 77));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        layout.Controls.Add(new Label
        {
            Text = "Browser status\nA live view of the connection and active website.",
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 14, 12, 0),
            BackColor = Navy,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12)
        }, 0, 0);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 12, 20, 8) };
        var fields = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, BackColor = Color.White, Padding = new Padding(13, 6, 13, 6) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var item in new (string Key, string Caption)[]
        {
            (nameof(BrowserDiagnostics.DesktopState), "Desktop logger"),
            (nameof(BrowserDiagnostics.ForegroundApplication), "Foreground app"),
            (nameof(BrowserDiagnostics.EdgeConnection), "Microsoft Edge"),
            (nameof(BrowserDiagnostics.ChromeConnection), "Google Chrome"),
            (nameof(BrowserDiagnostics.ActiveWebsite), "Active website"),
            (nameof(BrowserDiagnostics.MatchingState), "Website matching"),
            (nameof(BrowserDiagnostics.Details), "Details")
        })
        {
            var row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new Label { Text = item.Caption, AutoSize = true, ForeColor = Navy, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 9, 10, 9) }, 0, row);
            var value = new Label { Text = "Checking…", AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(70, 84, 107), Margin = new Padding(0, 9, 0, 9), MaximumSize = new Size(430, 0) };
            fields.Controls.Add(value, 1, row);
            values[item.Key] = value;
        }
        // Keep long status messages within the visible column when the dialog is resized.
        fields.SizeChanged += (_, _) =>
        {
            var width = Math.Max(180, fields.ClientSize.Width - 165 - fields.Padding.Horizontal - 8);
            foreach (var value in values.Values) value.MaximumSize = new Size(width, 0);
        };
        scroll.Controls.Add(fields);
        layout.Controls.Add(scroll, 0, 1);

        var tips = new Label
        {
            Text = "Connect: Browser setup → copy pairing token → extension options → Save and test connection.\n" +
                "Check that the extension is loaded in the browser profile you use and its Browser dropdown matches Edge or Chrome.\n" +
                "For private-mode flags, reload the updated extension and enable Allow in InPrivate / Incognito in its Details. Private page details are never reported.\n" +
                "Focus a normal website or private window, then select Today in the timeline. Use the Private browsing filter for flags.",
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 9, 24, 4),
            ForeColor = Color.FromArgb(82, 96, 115),
            Font = new Font("Segoe UI", 9)
        };
        layout.Controls.Add(tips, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(16, 9, 16, 0), BackColor = Color.White };
        var close = new Button { Text = "Close", AutoSize = true, Height = 33, DialogResult = DialogResult.Cancel };
        var copy = new Button { Text = "Copy diagnostics", AutoSize = true, Height = 33 };
        var refresh = new Button { Text = "Refresh", AutoSize = true, Height = 33, BackColor = Color.FromArgb(29, 79, 216), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        refresh.Click += (_, _) => RefreshDiagnostics();
        copy.Click += (_, _) => CopyDiagnostics();
        actions.Controls.Add(close);
        actions.Controls.Add(copy);
        actions.Controls.Add(refresh);
        actions.Controls.Add(updated);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
        CancelButton = close;
        refreshTimer.Tick += (_, _) => RefreshDiagnostics();
        Shown += (_, _) => { RefreshDiagnostics(); refreshTimer.Start(); };
        FormClosed += (_, _) => refreshTimer.Stop();
    }

    private void RefreshDiagnostics()
    {
        try
        {
            latest = read();
            values[nameof(BrowserDiagnostics.DesktopState)].Text = latest.DesktopState;
            values[nameof(BrowserDiagnostics.ForegroundApplication)].Text = latest.ForegroundApplication;
            values[nameof(BrowserDiagnostics.EdgeConnection)].Text = latest.EdgeConnection;
            values[nameof(BrowserDiagnostics.ChromeConnection)].Text = latest.ChromeConnection;
            values[nameof(BrowserDiagnostics.ActiveWebsite)].Text = latest.ActiveWebsite;
            values[nameof(BrowserDiagnostics.MatchingState)].Text = latest.MatchingState;
            values[nameof(BrowserDiagnostics.Details)].Text = latest.Details;
            updated.Text = "Updated " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch
        {
            // Callback exceptions may contain paths or raw browser metadata; keep them out of diagnostics.
            updated.Text = "Refresh unavailable";
            values[nameof(BrowserDiagnostics.Details)].Text = "Could not refresh status. Try Refresh or reopen this dialog.";
            latest = null;
        }
    }

    private void CopyDiagnostics()
    {
        RefreshDiagnostics();
        if (latest is null) return;
        var text = "Activity Ledger browser diagnostics · version " + typeof(BrowserStatusDialog).Assembly.GetName().Version?.ToString(3) + "\r\n" +
            "Desktop logger: " + latest.DesktopState + "\r\n" +
            "Foreground app: " + latest.ForegroundApplication + "\r\n" +
            "Microsoft Edge: " + latest.EdgeConnection + "\r\n" +
            "Google Chrome: " + latest.ChromeConnection + "\r\n" +
            "Active website: " + latest.ActiveWebsite + "\r\n" +
            "Website matching: " + latest.MatchingState + "\r\n" +
            "Details: " + latest.Details;
        try { Clipboard.SetText(text); updated.Text = "Diagnostics copied"; }
        catch (System.Runtime.InteropServices.ExternalException) { updated.Text = "Clipboard busy; try again"; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) refreshTimer.Dispose();
        base.Dispose(disposing);
    }
}
