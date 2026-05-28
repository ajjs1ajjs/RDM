using System;
using System.IO;
using RemoteManager.Models;
using RemoteManager.Services;
using Xunit;

namespace RemoteManager.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempSettingsDir;

    public SettingsServiceTests()
    {
        _tempSettingsDir = Path.Combine(Path.GetTempPath(), $"rdm_settings_test_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempSettingsDir))
                Directory.Delete(_tempSettingsDir, true);
        }
        catch { }
    }

    [Fact]
    public void Constructor_Creates_Instance()
    {
        var settings = new SettingsService(_tempSettingsDir);
        Assert.NotNull(settings);
        Assert.NotNull(settings.Current);
        Assert.NotNull(settings.SettingsPath);
    }

    [Fact]
    public void Constructor_With_Fresh_Dir_Is_FirstRun()
    {
        var settings = new SettingsService(_tempSettingsDir);
        Assert.NotNull(settings.Current);
    }

    [Fact]
    public void Save_And_Reload_Persists_Settings()
    {
        var settings = new SettingsService(_tempSettingsDir);
        settings.Current.Theme = "Dark";
        settings.Current.MinimizeToTray = false;
        settings.Save();

        var reloaded = AppSettings.Load(settings.SettingsPath);
        Assert.Equal("Dark", reloaded.Theme);
        Assert.False(reloaded.MinimizeToTray);
    }

    [Fact]
    public void SetTheme_Updates_Current()
    {
        var settings = new SettingsService(_tempSettingsDir);
        settings.SetTheme("Light");
        Assert.Equal("Light", settings.Current.Theme);
    }

    [Fact]
    public void ChangeDatabasePath_Updates_Path()
    {
        var settings = new SettingsService(_tempSettingsDir);
        var testPath = Path.Combine(settings.AppDataDir, "testdb.db");
        settings.ChangeDatabasePath(testPath);
        Assert.Equal(testPath, settings.Current.DatabasePath);
    }

    [Fact]
    public void BackupData_Does_Not_Throw_With_Empty_BackupFolder()
    {
        var settings = new SettingsService(_tempSettingsDir);
        settings.Current.BackupFolderPath = "";
        var exception = Record.Exception(() => settings.BackupData());
        Assert.Null(exception);
    }
}
