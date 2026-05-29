using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteManager.Controls;
using RemoteManager.Helpers;
using RemoteManager.Models;
using RemoteManager.Services;

namespace RemoteManager.ViewModels;

public partial class RdpSessionViewModel : SessionTabViewModel
{
    private readonly IDatabaseService _db;
    private readonly ICredentialService _credentialService;
    private readonly ISettingsService _settings;
    private bool _disposed;
    private RdpHost? _rdpHostRef;
    private int _reconnectAttempts;
    private const int MaxReconnectAttempts = 3;

    public Connection? Connection { get; set; }

    public (string Username, string Password) GetResolvedCredentials() =>
        CredentialResolver.ResolveCredentials(_credentialService, Connection, ConnectionId);

    public override string? GetPassword() => GetResolvedCredentials().Password;

    [ObservableProperty]
    private TerminalControl? _rdpHost;

    [ObservableProperty]
    private string _name = "";

    public string Host => Connection?.Host ?? "";

    public int Port => Connection?.Port ?? 3389;

    public string TypeIcon => "\uE7F4";

    public RdpSessionViewModel(IDatabaseService db, ICredentialService credentialService, ISettingsService settings, Connection connection)
    {
        _db = db;
        _credentialService = credentialService;
        _settings = settings;
        Connection = connection;
        Name = connection.Name;
        ConnectionId = connection.Id;
        Header = string.IsNullOrWhiteSpace(connection.Host) ? connection.Name : connection.Host;
        SessionInfo = $"RDP {connection.Username}@{connection.Host}:{connection.Port}";
    }

    public override void Connect() => ConnectRdp();

    [RelayCommand]
    private void ConnectRdp()
    {
        if (Connection == null) return;

        IsConnecting = true;
        StatusText = "Connecting...";
        _reconnectAttempts = 0;

        if (RdpHost is not RdpHost rdp)
        {
            StatusText = "RDP host is not ready";
            IsConnected = false;
            IsConnecting = false;
            return;
        }

        UnsubscribeRdpEvents(rdp);

        _rdpHostRef = rdp;
        rdp.Connected += OnRdpConnected;
        rdp.Disconnected += OnRdpDisconnected;
        rdp.ErrorOccurred += OnRdpError;

        var creds = GetResolvedCredentials();
        rdp.Connect(Host, Port, creds.Username, creds.Password, Connection.RdpSettings);
        StatusText = $"Connecting to {Host}";
    }

    private void UnsubscribeRdpEvents(RdpHost? rdp = null)
    {
        var target = rdp ?? _rdpHostRef;
        if (target == null) return;

        target.Connected -= OnRdpConnected;
        target.Disconnected -= OnRdpDisconnected;
        target.ErrorOccurred -= OnRdpError;
    }

    [RelayCommand]
    public override void Disconnect()
    {
        UnsubscribeRdpEvents();
        RdpHost?.Disconnect();
        IsConnected = false;
        IsConnecting = false;
        _reconnectAttempts = MaxReconnectAttempts;
        StatusText = "Disconnected";
    }

    [RelayCommand]
    private void SendCtrlAltDel() { if (RdpHost is RdpHost rdp) rdp.SendCtrlAltDel(); }

    [RelayCommand]
    private void ToggleFullScreen() { if (RdpHost is RdpHost rdp) rdp.ToggleFullScreen(); }

    [RelayCommand]
    private void Reconnect()
    {
        _reconnectAttempts = 0;
        Disconnect();
        ConnectRdp();
    }

    private void OnRdpConnected(object? sender, EventArgs e)
    {
        IsConnected = true;
        IsConnecting = false;
        StatusText = $"Connected to {Host}";
        if (Connection != null)
        {
            Connection.LastConnectedAt = DateTime.UtcNow;
            _db.SaveConnection(Connection);
        }
    }

    private void OnRdpDisconnected(object? sender, string message)
    {
        IsConnected = false;
        IsConnecting = false;

        if (message == "APIInitiatedLogoff" || message == "LogoffByUser")
        {
            _reconnectAttempts = MaxReconnectAttempts;
            StatusText = "Logged out";
            RequestClose();
            return;
        }

        if (message == "Disconnected" || message == "APIInitiatedDisconnect")
        {
            _reconnectAttempts = MaxReconnectAttempts;
            StatusText = "Disconnected";
            return;
        }

        var autoReconnect = _settings.Current?.AutoReconnect ?? false;
        if (autoReconnect && _reconnectAttempts < MaxReconnectAttempts)
        {
            _reconnectAttempts++;
            StatusText = $"Connection lost, reconnecting ({_reconnectAttempts}/{MaxReconnectAttempts})...";
            Log.Info($"Auto-reconnecting RDP session to {Host} (attempt {_reconnectAttempts})");
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000 * _reconnectAttempts);
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (!_disposed && Connection != null)
                        ConnectRdp();
                });
            });
            return;
        }

        StatusText = _reconnectAttempts >= MaxReconnectAttempts
            ? $"Disconnected (reconnect failed after {MaxReconnectAttempts} attempts)"
            : string.IsNullOrWhiteSpace(message) ? "Disconnected" : message;
    }

    private void OnRdpError(object? sender, string message)
    {
        IsConnected = false;
        IsConnecting = false;
        StatusText = message;
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
