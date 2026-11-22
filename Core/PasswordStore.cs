using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActivityLedger.Core;

/// <summary>
/// A local application password. This gates application controls; it does not encrypt history
/// or protect the credential file from somebody who can modify the current Windows user's files.
/// </summary>
public sealed class PasswordStore
{
    public const int MinimumPasswordLength = 10;
    public const int MaximumPasswordLength = 1024;
    private const int CurrentVersion = 1;
    private const int DerivationRounds = 600_000;
    private const int SaltBytes = 32;
    private const int HashBytes = 32;
    private const long MaximumDocumentBytes = 16_384;
    private readonly object gate = new();
    private readonly string path;
    private Credential? credential;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public PasswordStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        credential = Load();
    }

    public bool IsConfigured
    {
        get { lock (gate) return credential is not null; }
    }

    /// <summary>Creates a password once. Existing credentials cannot be replaced through this method.</summary>
    public void Create(string password)
    {
        lock (gate)
        {
            if (credential is not null || File.Exists(path))
                throw new InvalidOperationException("An application password is already configured. Use Change with the current password.");
            ValidateNewPassword(password);
            var next = DeriveNew(password);
            Save(next, replaceExisting: false);
            credential = next;
        }
    }

    public bool Verify(string password)
    {
        lock (gate) return VerifyInternal(password);
    }

    /// <summary>Replaces a password only after verifying the current password. There is no reset operation.</summary>
    public void Change(string current, string newPassword)
    {
        lock (gate)
        {
            if (!VerifyInternal(current))
                throw new UnauthorizedAccessException("The current password is incorrect.");
            ValidateNewPassword(newPassword);
            var next = DeriveNew(newPassword);
            Save(next, replaceExisting: true);
            credential = next;
        }
    }

    private bool VerifyInternal(string? password)
    {
        if (credential is null || password is null ||
            password.Length < MinimumPasswordLength || password.Length > MaximumPasswordLength)
            return false;
        var candidate = Rfc2898DeriveBytes.Pbkdf2(password, credential.Salt,
            DerivationRounds, HashAlgorithmName.SHA256, HashBytes);
        try { return CryptographicOperations.FixedTimeEquals(candidate, credential.Hash); }
        finally { CryptographicOperations.ZeroMemory(candidate); }
    }

    private static void ValidateNewPassword(string? password)
    {
        if (password is null || password.Length < MinimumPasswordLength || password.Length > MaximumPasswordLength)
            throw new ArgumentException($"Use a password between {MinimumPasswordLength} and {MaximumPasswordLength} characters.", nameof(password));
    }

    private static Credential DeriveNew(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return new(salt, Rfc2898DeriveBytes.Pbkdf2(password, salt, DerivationRounds, HashAlgorithmName.SHA256, HashBytes));
    }

    private Credential? Load()
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumDocumentBytes)
                throw new InvalidDataException("The password file exceeds the supported size.");
            var document = JsonSerializer.Deserialize<PasswordDocument>(stream, JsonOptions)
                ?? throw new InvalidDataException("The password file is empty.");
            if (document.Version != CurrentVersion)
                throw new InvalidDataException("The password file uses an unsupported version.");
            if (document.Rounds != DerivationRounds)
                throw new InvalidDataException("The password file contains unsupported password derivation settings.");
            var salt = Convert.FromBase64String(document.Salt);
            var hash = Convert.FromBase64String(document.Hash);
            if (salt.Length != SaltBytes || hash.Length != HashBytes)
                throw new InvalidDataException("The password file contains invalid credential lengths.");
            return new(salt, hash);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidDataException or ArgumentNullException)
        {
            throw new InvalidDataException("The application password file is invalid: " + path +
                ". Restore the original password file to continue. It has not been reset.", error);
        }
    }

    private void Save(Credential next, bool replaceExisting)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        // A unique adjacent temporary file keeps the final rename on the same volume.
        var temporary = Path.Combine(directory, ".password-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, new PasswordDocument
                {
                    Version = CurrentVersion,
                    Rounds = DerivationRounds,
                    Salt = Convert.ToBase64String(next.Salt),
                    Hash = Convert.ToBase64String(next.Hash)
                }, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: replaceExisting);
        }
        finally
        {
            // A failure before the final rename leaves the previous password intact.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record Credential(byte[] Salt, byte[] Hash);

    private sealed class PasswordDocument
    {
        public required int Version { get; init; }
        public required int Rounds { get; init; }
        public required string Salt { get; init; }
        public required string Hash { get; init; }
    }
}
