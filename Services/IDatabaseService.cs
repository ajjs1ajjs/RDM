using RemoteManager.Models;

namespace RemoteManager.Services;

public interface IDatabaseService
{
    void Initialize(string dbPath);
    void MigrateDatabase(string newPath);

    List<ConnectionGroup> GetAllGroups();
    ConnectionGroup? GetGroup(Guid id);
    void SaveGroup(ConnectionGroup group);
    void DeleteGroup(Guid id);

    List<Connection> GetAllConnections();
    List<Connection> GetConnectionsByGroup(Guid groupId);
    Connection? GetConnection(Guid id);
    void SaveConnection(Connection connection);
    void DeleteConnection(Guid id);
    void ImportData(ExportData data);
    ExportData ExportData();
}
