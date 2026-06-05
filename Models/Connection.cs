using LiteDB;

namespace RemoteManager.Models;

public enum ConnectionType
{
    RDP,
    SSH,
    Web
}

public class Connection
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public ConnectionType Type { get; set; }
    public Guid GroupId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public System.Collections.Generic.List<string> Tags { get; set; } = new();
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastConnectedAt { get; set; }

    public RDPSettings? RdpSettings { get; set; }
    public SSHSettings? SshSettings { get; set; }
    public WebSettings? WebSettings { get; set; }

    [BsonIgnore]
    public string? ImportedPassword { get; set; }
}

public class RDPSettings
{
    public int DesktopWidth { get; set; } = 1280;
    public int DesktopHeight { get; set; } = 720;
    public bool FullScreen { get; set; }
    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public bool RedirectPrinters { get; set; }
    public bool RedirectSmartCards { get; set; }
    public int AudioMode { get; set; } // 0=play locally, 1=play on server, 2=no audio
    public int ColorDepth { get; set; } = 32;
    public bool UseCredSsp { get; set; } = true;
    public bool NetworkLevelAuth { get; set; } = true;
    public string? GatewayHost { get; set; }
    public int GatewayPort { get; set; } = 443;
    public bool UseMultimon { get; set; }
}

public class PortForwarding
{
    public bool Enabled { get; set; }
    public uint LocalPort { get; set; } = 8080;
    public string RemoteHost { get; set; } = "127.0.0.1";
    public uint RemotePort { get; set; } = 80;
}

public class SSHSettings
{
    public SshAuthType AuthType { get; set; } = SshAuthType.Password;
    public string? PrivateKeyPath { get; set; }
    [BsonIgnore]
    public string? PrivateKeyPassphrase { get; set; }
    public int KeepAliveInterval { get; set; } = 30;
    public string? JumpHost { get; set; }
    public int JumpHostPort { get; set; } = 22;
    public string? JumpHostUsername { get; set; }
    [BsonIgnore]
    public string? JumpHostPassword { get; set; }
    public int TerminalColumns { get; set; } = 120;
    public int TerminalRows { get; set; } = 40;
    public PortForwarding PortForwarding { get; set; } = new();
}

public class WebSettings
{
    public string Url { get; set; } = "https://";
    public bool IgnoreCertificateErrors { get; set; } = false;
}
