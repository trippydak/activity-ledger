using ActivityLedger.Cloud;
using System.Diagnostics;
namespace ActivityLedger.UI;
public sealed class CloudSyncDialog:Form
{
    private CloudSettings draft;
    private readonly CheckBox enabled=new(){Text="Enable optional cloud sync",AutoSize=true};
    private readonly NumericUpDown days=new(){Minimum=1,Maximum=90,Value=30,Width=80};
    private readonly Label pairing=new(){AutoSize=true,MaximumSize=new(560,0)};
    private readonly Label status=new(){AutoSize=true,MaximumSize=new(560,0)};
    private readonly System.Windows.Forms.Timer timer=new(){Interval=1000};
    public CloudSettings Result{get;private set;}
    public CloudSyncDialog(CloudSyncService service)
    {
        draft=service.Settings;Result=draft;Text="Activity Ledger · Optional cloud sync";ClientSize=new(650,580);MinimumSize=new(610,560);Font=new("Segoe UI",10);StartPosition=FormStartPosition.CenterParent;BackColor=Color.FromArgb(245,247,251);MaximizeBox=false;MinimizeBox=false;
        enabled.Checked=draft.Enabled;days.Value=Math.Clamp(draft.Days,1,90);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};layout.RowStyles.Add(new(SizeType.Absolute,75));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,60));Controls.Add(layout);
        layout.Controls.Add(new Label{Text="Your ledger, available remotely\nCloud sync is optional. Local recording works independently.",Dock=DockStyle.Fill,BackColor=Color.FromArgb(20,33,61),ForeColor=Color.White,Padding=new(22,15,10,0),Font=new("Segoe UI",12)},0,0);
        var body=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new(24,15,24,10)};layout.Controls.Add(body,0,1);
        void Note(string text)=>body.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new(565,0),Margin=new(0,0,0,13),ForeColor=Color.FromArgb(74,91,115)});
        Note("1. Open your private dashboard, sign in with ChatGPT, and choose Connect a laptop. Download its pairing file.");
        var open=new Button{Text="Open cloud dashboard",AutoSize=true,Height=34};open.Click+=(_,_)=>Process.Start(new ProcessStartInfo(CloudSettings.DashboardOrigin){UseShellExecute=true});body.Controls.Add(open);
        Note("2. Import that file here. Each Windows installation should use its own pairing file.");
        var import=new Button{Text="Import pairing file",AutoSize=true,Height=34};import.Click+=(_,_)=>Import();body.Controls.Add(import);
        pairing.Text=draft.IsPaired?"A laptop upload key is configured.":"No laptop upload key imported.";body.Controls.Add(pairing);
        var period=new FlowLayoutPanel{AutoSize=true,Margin=new(0,14,0,14)};period.Controls.Add(new Label{Text="Upload the most recent",AutoSize=true,Padding=new(0,5,0,0)});period.Controls.Add(days);period.Controls.Add(new Label{Text="days of retained local history",AutoSize=true,Padding=new(0,5,0,0)});body.Controls.Add(period);
        body.Controls.Add(enabled);
        Note("When enabled, selected app/site history and private-mode flags are uploaded over HTTPS about once a minute. Private page addresses and titles remain excluded. Your management password is never uploaded. Pairing credentials are encrypted for your Windows account.");
        Note("Disabling sync stops future uploads; the last uploaded snapshot stays online. Local recording and its password controls are unchanged. Delete the downloaded pairing file after importing it, and keep your Windows account private.");
        status.Text=service.Status;body.Controls.Add(status);timer.Tick+=(_,_)=>status.Text=service.Status;Shown+=(_,_)=>timer.Start();
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new(15,10,15,0),BackColor=Color.White};var save=new Button{Text="Save cloud settings",AutoSize=true,Height=34};save.Click+=(_,_)=>Save();buttons.Controls.Add(save);buttons.Controls.Add(new Button{Text="Cancel",AutoSize=true,Height=34,DialogResult=DialogResult.Cancel});layout.Controls.Add(buttons,0,2);AcceptButton=save;
    }
    private void Import()
    {
        using var dialog=new OpenFileDialog{Filter="Pairing JSON|*.json|All files|*.*"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{var next=CloudSettings.Import(dialog.FileName);next.Days=(int)days.Value;draft=next;pairing.Text="Pairing file imported. Enable sync and save to start uploads.";}
        catch{MessageBox.Show(this,"This is not a valid pairing file for your Activity Ledger dashboard. Download a new file from Connect a laptop.","Pairing file");}
    }
    private void Save()
    {
        if(enabled.Checked&&!draft.IsPaired){MessageBox.Show(this,"Import a pairing file first.","Cloud sync");return;}
        draft.Enabled=enabled.Checked;draft.Days=(int)days.Value;Result=draft;DialogResult=DialogResult.OK;Close();
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
