using LiteDB;
using RemoteManager.Models;
using IOException = System.IO.IOException;

namespace RemoteManager.Services;

public class DatabaseService : IDatabaseService
{
    private readonly object _syncRoot = new();
    private LiteDatabase? _db;
    private string _currentPath = string.Empty;

    public void Initialize(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));

        lock (_syncRoot)
        {
            _db?.Dispose();
            _currentPath = dbPath;

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                _db = new LiteDatabase(new ConnectionString
                {
                    Filename = dbPath,
                    Connection = LiteDB.ConnectionType.Shared
                });

                EnsureIndexes();
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(
                    $"Cannot open database '{dbPath}'. Close other RemoteManager instances or choose a different database path.",
                    ex);
            }
            catch (LiteException ex)
            {
                throw new InvalidOperationException($"Cannot open database '{dbPath}': {ex.Message}", ex);
            }
        }
    }

    public void MigrateDatabase(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath))
            throw new ArgumentException("Database path cannot be empty.", nameof(newPath));

        lock (_syncRoot)
        {
            EnsureInitialized();

            var oldPath = _currentPath;
            _db?.Dispose();
            _db = null;

            var newDir = Path.GetDirectoryName(newPath);
            if (!string.IsNullOrEmpty(newDir) && !Directory.Exists(newDir))
                Directory.CreateDirectory(newDir);

            if (File.Exists(oldPath))
                File.Copy(oldPath, newPath, overwrite: true);

            Initialize(newPath);
        }
    }

    private ILiteCollection<ConnectionGroup> Groups
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureInitialized();
                return _db!.GetCollection<ConnectionGroup>("groups");
            }
        }
    }

    private ILiteCollection<Connection> Connections
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureInitialized();
                return _db!.GetCollection<Connection>("connections");
            }
        }
    }

    public List<ConnectionGroup> GetAllGroups() =>
        WithDb(() => Groups.FindAll().OrderBy(g => g.SortOrder).ToList());

    public ConnectionGroup? GetGroup(Guid id) =>
        WithDb(() => Groups.FindById(id));

    public void SaveGroup(ConnectionGroup group)
    {
        WithDb(() =>
        {
            if (group.Id == Guid.Empty)
                group.Id = Guid.NewGuid();
            Groups.Upsert(group);
        });
    }

    public void DeleteGroup(Guid id)
    {
        WithDb(() =>
        {
            var childGroups = Groups.Find(g => g.ParentId == id).ToList();
            foreach (var child in childGroups)
                DeleteGroup(child.Id);

            var groupConnections = Connections.Find(c => c.GroupId == id).ToList();
            foreach (var conn in groupConnections)
                DeleteConnection(conn.Id);

            Groups.Delete(id);
        });
    }

    public List<Connection> GetAllConnections() =>
        WithDb(() => Connections.FindAll().OrderBy(c => c.SortOrder).ToList());

    public List<Connection> GetConnectionsByGroup(Guid groupId) =>
        WithDb(() => Connections.Find(c => c.GroupId == groupId).OrderBy(c => c.SortOrder).ToList());

    public Connection? GetConnection(Guid id) =>
        WithDb(() => Connections.FindById(id));

    public void SaveConnection(Connection connection)
    {
        WithDb(() =>
        {
            connection.ModifiedAt = DateTime.UtcNow;
            if (connection.Id == Guid.Empty)
            {
                connection.Id = Guid.NewGuid();
                connection.CreatedAt = DateTime.UtcNow;
            }
            Connections.Upsert(connection);
        });
    }

    public void DeleteConnection(Guid id)
    {
        WithDb(() =>
        {
            CredentialManager.Delete(id);
            Connections.Delete(id);
        });
    }

    public void ImportData(ExportData data)
    {
        WithDb(() =>
        {
            var groupMap = new Dictionary<Guid, Guid>();

            foreach (var group in data.Groups.OrderBy(g => g.ParentId == null ? 0 : 1))
            {
                var oldId = group.Id;
                group.Id = Guid.NewGuid();
                groupMap[oldId] = group.Id;

                group.ParentId = group.ParentId.HasValue && groupMap.TryGetValue(group.ParentId.Value, out var newParentId)
                    ? newParentId
                    : null;

                Groups.Insert(group);
            }

            foreach (var conn in data.Connections)
            {
                conn.Id = Guid.NewGuid();
                if (conn.GroupId != Guid.Empty && groupMap.TryGetValue(conn.GroupId, out var newGroupId))
                {
                    conn.GroupId = newGroupId;
                }

                if (!string.IsNullOrEmpty(conn.ImportedPassword))
                {
                    CredentialManager.Save(conn.Id, conn.ImportedPassword);
                }

                conn.CreatedAt = DateTime.UtcNow;
                conn.ModifiedAt = DateTime.UtcNow;
                Connections.Insert(conn);
            }
        });
    }

    public ExportData ExportData()
    {
        return WithDb(() => new ExportData
        {
            Groups = GetAllGroups(),
            Connections = GetAllConnections()
        });
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _db?.Dispose();
            _db = null;
        }
    }

    private void EnsureIndexes()
    {
        Groups.EnsureIndex(g => g.ParentId);
        Groups.EnsureIndex(g => g.SortOrder);
        Connections.EnsureIndex(c => c.GroupId);
        Connections.EnsureIndex(c => c.SortOrder);
    }

    private void EnsureInitialized()
    {
        if (_db == null)
            throw new InvalidOperationException("Database service is not initialized.");
    }

    private T WithDb<T>(Func<T> action)
    {
        lock (_syncRoot)
            return action();
    }

    private void WithDb(Action action)
    {
        lock (_syncRoot)
            action();
    }
}
