using System;
using System.IO;
using System.Linq;
using RemoteManager.Models;

namespace RemoteManager.Services;

public class SettingsService
{
    private const string SettingsFileName = "settings.json";

    public static SettingsService? Instance { get; private set; }

    public AppSettings Current { get; private set; }
    public string SettingsPath { get; }
    public string AppDataDir { get; }
    public bool IsFirstRun { get; private set; }

    public SettingsService()
    {
        Instance = this;
        
        var legacySettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
        var targetAppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RemoteManager");
        
        AppDataDir = targetAppDataDir;
        if (!Directory.Exists(AppDataDir))
            Directory.CreateDirectory(AppDataDir);

        SettingsPath = Path.Combine(AppDataDir, SettingsFileName);

        // Migrate legacy settings.json if it exists in the application directory but not in AppData
        if (File.Exists(legacySettingsPath) && !File.Exists(SettingsPath))
        {
            try
            {
                File.Move(legacySettingsPath, SettingsPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to migrate legacy settings: {ex.Message}");
            }
        }

        IsFirstRun = !File.Exists(SettingsPath);
        Current = AppSettings.Load(SettingsPath);

        // Migrate legacy database if it exists in the application directory
        var legacyDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteManager.db");
        if (string.IsNullOrEmpty(Current.DatabasePath) || Current.DatabasePath.StartsWith(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var defaultDbPath = Path.Combine(AppDataDir, "RemoteManager.db");
            var currentDbFile = string.IsNullOrEmpty(Current.DatabasePath) ? legacyDbPath : Current.DatabasePath;

            if (File.Exists(currentDbFile) && !string.Equals(currentDbFile, defaultDbPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (!File.Exists(defaultDbPath))
                    {
                        File.Move(currentDbFile, defaultDbPath);
                    }
                    Current.DatabasePath = defaultDbPath;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to migrate legacy database: {ex.Message}");
                    Current.DatabasePath = defaultDbPath;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(Current.DatabasePath))
                {
                    Current.DatabasePath = defaultDbPath;
                }
            }
            Save();
        }
    }

    private readonly object _backupLock = new();

    public void Save()
    {
        Current.Save(SettingsPath);
        BackupData();
    }

    public void BackupData()
    {
        if (string.IsNullOrEmpty(Current.BackupFolderPath))
            return;

        lock (_backupLock)
        {
            try
            {
                var backupDir = Current.BackupFolderPath;
                if (!Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);

                // 1. Backup settings.json
                if (File.Exists(SettingsPath))
                {
                    var targetSettingsPath = Path.Combine(backupDir, SettingsFileName);
                    File.Copy(SettingsPath, targetSettingsPath, overwrite: true);
                }

                // 2. Backup database (.db)
                if (!string.IsNullOrEmpty(Current.DatabasePath) && File.Exists(Current.DatabasePath))
                {
                    var dbName = Path.GetFileName(Current.DatabasePath);
                    var targetDbPath = Path.Combine(backupDir, dbName);
                    File.Copy(Current.DatabasePath, targetDbPath, overwrite: true);
                }

                // 3. Backup credentials folder
                var sourceCredentialsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RemoteManager",
                    "credentials");

                if (Directory.Exists(sourceCredentialsDir))
                {
                    var targetCredentialsDir = Path.Combine(backupDir, "credentials");
                    if (!Directory.Exists(targetCredentialsDir))
                        Directory.CreateDirectory(targetCredentialsDir);

                    var files = Directory.GetFiles(sourceCredentialsDir);
                    var sourceFileNames = files.Select(Path.GetFileName).ToHashSet();

                    if (Directory.Exists(targetCredentialsDir))
                    {
                        var targetFiles = Directory.GetFiles(targetCredentialsDir);
                        foreach (var targetFile in targetFiles)
                        {
                            var targetName = Path.GetFileName(targetFile);
                            if (!sourceFileNames.Contains(targetName))
                            {
                                try { File.Delete(targetFile); } catch { }
                            }
                        }
                    }

                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file);
                        var targetPath = Path.Combine(targetCredentialsDir, fileName);
                        File.Copy(file, targetPath, overwrite: true);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup sync failed: {ex.Message}");
            }
        }
    }

    public static void RestoreBackup(string backupDir, SettingsService settings)
    {
        if (string.IsNullOrEmpty(backupDir) || !Directory.Exists(backupDir))
            throw new DirectoryNotFoundException("Backup directory does not exist.");

        // 1. Copy settings.json if exists
        var backupSettingsPath = Path.Combine(backupDir, SettingsFileName);
        if (File.Exists(backupSettingsPath))
        {
            File.Copy(backupSettingsPath, settings.SettingsPath, overwrite: true);
            // Reload settings
            settings.Current = AppSettings.Load(settings.SettingsPath);
        }

        // 2. Set/Update BackupFolderPath to the restored folder
        settings.Current.BackupFolderPath = backupDir;

        // 3. Find and copy database file (.db)
        var dbFiles = Directory.GetFiles(backupDir, "*.db");
        var restoredDbPath = Path.Combine(settings.AppDataDir, "RemoteManager.db");
        if (dbFiles.Length > 0)
        {
            var sourceDbPath = dbFiles[0];
            var dbName = Path.GetFileName(sourceDbPath);
            restoredDbPath = Path.Combine(settings.AppDataDir, dbName);
            File.Copy(sourceDbPath, restoredDbPath, overwrite: true);
        }
        // 4. Save path to database
        settings.Current.DatabasePath = restoredDbPath;

        // 5. Copy credentials
        var backupCredentialsDir = Path.Combine(backupDir, "credentials");
        if (Directory.Exists(backupCredentialsDir))
        {
            var targetCredentialsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RemoteManager",
                "credentials");

            if (!Directory.Exists(targetCredentialsDir))
                Directory.CreateDirectory(targetCredentialsDir);

            // Clean existing local credentials first to avoid mix-up
            if (Directory.Exists(targetCredentialsDir))
            {
                foreach (var file in Directory.GetFiles(targetCredentialsDir))
                {
                    try { File.Delete(file); } catch { }
                }
            }

            foreach (var file in Directory.GetFiles(backupCredentialsDir))
            {
                var fileName = Path.GetFileName(file);
                var targetPath = Path.Combine(targetCredentialsDir, fileName);
                File.Copy(file, targetPath, overwrite: true);
            }
        }

        // 6. Save the adjusted settings locally
        settings.Save();
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
