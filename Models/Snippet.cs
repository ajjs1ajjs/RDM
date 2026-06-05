using System;

namespace RemoteManager.Models;

public class Snippet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
}
