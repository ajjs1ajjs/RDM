using System;
using System.IO;
using System.Linq;
using RemoteManager.Models;
using RemoteManager.Services;
using Xunit;

namespace RemoteManager.Tests;

public class ImportExportTests
{
    private string GetTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"RemoteManager_test_{Guid.NewGuid():N}.db");
    }

    [Fact]
    public void TestExportAndImportJson()
    {
        // Arrange
        var dbPath = GetTempDatabasePath();
        var exportPath = Path.Combine(Path.GetTempPath(), $"RemoteManager_export_{Guid.NewGuid():N}.json");

        var dbService = new DatabaseService();
        dbService.Initialize(dbPath);

        try
        {
            var parentGroup = new ConnectionGroup { Name = "Production Servers" };
            dbService.SaveGroup(parentGroup);

            var connection = new Connection
            {
                Name = "Prod Web 01",
                Host = "10.0.0.10",
                Port = 22,
                Username = "root",
                Type = ConnectionType.SSH,
                GroupId = parentGroup.Id,
                SshSettings = new SSHSettings { KeepAliveInterval = 60 }
            };
            dbService.SaveConnection(connection);

            var importExport = new ImportExportService(dbService);

            // Act
            importExport.ExportToFile(exportPath);

            // Create new DB to import into
            var dbPathImport = GetTempDatabasePath();
            var dbServiceImport = new DatabaseService();
            dbServiceImport.Initialize(dbPathImport);

            try
            {
                var importExport2 = new ImportExportService(dbServiceImport);
                importExport2.ImportFromFile(exportPath);

                // Assert
                var groups = dbServiceImport.GetAllGroups();
                var connections = dbServiceImport.GetAllConnections();

                Assert.Single(groups);
                Assert.Equal("Production Servers", groups[0].Name);

                Assert.Single(connections);
                Assert.Equal("Prod Web 01", connections[0].Name);
                Assert.Equal("10.0.0.10", connections[0].Host);
                Assert.Equal(22, connections[0].Port);
                Assert.Equal("root", connections[0].Username);
                Assert.Equal(ConnectionType.SSH, connections[0].Type);
            }
            finally
            {
                dbServiceImport.Dispose();
                if (File.Exists(dbPathImport))
                    File.Delete(dbPathImport);
            }
        }
        finally
        {
            dbService.Dispose();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
            if (File.Exists(exportPath))
                File.Delete(exportPath);
        }
    }

    [Fact]
    public void TestParseDevolutionsXml()
    {
        // Arrange
        var dbPath = GetTempDatabasePath();
        var xmlPath = Path.Combine(Path.GetTempPath(), $"Devolutions_import_{Guid.NewGuid():N}.xml");

        var dbService = new DatabaseService();
        dbService.Initialize(dbPath);

        var xmlContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ArrayOfConnection>
  <Connection>
    <ID>8efb7a0d-9b16-43b9-87a3-e28a9b74052f</ID>
    <ConnectionType>RDP</ConnectionType>
    <Name>Test RDP Server</Name>
    <ComputerName>192.168.1.50</ComputerName>
    <Port>3389</Port>
    <UserName>admin</UserName>
    <ClearTextPassword>MySuperSecretPass</ClearTextPassword>
    <Group>Infrastructure\Windows</Group>
    <Description>Main Domain Controller</Description>
  </Connection>
  <Connection>
    <ID>f5b9c03b-d53d-4c3e-bb3d-a417df8a213e</ID>
    <ConnectionType>SSH</ConnectionType>
    <Name>Linux Box</Name>
    <Host>192.168.1.60</Host>
    <Port>22</Port>
    <UserName>ubuntu</UserName>
    <Group>Infrastructure\Linux</Group>
  </Connection>
</ArrayOfConnection>";

        File.WriteAllText(xmlPath, xmlContent);

        try
        {
            var importExport = new ImportExportService(dbService);

            // Act
            var preview = importExport.PreviewImport(xmlPath);

            // Assert preview
            Assert.Equal(3, preview.GroupCount); // Infrastructure, Infrastructure\Windows, Infrastructure\Linux
            Assert.Equal(2, preview.ConnectionCount);

            // Act import
            importExport.ImportFromFile(xmlPath);

            var groups = dbService.GetAllGroups();
            var connections = dbService.GetAllConnections();

            // Verify groups
            Assert.Contains(groups, g => g.Name == "Infrastructure");
            Assert.Contains(groups, g => g.Name == "Windows");
            Assert.Contains(groups, g => g.Name == "Linux");

            // Verify connections
            var rdpConn = connections.FirstOrDefault(c => c.Type == ConnectionType.RDP);
            Assert.NotNull(rdpConn);
            Assert.Equal("Test RDP Server", rdpConn.Name);
            Assert.Equal("192.168.1.50", rdpConn.Host);
            Assert.Equal(3389, rdpConn.Port);
            Assert.Equal("admin", rdpConn.Username);
            Assert.Equal("Main Domain Controller", rdpConn.Description);

            var sshConn = connections.FirstOrDefault(c => c.Type == ConnectionType.SSH);
            Assert.NotNull(sshConn);
            Assert.Equal("Linux Box", sshConn.Name);
            Assert.Equal("192.168.1.60", sshConn.Host);
            Assert.Equal(22, sshConn.Port);
            Assert.Equal("ubuntu", sshConn.Username);
        }
        finally
        {
            dbService.Dispose();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
            if (File.Exists(xmlPath))
                File.Delete(xmlPath);
        }
    }

    [Fact]
    public void TestSecureExportAndImportWithPasswords()
    {
        // Arrange
        var dbPath = GetTempDatabasePath();
        var backupPath = Path.Combine(Path.GetTempPath(), $"RemoteManager_backup_{Guid.NewGuid():N}.enc");
        var backupPassword = "SuperSecureBackupPassword123!";
        var connPassword = "TestConnectionPassword123!";

        var dbService = new DatabaseService();
        dbService.Initialize(dbPath);

        try
        {
            var parentGroup = new ConnectionGroup { Name = "Secure Group" };
            dbService.SaveGroup(parentGroup);

            var connection = new Connection
            {
                Name = "Secure Connection",
                Host = "secure.host.local",
                Port = 22,
                Username = "secure_user",
                Type = ConnectionType.SSH,
                GroupId = parentGroup.Id
            };
            dbService.SaveConnection(connection);
            CredentialManager.Save(connection.Id, connPassword);

            var importExport = new ImportExportService(dbService);

            // Act
            importExport.ExportEncrypted(backupPath, backupPassword);

            // Assert that file is encrypted (does not contain plain text name or host)
            var fileContent = File.ReadAllBytes(backupPath);
            var fileText = System.Text.Encoding.UTF8.GetString(fileContent);
            Assert.DoesNotContain("Secure Connection", fileText);
            Assert.DoesNotContain("secure.host.local", fileText);

            // Import into a new clean database
            var dbPathImport = GetTempDatabasePath();
            var dbServiceImport = new DatabaseService();
            dbServiceImport.Initialize(dbPathImport);

            try
            {
                var importExportImport = new ImportExportService(dbServiceImport);
                
                // Act preview
                var preview = importExportImport.PreviewImportEncrypted(backupPath, backupPassword);
                Assert.Equal(1, preview.GroupCount);
                Assert.Equal(1, preview.ConnectionCount);

                // Act import
                importExportImport.ImportEncrypted(backupPath, backupPassword);

                // Assert data is imported
                var groups = dbServiceImport.GetAllGroups();
                var connections = dbServiceImport.GetAllConnections();

                Assert.Single(groups);
                Assert.Equal("Secure Group", groups[0].Name);

                Assert.Single(connections);
                var importedConnection = connections[0];
                Assert.Equal("Secure Connection", importedConnection.Name);
                Assert.Equal("secure.host.local", importedConnection.Host);

                // Assert password is encrypted and saved under DPAPI for the new connections
                var restoredPassword = CredentialManager.Load(importedConnection.Id);
                Assert.Equal(connPassword, restoredPassword);

                // Clean up credentials created for imported connection in test environment
                CredentialManager.Delete(importedConnection.Id);
            }
            finally
            {
                dbServiceImport.Dispose();
                if (File.Exists(dbPathImport))
                    File.Delete(dbPathImport);
            }
        }
        finally
        {
            // Clean up original credentials
            var connections = dbService.GetAllConnections();
            foreach (var conn in connections)
            {
                CredentialManager.Delete(conn.Id);
            }

            dbService.Dispose();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
    }
}
