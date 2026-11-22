using ActivityLedger.Core;
int checks=0;
void Check(string? path,bool expected){if(SetupLaunchPolicy.IsSetupExecutable(path)!=expected)throw new Exception("Wrong setup entry mode: "+path);checks++;}
Check("ActivityLedgerSetup.exe",true);
Check("ActivityLedgerSetup (1).exe",true);
Check("ActivityLedgerSetup (2) (1).exe",true);
Check("ActivityLedgerSetup-v2.0.0-Windows-x64.exe",true);
Check("ActivityLedgerSetup-v2.0.0-Windows-x64 (1).exe",true);
Check("ActivityLedgerSetup-v2.0.0-Windows-arm64.exe",true);
Check(@"C:\Users\A & B\Downloads\ActivityLedgerSetup-v2.0.0-Windows-arm64 (2).exe",true);
Check("/tmp/ActivityLedgerSetup-v2.0.0-Windows-x64.exe",true);
Check("activityledgersetup_V2.exe",true);
Check(@"C:\Users\Me\Programs\ActivityLedger\ActivityLedger.exe",false);
Check("ActivityLedger.exe",false);
Check("NotActivityLedgerSetup.exe",false);
Check("ActivityLedgerSetupAssistant.exe",false);
Check(null,false);Check(" ",false);
Console.WriteLine($"Setup launch checks passed: {checks} assertions.");
