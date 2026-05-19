using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RemoteManager.Models;
using RemoteManager.Services;

namespace RemoteManager.ViewModels;

public partial class ConnectionEditViewModel : ObservableObject
{
    private static readonly Regex HostnameRegex = new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$", RegexOptions.Compiled);
    private static readonly Regex IpRegex = new(@"^(\d{1,3}\.){3}\d{1,3}$", RegexOptions.Compiled);

    private readonly IDatabaseService _db;
    private readonly Connection? _existing;
    private string? _validationError;

    public ConnectionEditViewModel(IDatabaseService db)
    {
        _db = db;
        _existing = null;
        LoadGroups();
    }

    public ConnectionEditViewModel(IDatabaseService db, Connection existing)
    {
        _db = db;
        _existing = existing;
        Name = existing.Name;
        Host = existing.Host;
        Port = existing.Port;
        UserName = existing.Username;
        Description = existing.Description;
        SelectedType = existing.Type;
        SelectedGroupId = existing.GroupId;

        if (existing.RdpSettings != null)
        {
            RdpWidth = existing.RdpSettings.DesktopWidth;
            RdpHeight = existing.RdpSettings.DesktopHeight;
            RdpRedirectClipboard = existing.RdpSettings.RedirectClipboard;
            RdpRedirectDrives = existing.RdpSettings.RedirectDrives;
            RdpRedirectPrinters = existing.RdpSettings.RedirectPrinters;
            RdpCredSsp = existing.RdpSettings.UseCredSsp;
            RdpAudioMode = existing.RdpSettings.AudioMode;
            RdpGatewayHost = existing.RdpSettings.GatewayHost ?? "";
            RdpGatewayPort = existing.RdpSettings.GatewayPort;
        }

        if (existing.SshSettings != null)
        {
            SshAuthType = existing.SshSettings.AuthType;
            SshKeyPath = existing.SshSettings.PrivateKeyPath ?? "";
            SshKeepAlive = existing.SshSettings.KeepAliveInterval;
            SshJumpHost = existing.SshSettings.JumpHost ?? "";
            SshJumpHostPort = existing.SshSettings.JumpHostPort;
            SshJumpHostUsername = existing.SshSettings.JumpHostUsername ?? "";
        }

        var password = CredentialManager.Load(existing.Id);
        if (password != null)
        {
            SavePassword = true;
            _password = password;
        }

        LoadGroups();
    }

    [ObservableProperty]
    private string _name = "New Connection";

    [ObservableProperty]
    private string _host = "";

    [ObservableProperty]
    private int _port = 3389;

    partial void OnSelectedTypeChanged(ConnectionType value)
    {
        Port = value == ConnectionType.SSH ? 22 : 3389;
    }

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private ConnectionType _selectedType = ConnectionType.RDP;

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private bool _savePassword;

    [ObservableProperty]
    private Guid? _selectedGroupId;

    [ObservableProperty]
    private int _rdpWidth = 1280;

    [ObservableProperty]
    private int _rdpHeight = 720;

    [ObservableProperty]
    private bool _rdpRedirectClipboard = true;

    [ObservableProperty]
    private bool _rdpRedirectDrives;

    [ObservableProperty]
    private bool _rdpRedirectPrinters;

    [ObservableProperty]
    private bool _rdpCredSsp = true;

    [ObservableProperty]
    private int _rdpAudioMode;

    [ObservableProperty]
    private string _rdpGatewayHost = "";

    [ObservableProperty]
    private int _rdpGatewayPort = 443;

    [ObservableProperty]
    private SshAuthType _sshAuthType = SshAuthType.Password;

    [ObservableProperty]
    private string _sshKeyPath = "";

    [ObservableProperty]
    private int _sshKeepAlive = 30;

    [ObservableProperty]
    private string _sshJumpHost = "";

    [ObservableProperty]
    private int _sshJumpHostPort = 22;

    [ObservableProperty]
    private string _sshJumpHostUsername = "";

    [ObservableProperty]
    private string _sshJumpHostPassword = "";

    public string WindowTitle => _existing == null ? "New Connection" : $"Edit {_existing.Name}";

    public string? ValidationError
    {
        get => _validationError;
        set => SetProperty(ref _validationError, value);
    }

    public ObservableCollection<ConnectionGroup> Groups { get; set; } = new();

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Connection name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            ValidationError = "Host address is required.";
            return false;
        }

        var trimmedHost = Host.Trim();
        bool isValidHost = HostnameRegex.IsMatch(trimmedHost) || IpRegex.IsMatch(trimmedHost);
        if (!isValidHost)
        {
            ValidationError = "Invalid host address. Use a valid hostname (e.g., server.example.com) or IP address (e.g., 192.168.1.1).";
            return false;
        }

        if (IpRegex.IsMatch(trimmedHost))
        {
            var parts = trimmedHost.Split('.');
            foreach (var part in parts)
            {
                if (int.TryParse(part, out var num) && (num < 0 || num > 255))
                {
                    ValidationError = "Invalid IP address. Each octet must be between 0 and 255.";
                    return false;
                }
            }
        }

        if (Port < 1 || Port > 65535)
        {
            ValidationError = "Port must be between 1 and 65535.";
            return false;
        }

        if (SelectedType == ConnectionType.SSH && SshAuthType == SshAuthType.Key && !string.IsNullOrEmpty(SshKeyPath))
        {
            if (!File.Exists(SshKeyPath))
            {
                ValidationError = $"SSH key file not found: {SshKeyPath}";
                return false;
            }
        }

        ValidationError = null;
        return true;
    }

    private void LoadGroups()
    {
        Groups.Clear();
        foreach (var g in _db.GetAllGroups())
            Groups.Add(g);
    }

    [RelayCommand]
    private void BrowseSshKey()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Key files (*.ppk;*.pem;*.key)|*.ppk;*.pem;*.key|All files (*.*)|*.*",
            Title = "Select SSH Private Key"
        };
        if (dialog.ShowDialog() == true)
            SshKeyPath = dialog.FileName;
    }

    [RelayCommand]
    private void Save()
    {
        if (!ValidateInputs())
            return;

        var conn = _existing ?? new Connection();
        conn.Name = Name.Trim();
        conn.Host = Host.Trim();
        conn.Port = Port;
        conn.Username = UserName.Trim();
        conn.Type = SelectedType;
        conn.GroupId = SelectedGroupId ?? Guid.Empty;
        conn.Description = Description.Trim();

        if (SelectedType == ConnectionType.RDP)
        {
            conn.RdpSettings = new RDPSettings
            {
                DesktopWidth = RdpWidth,
                DesktopHeight = RdpHeight,
                RedirectClipboard = RdpRedirectClipboard,
                RedirectDrives = RdpRedirectDrives,
                RedirectPrinters = RdpRedirectPrinters,
                UseCredSsp = RdpCredSsp,
                AudioMode = RdpAudioMode,
                GatewayHost = string.IsNullOrEmpty(RdpGatewayHost) ? null : RdpGatewayHost,
                GatewayPort = RdpGatewayPort
            };
        }

        if (SelectedType == ConnectionType.SSH)
        {
            conn.SshSettings = new SSHSettings
            {
                AuthType = SshAuthType,
                PrivateKeyPath = string.IsNullOrEmpty(SshKeyPath) ? null : SshKeyPath,
                KeepAliveInterval = SshKeepAlive,
                JumpHost = string.IsNullOrEmpty(SshJumpHost) ? null : SshJumpHost,
                JumpHostPort = SshJumpHostPort,
                JumpHostUsername = string.IsNullOrEmpty(SshJumpHostUsername) ? null : SshJumpHostUsername,
                JumpHostPassword = string.IsNullOrEmpty(SshJumpHostPassword) ? null : SshJumpHostPassword
            };
        }

        _db.SaveConnection(conn);

        if (SavePassword && !string.IsNullOrEmpty(Password))
            CredentialManager.Save(conn.Id, Password);

        if (!string.IsNullOrEmpty(SshJumpHostPassword))
        {
            var jumpCredId = Guid.NewGuid();
            CredentialManager.Save(jumpCredId, SshJumpHostPassword);
            Debug.WriteLine($"Jump host credential saved with ID: {jumpCredId}");
        }
    }
}
