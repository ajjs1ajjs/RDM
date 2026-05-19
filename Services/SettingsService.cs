using RemoteManager.Models;

namespace RemoteManager.Services;

public class SettingsService
{
    private const string SettingsFileName = "settings.json";

    public static SettingsService? Instance { get; private set; }

    public AppSettings Current { get; private set; }
    public string SettingsPath { get; }
    public string AppDataDir { get; }

    public SettingsService()
    {
        Instance = this;
        AppDataDir = AppDomain.CurrentDomain.BaseDirectory;

        if (!Directory.Exists(AppDataDir))
            Directory.CreateDirectory(AppDataDir);

        SettingsPath = Path.Combine(AppDataDir, SettingsFileName);
        Current = AppSettings.Load(SettingsPath);

        if (string.IsNullOrEmpty(Current.DatabasePath) || Current.DatabasePath.Contains("ApplicationData") || Current.DatabasePath.Contains("AppData"))
        {
            Current.DatabasePath = Path.Combine(AppDataDir, "RemoteManager.db");
            Save();
        }
    }

    public void Save()
    {
        Current.Save(SettingsPath);
    }

    public void ChangeDatabasePath(string newPath)
    {
        Current.DatabasePath = newPath;
        Save();
    }

    public void SetTheme(string theme)
    {
        Current.Theme = theme;
        Save();
    }
}
