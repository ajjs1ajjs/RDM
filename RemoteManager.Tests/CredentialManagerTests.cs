using System;
using System.IO;
using RemoteManager.Services;
using Xunit;

namespace RemoteManager.Tests;

public class CredentialManagerTests : IDisposable
{
    private readonly string _tempCredentialsDir;
    private readonly CredentialService _credentialService;

    public CredentialManagerTests()
    {
        _tempCredentialsDir = Path.Combine(Path.GetTempPath(), $"rdm_creds_test_{Guid.NewGuid():N}");
        _credentialService = new CredentialService(_tempCredentialsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempCredentialsDir))
                Directory.Delete(_tempCredentialsDir, true);
        }
        catch { }
    }

    [Fact]
    public void TestSaveAndLoadCredential()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var password = "SuperSecretPassword123!";

        // Act
        _credentialService.Save(connectionId, password);
        var loadedPassword = _credentialService.Load(connectionId);

        // Assert
        Assert.Equal(password, loadedPassword);
    }

    [Fact]
    public void TestLoadNonExistentCredentialReturnsNull()
    {
        // Arrange
        var connectionId = Guid.NewGuid();

        // Act
        var loadedPassword = _credentialService.Load(connectionId);

        // Assert
        Assert.Null(loadedPassword);
    }

    [Fact]
    public void TestDeleteCredential()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var password = "TempPasswordToDeleted";

        _credentialService.Save(connectionId, password);
        Assert.Equal(password, _credentialService.Load(connectionId));

        // Act
        _credentialService.Delete(connectionId);
        var loaded = _credentialService.Load(connectionId);

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public void TestDomainCredentialSaveAndLoad()
    {
        // Arrange
        var domain = "TestDomain";
        var username = "Administrator";
        var password = "SecureDomainPassword!";

        // Act
        _credentialService.SaveDomainCredential(domain, username, password);
        var result = _credentialService.LoadDomainCredential(domain);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(username, result.Value.Username);
        Assert.Equal(password, result.Value.Password);
    }

    [Fact]
    public void TestLoadNonExistentDomainCredentialReturnsNull()
    {
        // Act
        var result = _credentialService.LoadDomainCredential("NonExistentDomain123");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TestDeleteDomainCredential()
    {
        // Arrange
        var domain = "DeleteDomain";
        var username = "testuser";
        var password = "password";

        _credentialService.SaveDomainCredential(domain, username, password);
        Assert.NotNull(_credentialService.LoadDomainCredential(domain));

        // Act
        _credentialService.DeleteDomainCredential(domain);
        var result = _credentialService.LoadDomainCredential(domain);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TestAdditionalCredential_SaveAndLoad()
    {
        var connectionId = Guid.NewGuid();
        _credentialService.SaveAdditional(connectionId, "passphrase", "MyPrivateKeyPass123");
        _credentialService.SaveAdditional(connectionId, "jumphost_password", "JumpHostPass456!");

        var passphrase = _credentialService.LoadAdditional(connectionId, "passphrase");
        var jumpPass = _credentialService.LoadAdditional(connectionId, "jumphost_password");

        Assert.Equal("MyPrivateKeyPass123", passphrase);
        Assert.Equal("JumpHostPass456!", jumpPass);
    }

    [Fact]
    public void TestAdditionalCredential_LoadNonExistent_ReturnsNull()
    {
        var connectionId = Guid.NewGuid();
        Assert.Null(_credentialService.LoadAdditional(connectionId, "nonexistent"));
    }

    [Fact]
    public void TestAdditionalCredential_Delete()
    {
        var connectionId = Guid.NewGuid();
        _credentialService.SaveAdditional(connectionId, "passphrase", "DeleteMe");
        Assert.NotNull(_credentialService.LoadAdditional(connectionId, "passphrase"));

        _credentialService.DeleteAdditional(connectionId, "passphrase");

        Assert.Null(_credentialService.LoadAdditional(connectionId, "passphrase"));
    }

    [Fact]
    public void TestAdditionalCredential_Overwrite()
    {
        var connectionId = Guid.NewGuid();
        _credentialService.SaveAdditional(connectionId, "key", "OriginalValue");
        _credentialService.SaveAdditional(connectionId, "key", "UpdatedValue");

        var loaded = _credentialService.LoadAdditional(connectionId, "key");
        Assert.Equal("UpdatedValue", loaded);
    }

    [Fact]
    public void TestAdditionalCredential_EmptyValue_DeletesFile()
    {
        var connectionId = Guid.NewGuid();
        _credentialService.SaveAdditional(connectionId, "tempsetting", "SomeValue");
        Assert.NotNull(_credentialService.LoadAdditional(connectionId, "tempsetting"));

        _credentialService.SaveAdditional(connectionId, "tempsetting", "");

        Assert.Null(_credentialService.LoadAdditional(connectionId, "tempsetting"));
    }

    [Fact]
    public void TestAdditionalCredential_EmptyConnectionId_ReturnsNull()
    {
        Assert.Null(_credentialService.LoadAdditional(Guid.Empty, "anykey"));
    }

    [Fact]
    public void TestAdditionalCredential_EmptyKey_ReturnsNull()
    {
        Assert.Null(_credentialService.LoadAdditional(Guid.NewGuid(), ""));
        Assert.Null(_credentialService.LoadAdditional(Guid.NewGuid(), "   "));
    }
}
