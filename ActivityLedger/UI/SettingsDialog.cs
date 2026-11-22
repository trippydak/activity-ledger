using ActivityLedger.Core;

namespace ActivityLedger.UI;

public sealed class SettingsDialog : Form
{
    private readonly AppSettings original;
    private readonly NumericUpDown idle = new() { Minimum = 15, Maximum = 3600, Width = 95 };
    private readonly NumericUpDown retention = new() { Minimum = 0, Maximum = 3650, Width = 95 };
    private readonly CheckBox titles = new() { Text = "Record application window titles", AutoSize = true };
    private readonly CheckBox urls = new() { Text = "Record full website paths", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "Start logging when I sign in to Windows", AutoSize = true };
    private readonly CheckBox tray = new() { Text = "Keep logging in the system tray when I close the window", AutoSize = true };
    private readonly TextBox processes = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 74, Dock = DockStyle.Fill };
    private readonly TextBox domains = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 74, Dock = DockStyle.Fill };

    public AppSettings Result { get; private set; }

    public SettingsDialog(AppSettings settings)
    {
        original = settings;
        Result = settings;
        Text = "Activity Ledger · Settings";
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 247, 251);
        ClientSize = new Size(660, 680);
        MinimumSize = new Size(600, 660);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        idle.Value = Math.Clamp(settings.IdleThresholdSeconds, 15, 3600);
        retention.Value = Math.Clamp(settings.RetentionDays, 0, 3650);
        titles.Checked = settings.CaptureWindowTitles;
        urls.Checked = settings.CaptureFullUrls;
        startup.Checked = true;
        startup.Enabled = false;
        tray.Checked = true;
        tray.Enabled = false;
        processes.Lines = settings.ExcludedProcesses.ToArray();
        domains.Lines = settings.ExcludedDomains.ToArray();

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 73));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        var header = new Label { Text = "Make the log yours\nChoose what to keep and what to leave out.", Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 34, 59), ForeColor = Color.White, Padding = new Padding(24, 13, 10, 0), Font = new Font("Segoe UI", 12), AutoSize = false };
        outer.Controls.Add(header, 0, 0);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(22, 14, 22, 6) };
        var rows = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control) { control.Margin = new Padding(0, 0, 0, 7); rows.Controls.Add(control); }
        Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Color.FromArgb(82, 96, 115), Margin = new Padding(0, 0, 0, 10) };
        var timing = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        timing.Controls.Add(new Label { Text = "Mark idle after", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        timing.Controls.Add(idle);
        timing.Controls.Add(new Label { Text = "seconds without input", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        Add(timing);
        var keeping = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        keeping.Controls.Add(new Label { Text = "Keep history for", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        keeping.Controls.Add(retention);
        keeping.Controls.Add(new Label { Text = "days (0 = forever)", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        Add(keeping);
        Add(titles);
        Add(Note("Titles may include document names. Browser titles come only from normal tabs through the extension. Private-mode entries use a fixed label and never save private page titles."));
        Add(urls);
        Add(Note("By default, only website domains are saved. Full paths never include query strings, fragments, or credentials."));
        Add(startup);
        Add(tray);
        Add(Note("Automatic recording at sign-in stays enabled. Pause or quit from the tracker with your password. Closing the window keeps it in the tray."));
        Add(new Label { Text = "Excluded apps · one executable name per line", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        Add(processes);
        Add(Note("Example: passwordmanager.exe. Excluded activity creates a gap in the timeline."));
        Add(new Label { Text = "Excluded sites · one domain per line", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        Add(domains);
        Add(Note("Example: bank.example. Subdomains are excluded too. These settings apply to future activity."));
        scroll.Controls.Add(rows);
        outer.Controls.Add(scroll, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 10, 16, 0), BackColor = Color.White };
        var save = new Button { Text = "Save settings", AutoSize = true, Height = 34, BackColor = Color.FromArgb(29, 79, 216), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Height = 34, DialogResult = DialogResult.Cancel };
        save.Click += Save;
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        outer.Controls.Add(actions, 0, 2);
        Controls.Add(outer);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void Save(object? sender, EventArgs e)
    {
        var siteList = new List<string>();
        foreach (var entry in domains.Lines.Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            var value = entry.StartsWith("*.") ? entry[2..] : entry;
            if (!Uri.TryCreate(value.Contains("://") ? value : "https://" + value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrWhiteSpace(uri.Host) || uri.HostNameType == UriHostNameType.Unknown)
            {
                MessageBox.Show(this, "Enter a domain such as example.com, one per line.\nInvalid entry: " + entry, "Check excluded sites", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            siteList.Add(uri.IdnHost.TrimEnd('.').ToLowerInvariant());
        }
        Result = new AppSettings
        {
            IdleThresholdSeconds = (int)idle.Value,
            RetentionDays = (int)retention.Value,
            CaptureWindowTitles = titles.Checked,
            CaptureFullUrls = urls.Checked,
            StartWithWindows = true,
            MinimizeToTray = true,
            WelcomeCompleted = original.WelcomeCompleted,
            ExcludedProcesses = processes.Lines.Select(x => Path.GetFileName(x.Trim())).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ExcludedDomains = siteList.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            BridgeToken = original.BridgeToken,
            BridgePort = original.BridgePort
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
