using System;
using System.IO;
using System.Linq;
using RemoteManager.Models;
using RemoteManager.Services;
using Xunit;

using System.Threading.Tasks;

namespace RemoteManager.Tests;

public class ImportExportTests
{
    private string GetTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"RemoteManager_test_{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task TestExportAndImportJson()
    {
        // Arrange
        var dbPath = GetTempDatabasePath();
        var exportPath = Path.Combine(Path.GetTempPath(), $"RemoteManager_export_{Guid.NewGuid():N}.json");

        var tempCredDir = Path.Combine(Path.GetTempPath(), $"rdm_test_creds_{Guid.NewGuid():N}");
        var credService = new CredentialService(tempCredDir);
        var dbService = new DatabaseService(credService);
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

            var importExport = new ImportExportService(dbService, credService);

            // Act
            await importExport.ExportToFileAsync(exportPath);

            // Create new DB to import into
            var dbPathImport = GetTempDatabasePath();
            var tempCredDirImport = Path.Combine(Path.GetTempPath(), $"rdm_test_creds_{Guid.NewGuid():N}");
            var credServiceImport = new CredentialService(tempCredDirImport);
            var dbServiceImport = new DatabaseService(credServiceImport);
            dbServiceImport.Initialize(dbPathImport);

            try
            {
                var importExport2 = new ImportExportService(dbServiceImport, credServiceImport);
                await importExport2.ImportFromFileAsync(exportPath);

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
                if (Directory.Exists(tempCredDirImport))
                    Directory.Delete(tempCredDirImport, true);
            }
        }
        finally
        {
            dbService.Dispose();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
            if (File.Exists(exportPath))
                File.Delete(exportPath);
            if (Directory.Exists(tempCredDir))
                Directory.Delete(tempCredDir, true);
        }
    }

    [Fact]
    public async Task TestParseDevolutionsXml()
    {
        // Arrange
        var dbPath = GetTempDatabasePath();
        var xmlPath = Path.Combine(Path.GetTempPath(), $"Devolutions_import_{Guid.NewGuid():N}.xml");

        var tempCredDir = Path.Combine(Path.GetTempPath(), $"rdm_test_creds_{Guid.NewGuid():N}");
        var credService = new CredentialService(tempCredDir);
        var dbService = new DatabaseService(credService);
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
            var importExport = new ImportExportService(dbService, credService);

            // Act
            var preview = await importExport.PreviewImportAsync(xmlPath);

            // Assert preview
            Assert.Equal(3, preview.GroupCount); // Infrastructure, Infrastructure\Windows, Infrastructure\Linux
            Assert.Equal(2, preview.ConnectionCount);

            // Act import
            await importExport.ImportFromFileAsync(xmlPath);

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
            if (Directory.Exists(tempCredDir))
                Directory.Delete(tempCredDir, true);
        }
    }

    [Fact]
    public async Task TestSecureExportAndImportWithPasswords()
    {
        // Arrange
        var dbPath = GetTempDatabasePath();
        var backupPath = Path.Combine(Path.GetTempPath(), $"RemoteManager_backup_{Guid.NewGuid():N}.enc");
        var backupPassword = "SuperSecureBackupPassword123!";
        var connPassword = "TestConnectionPassword123!";

        var tempCredDir = Path.Combine(Path.GetTempPath(), $"rdm_test_creds_{Guid.NewGuid():N}");
        var credService = new CredentialService(tempCredDir);
        var dbService = new DatabaseService(credService);
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
                GroupId = parentGroup.Id,
                SshSettings = new SSHSettings()
            };
            dbService.SaveConnection(connection);
            credService.Save(connection.Id, connPassword);
            credService.SaveAdditional(connection.Id, "passphrase", "Passphrase123!");
            credService.SaveAdditional(connection.Id, "jumphost_password", "JumpHostPass123!");

            var importExport = new ImportExportService(dbService, credService);

            // Act
            await importExport.ExportEncryptedAsync(backupPath, backupPassword);

            // Assert that file is encrypted (does not contain plain text name or host)
            var fileContent = File.ReadAllBytes(backupPath);
            var fileText = System.Text.Encoding.UTF8.GetString(fileContent);
            Assert.DoesNotContain("Secure Connection", fileText);
            Assert.DoesNotContain("secure.host.local", fileText);

            // Import into a new clean database
            var dbPathImport = GetTempDatabasePath();
            var tempCredDirImport = Path.Combine(Path.GetTempPath(), $"rdm_test_creds_{Guid.NewGuid():N}");
            var credServiceImport = new CredentialService(tempCredDirImport);
            var dbServiceImport = new DatabaseService(credServiceImport);
            dbServiceImport.Initialize(dbPathImport);

            try
            {
                var importExportImport = new ImportExportService(dbServiceImport, credServiceImport);
                
                // Act preview
                var preview = await importExportImport.PreviewImportEncryptedAsync(backupPath, backupPassword);
                Assert.Equal(1, preview.GroupCount);
                Assert.Equal(1, preview.ConnectionCount);

                // Act import
                await importExportImport.ImportEncryptedAsync(backupPath, backupPassword);

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
                var restoredPassword = credServiceImport.Load(importedConnection.Id);
                Assert.Equal(connPassword, restoredPassword);

                var restoredPassphrase = credServiceImport.LoadAdditional(importedConnection.Id, "passphrase");
                Assert.Equal("Passphrase123!", restoredPassphrase);

                var restoredJumpPass = credServiceImport.LoadAdditional(importedConnection.Id, "jumphost_password");
                Assert.Equal("JumpHostPass123!", restoredJumpPass);

                // Clean up credentials created for imported connection in test environment
                credServiceImport.Delete(importedConnection.Id);
            }
            finally
            {
                dbServiceImport.Dispose();
                if (File.Exists(dbPathImport))
                    File.Delete(dbPathImport);
                if (Directory.Exists(tempCredDirImport))
                    Directory.Delete(tempCredDirImport, true);
            }
        }
        finally
        {
            // Clean up original credentials
            var connections = dbService.GetAllConnections();
            foreach (var conn in connections)
            {
                credService.Delete(conn.Id);
            }

            dbService.Dispose();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            if (Directory.Exists(tempCredDir))
                Directory.Delete(tempCredDir, true);
        }
    }

    [Fact]
    public void TestAutoBackupAndRestore()
    {
        // Arrange
        var backupDir = Path.Combine(Path.GetTempPath(), $"RemoteManager_Backup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDir);

        var tempSettingsDir = Path.Combine(Path.GetTempPath(), $"rdm_settings_test_{Guid.NewGuid():N}");
        var settings = new SettingsService(tempSettingsDir);

        var testDbPath = Path.Combine(settings.AppDataDir, $"RemoteManager_backup_test_{Guid.NewGuid():N}.db");
        var tempCredDir = Path.Combine(settings.AppDataDir, "credentials");
        var credService = new CredentialService(tempCredDir, settings);
        var dbService = new DatabaseService(credService, settings);

        try
        {
            // Set backup folder and database
            settings.Current.BackupFolderPath = backupDir;
            settings.Current.DatabasePath = testDbPath;
            settings.Save();

            dbService.Initialize(testDbPath);

            var parentGroup = new ConnectionGroup { Name = "Backup Group" };
            dbService.SaveGroup(parentGroup);

            var connection = new Connection
            {
                Name = "Backup Connection",
                Host = "backup.local",
                Port = 22,
                Username = "backup_user",
                Type = ConnectionType.SSH,
                GroupId = parentGroup.Id
            };
            dbService.SaveConnection(connection);
            var testPassword = "BackupPassword123!";
            credService.Save(connection.Id, testPassword);

            // Act - Trigger backup manually (SettingsService is passed, and credService.Save already triggers BackupData)
            settings.BackupData();

            // Assert backup files exist
            var dbFileName = Path.GetFileName(testDbPath);
            Assert.True(File.Exists(Path.Combine(backupDir, "settings.json")), "settings.json should exist in backup folder");
            Assert.True(File.Exists(Path.Combine(backupDir, dbFileName)), "database should exist in backup folder");
            
            var backupCredsFolder = Path.Combine(backupDir, "credentials");
            Assert.True(Directory.Exists(backupCredsFolder), "credentials folder should exist in backup");
            Assert.True(File.Exists(Path.Combine(backupCredsFolder, $"{connection.Id:N}.bin")), "connection credential should exist in backup credentials");

            // Now clean local database and credentials (simulating local data loss)
            dbService.Dispose();
            if (File.Exists(testDbPath))
                File.Delete(testDbPath);

            var localCredPath = Path.Combine(tempCredDir, $"{connection.Id:N}.bin");
            if (File.Exists(localCredPath))
                File.Delete(localCredPath);

            // Act - Restore backup
            SettingsService.RestoreBackup(backupDir, settings);

            // Assert files are restored locally
            Assert.True(File.Exists(settings.Current.DatabasePath), "Restored database file should exist");
            Assert.True(File.Exists(localCredPath), "Restored local credential file should exist");
            
            var restoredPassword = credService.Load(connection.Id);
            Assert.NotNull(restoredPassword);
            Assert.Equal(testPassword, restoredPassword);

            // Cleanup restored files
            credService.Delete(connection.Id);
            if (File.Exists(settings.Current.DatabasePath))
                File.Delete(settings.Current.DatabasePath);
        }
        finally
        {
            dbService.Dispose();
            if (File.Exists(testDbPath))
                File.Delete(testDbPath);

            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, true);

            if (Directory.Exists(tempSettingsDir))
                Directory.Delete(tempSettingsDir, true);
        }
    }
}
