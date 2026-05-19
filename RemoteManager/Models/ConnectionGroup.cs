using LiteDB;

namespace RemoteManager.Models;

public class ConnectionGroup
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Group";
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
