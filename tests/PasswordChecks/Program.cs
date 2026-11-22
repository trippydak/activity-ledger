using System.Text.Json;
using System.Text.Json.Nodes;
using ActivityLedger.Core;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}
void Throws<T>(Action action, string name) where T : Exception
{
    var threw = false;
    try { action(); } catch (T) { threw = true; }
    Check(threw, name);
}

const string firstPassword = "correct horse battery staple";
const string secondPassword = "a different strong passphrase";
var folder = Path.Combine(Path.GetTempPath(), "ActivityLedger-password-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var path = Path.Combine(folder, "password.json");
    var store = new PasswordStore(path);
    Check(!store.IsConfigured && !store.Verify(firstPassword), "missing file means unconfigured and cannot authenticate");
    Throws<ArgumentException>(() => store.Create(""), "empty password rejected");
    Throws<ArgumentException>(() => store.Create("ninechars"), "passwords below ten characters rejected");
    Throws<ArgumentException>(() => store.Create(new string('a', 1025)), "passwords above 1024 characters rejected");
    Check(!store.IsConfigured && !File.Exists(path), "invalid creation never creates a credential file");

    var staleStore = new PasswordStore(path);
    store.Create(firstPassword);
    Check(store.IsConfigured && store.Verify(firstPassword), "created password authenticates");
    Check(!store.Verify("incorrect password") && !store.Verify("") && !store.Verify(null!), "wrong and invalid passwords fail");
    var firstDocument = File.ReadAllText(path);
    Check(!firstDocument.Contains(firstPassword), "plaintext password is not written to disk");
    using (var json = JsonDocument.Parse(firstDocument))
    {
        Check(json.RootElement.GetProperty("version").GetInt32() == 1 &&
            json.RootElement.GetProperty("rounds").GetInt32() == 600_000,
            "persisted format versions password derivation settings");
        Check(Convert.FromBase64String(json.RootElement.GetProperty("salt").GetString()!).Length == 32 &&
            Convert.FromBase64String(json.RootElement.GetProperty("hash").GetString()!).Length == 32,
            "salt and derived hash are each 32 bytes");
    }
    Check(new PasswordStore(path).Verify(firstPassword), "password persists after reopening");
    Throws<InvalidOperationException>(() => store.Create(secondPassword), "Create cannot overwrite a configured password");
    Throws<InvalidOperationException>(() => staleStore.Create(secondPassword), "a stale unconfigured instance cannot overwrite a password");
    Check(File.ReadAllText(path) == firstDocument, "rejected creates preserve credential file byte for byte");

    Throws<UnauthorizedAccessException>(() => store.Change("wrong current password", secondPassword), "changing password requires correct current password");
    Throws<ArgumentException>(() => store.Change(firstPassword, "short"), "new password rules also apply to changes");
    Check(File.ReadAllText(path) == firstDocument && store.Verify(firstPassword), "rejected changes preserve the current password");
    store.Change(firstPassword, secondPassword);
    Check(store.Verify(secondPassword) && !store.Verify(firstPassword), "successful change replaces old authentication");
    Check(new PasswordStore(path).Verify(secondPassword), "changed password is persisted atomically");
    Check(Directory.GetFiles(folder, ".password-*.tmp").Length == 0, "successful writes leave no temporary password files");

    // Force the final atomic rename to fail. In-memory authentication must change only after commit.
    var original = path + ".original";
    File.Move(path, original);
    Directory.CreateDirectory(path);
    Throws<IOException>(() => store.Change(secondPassword, firstPassword), "failed atomic rename reports the storage failure");
    Check(store.Verify(secondPassword) && !store.Verify(firstPassword), "failed persistence keeps the previous in-memory password");
    Check(Directory.GetFiles(folder, ".password-*.tmp").Length == 0, "failed writes clean temporary password files");
    Directory.Delete(path);
    File.Move(original, path);
    Check(new PasswordStore(path).Verify(secondPassword), "previous persisted credential survives failed update");

    var secondPath = Path.Combine(folder, "second.json");
    new PasswordStore(secondPath).Create(secondPassword);
    Check(File.ReadAllText(secondPath) != File.ReadAllText(path), "same password gets a fresh random salt");

    var invalidPath = Path.Combine(folder, "invalid.json");
    void RejectDocument(string document, string name)
    {
        File.WriteAllText(invalidPath, document);
        Throws<InvalidDataException>(() => _ = new PasswordStore(invalidPath), name);
    }
    RejectDocument("{broken", "malformed JSON fails closed");
    RejectDocument("{}", "missing credential properties fail closed");
    RejectDocument("null", "null password document fails closed");
    RejectDocument(firstDocument.Replace("\"version\": 1", "\"version\": 2"), "future credential versions fail closed");
    RejectDocument(firstDocument.Replace("600000", "1"), "lowered password derivation rounds fail closed");
    var invalidSalt = JsonNode.Parse(firstDocument)!;
    invalidSalt["salt"] = "AA==";
    RejectDocument(invalidSalt.ToJsonString(), "short salt fails closed");
    var invalidHash = JsonNode.Parse(firstDocument)!;
    invalidHash["hash"] = "not valid base64";
    RejectDocument(invalidHash.ToJsonString(), "malformed password hash fails closed");
    var nullHash = JsonNode.Parse(firstDocument)!;
    nullHash["hash"] = null;
    RejectDocument(nullHash.ToJsonString(), "null credential properties fail closed");
    RejectDocument(new string(' ', 16_385), "oversized credential files fail closed");
}
finally { Directory.Delete(folder, true); }

Console.WriteLine($"All {passed} password checks passed.");
