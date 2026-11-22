using ActivityLedger.Cloud;using ActivityLedger.Core;using System.Net;using System.Text;using System.Text.Json;using System.Security.Cryptography;
int count=0;void Check(bool v,string message){if(!v)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
var config=new CloudSettings{Enabled=true,DeviceId=new('a',32),DeviceToken=new('b',64),ServiceToken="fake-service-credential-0123456789",Days=30};
Check(!new CloudSettings().Enabled,"cloud sync defaults off");
var privateRecord=new ActivityRecord(new string('a',32),DateTimeOffset.UtcNow.AddMinutes(-2),DateTimeOffset.UtcNow.AddMinutes(-1),"private","msedge.exe","PRIVATE_TITLE_SENTINEL","https://PRIVATE_URL_SENTINEL","PRIVATE_DOMAIN_SENTINEL");
var projection=CloudSyncService.Project(privateRecord);Check(projection.Title=="Private browsing (Edge InPrivate)"&&projection.Url==""&&projection.Domain=="","private projection removes every private page detail");
var big=Enumerable.Range(0,300).Select(i=>projection with{Id=i.ToString("x32"),Kind="app",Title=new string('語',4096)}).ToArray();var chunks=CloudSyncService.SerializeChunks(big).ToArray();Check(chunks.Length>1&&chunks.All(x=>Encoding.UTF8.GetByteCount(x)<=500000),"Unicode-heavy metadata is split into bounded requests");Check(chunks.Sum(x=>JsonDocument.Parse(x).RootElement.GetArrayLength())==300,"chunk splitting preserves all sessions");
using var store=new ActivityStore(":memory:");store.Upsert(privateRecord);var normal=new ActivityRecord(new string('c',32),DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddMinutes(-4),"website","msedge.exe","Normal title","https://example.com","example.com");store.Upsert(normal);
var handler=new Transport(config);using var service=new CloudSyncService(store,config,handler);service.Schedule(true);
async Task Until(Func<bool> condition){for(int i=0;i<100;i++){if(condition())return;await Task.Delay(50);}throw new Exception("Timed out: "+service.Status);}
await Until(()=>service.Status.StartsWith("Synced"));Check(handler.CommitCount==1&&handler.UploadCount==2,"enabled sync uploads a complete snapshot and commits it");Check(!string.Join("",handler.Uploaded).Contains("SENTINEL"),"no private URL, title or domain enters upload bodies");Check(handler.SessionCount==2,"snapshot commit reports its full session count");
service.Schedule(true);await Until(()=>handler.CommitCount==2);Check(handler.UploadCount==2,"unchanged history reuses existing content chunks");
var disabled=config.Copy();disabled.Enabled=false;service.Configure(disabled,true);service.Schedule(true);await Task.Delay(100);Check(handler.CommitCount==2&&service.Status.Contains("off"),"disabling prevents later upload attempts");
var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{File.WriteAllText(path,JsonSerializer.Serialize(new{version=1,origin=CloudSettings.DashboardOrigin,deviceId=config.DeviceId,deviceToken=config.DeviceToken,serviceToken=config.ServiceToken}));Check(CloudSettings.Import(path).IsPaired,"valid pairing file is accepted without enabling sync");File.WriteAllText(path,File.ReadAllText(path).Replace(CloudSettings.DashboardOrigin,"https://attacker.example"));bool rejected=false;try{CloudSettings.Import(path);}catch(InvalidDataException){rejected=true;}Check(rejected,"pairing file cannot redirect uploads to another server");}finally{File.Delete(path);}
Console.WriteLine($"All {count} cloud sync checks passed.");
sealed class Transport(CloudSettings expected):HttpMessageHandler
{
 public int UploadCount,CommitCount,SessionCount;public List<string> Uploaded=[];private HashSet<string> hashes=[];
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
 {
  if(request.RequestUri!.GetLeftPart(UriPartial.Authority)!=CloudSettings.DashboardOrigin)throw new Exception("Unexpected host");
  if(request.Headers.Authorization?.Parameter!=expected.DeviceToken||request.Headers.GetValues("OAI-Sites-Authorization").Single()!="Bearer "+expected.ServiceToken)throw new Exception("Missing scoped credentials");
  object response=new{ok=true};var path=request.RequestUri.AbsolutePath;
  if(path=="/api/upload-state")response=new{hashes=hashes.ToArray()};
  else{using var d=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var b=d.RootElement;if(path=="/api/chunk"){var text=b.GetProperty("recordsJson").GetString()!;var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();if(hash!=b.GetProperty("hash").GetString())throw new Exception("Checksum");Uploaded.Add(text);hashes.Add(hash);UploadCount++;}else if(path=="/api/commit"){SessionCount=b.GetProperty("count").GetInt32();hashes=b.GetProperty("hashes").EnumerateArray().Select(x=>x.GetString()!).ToHashSet();CommitCount++;}else throw new Exception("Unexpected endpoint");}
  return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(response),Encoding.UTF8,"application/json")};
 }
}
