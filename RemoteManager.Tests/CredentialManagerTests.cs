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
}
