using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RemoteManager.Services;
using IOException = System.IO.IOException;

namespace RemoteManager.Services;

public class CredentialService : ICredentialService
{
    private const string LegacyCredentialDir = "RemoteManager.credentials";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RemoteManager.Credentials.v2");
    private readonly object _syncRoot = new();
    private readonly ISettingsService? _settings;

    public string CredentialDir { get; }

    public CredentialService(string? credentialDir = null, ISettingsService? settings = null)
    {
        _settings = settings;
        CredentialDir = credentialDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RemoteManager",
            "credentials");
    }

    public void Save(Guid connectionId, string password)
    {
        if (connectionId == Guid.Empty)
            throw new ArgumentException("Connection id cannot be empty.", nameof(connectionId));

        lock (_syncRoot)
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
        _settings?.BackupData();
    }

    public string? Load(Guid connectionId)
    {
        if (connectionId == Guid.Empty)
            return null;

        lock (_syncRoot)
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

    public void Delete(Guid connectionId)
    {
        if (connectionId == Guid.Empty)
            return;

        lock (_syncRoot)
        {
            DeleteIfExists(GetCredentialPath(connectionId));
            DeleteIfExists(Path.Combine(CredentialDir, $"{connectionId:N}_passphrase.bin"));
            DeleteIfExists(Path.Combine(CredentialDir, $"{connectionId:N}_jumphost_password.bin"));
            DeleteIfExists(GetLegacyCredentialPath(connectionId));
            DeleteIfExists(Path.Combine(LegacyCredentialDir, $"{connectionId:N}_key.bin"));
        }
        _settings?.BackupData();
    }

    public void SaveAdditional(Guid connectionId, string key, string value)
    {
        if (connectionId == Guid.Empty)
            throw new ArgumentException("Connection id cannot be empty.", nameof(connectionId));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty.", nameof(key));

        lock (_syncRoot)
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
        _settings?.BackupData();
    }

    public string? LoadAdditional(Guid connectionId, string key)
    {
        if (connectionId == Guid.Empty || string.IsNullOrWhiteSpace(key))
            return null;

        lock (_syncRoot)
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

    public void DeleteAdditional(Guid connectionId, string key)
    {
        if (connectionId == Guid.Empty || string.IsNullOrWhiteSpace(key))
            return;

        lock (_syncRoot)
        {
            var path = Path.Combine(CredentialDir, $"{connectionId:N}_{key}.bin");
            DeleteIfExists(path);
        }
        _settings?.BackupData();
    }

    private string GetDomainCredentialKey(string domain) =>
        $"domain_{domain.ToLowerInvariant()}";

    public void SaveDomainCredential(string domain, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain cannot be empty.", nameof(domain));

        lock (_syncRoot)
        {
            Directory.CreateDirectory(CredentialDir);
            var key = GetDomainCredentialKey(domain);
            var path = Path.Combine(CredentialDir, $"{key}.bin");

            var payload = Encoding.UTF8.GetBytes($"{username}|{password}");
            var protectedData = ProtectedData.Protect(payload, Entropy, DataProtectionScope.CurrentUser);
            WriteAtomic(path, protectedData);
        }
        _settings?.BackupData();
    }

    public (string Username, string Password)? LoadDomainCredential(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        lock (_syncRoot)
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

    public void DeleteDomainCredential(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return;

        lock (_syncRoot)
        {
            var key = GetDomainCredentialKey(domain);
            var path = Path.Combine(CredentialDir, $"{key}.bin");
            DeleteIfExists(path);
        }
        _settings?.BackupData();
    }

    private string GetCredentialPath(Guid connectionId) =>
        Path.Combine(CredentialDir, $"{connectionId:N}.bin");

    private string GetLegacyCredentialPath(Guid connectionId) =>
        Path.Combine(LegacyCredentialDir, connectionId.ToString("N"));

    private string? TryLoadLegacyCredential(Guid connectionId)
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

    private void WriteAtomic(string path, byte[] bytes)
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
            catch (Exception ex)
            {
                Log.Warn("Temp file cleanup error: " + ex.Message);
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
        catch (IOException ex)
        {
            Log.Debug("DeleteIfExists IO error: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Debug("DeleteIfExists access error: " + ex.Message);
        }
    }
}
