using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteManager.Controls;
using RemoteManager.Helpers;
using RemoteManager.Models;
using RemoteManager.Services;

namespace RemoteManager.ViewModels;

public partial class SshSessionViewModel : SessionTabViewModel, IDisposable
{
    private readonly IDatabaseService _db;
    private bool _disposed;
    private SshTerminalControl? _sshTerminalRef;
    private int _reconnectAttempts;
    private const int MaxReconnectAttempts = 3;

    public Connection? Connection { get; set; }

    public (string Username, string Password) GetResolvedCredentials() =>
        CredentialResolver.ResolveCredentials(Connection, ConnectionId);

    public override string? GetPassword() => GetResolvedCredentials().Password;

    [ObservableProperty]
    private TerminalControl? _terminal;

    [ObservableProperty]
    private string _name = "";

    public string Host => Connection?.Host ?? "";

    public int Port => Connection?.Port ?? 22;

    public string TypeIcon => "🔌";

    public SshSessionViewModel(IDatabaseService db, Connection connection)
    {
        _db = db;
        Connection = connection;
        Name = connection.Name;
        ConnectionId = connection.Id;
        Header = string.IsNullOrWhiteSpace(connection.Host) ? connection.Name : connection.Host;
        SessionInfo = $"SSH {connection.Username}@{connection.Host}:{connection.Port}";
    }

    public override async Task ConnectAsync() => await ConnectSsh();

    [RelayCommand]
    private async Task ConnectSsh()
    {
        if (Connection == null) return;
        if (IsConnecting) return;

        IsConnecting = true;
        StatusText = "Connecting...";
        _reconnectAttempts = 0;

        var settings = new SSHSettings
        {
            AuthType = Connection.SshSettings?.AuthType ?? SshAuthType.Password,
            PrivateKeyPath = Connection.SshSettings?.PrivateKeyPath,
            PrivateKeyPassphrase = CredentialManager.LoadAdditional(Connection.Id, "passphrase"),
            KeepAliveInterval = Connection.SshSettings?.KeepAliveInterval ?? 30,
            TerminalColumns = Connection.SshSettings?.TerminalColumns ?? 120,
            TerminalRows = Connection.SshSettings?.TerminalRows ?? 40,
            JumpHost = Connection.SshSettings?.JumpHost,
            JumpHostPort = Connection.SshSettings?.JumpHostPort ?? 22,
            JumpHostUsername = Connection.SshSettings?.JumpHostUsername,
            JumpHostPassword = CredentialManager.LoadAdditional(Connection.Id, "jumphost_password")
        };

        if (Terminal is not SshTerminalControl ssh)
        {
            StatusText = "SSH terminal is not ready";
            IsConnected = false;
            IsConnecting = false;
            return;
        }

        UnsubscribeSshEvents(ssh);

        _sshTerminalRef = ssh;
        ssh.ConnectionClosed += OnTerminalConnectionClosed;

        try
        {
            var creds = GetResolvedCredentials();
            IsConnected = await ssh.ConnectAsync(Host, Port, creds.Username, creds.Password, settings);
            StatusText = IsConnected ? $"Connected to {Host}" : "Connection failed";
            if (IsConnected)
            {
                Connection.LastConnectedAt = DateTime.UtcNow;
                _db.SaveConnection(Connection);
            }
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void UnsubscribeSshEvents(SshTerminalControl? ssh = null)
    {
        var target = ssh ?? _sshTerminalRef;
        if (target == null) return;

        target.ConnectionClosed -= OnTerminalConnectionClosed;
    }

    [RelayCommand]
    public override void Disconnect()
    {
        UnsubscribeSshEvents();
        Terminal?.Disconnect();
        IsConnected = false;
        IsConnecting = false;
        _reconnectAttempts = MaxReconnectAttempts; // Prevent auto-reconnect on manual disconnect
        StatusText = "Disconnected";
    }

    [RelayCommand]
    private async Task Reconnect()
    {
        _reconnectAttempts = 0;
        Disconnect();
        await Task.Delay(150);
        await ConnectSsh();
    }

    [RelayCommand]
    private void Clear() => Terminal?.Clear();

    private async void OnTerminalConnectionClosed(object? sender, string reason)
    {
        IsConnected = false;
        IsConnecting = false;

        if (reason == "Clean")
        {
            _reconnectAttempts = MaxReconnectAttempts;
            StatusText = "Disconnected";
            RequestClose();
            return;
        }

        if (reason == "Manual")
        {
            _reconnectAttempts = MaxReconnectAttempts;
            StatusText = "Disconnected";
            return;
        }

        if (_reconnectAttempts < MaxReconnectAttempts)
        {
            _reconnectAttempts++;
            StatusText = $"Connection lost, reconnecting ({_reconnectAttempts}/{MaxReconnectAttempts})...";
            Log.Info($"Auto-reconnecting SSH session to {Host} (attempt {_reconnectAttempts})");
            await Task.Delay(2000 * _reconnectAttempts);

            if (!_disposed && Connection != null)
            {
                await ConnectSsh();
                return;
            }
        }

        StatusText = _reconnectAttempts >= MaxReconnectAttempts
            ? $"Disconnected (reconnect failed after {MaxReconnectAttempts} attempts)"
            : "Disconnected";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Disconnect();
        Connection = null;
    }
}
