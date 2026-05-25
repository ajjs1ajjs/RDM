using System.Security.Cryptography;
using System.Text;
using IOException = System.IO.IOException;

namespace RemoteManager.Services;

internal static class CredentialManager
{
    private const string LegacyCredentialDir = "RemoteManager.credentials";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RemoteManager.Credentials.v2");
    private static readonly object SyncRoot = new();

    internal static string CredentialDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RemoteManager",
            "credentials");

    public static void Save(Guid connectionId, string password)
    {
        if (connectionId == Guid.Empty)
            throw new ArgumentException("Connection id cannot be empty.", nameof(connectionId));

        lock (SyncRoot)
        {
            Directory.CreateDirectory(CredentialDir);
            var path = GetCredentialPath(connectionId);

            if (string.IsNullOrEmpty(password))
            {
                Delete(connectionId);
                return;
            }

            var plainText = Encoding.UTF8.GetBytes(password);
            var protectedData = ProtectedData.Protect(plainText, Entropy, DataProtectionScope.CurrentUser);
            WriteAtomic(path, protectedData);
        }
        SettingsService.Instance?.BackupData();
    }

    public static string? Load(Guid connectionId)
    {
        if (connectionId == Guid.Empty)
            return null;

        lock (SyncRoot)
        {
            var path = GetCredentialPath(connectionId);
            if (!File.Exists(path))
                return TryLoadLegacyCredential(connectionId);

            try
            {
                var protectedData = File.ReadAllBytes(path);
                var plainText = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainText);
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public static void Delete(Guid connectionId)
    {
        if (connectionId == Guid.Empty)
            return;

        lock (SyncRoot)
        {
            DeleteIfExists(GetCredentialPath(connectionId));
            DeleteIfExists(Path.Combine(CredentialDir, $"{connectionId:N}_passphrase.bin"));
            DeleteIfExists(Path.Combine(CredentialDir, $"{connectionId:N}_jumphost_password.bin"));
            DeleteIfExists(GetLegacyCredentialPath(connectionId));
            DeleteIfExists(Path.Combine(LegacyCredentialDir, $"{connectionId:N}_key.bin"));
        }
        SettingsService.Instance?.BackupData();
    }

    public static void SaveAdditional(Guid connectionId, string key, string value)
    {
        if (connectionId == Guid.Empty)
            throw new ArgumentException("Connection id cannot be empty.", nameof(connectionId));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty.", nameof(key));

        lock (SyncRoot)
        {
            Directory.CreateDirectory(CredentialDir);
            var pathCorrect = Path.Combine(CredentialDir, $"{connectionId:N}_{key}.bin");

            if (string.IsNullOrEmpty(value))
            {
                DeleteIfExists(pathCorrect);
                return;
            }

            var plainText = Encoding.UTF8.GetBytes(value);
            var protectedData = ProtectedData.Protect(plainText, Entropy, DataProtectionScope.CurrentUser);
            WriteAtomic(pathCorrect, protectedData);
        }
        SettingsService.Instance?.BackupData();
    }

    public static string? LoadAdditional(Guid connectionId, string key)
    {
        if (connectionId == Guid.Empty || string.IsNullOrWhiteSpace(key))
            return null;

        lock (SyncRoot)
        {
            var path = Path.Combine(CredentialDir, $"{connectionId:N}_{key}.bin");
            if (!File.Exists(path))
                return null;

            try
            {
                var protectedData = File.ReadAllBytes(path);
                var plainText = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainText);
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public static void DeleteAdditional(Guid connectionId, string key)
    {
        if (connectionId == Guid.Empty || string.IsNullOrWhiteSpace(key))
            return;

        lock (SyncRoot)
        {
            var path = Path.Combine(CredentialDir, $"{connectionId:N}_{key}.bin");
            DeleteIfExists(path);
        }
        SettingsService.Instance?.BackupData();
    }

    private static string GetDomainCredentialKey(string domain) =>
        $"domain_{domain.ToLowerInvariant()}";

    public static void SaveDomainCredential(string domain, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain cannot be empty.", nameof(domain));

        lock (SyncRoot)
        {
            Directory.CreateDirectory(CredentialDir);
            var key = GetDomainCredentialKey(domain);
            var path = Path.Combine(CredentialDir, $"{key}.bin");

            var payload = Encoding.UTF8.GetBytes($"{username}|{password}");
            var protectedData = ProtectedData.Protect(payload, Entropy, DataProtectionScope.CurrentUser);
            WriteAtomic(path, protectedData);
        }
        SettingsService.Instance?.BackupData();
    }

    public static (string Username, string Password)? LoadDomainCredential(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        lock (SyncRoot)
        {
            var key = GetDomainCredentialKey(domain);
            var path = Path.Combine(CredentialDir, $"{key}.bin");
            if (!File.Exists(path))
                return null;

            try
            {
                var protectedData = File.ReadAllBytes(path);
                var plainText = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
                var parts = Encoding.UTF8.GetString(plainText).Split(new[] { '|' }, 2);
                return parts.Length == 2 ? (parts[0], parts[1]) : null;
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public static void DeleteDomainCredential(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return;

        lock (SyncRoot)
        {
            var key = GetDomainCredentialKey(domain);
            var path = Path.Combine(CredentialDir, $"{key}.bin");
            DeleteIfExists(path);
        }
        SettingsService.Instance?.BackupData();
    }

    private static string GetCredentialPath(Guid connectionId) =>
        Path.Combine(CredentialDir, $"{connectionId:N}.bin");

    private static string GetLegacyCredentialPath(Guid connectionId) =>
        Path.Combine(LegacyCredentialDir, connectionId.ToString("N"));

    private static string? TryLoadLegacyCredential(Guid connectionId)
    {
        var legacyPath = GetLegacyCredentialPath(connectionId);
        if (!File.Exists(legacyPath))
            return null;

        try
        {
            var data = File.ReadAllBytes(legacyPath);
            var plainText = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            var password = Encoding.UTF8.GetString(plainText);
            Save(connectionId, password);
            return password;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(tempPath, bytes);
        try
        {
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Ignore cleanup error
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
