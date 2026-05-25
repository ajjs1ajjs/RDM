using System;
using RemoteManager.Helpers;
using RemoteManager.Models;
using RemoteManager.Services;
using Xunit;

namespace RemoteManager.Tests;

public class CredentialResolverTests
{
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

        try
        {
            // Even if a domain credential with the name "admin" exists,
            // a plain user "admin" should NOT match it as a domain.
            CredentialManager.SaveDomainCredential(domain, domainUser, domainPassword);

            // Act
            var resolved = CredentialResolver.ResolveCredentials(conn, connectionId);

            // Assert
            Assert.Equal("admin", resolved.Username);
            Assert.Equal(string.Empty, resolved.Password);
        }
        finally
        {
            CredentialManager.DeleteDomainCredential(domain);
        }
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

        try
        {
            CredentialManager.SaveDomainCredential(domain, domainUser, domainPassword);

            // Act
            var resolved = CredentialResolver.ResolveCredentials(conn, connectionId);

            // Assert
            // Since username was CONTOSO\admin, it has domain = CONTOSO but specifies a user.
            // Only the password should be replaced, and username remains CONTOSO\admin.
            Assert.Equal("CONTOSO\\admin", resolved.Username);
            Assert.Equal(domainPassword, resolved.Password);
        }
        finally
        {
            CredentialManager.DeleteDomainCredential(domain);
        }
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

        try
        {
            CredentialManager.SaveDomainCredential(domain, domainUser, domainPassword);

            // Act
            var resolved = CredentialResolver.ResolveCredentials(conn, connectionId);

            // Assert
            // Since username was CONTOSO\, it is just the domain.
            // Both username and password should be replaced.
            Assert.Equal(domainUser, resolved.Username);
            Assert.Equal(domainPassword, resolved.Password);
        }
        finally
        {
            CredentialManager.DeleteDomainCredential(domain);
        }
    }
}
