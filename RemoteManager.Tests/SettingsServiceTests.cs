using System.IO;
using RemoteManager.Models;
using RemoteManager.Services;
using Xunit;

namespace RemoteManager.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void Constructor_Creates_Instance()
    {
        var settings = new SettingsService();
        Assert.NotNull(settings);
        Assert.NotNull(settings.Current);
        Assert.NotNull(settings.SettingsPath);
    }

    [Fact]
    public void Constructor_With_Fresh_Dir_Is_FirstRun()
    {
        var settings = new SettingsService();
        Assert.NotNull(settings.Current);
    }

    [Fact]
    public void Save_And_Reload_Persists_Settings()
    {
        var settings = new SettingsService();
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
        var settings = new SettingsService();
        settings.SetTheme("Light");
        Assert.Equal("Light", settings.Current.Theme);
    }

    [Fact]
    public void ChangeDatabasePath_Updates_Path()
    {
        var settings = new SettingsService();
        var testPath = Path.Combine(settings.AppDataDir, "testdb.db");
        settings.ChangeDatabasePath(testPath);
        Assert.Equal(testPath, settings.Current.DatabasePath);
    }

    [Fact]
    public void BackupData_Does_Not_Throw_With_Empty_BackupFolder()
    {
        var settings = new SettingsService();
        settings.Current.BackupFolderPath = "";
        var exception = Record.Exception(() => settings.BackupData());
        Assert.Null(exception);
    }
}
