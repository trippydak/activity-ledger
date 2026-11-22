using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ActivityLedger.Cloud;
public sealed class CloudSettings
{
    public const string DashboardOrigin = "https://activity-ledger-cloud.realtrippydak.chatgpt.site";
    public bool Enabled { get; set; }
    public int Days { get; set; } = 30;
    public string DeviceId { get; set; } = "";
    public string DeviceToken { get; set; } = "";
    public string ServiceToken { get; set; } = "";
    public bool IsPaired => DeviceId.Length == 32 && DeviceToken.Length == 64 && ServiceToken.Length > 0;
    public CloudSettings Copy() => new() { Enabled=Enabled, Days=Days, DeviceId=DeviceId, DeviceToken=DeviceToken, ServiceToken=ServiceToken };
    public static CloudSettings Import(string path)
    {
        if (new FileInfo(path).Length > 32768) throw new InvalidDataException("The pairing file is too large.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root=document.RootElement;
        if(root.GetProperty("version").GetInt32()!=1 || root.GetProperty("origin").GetString()?.TrimEnd('/')!=DashboardOrigin)
            throw new InvalidDataException("This pairing file belongs to a different dashboard.");
        var s=new CloudSettings { DeviceId=root.GetProperty("deviceId").GetString()??"", DeviceToken=root.GetProperty("deviceToken").GetString()??"", ServiceToken=root.GetProperty("serviceToken").GetString()??"" };
        if(!System.Text.RegularExpressions.Regex.IsMatch(s.DeviceId,"^[a-f0-9]{32}$")||!System.Text.RegularExpressions.Regex.IsMatch(s.DeviceToken,"^[a-f0-9]{64}$")||s.ServiceToken.Length is <24 or >16000||s.ServiceToken.Any(char.IsControl))
            throw new InvalidDataException("The pairing credentials are invalid.");
        return s;
    }
}
public sealed class CloudSettingsStore(string path)
{
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("ActivityLedger.Cloud.v1");
    public CloudSettings Load()
    {
        if(!File.Exists(path))return new();
        var bytes=ProtectedData.Unprotect(File.ReadAllBytes(path),Entropy,DataProtectionScope.CurrentUser);
        try{return JsonSerializer.Deserialize<CloudSettings>(bytes)??new();}
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public void Save(CloudSettings settings)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(settings);
        try
        {
            var encrypted=ProtectedData.Protect(bytes,Entropy,DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path+".tmp",encrypted);File.Move(path+".tmp",path,true);
        }
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }
}
