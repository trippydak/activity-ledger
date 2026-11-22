using System.Diagnostics;
using ActivityLedger.Cloud;

namespace ActivityLedger.UI;

/// <summary>Visible, local onboarding. Recording continues while the setup guide is open.</summary>
public sealed class WelcomeDialog : Form
{
    public WelcomeDialog(Action browserSetup, Action cloudSetup)
    {
        Text = "Activity Ledger v2 · Set up this computer";
        ClientSize = new(700, 590);
        MinimumSize = new(640, 560);
        Font = new("Segoe UI", 10);
        BackColor = Color.FromArgb(245,247,251);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount=1, RowCount=3 };
        layout.RowStyles.Add(new(SizeType.Absolute,86));
        layout.RowStyles.Add(new(SizeType.Percent,100));
        layout.RowStyles.Add(new(SizeType.Absolute,60));
        layout.Controls.Add(new Label { Text="Activity Ledger v2\nSet up this Windows computer", BackColor=Color.FromArgb(20,33,61),ForeColor=Color.White,Font=new("Segoe UI",15,FontStyle.Bold),Dock=DockStyle.Fill,Padding=new(24,12,10,0) },0,0);
        var body = new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new(24,16,24,10) };
        void Note(string text) => body.Controls.Add(new Label { Text=text,AutoSize=true,MaximumSize=new(620,0),Margin=new(0,0,0,14),ForeColor=Color.FromArgb(62,82,107) });
        void ActionButton(string title,Action action)
        {
            var button=new Button { Text=title,AutoSize=true,Height=35,Margin=new(0,0,0,15) };
            button.Click+=(_,_)=>action();body.Controls.Add(button);
        }
        Note("App recording is already on. Closing the journal keeps it in the system tray. Pause, quit, settings and uninstall require this computer's management password.");
        Note("Website tracking: connect the bundled Chrome/Edge companion separately in every browser profile you use. Private-mode flags also need Allow in InPrivate / Incognito in the browser's extension Details.");
        ActionButton("Set up website tracking",browserSetup);
        Note("Optional remote viewing: sign in to your private dashboard and choose Connect a laptop. Give each computer a unique name and download a separate pairing file for it.");
        ActionButton("Open cloud dashboard",()=>Process.Start(new ProcessStartInfo(CloudSettings.DashboardOrigin){UseShellExecute=true}));
        ActionButton("Configure optional cloud sync",cloudSetup);
        Note("Use the same installer on other compatible Windows machines. Each has separate local history, management password and browser pairing. No password or cloud upload key is built into the installer. Do not copy another machine's encrypted cloud settings; pair each computer separately.");
        Note("You can reopen this guide from the tray menu: Set up this computer. Windows administrators and users with sufficient process/file permissions can still stop or remove the program outside its controls.");
        layout.Controls.Add(body,0,1);
        var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new(18,12,18,0),BackColor=Color.White };
        var done=new Button {Text="Done",AutoSize=true,Height=34,DialogResult=DialogResult.OK};actions.Controls.Add(done);layout.Controls.Add(actions,0,2);Controls.Add(layout);AcceptButton=done;CancelButton=done;
    }
}
