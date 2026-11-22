using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActivityLedger.Core;
namespace ActivityLedger.Cloud;
public sealed record CloudRecord(string Id,DateTimeOffset StartUtc,DateTimeOffset EndUtc,string Kind,string ProcessName,string Title,string Url,string Domain);
public sealed class CloudSyncService : IDisposable
{
    private readonly ActivityStore store;
    private readonly HttpClient http;
    private readonly object gate=new();
    private readonly List<CancellationTokenSource> cancellationSources=[];
    private CloudSettings configuration;
    private CancellationTokenSource cancellation=new();
    private bool busy,pending,recording,disposed;
    private string status="Cloud sync is off";
    private static readonly JsonSerializerOptions JsonOptions=new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase };
    public CloudSyncService(ActivityStore store,CloudSettings configuration,HttpMessageHandler? transport=null)
    {
        this.store=store;this.configuration=configuration.Copy();
        http=new(transport??new HttpClientHandler{AllowAutoRedirect=false}) { Timeout=TimeSpan.FromSeconds(45) };
        cancellationSources.Add(cancellation);status=configuration.Enabled?"Waiting for first sync":"Cloud sync is off";
    }
    public CloudSettings Settings {get {lock(gate)return configuration.Copy();}}
    public string Status {get {lock(gate)return status;}}
    private void SetStatus(string value){lock(gate)status=value;}
    public void Configure(CloudSettings settings,bool isRecording)
    {
        lock(gate)
        {
            cancellation.Cancel();cancellation=new();cancellationSources.Add(cancellation);
            configuration=settings.Copy();status=settings.Enabled?"Waiting for sync":"Cloud sync is off; last uploaded history remains online";
        }
        Schedule(isRecording);
    }
    public void Schedule(bool isRecording)
    {
        lock(gate)
        {
            recording=isRecording;
            if(disposed||!configuration.Enabled||!configuration.IsPaired)return;
            pending=true;if(busy)return;busy=true;
        }
        _=Task.Run(Run);
    }
    private async Task Run()
    {
        while(true)
        {
            CloudSettings config;CancellationToken token;bool currentRecording;
            lock(gate)
            {
                if(disposed||!configuration.Enabled||!pending){busy=false;return;}
                pending=false;config=configuration.Copy();token=cancellation.Token;currentRecording=recording;
            }
            try{await Sync(config,currentRecording,token).ConfigureAwait(false);}
            catch(OperationCanceledException){ }
            catch(Exception){if(!token.IsCancellationRequested)SetStatus("Sync failed. Local recording continues; check your connection or import a new upload key.");}
        }
    }
    private async Task<T> Request<T>(CloudSettings config,string path,HttpMethod method,object? body,CancellationToken token)
    {
        using var request=new HttpRequestMessage(method,CloudSettings.DashboardOrigin+path);
        request.Headers.Add("OAI-Sites-Authorization","Bearer "+config.ServiceToken);
        request.Headers.Authorization=new("Bearer",config.DeviceToken);
        if(body is not null)request.Content=JsonContent.Create(body,options:JsonOptions);
        using var response=await http.SendAsync(request,token).ConfigureAwait(false);
        if(!response.IsSuccessStatusCode)throw new IOException("Cloud request rejected: "+(int)response.StatusCode);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions,token).ConfigureAwait(false)??throw new IOException("Empty response");
    }
    public static CloudRecord Project(ActivityRecord r)=>new(r.Id,r.StartUtc,r.EndUtc,r.Kind,r.ProcessName,
        r.Kind=="private"?(PrivacyRules.NormalizeProcess(r.ProcessName).Equals("msedge",StringComparison.OrdinalIgnoreCase)?"Private browsing (Edge InPrivate)":"Private browsing (Chrome Incognito)"):r.Title,
        r.Kind=="private"?"":r.Url,r.Kind=="private"?"":r.Domain);
    private async Task Sync(CloudSettings config,bool isRecording,CancellationToken token)
    {
        SetStatus("Syncing selected history…");
        var days=Math.Clamp(config.Days,1,90);var now=DateTimeOffset.UtcNow;
        var records=store.Query(now.AddDays(-days),now.AddSeconds(1),limit:500001);
        if(records.Count>500000){SetStatus("More than 500,000 sessions selected. Choose fewer days; no partial snapshot was uploaded.");return;}
        var known=await Request<UploadState>(config,"/api/upload-state",HttpMethod.Get,null,token).ConfigureAwait(false);
        var existing=new HashSet<string>(known.Hashes??[]);var hashes=new List<string>();
        // UTC-day and ID-prefix partitions let unchanged history reuse its existing cloud chunks.
        foreach(var group in records.Select(Project).GroupBy(r=>r.StartUtc.UtcDateTime.ToString("yyyy-MM-dd")+"/"+r.Id[..2]).OrderBy(g=>g.Key,StringComparer.Ordinal))
        {
            foreach(var chunk in group.OrderBy(r=>r.Id,StringComparer.Ordinal).Chunk(300))
            {
                token.ThrowIfCancellationRequested();
                foreach(var serialized in SerializeChunks(chunk))
                {
                var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant();
                hashes.Add(hash);
                if(!existing.Contains(hash))await Request<JsonElement>(config,"/api/chunk",HttpMethod.Post,new{hash,recordsJson=serialized},token).ConfigureAwait(false);
                }
            }
        }
        if(hashes.Count>10000)throw new IOException("Too many history chunks selected.");
        await Request<JsonElement>(config,"/api/commit",HttpMethod.Post,new{hashes,count=records.Count,recording=isRecording,retentionDays=days},token).ConfigureAwait(false);
        SetStatus($"Synced {records.Count:N0} sessions at {DateTime.Now:HH:mm:ss}; last {days} days");
    }
    public static IEnumerable<string> SerializeChunks(CloudRecord[] records)
    {
        var value=JsonSerializer.Serialize(records,JsonOptions);
        if(Encoding.UTF8.GetByteCount(value)<=500000){yield return value;yield break;}
        if(records.Length<=1)throw new IOException("A session exceeds the upload size limit.");
        foreach(var part in SerializeChunks(records[..(records.Length/2)]))yield return part;
        foreach(var part in SerializeChunks(records[(records.Length/2)..]))yield return part;
    }
    private sealed class UploadState {public string[]? Hashes{get;set;}}
    public void Dispose()
    {
        lock(gate){disposed=true;foreach(var c in cancellationSources)c.Cancel();}
        http.Dispose();
    }
}
