using System;
using System.IO;
using RemoteManager.Helpers;
using RemoteManager.Models;
using RemoteManager.Services;
using Xunit;

namespace RemoteManager.Tests;

public class CredentialResolverTests : IDisposable
{
    private readonly string _tempCredentialsDir;
    private readonly CredentialService _credentialService;

    public CredentialResolverTests()
    {
        _tempCredentialsDir = Path.Combine(Path.GetTempPath(), $"rdm_creds_resolver_test_{Guid.NewGuid():N}");
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

    [Theory]
    [InlineData("admin", null)]
    [InlineData("CONTOSO\\admin", "CONTOSO")]
    [InlineData("admin@contoso.com", "contoso.com")]
    [InlineData("user.name@sub.domain.org", "sub.domain.org")]
    [InlineData("DOMAIN\\user.name", "DOMAIN")]
    [InlineData("plainUser", null)]
    public void TestExtractDomain(string username, string? expectedDomain)
    {
        // Act
        var result = CredentialResolver.ExtractDomain(username);

        // Assert
        Assert.Equal(expectedDomain, result);
    }

    [Fact]
    public void TestResolveCredentialsPlainUserNoDomainMatch()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var conn = new Connection
        {
            Id = connectionId,
            Username = "admin",
            Type = ConnectionType.RDP
        };

        var domain = "admin";
        var domainUser = "domainAdmin";
        var domainPassword = "DomainPassword123!";

        _credentialService.SaveDomainCredential(domain, domainUser, domainPassword);

        // Act
        var resolved = CredentialResolver.ResolveCredentials(_credentialService, conn, connectionId);

        // Assert
        Assert.Equal("admin", resolved.Username);
        Assert.Equal(string.Empty, resolved.Password);
    }

    [Fact]
    public void TestResolveCredentialsWithDomainMatchOnlyPassword()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var conn = new Connection
        {
            Id = connectionId,
            Username = "CONTOSO\\admin",
            Type = ConnectionType.RDP
        };

        var domain = "CONTOSO";
        var domainUser = "contoso_admin";
        var domainPassword = "ContosoPassword123!";

        _credentialService.SaveDomainCredential(domain, domainUser, domainPassword);

        // Act
        var resolved = CredentialResolver.ResolveCredentials(_credentialService, conn, connectionId);

        // Assert
        Assert.Equal("CONTOSO\\admin", resolved.Username);
        Assert.Equal(domainPassword, resolved.Password);
    }

    [Fact]
    public void TestResolveCredentialsWithDomainMatchFullReplacement()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var conn = new Connection
        {
            Id = connectionId,
            Username = "CONTOSO\\",
            Type = ConnectionType.RDP
        };

        var domain = "CONTOSO";
        var domainUser = "contoso_admin";
        var domainPassword = "ContosoPassword123!";

        _credentialService.SaveDomainCredential(domain, domainUser, domainPassword);

        // Act
        var resolved = CredentialResolver.ResolveCredentials(_credentialService, conn, connectionId);

        // Assert
        Assert.Equal(domainUser, resolved.Username);
        Assert.Equal(domainPassword, resolved.Password);
    }
}
