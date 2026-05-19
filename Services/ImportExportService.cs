using System.Text.Json;
using RemoteManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace RemoteManager.Services;

public class ImportExportService
{
    private const long MaxImportFileSize = 50 * 1024 * 1024; // 50 MB limit

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IDatabaseService _db;

    public ImportExportService(IDatabaseService db)
    {
        _db = db;
    }

    public void ExportToFile(string filePath)
    {
        var data = _db.ExportData();
        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public ExportData? LoadFromFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxImportFileSize)
            throw new InvalidOperationException($"Import file is too large. Maximum allowed size is {MaxImportFileSize / 1024 / 1024} MB.");

        var content = File.ReadAllText(filePath);
        if (filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".rdm", StringComparison.OrdinalIgnoreCase) ||
            content.TrimStart().StartsWith("<"))
        {
            return ParseDevolutionsXml(filePath);
        }
        return JsonSerializer.Deserialize<ExportData>(content, JsonOptions);
    }

    private ExportData ParseDevolutionsXml(string filePath)
    {
        var data = new ExportData();
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 1024 * 1024, // 1 MB limit for entity expansion
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };

            using (var reader = XmlReader.Create(filePath, settings))
            {
                doc = XDocument.Load(reader);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse XML file: {ex.Message}", ex);
        }

        var connElements = doc.Descendants()
            .Where(e => e.Name.LocalName.Equals("Connection", StringComparison.OrdinalIgnoreCase) &&
                        (e.Parent == null || !e.Parent.Name.LocalName.Equals("Connection", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (!connElements.Any())
        {
            throw new InvalidOperationException("No <Connection> entries found in the file.");
        }

        // First pass: Build a dictionary of shared credentials in the file
        var credentialMap = new Dictionary<string, (string username, string password, string domain)>(StringComparer.OrdinalIgnoreCase);

        foreach (var el in connElements)
        {
            var id = el.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("ID", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
            var connTypeStr = el.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("ConnectionType", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

            var credentialsNode = el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Credentials", StringComparison.OrdinalIgnoreCase));
            if (credentialsNode != null || connTypeStr.Equals("Credential", StringComparison.OrdinalIgnoreCase))
            {
                var credNode = credentialsNode ?? el;
                var username = credNode.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("UserName", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
                var domain = credNode.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Domain", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
                
                // Read cleartext or password tags if available
                var password = credNode.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("ClearTextPassword", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? 
                               credNode.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Password", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

                if (!string.IsNullOrEmpty(id))
                {
                    credentialMap[id] = (username, password, domain);
                }
            }
        }

        var pathMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var el in connElements)
        {
            var name = el.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "Imported Connection";
            var connTypeStr = el.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("ConnectionType", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
            
            // Skip folder group entries since they are just directories, not actual connections
            if (connTypeStr.Equals("Group", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip credential entries from being added as actual connections in the UI
            if (connTypeStr.Equals("Credential", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Look recursively (Descendants) to fetch nested connection details (e.g. Host/Url/ComputerName, Port, Username, Password)
            var host = el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Url", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ??
                       el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Host", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? 
                       el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("ComputerName", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
            
            var portStr = el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Port", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
            
            var username = el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("UserName", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? 
                           el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("LoginName", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
            
            var domain = el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Domain", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
            
            var password = el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("ClearTextPassword", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? 
                           el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Password", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

            // If credentials are empty, check if we reference a shared credential ID from the file
            if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password))
            {
                var credId = el.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("CredentialConnectionID", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
                if (!string.IsNullOrEmpty(credId) && credentialMap.TryGetValue(credId, out var cred))
                {
                    username = cred.username;
                    password = cred.password;
                    domain = cred.domain;
                }
            }

            if (!string.IsNullOrEmpty(domain) && !string.IsNullOrEmpty(username))
            {
                username = $"{domain}\\{username}";
            }

            var description = el.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Description", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
            var groupPath = el.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Group", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

            // Determine connection type
            ConnectionType type = ConnectionType.RDP;
            if (connTypeStr.Contains("SSH", StringComparison.OrdinalIgnoreCase) || 
                connTypeStr.Contains("Putty", StringComparison.OrdinalIgnoreCase) || 
                name.Contains("SSH", StringComparison.OrdinalIgnoreCase))
            {
                type = ConnectionType.SSH;
            }

            int port = type == ConnectionType.RDP ? 3389 : 22;
            if (int.TryParse(portStr, out int parsedPort) && parsedPort > 0)
            {
                port = parsedPort;
            }

            // Parse group hierarchy
            Guid groupId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(groupPath))
            {
                var parts = groupPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                string currentPath = "";
                Guid? parentId = null;

                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i].Trim();
                    currentPath = i == 0 ? part : $"{currentPath}\\{part}";

                    if (!pathMap.TryGetValue(currentPath, out var currentGuid))
                    {
                        currentGuid = Guid.NewGuid();
                        pathMap[currentPath] = currentGuid;

                        data.Groups.Add(new ConnectionGroup
                        {
                            Id = currentGuid,
                            Name = part,
                            ParentId = parentId,
                            SortOrder = data.Groups.Count
                        });
                    }

                    parentId = currentGuid;
                    if (i == parts.Length - 1)
                    {
                        groupId = currentGuid;
                    }
                }
            }

            var connection = new Connection
            {
                Id = Guid.NewGuid(),
                Name = name,
                Host = host,
                Port = port,
                Username = username,
                ImportedPassword = string.IsNullOrEmpty(password) ? null : password,
                Type = type,
                GroupId = groupId,
                Description = description,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                RdpSettings = type == ConnectionType.RDP ? new RDPSettings() : null,
                SshSettings = type == ConnectionType.SSH ? new SSHSettings() : null
            };

            data.Connections.Add(connection);
        }

        return data;
    }

    public ImportPreview PreviewImport(string filePath)
    {
        var data = LoadFromFile(filePath);
        if (data == null)
            throw new InvalidOperationException("Invalid export file");

        return new ImportPreview
        {
            GroupCount = data.Groups.Count,
            ConnectionCount = data.Connections.Count,
            Groups = data.Groups.Select(g => g.Name).ToList(),
            Connections = data.Connections.Select(c => $"{c.Name} ({c.Host}:{c.Port})").ToList()
        };
    }

    public void ImportFromFile(string filePath)
    {
        var data = LoadFromFile(filePath);
        if (data == null)
            throw new InvalidOperationException("Invalid export file");

        _db.ImportData(data);
    }

    public void ExportEncrypted(string filePath, string password)
    {
        var data = _db.ExportData();
        foreach (var conn in data.Connections)
        {
            conn.ImportedPassword = CredentialManager.Load(conn.Id);
        }
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var encryptedBytes = Helpers.EncryptionHelper.Encrypt(json, password);
        File.WriteAllBytes(filePath, encryptedBytes);
    }

    public void ImportEncrypted(string filePath, string password)
    {
        var encryptedBytes = File.ReadAllBytes(filePath);
        var json = Helpers.EncryptionHelper.Decrypt(encryptedBytes, password);
        var data = JsonSerializer.Deserialize<ExportData>(json, JsonOptions);
        if (data == null)
            throw new InvalidOperationException("Failed to deserialize decrypted backup data.");

        _db.ImportData(data);
    }

    public ImportPreview PreviewImportEncrypted(string filePath, string password)
    {
        var encryptedBytes = File.ReadAllBytes(filePath);
        var json = Helpers.EncryptionHelper.Decrypt(encryptedBytes, password);
        var data = JsonSerializer.Deserialize<ExportData>(json, JsonOptions);
        if (data == null)
            throw new InvalidOperationException("Failed to deserialize decrypted backup data.");

        return new ImportPreview
        {
            GroupCount = data.Groups.Count,
            ConnectionCount = data.Connections.Count,
            Groups = data.Groups.Select(g => g.Name).ToList(),
            Connections = data.Connections.Select(c => $"{c.Name} ({c.Host}:{c.Port})").ToList()
        };
    }
}

public class ImportPreview
{
    public int GroupCount { get; set; }
    public int ConnectionCount { get; set; }
    public List<string> Groups { get; set; } = new();
    public List<string> Connections { get; set; } = new();
}
