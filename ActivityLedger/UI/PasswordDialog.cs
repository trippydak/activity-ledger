using ActivityLedger.Core;

namespace ActivityLedger.UI;

/// <summary>Local password entry for protected Activity Ledger controls.</summary>
public sealed class PasswordDialog : Form
{
    private enum PasswordAction { Create, Confirm, Change }

    private readonly PasswordStore store;
    private readonly PasswordAction action;
    private readonly TextBox current = PasswordBox();
    private readonly TextBox password = PasswordBox();
    private readonly TextBox repeated = PasswordBox();
    private readonly Button submit;
    private readonly Label feedback;
    private readonly System.Windows.Forms.Timer retryTimer = new() { Interval = 1100 };
    private readonly string submitText;
    private int failedAttempts;

    public static bool CreatePassword(IWin32Window? owner, PasswordStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.IsConfigured) return false;
        using var dialog = new PasswordDialog(store, PasswordAction.Create, "");
        return Show(dialog, owner) == DialogResult.OK;
    }

    public static bool ConfirmPassword(IWin32Window? owner, PasswordStore store, string reason)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!store.IsConfigured) return false;
        using var dialog = new PasswordDialog(store, PasswordAction.Confirm, reason);
        return Show(dialog, owner) == DialogResult.OK;
    }

    public static bool ChangePassword(IWin32Window? owner, PasswordStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!store.IsConfigured) return false;
        using var dialog = new PasswordDialog(store, PasswordAction.Change, "");
        return Show(dialog, owner) == DialogResult.OK;
    }

    private static DialogResult Show(PasswordDialog dialog, IWin32Window? owner) =>
        owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);

    private static TextBox PasswordBox() => new()
    {
        UseSystemPasswordChar = true,
        MaxLength = 1024,
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 3, 0, 12)
    };

    private PasswordDialog(PasswordStore store, PasswordAction action, string reason)
    {
        this.store = store;
        this.action = action;
        Text = action switch
        {
            PasswordAction.Create => "Activity Ledger · Set password",
            PasswordAction.Change => "Activity Ledger · Change password",
            _ => "Activity Ledger · Confirm password"
        };
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 247, 251);
        ClientSize = new Size(540, action == PasswordAction.Confirm ? 350 : action == PasswordAction.Create ? 470 : 525);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = action == PasswordAction.Create;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.Controls.Add(new Label
        {
            Text = action switch
            {
                PasswordAction.Create => "Protect your logging controls",
                PasswordAction.Change => "Choose a new password",
                _ => "Confirm this action"
            },
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 33, 61),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            Padding = new Padding(24, 18, 10, 0)
        }, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(24, 17, 24, 5) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control) => body.Controls.Add(control);
        Label Note(string text) => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            ForeColor = Color.FromArgb(82, 96, 115),
            Margin = new Padding(0, 0, 0, 12)
        };
        void Field(string label, TextBox input)
        {
            Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 0) });
            Add(input);
        }
        if (action == PasswordAction.Create)
        {
            Add(Note("Use at least 10 characters. This password is required to pause or quit logging, change settings, or run the app's uninstaller."));
            Field("Password", password);
            Field("Confirm password", repeated);
            Add(Note("Keep this password somewhere safe. The app cannot display it or reset it."));
        }
        else if (action == PasswordAction.Change)
        {
            Add(Note("Enter your current password, then choose a new password of at least 10 characters."));
            Field("Current password", current);
            Field("New password", password);
            Field("Confirm new password", repeated);
        }
        else
        {
            Add(Note(string.IsNullOrWhiteSpace(reason) ? "Enter your password to continue." : reason));
            Field("Password", current);
        }

        var reveal = new CheckBox { Text = "Show password", AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        reveal.CheckedChanged += (_, _) =>
        {
            current.UseSystemPasswordChar = !reveal.Checked;
            password.UseSystemPasswordChar = !reveal.Checked;
            repeated.UseSystemPasswordChar = !reveal.Checked;
        };
        Add(reveal);
        feedback = new Label { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Color.FromArgb(176, 44, 44), Margin = new Padding(0), Text = " " };
        Add(feedback);
        layout.Controls.Add(body, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(18, 11, 18, 0), BackColor = Color.White };
        submitText = action switch { PasswordAction.Create => "Set password", PasswordAction.Change => "Change password", _ => "Continue" };
        submit = new Button { Text = submitText, AutoSize = true, Height = 34, BackColor = Color.FromArgb(29, 79, 216), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Height = 34, DialogResult = DialogResult.Cancel };
        submit.Click += Submit;
        buttons.Controls.Add(submit);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        AcceptButton = submit;
        CancelButton = cancel;

        retryTimer.Tick += (_, _) =>
        {
            retryTimer.Stop();
            submit.Enabled = true;
            submit.Text = submitText;
            current.Focus();
        };
        Shown += (_, _) => (action == PasswordAction.Create ? password : current).Focus();
    }

    private void Submit(object? sender, EventArgs e)
    {
        if (!submit.Enabled) return;
        feedback.Text = " ";
        if (action != PasswordAction.Confirm)
        {
            if (password.Text.Length < 10 || password.Text.Length > 1024)
            {
                feedback.Text = "Use between 10 and 1,024 characters for the password.";
                password.Focus();
                return;
            }
            if (!string.Equals(password.Text, repeated.Text, StringComparison.Ordinal))
            {
                feedback.Text = "The new passwords do not match.";
                repeated.Clear();
                repeated.Focus();
                return;
            }
        }

        try
        {
            switch (action)
            {
                case PasswordAction.Create:
                    store.Create(password.Text);
                    break;
                case PasswordAction.Change:
                    if (!store.Verify(current.Text))
                    {
                        RejectPassword();
                        return;
                    }
                    store.Change(current.Text, password.Text);
                    break;
                default:
                    if (!store.Verify(current.Text))
                    {
                        RejectPassword();
                        return;
                    }
                    break;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            feedback.Text = "Unable to complete this action: " + error.Message;
        }
    }

    private void RejectPassword()
    {
        failedAttempts++;
        current.Clear();
        if (failedAttempts >= 3)
        {
            MessageBox.Show(this, "The password was incorrect three times. The requested action has been cancelled.", "Password not confirmed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }
        feedback.Text = "Incorrect password. " + (3 - failedAttempts) + " attempts remaining.";
        submit.Enabled = false;
        submit.Text = "Please wait…";
        retryTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            retryTimer.Dispose();
            current.Clear();
            password.Clear();
            repeated.Clear();
        }
        base.Dispose(disposing);
    }
}
