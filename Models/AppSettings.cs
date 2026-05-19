using System.Text.Json;
using System.Text.Json.Serialization;
using IOException = System.IO.IOException;

namespace RemoteManager.Models;

public class DomainCredential
{
    public string Domain { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public class AppSettings
{
    public string DatabasePath { get; set; } = string.Empty;
    public string Theme { get; set; } = "Light";
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowSystemTrayNotifications { get; set; } = true;
    public bool AutoReconnect { get; set; }
    public string DefaultRdpPort { get; set; } = "3389";
    public string DefaultSshPort { get; set; } = "22";
    public string Language { get; set; } = "uk-UA";
    public string BackupFolderPath { get; set; } = string.Empty;
    public System.Collections.Generic.List<DomainCredential> DomainCredentials { get; set; } = new System.Collections.Generic.List<DomainCredential>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(this, JsonOptions));

        if (File.Exists(path))
            File.Replace(tempPath, path, null);
        else
            File.Move(tempPath, path);
    }
}
