using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ActivityLedger.Browser;
using ActivityLedger.Core;
using ActivityLedger.Cloud;
using ActivityLedger.Installation;
using ActivityLedger.Native;
using ActivityLedger.Tracking;
using Microsoft.Win32;

namespace ActivityLedger.UI;

public sealed class MainForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 33, 61);
    private static readonly Color Muted = Color.FromArgb(99, 113, 137);
    private static readonly Color Mint = Color.FromArgb(102, 222, 195);
    private readonly SettingsStore settingsStore;
    private AppSettings settings;
    private readonly ActivityStore store;
    private readonly ActivitySessionTracker tracker;
    private readonly PasswordStore passwords;
    private readonly CloudSettingsStore cloudSettingsStore;
    private readonly CloudSyncService cloudSync;
    private string? cloudStartupWarning;
    private int cloudTicks;
    private readonly WindowsActivitySensor sensor = new();
    private readonly BrowserBridge bridge;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly NotifyIcon tray;
    private readonly Button toggle = new();
    private readonly Label recording = new();
    private readonly Label footer = new();
    private readonly Label headline = new();
    private readonly Label subhead = new();
    private readonly Label[] metrics = new Label[5];
    private readonly DateTimePicker from = new() { Format = DateTimePickerFormat.Short, Width = 120 };
    private readonly DateTimePicker to = new() { Format = DateTimePickerFormat.Short, Width = 120 };
    private readonly TextBox search = new() { PlaceholderText = "Search apps, sites, titles…", Width = 230 };
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly DataGridView grid = new();
    private readonly ToolStripMenuItem trayToggle;
    private readonly List<Button> navigation = [];
    private bool logging;
    private bool exiting;
    private bool ready;
    private bool refreshing;
    private string? recordingFault;
    private string? startupFailure;
    private int ticks;
    private string view = "Timeline";
    private string liveState = "Recording foreground activity";
    private DateOnly lastRetention;
    private DateTime lastToday = DateTime.Today;
    private BrowserObservation? lastBrowserObservation;
    private sealed record BrowserObservation(string Browser, DateTimeOffset Time, string Domain, string Match, string Detail, bool IsPrivate = false);

    public MainForm(bool startLogging)
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        settingsStore = new(Path.Combine(SettingsStore.DataDirectory, "settings.json"));
        settings = settingsStore.Load();
        passwords = new(Path.Combine(SettingsStore.DataDirectory, "password.json"));
        settings.StartWithWindows = true;
        settings.MinimizeToTray = true;
        settingsStore.Save(settings);
        try { RegisterStartup(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            startupFailure = error.Message;
        }
        store = new(Path.Combine(SettingsStore.DataDirectory, "activity.db"));
        tracker = new(store.Upsert);
        cloudSettingsStore = new(Path.Combine(SettingsStore.DataDirectory, "cloud-settings.dat"));
        CloudSettings cloudSettings;
        try { cloudSettings = cloudSettingsStore.Load(); }
        catch (Exception) { cloudSettings = new(); cloudStartupWarning = "Saved cloud credentials could not be read. Cloud sync is off; local recording continues. Import a new pairing file in Cloud sync to reconnect."; }
        cloudSync = new(store, cloudSettings);
        bridge = new(settings.BridgeToken, settings.BridgePort);
        bridge.Start();

        Text = "Activity Ledger " + typeof(MainForm).Assembly.GetName().Version?.ToString(3);
        Size = new(1280, 820);
        MinimumSize = new(1060, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 251);
        Font = new("Segoe UI", 10);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        BuildLayout();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Activity Ledger", null, (_, _) => RestoreWindow());
        menu.Items.Add("Browser status", null, (_, _) => ShowBrowserStatus());
        menu.Items.Add("Cloud sync", null, (_, _) => OpenCloudSync());
        menu.Items.Add("Open data folder", null, (_, _) => { if (Authorize("open the tracker data folder")) Process.Start(new ProcessStartInfo(SettingsStore.DataDirectory) { UseShellExecute = true }); });
        menu.Items.Add("How it works", null, (_, _) => ShowHelp());
        menu.Items.Add("Set up this computer", null, (_, _) => ShowWelcome());
        trayToggle = (ToolStripMenuItem)menu.Items.Add("Pause logging (password)", null, (_, _) => RequestToggle());
        menu.Items.Add("Change password", null, (_, _) => ChangePassword());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        tray = new() { Icon = Icon, Text = "Activity Ledger — paused", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => RestoreWindow();

        from.Value = DateTime.Today;
        to.Value = DateTime.Today;
        kind.Items.AddRange(["All activity", "Apps", "Websites", "Idle", "Private browsing"]);
        kind.SelectedIndex = 0;
        from.ValueChanged += (_, _) => RefreshHistory();
        to.ValueChanged += (_, _) => RefreshHistory();
        kind.SelectedIndexChanged += (_, _) =>
        {
            if (kind.SelectedIndex == 4) view = "Timeline";
            RefreshHistory();
        };
        search.TextChanged += (_, _) => { ticks = 4; };
        timer.Tick += (_, _) => Tick();
        FormClosing += OnClosing;
        ready = true;
        ApplyRetention();
        RefreshHistory();
        // Every launch begins recording; an authenticated pause is never persisted to the next launch.
        SetLogging(true);
        timer.Start();
        Shown += (_, _) =>
        {
            if (!passwords.IsConfigured)
            {
                PasswordDialog.CreatePassword(this, passwords);
                UpdateStatus();
            }
            if (startupFailure is not null)
                MessageBox.Show(this, "Recording is running now, but Windows sign-in startup could not be registered:\n" + startupFailure +
                    "\nOpen the tracker manually after sign-in until this Windows permission issue is resolved.", "Startup needs attention", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (cloudStartupWarning is not null) MessageBox.Show(this, cloudStartupWarning, "Cloud sync needs attention");
            cloudSync.Schedule(logging && recordingFault is null);
            if (!startLogging && !settings.WelcomeCompleted)
            {
                ShowWelcome();
                settings.WelcomeCompleted = true;
                try { settingsStore.Save(settings); }
                catch (IOException) { /* The guide may reappear; recording can continue. */ }
            }
            // Keep the journal visible on manual launches; sign-in launches use the tray.
            if (startLogging && passwords.IsConfigured) Hide();
        };
    }

    private void BuildLayout()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new(SizeType.Absolute, 208));
        shell.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(shell);

        var side = new Panel { Dock = DockStyle.Fill, BackColor = Navy, Padding = new(20) };
        shell.Controls.Add(side, 0, 0);
        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10 };
        sidebar.ColumnStyles.Add(new(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new(SizeType.Absolute, 100));
        for (int i = 1; i <= 3; i++) sidebar.RowStyles.Add(new(SizeType.Absolute, 52));
        sidebar.RowStyles.Add(new(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new(SizeType.Absolute, 50));
        sidebar.RowStyles.Add(new(SizeType.Absolute, 50));
        sidebar.RowStyles.Add(new(SizeType.Absolute, 50));
        sidebar.RowStyles.Add(new(SizeType.Absolute, 64));
        sidebar.RowStyles.Add(new(SizeType.Absolute, 56));
        side.Controls.Add(sidebar);
        sidebar.Controls.Add(new Label { Text = "ACTIVITY\nLEDGER", ForeColor = Color.White, Font = new("Segoe UI", 20, FontStyle.Bold), AutoSize = true }, 0, 0);
        int row = 1;
        foreach (var name in new[] { "Timeline", "Applications", "Websites" })
        {
            var button = MakeButton(name, Color.FromArgb(36, 51, 80), Color.White);
            button.Dock = DockStyle.Fill;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Click += (_, _) =>
            {
                view = name;
                // A saved Apps-only filter must not hide every row after switching to Websites.
                kind.SelectedIndex = name == "Websites" ? 2 : 0;
                RefreshHistory();
            };
            navigation.Add(button);
            sidebar.Controls.Add(button, 0, row++);
        }
        var browser = MakeButton("Browser setup", Navy, Color.White);
        browser.Dock = DockStyle.Fill;
        browser.Click += (_, _) => { if (Authorize("open browser pairing")) new BrowserSetupDialog(settings, ReadBrowserDiagnostics).ShowDialog(this); };
        sidebar.Controls.Add(browser, 0, 5);
        var options = MakeButton("Settings", Navy, Color.White);
        options.Dock = DockStyle.Fill;
        options.Click += (_, _) => OpenSettings();
        sidebar.Controls.Add(options, 0, 6);
        var data = MakeButton("Cloud sync", Navy, Color.White);
        data.Dock = DockStyle.Fill;
        data.Click += (_, _) => OpenCloudSync();
        sidebar.Controls.Add(data, 0, 7);
        recording.ForeColor = Mint;
        recording.Dock = DockStyle.Fill;
        recording.Font = new("Segoe UI", 9);
        recording.TextAlign = ContentAlignment.MiddleLeft;
        sidebar.Controls.Add(recording, 0, 8);
        toggle.Text = "Start logging";
        StyleButton(toggle, Mint, Navy);
        toggle.Font = new("Segoe UI", 10, FontStyle.Bold);
        toggle.Dock = DockStyle.Fill;
        toggle.Click += (_, _) => RequestToggle();
        sidebar.Controls.Add(toggle, 0, 9);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new(28, 22, 28, 16) };
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.Absolute, 46));
        content.RowStyles.Add(new(SizeType.Absolute, 38));
        content.RowStyles.Add(new(SizeType.Absolute, 100));
        content.RowStyles.Add(new(SizeType.Absolute, 64));
        content.RowStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.Absolute, 55));
        content.RowStyles.Add(new(SizeType.Absolute, 42));
        shell.Controls.Add(content, 1, 0);
        headline.Text = "Your day, in detail.";
        headline.Font = new("Segoe UI", 25, FontStyle.Bold);
        headline.ForeColor = Navy;
        headline.AutoSize = true;
        content.Controls.Add(headline, 0, 0);
        subhead.Text = "Recording automatically. A password is required to pause or quit.";
        subhead.ForeColor = Muted;
        subhead.AutoSize = true;
        content.Controls.Add(subhead, 0, 1);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new(0, 0, 0, 8) };
        string[] labels = ["ACTIVE TIME", "WEBSITE TIME", "SESSIONS", "IDLE TIME", "PRIVATE TIME"];
        for (int i = 0; i < metrics.Length; i++)
        {
            cards.ColumnStyles.Add(new(SizeType.Percent, 20));
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new(i == 0 ? 0 : 8, 0, 0, 0), Padding = new(14, 12, 10, 10) };
            metrics[i] = new() { Text = "—", Font = new("Segoe UI", 22, FontStyle.Bold), ForeColor = Navy, Dock = DockStyle.Fill };
            var label = new Label { Text = labels[i], Font = new("Segoe UI", 8, FontStyle.Bold), ForeColor = Muted, Dock = DockStyle.Top, Height = 23 };
            card.Controls.Add(metrics[i]);
            card.Controls.Add(label);
            cards.Controls.Add(card, i, 0);
        }
        content.Controls.Add(cards, 0, 2);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new(0, 12, 0, 0), Margin = Padding.Empty };
        filters.Controls.Add(new Label { Text = "From", AutoSize = true, Margin = new(0, 6, 4, 0), ForeColor = Muted });
        filters.Controls.Add(from);
        filters.Controls.Add(new Label { Text = "to", AutoSize = true, Margin = new(4, 6, 4, 0), ForeColor = Muted });
        filters.Controls.Add(to);
        var today = MakeButton("Today", Color.White, Navy);
        today.Size = new(64, 31);
        today.Click += (_, _) => { from.Value = DateTime.Today; to.Value = DateTime.Today; };
        filters.Controls.Add(today);
        filters.Controls.Add(kind);
        filters.Controls.Add(search);
        content.Controls.Add(filters, 0, 3);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new() { BackColor = Color.FromArgb(233, 238, 246), ForeColor = Navy, Font = new("Segoe UI", 9, FontStyle.Bold), Padding = new(8) };
        grid.ColumnHeadersHeight = 42;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.DefaultCellStyle = new() { ForeColor = Navy, BackColor = Color.White, SelectionBackColor = Color.FromArgb(217, 244, 237), SelectionForeColor = Navy, Padding = new(6), Font = new("Segoe UI", 9) };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
        grid.RowTemplate.Height = 36;
        grid.CellFormatting += (_, e) =>
        {
            if (grid.Columns[e.ColumnIndex].Name == "Duration" && e.Value is double seconds)
            {
                e.Value = Duration(seconds);
                e.FormattingApplied = true;
            }
            if (grid.Columns[e.ColumnIndex].Name == "Type" && e.Value is string value && value == "private")
            {
                e.Value = "Private browsing";
                e.CellStyle!.ForeColor = Color.FromArgb(102, 48, 153);
                e.CellStyle.BackColor = Color.FromArgb(245, 236, 255);
                e.FormattingApplied = true;
            }
        };
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowDetails(e.RowIndex); };
        content.Controls.Add(grid, 0, 4);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(0, 10, 0, 0), Margin = Padding.Empty, WrapContents = false };
        foreach (var format in new[] { "CSV", "JSON" })
        {
            var export = MakeButton("Export " + format, Color.White, Navy);
            export.Size = new(120, 34);
            export.Click += (_, _) => Export(format);
            actions.Controls.Add(export);
        }
        var clear = MakeButton("Delete history…", Color.White, Color.FromArgb(160, 55, 60));
        clear.Size = new(142, 34);
        clear.Click += (_, _) => DeleteHistory();
        actions.Controls.Add(clear);
        var help = MakeButton("Browser status", Color.White, Navy);
        help.Size = new(125, 34);
        help.Click += (_, _) => ShowBrowserStatus();
        actions.Controls.Add(help);
        var password = MakeButton("Change password", Color.White, Navy);
        password.Size = new(155, 34);
        password.Click += (_, _) => ChangePassword();
        actions.Controls.Add(password);
        content.Controls.Add(actions, 0, 5);
        footer.ForeColor = Muted;
        footer.Font = new("Segoe UI", 9);
        footer.Dock = DockStyle.Fill;
        content.Controls.Add(footer, 0, 6);
    }

    private static Button MakeButton(string text, Color background, Color foreground)
    {
        var result = new Button { Text = text, Height = 36, Margin = new(0, 3, 7, 4) };
        StyleButton(result, background, foreground);
        return result;
    }

    private static void StyleButton(Button button, Color background, Color foreground)
    {
        button.BackColor = background;
        button.ForeColor = foreground;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
    }

    private void Tick()
    {
        try
        {
            var today = DateTime.Today;
            if (DateRangeFollowPolicy.ShouldAdvance(from.Value, to.Value, lastToday, today))
            {
                from.Value = today;
                to.Value = today;
            }
            lastToday = today;
            if (++cloudTicks >= 60)
            {
                cloudTicks = 0;
                cloudSync.Schedule(logging && recordingFault is null);
            }
            if (logging)
            {
                var raw = sensor.Sample();
                var sample = CreateSample(raw);
                tracker.Observe(sample, raw.CapturedAtUtc);
                liveState = raw.SessionLocked ? "Screen locked — not logging" : sample is null ? "Excluded or unavailable" :
                    sample.Kind == "idle" ? "Idle" : sample.Kind == "private" ? "Private browsing active — page details are not saved" :
                    sample.Kind == "website" ? sample.Domain : sample.ProcessName;
            }
            if (++ticks >= 5)
            {
                ticks = 0;
                tracker.Flush(DateTimeOffset.UtcNow);
                ApplyRetention();
                RefreshHistory();
                // Clear a prior persistence error only after a successful checkpoint and refresh.
                recordingFault = null;
            }
            UpdateStatus();
        }
        catch (Exception ex)
        {
            // Keep the requested recording state and retry; disk/OS failures remain visible.
            // Software cannot promise successful recording when Windows or storage is unavailable.
            if (recordingFault is null)
                tray.ShowBalloonTip(5000, "Activity Ledger needs attention", "Recording encountered an error and is retrying: " + ex.Message, ToolTipIcon.Error);
            recordingFault = ex.Message;
            UpdateStatus();
        }
    }

    private ActivitySample? CreateSample(WindowSample raw)
    {
        var snapshot = bridge.GetCurrent(raw.ProcessName);
        var sample = ActivityClassifier.Classify(raw, snapshot, settings, Environment.ProcessId);
        if (WindowsActivitySensor.GetSupportedBrowser(raw.ProcessName) is { } browser)
            ObserveBrowser(raw, snapshot, sample, browser);
        return sample;
    }

    private void ObserveBrowser(WindowSample raw, BrowserSnapshot? snapshot, ActivitySample? sample, string browser)
    {
        string domain = "";
        string match;
        string detail;
        if (raw.SessionLocked)
        {
            match = "Screen locked";
            detail = "Locked-screen time is not assigned to websites.";
        }
        else if (raw.IdleSeconds >= settings.IdleThresholdSeconds)
        {
            match = "Idle threshold reached";
            detail = "Activity is currently recorded as idle; move the mouse or use the keyboard while browsing.";
        }
        else if (snapshot?.IsPrivate == true)
        {
            // A private-mode flag has no page title or URL to match. Never inspect its native caption.
            if (sample?.Kind == "private")
            {
                match = "Private browsing active";
                detail = "Only the active private-mode interval is recorded. Page addresses, page titles and contents are not saved. Find these entries in Timeline using the Private browsing filter.";
            }
            else if (sample is null)
            {
                match = "Private browser excluded in Settings";
                detail = "The browser process exclusion also suppresses private-mode flags.";
            }
            else
            {
                match = "Private-mode report is no longer fresh";
                detail = "Waiting for an updated focused-window report; only browser app usage is counted meanwhile.";
            }
        }
        else if (snapshot is null)
        {
            match = "No fresh active-tab report for this browser";
            detail = "For websites, use an HTTP/HTTPS tab in the profile containing the extension. For private-mode flags, enable Allow in InPrivate / Incognito in that extension's Details. Check its selected Browser and live delivery status.";
            domain = "";
        }
        else if (!BrowserTitleMatcher.Matches(raw.ProcessName, raw.WindowTitle, snapshot.Title))
        {
            match = "Tab/window titles did not match";
            detail = "The extension is delivering a page, but it does not match the focused window. Switch to another tab and back. Only browser app usage is counted until they match.";
            detail += $" Caption length: {raw.WindowTitle.Length}; reported title length: {snapshot.Title.Length}; caption contains reported title: " +
                (raw.WindowTitle.Contains(snapshot.Title, StringComparison.Ordinal) ? "yes." : "no.");
            domain = "";
        }
        else if (sample is null)
        {
            match = "Activity excluded in Settings";
            detail = "Review the app and website exclusion lists with your management password.";
            domain = "";
        }
        else if (sample.Kind == "website")
        {
            domain = sample.Domain;
            match = "Website recognized";
            detail = "This site is eligible for the website timeline. While recording, allow five seconds for a database checkpoint, select today's dates, and clear search filters.";
        }
        else
        {
            match = "Browser app usage only";
            detail = "The report could not be used as an active website. Private page details and browser settings pages are excluded; private-mode flags require the updated companion and its private-window permission.";
            domain = "";
        }
        // Retain safe diagnostic text only, never native browser titles, tokens or full URLs.
        lastBrowserObservation = new(browser, raw.CapturedAtUtc, domain, match, detail, sample?.Kind == "private");
    }

    private BrowserDiagnostics ReadBrowserDiagnostics()
    {
        var raw = sensor.Sample();
        if (WindowsActivitySensor.GetSupportedBrowser(raw.ProcessName) is { } browser)
        {
            var snapshot = bridge.GetCurrent(raw.ProcessName);
            ObserveBrowser(raw, snapshot, ActivityClassifier.Classify(raw, snapshot, settings, Environment.ProcessId), browser);
        }
        var statuses = bridge.GetStatuses();
        string Describe(string name)
        {
            var status = statuses.FirstOrDefault(s => s.Browser == name);
            if (status?.LastReportUtc is not { } time) return "No website reports received. A successful connection test alone is not a website report.";
            var age = Math.Max(0, (DateTimeOffset.UtcNow - time).TotalSeconds);
            return $"{status.State} · Last activity report {age:0}s ago · {status.Instances} profile(s) · " +
                (status.HasCurrentSnapshot ? status.State == "private" ? "Private-mode flag available; no page details" : "Fresh active tab available" : "No fresh active report");
        }
        var observed = lastBrowserObservation;
        var state = !logging ? "Paused with password" : recordingFault is not null ? "Recording error; check the main window" : "Recording";
        var focus = raw.ProcessName + (observed is null ? "" : $" · Last browser: {observed.Browser} at {observed.Time.LocalDateTime:HH:mm:ss}");
        var detail = observed?.Detail ?? "Leave this window open, switch to Edge, and browse a normal website for ten seconds. Return here to see the last browser result.";
        if (observed?.Browser == "msedge" && statuses.FirstOrDefault(s => s.Browser == "msedge")?.HasCurrentSnapshot != true &&
            statuses.Any(s => s.Browser == "chrome" && s.HasCurrentSnapshot))
            detail = "Reports labelled Chrome are arriving while you use Edge. In the extension options select Microsoft Edge, then Save and test connection.";
        if (bridge.Error is { Length: > 0 }) detail = "Browser bridge unavailable. Check the main window and whether the configured local port is already in use.";
        else if (observed is not null && statuses.Any(s => s.Browser == observed.Browser && s.State == "private-permission-required"))
            detail = "Private-mode flags need the browser's permission. Open the companion's Details at edge://extensions or chrome://extensions and enable Allow in InPrivate / Allow in Incognito for this profile.";
        detail += $"\nTimeline range: {from.Value:yyyy-MM-dd} to {to.Value:yyyy-MM-dd}; filter: {kind.Text}; search: {(search.TextLength > 0 ? "active (clear it to show all sites)" : "empty")}.";
        return new(state, focus, Describe("msedge"), Describe("chrome"),
            observed?.IsPrivate == true ? "Private browsing — no page details saved" : string.IsNullOrEmpty(observed?.Domain) ? "No matched website yet" : observed.Domain,
            observed is null ? "Waiting for a browser observation" : observed.Match + $" (observed {observed.Time.LocalDateTime:HH:mm:ss})", detail);
    }

    private void ShowBrowserStatus()
    {
        using var dialog = new BrowserStatusDialog(ReadBrowserDiagnostics);
        dialog.ShowDialog(this);
    }

    private void OpenCloudSync()
    {
        RestoreWindow();
        if (!Authorize("configure optional cloud sync")) return;
        using var dialog = new CloudSyncDialog(cloudSync);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            cloudSettingsStore.Save(dialog.Result);
            cloudSync.Configure(dialog.Result, logging && recordingFault is null);
            cloudStartupWarning = null;
            UpdateStatus();
        }
        catch (Exception) { MessageBox.Show(this, "Cloud settings could not be encrypted and saved. Local recording continues; your previous cloud settings remain in effect.", "Cloud sync"); }
    }

    private void ShowWelcome()
    {
        RestoreWindow();
        using var guide = new WelcomeDialog(
            () => { if (Authorize("set up website tracking")) new BrowserSetupDialog(settings, ReadBrowserDiagnostics).ShowDialog(this); },
            OpenCloudSync);
        guide.ShowDialog(this);
    }

    private void SetLogging(bool value)
    {
        if (!value) tracker.Stop(DateTimeOffset.UtcNow);
        logging = value;
        recordingFault = null;
        liveState = value ? "Recording foreground activity" : "Paused with password — sign-in or relaunch resumes recording";
        UpdateStatus();
        RefreshHistory();
    }

    private void UpdateStatus()
    {
        toggle.Text = logging ? "Pause logging" : "Start logging";
        trayToggle.Text = toggle.Text + " (password)";
        recording.Text = (recordingFault is not null ? "● RECORDING ERROR" : logging ? "● RECORDING" : "● PAUSED") +
            "\n" + (passwords.IsConfigured ? "Controls locked by password" : "Set your password to lock controls");
        recording.ForeColor = recordingFault is not null ? Color.FromArgb(255, 183, 123) : logging ? Mint : Color.FromArgb(185, 198, 220);
        var status = "Activity Ledger — " + (recordingFault is not null ? "recording error; retrying" : logging ? "recording (locked)" : "paused");
        tray.Text = status;
        subhead.Text = (recordingFault is not null ? "Recording error — retrying: " + recordingFault : liveState) +
            (startupFailure is not null ? " · Sign-in startup needs attention" : "") +
            (bridge.Error is { Length: > 0 } error ? " · Browser bridge: " + error : "") +
            (cloudSync.Settings.Enabled ? " · Cloud sync enabled" : " · Cloud sync off");
    }

    private bool Authorize(string action)
    {
        if (!passwords.IsConfigured)
        {
            var created = PasswordDialog.CreatePassword(this, passwords);
            UpdateStatus();
            return created;
        }
        return PasswordDialog.ConfirmPassword(this, passwords, action);
    }

    private void RequestToggle()
    {
        if (!Authorize(logging ? "pause recording" : "resume recording")) return;
        SetLogging(!logging);
    }

    private void ChangePassword()
    {
        if (!passwords.IsConfigured) PasswordDialog.CreatePassword(this, passwords);
        else PasswordDialog.ChangePassword(this, passwords);
        UpdateStatus();
    }

    private static void RegisterStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key.SetValue("ActivityLedger", '"' + Environment.ProcessPath! + "\" --start-logging");
    }

    private (DateTimeOffset Start, DateTimeOffset End) Range()
    {
        var first = new DateTimeOffset(DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Local));
        var last = new DateTimeOffset(DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Local));
        return (first.ToUniversalTime(), last.ToUniversalTime());
    }

    private IReadOnlyList<ActivityRecord> ReadSelected()
    {
        var range = Range();
        var selectedKind = kind.SelectedIndex switch { 1 => "app", 2 => "website", 3 => "idle", 4 => "private", _ => "" };
        return store.Query(range.Start, range.End, search.Text.Trim(), selectedKind, int.MaxValue);
    }

    private void RefreshHistory()
    {
        if (!ready || refreshing) return;
        refreshing = true;
        try
        {
            var scrollRow = grid.FirstDisplayedScrollingRowIndex;
            var selectedRow = grid.CurrentRow?.Index ?? -1;
            var sortColumn = grid.SortedColumn?.Name;
            var sortDirection = grid.SortOrder;
            var rows = ReadSelected();
            metrics[0].Text = Duration(rows.Where(r => r.Kind != "idle").Sum(r => r.DurationSeconds));
            metrics[1].Text = Duration(rows.Where(r => r.Kind == "website").Sum(r => r.DurationSeconds));
            metrics[2].Text = rows.Count.ToString("N0");
            metrics[3].Text = Duration(rows.Where(r => r.Kind == "idle").Sum(r => r.DurationSeconds));
            metrics[4].Text = Duration(rows.Where(r => r.Kind == "private").Sum(r => r.DurationSeconds));
            foreach (var button in navigation) button.BackColor = button.Text == view ? Color.FromArgb(49, 72, 104) : Color.FromArgb(36, 51, 80);
            headline.Text = view switch { "Applications" => "Every app. Every session.", "Websites" => "Where you spend time online.", _ => "Your day, in detail." };
            var table = new DataTable();
            int displayed;
            if (view == "Timeline")
            {
                table.Columns.Add("Started", typeof(DateTime));
                table.Columns.Add("Ended", typeof(DateTime));
                table.Columns.Add("Duration", typeof(double));
                table.Columns.Add("Type");
                table.Columns.Add("Application");
                table.Columns.Add("Website");
                table.Columns.Add("Title");
                table.Columns.Add("Address");
                foreach (var r in rows.Take(10000)) table.Rows.Add(r.StartUtc.LocalDateTime, r.EndUtc.LocalDateTime, r.DurationSeconds,
                    r.Kind, r.ProcessName, r.Domain, r.Title, r.Url);
                displayed = table.Rows.Count;
            }
            else
            {
                bool sites = view == "Websites";
                table.Columns.Add(sites ? "Website" : "Application");
                table.Columns.Add("Duration", typeof(double));
                table.Columns.Add("Sessions", typeof(int));
                table.Columns.Add("Share");
                var groups = rows.Where(r => sites ? r.Kind == "website" : r.Kind != "idle")
                    .GroupBy(r => sites ? r.Domain : r.ProcessName)
                    .Select(g => new { Label = g.Key, Seconds = g.Sum(r => r.DurationSeconds), Sessions = g.Count() })
                    .OrderByDescending(g => g.Seconds).ToArray();
                var total = groups.Sum(g => g.Seconds);
                foreach (var g in groups) table.Rows.Add(g.Label, g.Seconds, g.Sessions, total > 0 ? (g.Seconds / total).ToString("P1") : "0%");
                displayed = groups.Length;
            }
            grid.DataSource = table;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.ValueType == typeof(DateTime)) { column.DefaultCellStyle.Format = "MMM dd HH:mm:ss"; column.FillWeight = 125; }
                if (column.Name == "Duration") column.FillWeight = 80;
                if (column.Name == "Type") column.FillWeight = 110;
                if (column.Name == "Address") column.Visible = false;
            }
            if (sortColumn is not null && grid.Columns.Contains(sortColumn) && sortDirection != SortOrder.None)
                grid.Sort(grid.Columns[sortColumn]!, sortDirection == SortOrder.Ascending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending);
            if (selectedRow >= 0 && selectedRow < grid.Rows.Count) grid.Rows[selectedRow].Selected = true;
            if (scrollRow >= 0 && scrollRow < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = scrollRow;
            footer.Text = from.Value.Date > to.Value.Date ? "Choose an end date on or after the start date." :
                rows.Count == 0 ? "No sessions in this range. Recording starts automatically; switch to another app to record a session." :
                $"{displayed:N0} rows · {rows.Count:N0} matching sessions · Local times · Double-click a row for details" +
                (view == "Timeline" && rows.Count > 10000 ? " · Showing newest 10,000; export includes all matches" : "");
        }
        finally { refreshing = false; }
    }

    private void ShowDetails(int row)
    {
        var parts = grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText + ": " +
            (c.Name == "Duration" && grid.Rows[row].Cells[c.Index].Value is double seconds ? Duration(seconds) : grid.Rows[row].Cells[c.Index].Value?.ToString()));
        MessageBox.Show(this, string.Join("\n\n", parts), "Session details", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Export(string format)
    {
        if (!Authorize("export recorded history")) return;
        tracker.Flush(DateTimeOffset.UtcNow);
        using var dialog = new SaveFileDialog { Filter = format == "CSV" ? "CSV files|*.csv" : "JSON files|*.json", FileName = "ActivityLedger-" + DateTime.Now.ToString("yyyy-MM-dd") + "." + format.ToLowerInvariant(), OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (ExportPathPolicy.IsProtectedDestination(dialog.FileName, SettingsStore.DataDirectory,
                InstallationManager.InstalledExecutable, Environment.ProcessPath!))
        {
            MessageBox.Show(this, "Choose a location outside Activity Ledger's data and installation folders. Exports cannot overwrite the tracker, its password, or its journal.",
                "Choose another export location", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var rows = ReadSelected();
        try
        {
            if (format == "CSV") ActivityStore.ExportCsv(rows, dialog.FileName);
            else File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            footer.Text = $"Exported {rows.Count:N0} sessions to {dialog.FileName}";
        }
        catch (Exception e) { MessageBox.Show(this, "Export failed: " + e.Message, "Export"); }
    }

    private void OpenSettings()
    {
        if (!Authorize("change recording settings")) return;
        using var dialog = new SettingsDialog(settings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        tracker.Stop(DateTimeOffset.UtcNow);
        var next = dialog.Result;
        try
        {
            next.StartWithWindows = true;
            next.MinimizeToTray = true;
            RegisterStartup();
            startupFailure = null;
            settingsStore.Save(next);
            settings = next;
            lastRetention = default;
            ApplyRetention();
            RefreshHistory();
        }
        catch (Exception e) { MessageBox.Show(this, "Could not save settings: " + e.Message, "Settings"); }
    }

    private void ApplyRetention()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (lastRetention == today) return;
        if (settings.RetentionDays > 0) store.DeleteBefore(DateTimeOffset.UtcNow.AddDays(-settings.RetentionDays));
        lastRetention = today;
    }

    private void DeleteHistory()
    {
        if (!Authorize("delete recorded history")) return;
        if (MessageBox.Show(this, "Delete ALL recorded history permanently?\nExport anything you want to keep first. Your settings will remain.", "Delete all history", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        // End the current row so deletion cannot resurrect it at the next checkpoint.
        // Preserve recording state: clearing history is not a request to pause collection.
        tracker.Stop(DateTimeOffset.UtcNow);
        store.DeleteAll();
        RefreshHistory();
    }

    private void ShowHelp() => MessageBox.Show(this,
        "1. Recording starts automatically on launch and at Windows sign-in.\n2. Set your password locally. It protects pause/resume, quit, settings, and uninstall.\n3. Open Browser setup to connect Chrome or Edge.\n4. Select dates, search, and export CSV/JSON.\n\n" +
        "Only the foreground app or active browser site receives time; website time is already included in app totals. Collection samples about once per second and cannot reconstruct earlier activity.\n\n" +
        "Idle time starts after the configured inactivity threshold. Locked screens, sleep gaps, and excluded activities are not counted as active time. Activity Ledger itself is recorded under its app name.\n\n" +
        "Private-mode flags: update the companion, Reload it, and enable Allow in InPrivate / Allow in Incognito in its browser Details. The timeline then shows when a private window was active and for how long; private page addresses, titles and contents are not saved. Choose Timeline and the Private browsing filter. Flags follow the same idle/exclusion rules, are approximate, and cannot recover past or background private-window activity.\n\n" +
        "Closing the window keeps logging in the tray. Pause, resume, and Quit require your password. Windows shutdown ends this session; recording resumes at your next sign-in.\n\n" +
        "This is an application control lock. A Windows user with sufficient process or file permissions can still stop or remove software outside its normal controls. Sleep, shutdown, storage failures, and other Windows accounts cannot be covered by this single-user tracker.", "How Activity Ledger works", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            tray.ShowBalloonTip(2500, "Activity Ledger is in your tray", logging ? "Logging continues. Pause or quit from the tray with your password." : "Logging is paused with your password. Reopen from the tray to resume.", ToolTipIcon.Info);
            return;
        }
        if (!exiting && e.CloseReason != CloseReason.WindowsShutDown && !Authorize("quit Activity Ledger"))
        {
            e.Cancel = true;
            return;
        }
        timer.Stop();
        try { tracker.Stop(DateTimeOffset.UtcNow); }
        finally { cloudSync.Dispose(); bridge.Dispose(); tray.Visible = false; tray.Dispose(); store.Dispose(); }
    }

    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void Quit()
    {
        if (!Authorize("quit Activity Ledger and stop recording")) return;
        exiting = true;
        Close();
    }
    private static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s" : $"{(int)t.TotalSeconds}s";
    }
}
