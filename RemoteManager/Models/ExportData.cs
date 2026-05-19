namespace RemoteManager.Models;

public class ExportData
{
    public int Version { get; set; } = 1;
    public DateTime ExportDate { get; set; } = DateTime.UtcNow;
    public List<ConnectionGroup> Groups { get; set; } = new();
    public List<Connection> Connections { get; set; } = new();
}
