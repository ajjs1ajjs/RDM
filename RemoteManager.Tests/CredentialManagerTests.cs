using System;
using System.IO;
using RemoteManager.Services;
using Xunit;

namespace RemoteManager.Tests;

public class CredentialManagerTests
{
    [Fact]
    public void TestSaveAndLoadCredential()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var password = "SuperSecretPassword123!";

        try
        {
            // Act
            CredentialManager.Save(connectionId, password);
            var loadedPassword = CredentialManager.Load(connectionId);

            // Assert
            Assert.Equal(password, loadedPassword);
        }
        finally
        {
            // Clean up
            CredentialManager.Delete(connectionId);
        }
    }

    [Fact]
    public void TestLoadNonExistentCredentialReturnsNull()
    {
        // Arrange
        var connectionId = Guid.NewGuid();

        // Act
        var loadedPassword = CredentialManager.Load(connectionId);

        // Assert
        Assert.Null(loadedPassword);
    }

    [Fact]
    public void TestDeleteCredential()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var password = "TempPasswordToDeleted";

        CredentialManager.Save(connectionId, password);
        Assert.Equal(password, CredentialManager.Load(connectionId));

        // Act
        CredentialManager.Delete(connectionId);
        var loaded = CredentialManager.Load(connectionId);

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

        try
        {
            // Act
            CredentialManager.SaveDomainCredential(domain, username, password);
            var result = CredentialManager.LoadDomainCredential(domain);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(username, result.Value.Username);
            Assert.Equal(password, result.Value.Password);
        }
        finally
        {
            // Clean up
            CredentialManager.DeleteDomainCredential(domain);
        }
    }

    [Fact]
    public void TestLoadNonExistentDomainCredentialReturnsNull()
    {
        // Act
        var result = CredentialManager.LoadDomainCredential("NonExistentDomain123");

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

        CredentialManager.SaveDomainCredential(domain, username, password);
        Assert.NotNull(CredentialManager.LoadDomainCredential(domain));

        // Act
        CredentialManager.DeleteDomainCredential(domain);
        var result = CredentialManager.LoadDomainCredential(domain);

        // Assert
        Assert.Null(result);
    }
}
