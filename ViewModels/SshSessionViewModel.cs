using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteManager.Controls;
using RemoteManager.Helpers;
using RemoteManager.Models;
using RemoteManager.Services;

namespace RemoteManager.ViewModels;

public partial class SshSessionViewModel : SessionTabViewModel
{
    private readonly IDatabaseService _db;
    private readonly ICredentialService _credentialService;
    private readonly ISettingsService _settings;
    private bool _disposed;
    private SshWebViewTerminalControl? _sshTerminalRef;
    private int _reconnectAttempts;
    private const int MaxReconnectAttempts = 3;
    private EventHandler<string>? _connectionClosedHandler;

    public Connection? Connection { get; set; }

    public (string Username, string Password) GetResolvedCredentials() =>
        CredentialResolver.ResolveCredentials(_credentialService, Connection, ConnectionId);

    public override string? GetPassword() => GetResolvedCredentials().Password;

    [ObservableProperty]
    private TerminalControl? _terminal;

    [ObservableProperty]
    private string _name = "";

    public string Host => Connection?.Host ?? "";

    public int Port => Connection?.Port ?? 22;

    public override string TypeIcon => "\uE9A9";

    public SshSessionViewModel(IDatabaseService db, ICredentialService credentialService, ISettingsService settings, Connection connection)
    {
        _db = db;
        _credentialService = credentialService;
        _settings = settings;
        Connection = connection;
        Name = connection.Name;
        ConnectionId = connection.Id;
        Header = string.IsNullOrWhiteSpace(connection.Host) ? connection.Name : connection.Host;
        SessionInfo = L.Get("SessionInfo_Ssh", connection.Username, connection.Host, connection.Port);

        foreach (var snippet in _db.GetAllSnippets())
        {
            Snippets.Add(snippet);
        }
    }

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<Snippet> _snippets = new();

    public override async Task ConnectAsync() => await ConnectSsh();

    [RelayCommand]
    private async Task ConnectSsh()
    {
        if (Connection == null) return;
        if (IsConnecting) return;

        IsConnecting = true;
        StatusText = L.Status_Connecting;
        _reconnectAttempts = 0;

        var settings = new SSHSettings
        {
            AuthType = Connection.SshSettings?.AuthType ?? SshAuthType.Password,
            PrivateKeyPath = Connection.SshSettings?.PrivateKeyPath,
            PrivateKeyPassphrase = _credentialService.LoadAdditional(Connection.Id, "passphrase"),
            KeepAliveInterval = Connection.SshSettings?.KeepAliveInterval ?? 30,
            TerminalColumns = Connection.SshSettings?.TerminalColumns ?? 120,
            TerminalRows = Connection.SshSettings?.TerminalRows ?? 40,
            JumpHost = Connection.SshSettings?.JumpHost,
            JumpHostPort = Connection.SshSettings?.JumpHostPort ?? 22,
            JumpHostUsername = Connection.SshSettings?.JumpHostUsername,
            JumpHostPassword = _credentialService.LoadAdditional(Connection.Id, "jumphost_password"),
            PortForwarding = Connection.SshSettings?.PortForwarding ?? new PortForwarding()
        };

        if (Terminal is not SshWebViewTerminalControl ssh)
        {
            StatusText = L.Status_TerminalNotReady;
            IsConnected = false;
            IsConnecting = false;
            return;
        }

        UnsubscribeSshEvents();

        _sshTerminalRef = ssh;
        _connectionClosedHandler = async (s, r) => await OnTerminalConnectionClosedAsync(s, r);
        ssh.ConnectionClosed += _connectionClosedHandler;

        try
        {
            var creds = GetResolvedCredentials();
            IsConnected = await ssh.ConnectAsync(Host, Port, creds.Username, creds.Password, settings);
            StatusText = IsConnected ? L.Get("Status_Connected", Host) : L.Status_ConnectionFailed;
            if (IsConnected)
            {
                Connection.LastConnectedAt = DateTime.UtcNow;
                _db.SaveConnection(Connection);
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = L.Status_ConnectionFailed + ": " + ex.Message;
            Log.Error("SSH connection failed with exception", ex);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void UnsubscribeSshEvents()
    {
        var target = _sshTerminalRef;
        if (target == null) return;

        if (_connectionClosedHandler != null)
            target.ConnectionClosed -= _connectionClosedHandler;
    }

    [RelayCommand]
    public override void Disconnect()
    {
        UnsubscribeSshEvents();
        Terminal?.Disconnect();
        IsConnected = false;
        IsConnecting = false;
        _reconnectAttempts = MaxReconnectAttempts; // Prevent auto-reconnect on manual disconnect
        StatusText = L.Status_Disconnected;
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

    [RelayCommand]
    private void ExecuteSnippet(Snippet snippet)
    {
        if (snippet != null && !string.IsNullOrEmpty(snippet.Command))
        {
            Terminal?.SendText(snippet.Command + "\n");
        }
    }

    private async Task OnTerminalConnectionClosedAsync(object? sender, string reason)
    {
        IsConnected = false;
        IsConnecting = false;

        if (reason == "Clean")
        {
            _reconnectAttempts = MaxReconnectAttempts;
            StatusText = L.Status_Disconnected;
            RequestClose();
            return;
        }

        if (reason == "Manual")
        {
            _reconnectAttempts = MaxReconnectAttempts;
            StatusText = L.Status_Disconnected;
            return;
        }

        var autoReconnect = _settings.Current?.AutoReconnect ?? false;
        if (autoReconnect && _reconnectAttempts < MaxReconnectAttempts)
        {
            _reconnectAttempts++;
            StatusText = L.Get("Status_Reconnecting", _reconnectAttempts, MaxReconnectAttempts);
            Log.Info($"Auto-reconnecting SSH session to {Host} (attempt {_reconnectAttempts})");
            await Task.Delay(2000 * _reconnectAttempts);

            if (!_disposed && Connection != null)
            {
                await ConnectSsh();
                return;
            }
        }

        StatusText = _reconnectAttempts >= MaxReconnectAttempts
            ? L.Get("Status_ReconnectFailed", MaxReconnectAttempts)
            : L.Status_Disconnected;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Disconnect();
        Connection = null;
        base.Dispose();
    }
}
