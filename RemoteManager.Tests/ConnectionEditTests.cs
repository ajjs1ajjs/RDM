using System;
using System.IO;
using RemoteManager.Models;
using RemoteManager.Services;
using RemoteManager.ViewModels;
using Xunit;

namespace RemoteManager.Tests;

public class ConnectionEditTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly string _tempCredentialsDir;
    private readonly CredentialService _credentialService;
    private readonly DatabaseService _db;
    private readonly string _tempSettingsDir;
    private readonly SettingsService _settings;

    public ConnectionEditTests()
    {
        _tempSettingsDir = Path.Combine(Path.GetTempPath(), $"rdm_edit_test_settings_{Guid.NewGuid():N}");
        _settings = new SettingsService(_tempSettingsDir);

        _testDbPath = Path.Combine(Path.GetTempPath(), $"rdm_edit_test_{Guid.NewGuid():N}.db");
        _tempCredentialsDir = Path.Combine(Path.GetTempPath(), $"rdm_edit_test_creds_{Guid.NewGuid():N}");
        _credentialService = new CredentialService(_tempCredentialsDir);
        _db = new DatabaseService(_credentialService);
        _db.Initialize(_testDbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { if (File.Exists(_testDbPath)) File.Delete(_testDbPath); } catch { }
        try { if (Directory.Exists(_tempCredentialsDir)) Directory.Delete(_tempCredentialsDir, true); } catch { }
        try { if (Directory.Exists(_tempSettingsDir)) Directory.Delete(_tempSettingsDir, true); } catch { }
    }

    [Fact]
    public void EditConnection_PreservesPassword_WhenPasswordFieldIsEmpty()
    {
        var conn = new Connection
        {
            Name = "Test Server",
            Host = "192.168.1.1",
            Port = 3389,
            Type = ConnectionType.RDP,
            Username = "admin"
        };
        _db.SaveConnection(conn);
        _credentialService.Save(conn.Id, "SuperSecret123");

        var editVm = new ConnectionEditViewModel(_db, _credentialService, _settings, conn);

        Assert.NotNull(editVm);
        Assert.Equal("Test Server", editVm.Name);
        Assert.True(editVm.SavePassword);
        Assert.Equal("SuperSecret123", editVm.Password);
    }

    [Fact]
    public void Save_ExistingConnection_WithEmptyPassword_KeepsExistingPassword()
    {
        var conn = new Connection
        {
            Name = "Save Test",
            Host = "10.0.0.1",
            Port = 22,
            Type = ConnectionType.SSH,
            Username = "root",
            SshSettings = new SSHSettings()
        };
        _db.SaveConnection(conn);
        _credentialService.Save(conn.Id, "OriginalPassword123");

        var editVm = new ConnectionEditViewModel(_db, _credentialService, _settings, conn);
        editVm.Name = "Updated Name";

        editVm.SaveCommand.Execute(null);

        var savedPassword = _credentialService.Load(conn.Id);
        Assert.Equal("OriginalPassword123", savedPassword);
    }

    [Fact]
    public void Save_NewConnection_WithPassword_SavesCorrectly()
    {
        var editVm = new ConnectionEditViewModel(_db, _credentialService, _settings);
        editVm.Name = "New Server";
        editVm.Host = "10.0.0.2";
        editVm.Port = 3389;
        editVm.SelectedType = ConnectionType.RDP;
        editVm.UserName = "admin";
        editVm.Password = "NewPassword123";
        editVm.SavePassword = true;

        editVm.SaveCommand.Execute(null);

        var connections = _db.GetAllConnections();
        Assert.Single(connections);
        Assert.Equal("New Server", connections[0].Name);
        Assert.Equal("NewPassword123", _credentialService.Load(connections[0].Id));
    }

    [Fact]
    public void Save_ExistingConnection_WithNewPassword_UpdatesPassword()
    {
        var conn = new Connection
        {
            Name = "Update Pwd",
            Host = "10.0.0.3",
            Port = 22,
            Type = ConnectionType.SSH,
            Username = "user",
            SshSettings = new SSHSettings()
        };
        _db.SaveConnection(conn);
        _credentialService.Save(conn.Id, "OldPassword");

        var editVm = new ConnectionEditViewModel(_db, _credentialService, _settings, conn);
        editVm.Password = "NewPassword456";
        editVm.SavePassword = true;

        editVm.SaveCommand.Execute(null);

        Assert.Equal("NewPassword456", _credentialService.Load(conn.Id));
    }

    [Fact]
    public void Save_ExistingConnection_WithSavePasswordUnchecked_DeletesPassword()
    {
        var conn = new Connection
        {
            Name = "Delete Pwd",
            Host = "10.0.0.4",
            Port = 3389,
            Type = ConnectionType.RDP,
            Username = "user"
        };
        _db.SaveConnection(conn);
        _credentialService.Save(conn.Id, "WillBeDeleted");

        var editVm = new ConnectionEditViewModel(_db, _credentialService, _settings, conn);
        editVm.SavePassword = false;

        editVm.SaveCommand.Execute(null);

        Assert.Null(_credentialService.Load(conn.Id));
    }
}
