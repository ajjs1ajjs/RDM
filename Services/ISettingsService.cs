using RemoteManager.Models;

namespace RemoteManager.Services;

public interface ISettingsService
{
    AppSettings Current { get; set; }
    string SettingsPath { get; }
    string AppDataDir { get; }
    bool IsFirstRun { get; }

    void Save();
    void BackupData();
    void ChangeDatabasePath(string newPath);
    void SetTheme(string theme);
}
