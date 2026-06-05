using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RemoteManager.Helpers;
using RemoteManager.Models;
using RemoteManager.Services;

namespace RemoteManager.ViewModels;

public partial class ConnectionEditViewModel : ObservableObject
{
    private static readonly Regex HostnameRegex = new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$", RegexOptions.Compiled);
    private static readonly Regex IpRegex = new(@"^(\d{1,3}\.){3}\d{1,3}$", RegexOptions.Compiled);
    private static readonly Regex IPv6Regex = new(@"^\[?([0-9a-fA-F:]+)\]?$", RegexOptions.Compiled);

    private readonly IDatabaseService _db;
    private readonly ICredentialService _credentialService;
    private readonly ISettingsService _settings;
    private readonly Connection? _existing;
    private string? _validationError;

    public ConnectionEditViewModel(IDatabaseService db, ICredentialService credentialService, ISettingsService settings)
    {
        _db = db;
        _credentialService = credentialService;
        _settings = settings;
        _existing = null;
        if (int.TryParse(settings.Current?.DefaultRdpPort, out int rdpPort))
        {
            Port = rdpPort;
        }
        else
        {
            Port = 3389;
        }
        LoadGroups();
    }

    public ConnectionEditViewModel(IDatabaseService db, ICredentialService credentialService, ISettingsService settings, Connection existing)
    {
        _db = db;
        _credentialService = credentialService;
        _settings = settings;
        _existing = existing;
        Name = existing.Name ?? "";
        Host = existing.Host ?? "";
        Port = existing.Port;
        UserName = existing.Username ?? "";
        Description = existing.Description ?? "";
        MacAddress = existing.MacAddress ?? "";
        TagsText = existing.Tags != null ? string.Join(", ", existing.Tags) : "";
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
            RdpUseMultimon = existing.RdpSettings.UseMultimon;
        }

        if (existing.SshSettings != null)
        {
            SshAuthType = existing.SshSettings.AuthType;
            SshKeyPath = existing.SshSettings.PrivateKeyPath ?? "";
            SshKeepAlive = existing.SshSettings.KeepAliveInterval;
            SshJumpHost = existing.SshSettings.JumpHost ?? "";
            SshJumpHostPort = existing.SshSettings.JumpHostPort;
            SshJumpHostUsername = existing.SshSettings.JumpHostUsername ?? "";
            SshJumpHostPassword = credentialService.LoadAdditional(existing.Id, "jumphost_password") ?? "";
            SshKeyPassphrase = credentialService.LoadAdditional(existing.Id, "passphrase") ?? "";

            SshPortForwardingEnabled = existing.SshSettings.PortForwarding?.Enabled ?? false;
            SshPortForwardingLocalPort = existing.SshSettings.PortForwarding?.LocalPort ?? 8080;
            SshPortForwardingRemoteHost = existing.SshSettings.PortForwarding?.RemoteHost ?? "127.0.0.1";
            SshPortForwardingRemotePort = existing.SshSettings.PortForwarding?.RemotePort ?? 80;
        }

        if (existing.WebSettings != null)
        {
            WebUrl = existing.WebSettings.Url ?? "https://";
            WebIgnoreCertificateErrors = existing.WebSettings.IgnoreCertificateErrors;
        }

        var password = credentialService.Load(existing.Id);
        if (password != null)
        {
            SavePassword = true;
            _password = password;
        }

        LoadGroups();
    }

    [ObservableProperty]
    private string _name = L.ConnEdit_DefaultName;

    [ObservableProperty]
    private string _host = "";

    [ObservableProperty]
    private int _port = 3389;

    partial void OnSelectedTypeChanged(ConnectionType value)
    {
        if (value == ConnectionType.SSH)
        {
            if (int.TryParse(_settings.Current?.DefaultSshPort, out int sshPort))
            {
                Port = sshPort;
            }
            else
            {
                Port = 22;
            }
        }
        else if (value == ConnectionType.Web)
        {
            Port = 443;
        }
        else
        {
            if (int.TryParse(_settings.Current?.DefaultRdpPort, out int rdpPort))
            {
                Port = rdpPort;
            }
            else
            {
                Port = 3389;
            }
        }
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
    private string _macAddress = "";

    [ObservableProperty]
    private string _tagsText = "";

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
    private bool _rdpUseMultimon;

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
    private string _webUrl = "https://";

    [ObservableProperty]
    private bool _webIgnoreCertificateErrors = false;

    [ObservableProperty]
    private string _sshJumpHostUsername = "";

    [ObservableProperty]
    private string _sshJumpHostPassword = "";

    [ObservableProperty]
    private string _sshKeyPassphrase = "";

    [ObservableProperty]
    private bool _sshPortForwardingEnabled;

    [ObservableProperty]
    private uint _sshPortForwardingLocalPort = 8080;

    [ObservableProperty]
    private string _sshPortForwardingRemoteHost = "127.0.0.1";

    [ObservableProperty]
    private uint _sshPortForwardingRemotePort = 80;

    public string WindowTitle => _existing == null ? L.ConnEdit_TitleNew : L.Get("ConnEdit_TitleEdit", _existing.Name);

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
            ValidationError = L.ConnEdit_NameRequired;
            return false;
        }

        if (SelectedType != ConnectionType.Web && string.IsNullOrWhiteSpace(Host))
        {
            ValidationError = L.ConnEdit_HostRequired;
            return false;
        }

        if (SelectedType == ConnectionType.Web && string.IsNullOrWhiteSpace(WebUrl))
        {
            ValidationError = L.ConnEdit_UrlRequired;
            return false;
        }

        if (SelectedType != ConnectionType.Web)
        {
            var trimmedHost = Host.Trim();
            bool isValidHost = HostnameRegex.IsMatch(trimmedHost) || IpRegex.IsMatch(trimmedHost) || IPv6Regex.IsMatch(trimmedHost);
            if (!isValidHost)
            {
                ValidationError = L.ConnEdit_InvalidHost;
                return false;
            }

            if (IpRegex.IsMatch(trimmedHost))
            {
                var parts = trimmedHost.Split('.');
                foreach (var part in parts)
                {
                    if (int.TryParse(part, out var num) && (num < 0 || num > 255))
                    {
                        ValidationError = L.ConnEdit_InvalidIp;
                        return false;
                    }
                }
            }
        }

        if (Port < 1 || Port > 65535)
        {
            ValidationError = L.ConnEdit_InvalidPort;
            return false;
        }

        if (SelectedType == ConnectionType.SSH && SshAuthType == SshAuthType.Key)
        {
            if (string.IsNullOrEmpty(SshKeyPath))
            {
                ValidationError = L.ConnEdit_KeyRequired;
                return false;
            }
            if (!File.Exists(SshKeyPath))
            {
                ValidationError = L.Get("ConnEdit_KeyNotFound", SshKeyPath);
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
            Title = L.ConnEdit_BrowseKeyTitle
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
        conn.Name = Name?.Trim() ?? "";
        conn.Host = Host?.Trim() ?? "";
        conn.Port = Port;
        conn.Username = UserName?.Trim() ?? "";
        conn.Type = SelectedType;
        conn.GroupId = SelectedGroupId ?? Guid.Empty;
        conn.Description = Description?.Trim() ?? "";
        conn.MacAddress = MacAddress?.Trim() ?? "";
        conn.Tags = string.IsNullOrWhiteSpace(TagsText)
            ? new System.Collections.Generic.List<string>()
            : TagsText.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();

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
                GatewayPort = RdpGatewayPort,
                UseMultimon = RdpUseMultimon
            };
            conn.SshSettings = null;
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
                PrivateKeyPassphrase = null,
                JumpHostPassword = null,
                PortForwarding = new PortForwarding
                {
                    Enabled = SshPortForwardingEnabled,
                    LocalPort = SshPortForwardingLocalPort,
                    RemoteHost = SshPortForwardingRemoteHost,
                    RemotePort = SshPortForwardingRemotePort
                }
            };
            conn.RdpSettings = null;
            conn.WebSettings = null;
        }

        if (SelectedType == ConnectionType.Web)
        {
            conn.WebSettings = new WebSettings
            {
                Url = WebUrl?.Trim() ?? "",
                IgnoreCertificateErrors = WebIgnoreCertificateErrors
            };
            conn.RdpSettings = null;
            conn.SshSettings = null;
        }

        _db.SaveConnection(conn);

        if (SavePassword)
        {
            if (!string.IsNullOrEmpty(Password))
                _credentialService.Save(conn.Id, Password);
        }
        else
        {
            _credentialService.Delete(conn.Id);
        }

        if (SelectedType == ConnectionType.SSH)
        {
            _credentialService.SaveAdditional(conn.Id, "passphrase", SshKeyPassphrase);
            _credentialService.SaveAdditional(conn.Id, "jumphost_password", SshJumpHostPassword);
        }
    }

    [RelayCommand]
    private void GeneratePassword()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()_-+=";
        var password = new string(Enumerable.Repeat(chars, 16)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
        Password = password;
        SavePassword = true;
    }
}
