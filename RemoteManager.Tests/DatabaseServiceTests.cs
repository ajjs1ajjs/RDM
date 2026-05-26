using System.IO;
using RemoteManager.Models;
using RemoteManager.Services;
using Xunit;

namespace RemoteManager.Tests;

public class DatabaseServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly DatabaseService _db;

    public DatabaseServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"rdm_test_{Guid.NewGuid():N}.db");
        _db = new DatabaseService();
        _db.Initialize(_testDbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { if (File.Exists(_testDbPath)) File.Delete(_testDbPath); } catch { }
    }

    [Fact]
    public void Initialize_Creates_Database()
    {
        Assert.True(File.Exists(_testDbPath));
    }

    [Fact]
    public void SaveAndGetGroup_Works()
    {
        var group = new ConnectionGroup { Name = "Test Group" };
        _db.SaveGroup(group);

        Assert.NotEqual(Guid.Empty, group.Id);

        var loaded = _db.GetGroup(group.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Test Group", loaded!.Name);
    }

    [Fact]
    public void GetAllGroups_Returns_All()
    {
        _db.SaveGroup(new ConnectionGroup { Name = "Group A" });
        _db.SaveGroup(new ConnectionGroup { Name = "Group B" });

        var groups = _db.GetAllGroups();
        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void DeleteGroup_Removes_Group()
    {
        var group = new ConnectionGroup { Name = "To Delete" };
        _db.SaveGroup(group);

        _db.DeleteGroup(group.Id);
        Assert.Null(_db.GetGroup(group.Id));
    }

    [Fact]
    public void SaveAndGetConnection_Works()
    {
        var conn = new Connection { Name = "Test RDP", Host = "192.168.1.1", Port = 3389, Type = ConnectionType.RDP };
        _db.SaveConnection(conn);

        Assert.NotEqual(Guid.Empty, conn.Id);

        var loaded = _db.GetConnection(conn.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Test RDP", loaded!.Name);
        Assert.Equal("192.168.1.1", loaded.Host);
    }

    [Fact]
    public void SaveConnection_Sets_Timestamps()
    {
        var conn = new Connection { Name = "TS", Host = "10.0.0.1", Port = 22, Type = ConnectionType.SSH };
        _db.SaveConnection(conn);

        Assert.NotEqual(default, conn.CreatedAt);
        Assert.NotEqual(default, conn.ModifiedAt);
    }

    [Fact]
    public void GetConnectionsByGroup_Filters_Correctly()
    {
        var group = new ConnectionGroup { Name = "Filter Group" };
        _db.SaveGroup(group);

        _db.SaveConnection(new Connection { Name = "C1", Host = "1.1.1.1", Port = 3389, Type = ConnectionType.RDP, GroupId = group.Id });
        _db.SaveConnection(new Connection { Name = "C2", Host = "2.2.2.2", Port = 22, Type = ConnectionType.SSH });

        var filtered = _db.GetConnectionsByGroup(group.Id);
        Assert.Single(filtered);
        Assert.Equal("C1", filtered[0].Name);
    }

    [Fact]
    public void GetAllConnections_Returns_All()
    {
        _db.SaveConnection(new Connection { Name = "A", Host = "a.com", Port = 3389, Type = ConnectionType.RDP });
        _db.SaveConnection(new Connection { Name = "B", Host = "b.com", Port = 22, Type = ConnectionType.SSH });

        var all = _db.GetAllConnections();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void DeleteConnection_Removes_It()
    {
        var conn = new Connection { Name = "Del", Host = "x.com", Port = 3389, Type = ConnectionType.RDP };
        _db.SaveConnection(conn);

        _db.DeleteConnection(conn.Id);
        Assert.Null(_db.GetConnection(conn.Id));
    }

    [Fact]
    public void ImportData_Imports_Groups()
    {
        var data = new ExportData
        {
            Groups = new List<ConnectionGroup>
            {
                new() { Name = "Imported Group" }
            },
            Connections = new List<Connection>()
        };
        _db.ImportData(data);

        var groups = _db.GetAllGroups();
        Assert.Single(groups);
        Assert.Equal("Imported Group", groups[0].Name);
    }

    [Fact]
    public void ExportData_Exports_All()
    {
        _db.SaveGroup(new ConnectionGroup { Name = "Export Group" });
        _db.SaveConnection(new Connection { Name = "Export Conn", Host = "e.com", Port = 3389, Type = ConnectionType.RDP });

        var data = _db.ExportData();

        Assert.Single(data.Groups);
        Assert.Single(data.Connections);
    }

    [Fact]
    public void MigrateDatabase_Copies_Data()
    {
        _db.SaveGroup(new ConnectionGroup { Name = "Migrate Me" });

        var newPath = Path.Combine(Path.GetTempPath(), $"rdm_migrate_{Guid.NewGuid():N}.db");
        try
        {
            _db.MigrateDatabase(newPath);

            var groups = _db.GetAllGroups();
            Assert.Single(groups);
            Assert.Equal("Migrate Me", groups[0].Name);
        }
        finally
        {
            try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
        }
    }

    [Fact]
    public void Initialize_Throws_On_Empty_Path()
    {
        var db = new DatabaseService();
        Assert.Throws<ArgumentException>(() => db.Initialize(""));
    }
}
