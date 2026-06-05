using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteManager.Helpers;
using RemoteManager.Models;
using RemoteManager.Services;
using Renci.SshNet;
using Microsoft.Win32;

namespace RemoteManager.ViewModels;

public partial class SftpSessionViewModel : SessionTabViewModel
{
    private readonly IDatabaseService _db;
    private readonly ICredentialService _credentialService;
    private SftpClient? _sftpClient;
    private bool _disposed;

    public Connection? Connection { get; set; }

    [ObservableProperty]
    private string _currentPath = "/";

    [ObservableProperty]
    private ObservableCollection<SftpFileViewModel> _files = new();

    [ObservableProperty]
    private SftpFileViewModel? _selectedFile;

    public string Host => Connection?.Host ?? "";
    public int Port => Connection?.Port ?? 22;
    public override string? GetPassword() => CredentialResolver.ResolveCredentials(_credentialService, Connection, ConnectionId).Password;

    public SftpSessionViewModel(IDatabaseService db, ICredentialService credentialService, Connection connection)
    {
        _db = db;
        _credentialService = credentialService;
        Connection = connection;
        ConnectionId = connection.Id;
        Header = "SFTP - " + (string.IsNullOrWhiteSpace(connection.Host) ? connection.Name : connection.Host);
        SessionInfo = $"SFTP {connection.Username}@{connection.Host}:{connection.Port}";
    }

    public override async Task ConnectAsync()
    {
        if (Connection == null || IsConnecting) return;
        IsConnecting = true;
        StatusText = "Connecting SFTP...";

        var creds = CredentialResolver.ResolveCredentials(_credentialService, Connection, ConnectionId);

        try
        {
            await Task.Run(() =>
            {
                var connectionInfo = Connection.SshSettings?.AuthType == SshAuthType.Key && !string.IsNullOrWhiteSpace(Connection.SshSettings.PrivateKeyPath)
                    ? new ConnectionInfo(Host, Port, creds.Username, new PrivateKeyAuthenticationMethod(creds.Username, new PrivateKeyFile(Connection.SshSettings.PrivateKeyPath, _credentialService.LoadAdditional(Connection.Id, "passphrase") ?? creds.Password)))
                    : new ConnectionInfo(Host, Port, creds.Username, new PasswordAuthenticationMethod(creds.Username, creds.Password));

                connectionInfo.Timeout = TimeSpan.FromSeconds(15);
                _sftpClient = new SftpClient(connectionInfo);
                _sftpClient.Connect();
                CurrentPath = _sftpClient.WorkingDirectory;
            });

            IsConnected = true;
            StatusText = $"Connected to {Host}";
            await RefreshFiles();
        }
        catch (Exception ex)
        {
            StatusText = $"SFTP Connection Failed: {ex.Message}";
            Log.Warn($"SFTP connect error: {ex.Message}");
        }
        finally
        {
            IsConnecting = false;
        }
    }

    public override void Disconnect()
    {
        IsConnected = false;
        IsConnecting = false;
        StatusText = "Disconnected";

        var client = _sftpClient;
        _sftpClient = null;

        if (client != null)
        {
            Task.Run(() =>
            {
                try { client.Disconnect(); } catch { }
                try { client.Dispose(); } catch { }
            });
        }
    }

    [RelayCommand]
    private async Task RefreshFiles()
    {
        if (_sftpClient == null || !IsConnected) return;

        try
        {
            StatusText = "Loading directory...";
            var files = await Task.Run(() => _sftpClient.ListDirectory(CurrentPath));
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                Files.Clear();
                foreach (var f in files.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name))
                {
                    if (f.Name == "." || (f.Name == ".." && CurrentPath == "/")) continue;

                    Files.Add(new SftpFileViewModel
                    {
                        Name = f.Name,
                        FullName = f.FullName,
                        IsDirectory = f.IsDirectory,
                        Length = f.Length,
                        LastWriteTime = f.LastWriteTime,
                        Permissions = f.OwnerCanRead ? "r" : "-" // Simple permissions dummy string for UI if needed
                    });
                }
            });
            StatusText = $"Ready - {Files.Count} items";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading directory: {ex.Message}";
            Log.Warn($"SFTP list dir error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task NavigateTo(SftpFileViewModel file)
    {
        if (file == null) return;

        if (file.IsDirectory)
        {
            if (file.Name == "..")
            {
                await NavigateUp();
                return;
            }

            CurrentPath = file.FullName;
            await RefreshFiles();
        }
    }

    [RelayCommand]
    private async Task NavigateUp()
    {
        if (CurrentPath == "/" || string.IsNullOrEmpty(CurrentPath)) return;
        
        int lastSlash = CurrentPath.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            CurrentPath = "/";
        }
        else
        {
            CurrentPath = CurrentPath.Substring(0, lastSlash);
        }
        await RefreshFiles();
    }

    [ObservableProperty]
    private bool _isTransferring;

    [ObservableProperty]
    private double _transferProgress;

    [ObservableProperty]
    private string _transferStatus = "";

    [RelayCommand]
    private async Task DownloadFile(SftpFileViewModel file)
    {
        if (file == null || file.IsDirectory || _sftpClient == null) return;

        var dialog = new SaveFileDialog
        {
            FileName = file.Name,
            Title = "Download File"
        };

        if (dialog.ShowDialog() == true)
        {
            await ExecuteTransferAsync($"Downloading {file.Name}", () =>
            {
                using var stream = File.OpenWrite(dialog.FileName);
                _sftpClient.DownloadFile(file.FullName, stream, downloaded =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        TransferProgress = file.Length > 0 ? (double)downloaded / file.Length * 100 : 0;
                    });
                });
            });
        }
    }

    [RelayCommand]
    public async Task UploadFile(string? localPath = null)
    {
        if (_sftpClient == null || !IsConnected) return;

        if (string.IsNullOrEmpty(localPath))
        {
            var dialog = new OpenFileDialog { Title = "Select File to Upload" };
            if (dialog.ShowDialog() == true) localPath = dialog.FileName;
        }

        if (string.IsNullOrEmpty(localPath)) return;

        var fileName = Path.GetFileName(localPath);
        var remotePath = CurrentPath.EndsWith("/") ? CurrentPath + fileName : CurrentPath + "/" + fileName;

        var fileInfo = new FileInfo(localPath);
        var totalBytes = fileInfo.Length;

        await ExecuteTransferAsync($"Uploading {fileName}", () =>
        {
            using var stream = File.OpenRead(localPath);
            _sftpClient.UploadFile(stream, remotePath, uploaded =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TransferProgress = totalBytes > 0 ? (double)uploaded / totalBytes * 100 : 0;
                });
            });
        });

        await RefreshFiles();
    }

    private async Task ExecuteTransferAsync(string actionName, Action action)
    {
        IsTransferring = true;
        TransferProgress = 0;
        TransferStatus = actionName + "...";
        StatusText = actionName + "...";

        try
        {
            await Task.Run(action);
            StatusText = $"{actionName} completed.";
        }
        catch (Exception ex)
        {
            StatusText = $"{actionName} failed: {ex.Message}";
            MessageBox.Show(StatusText, "Transfer Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsTransferring = false;
        }
    }

    [RelayCommand]
    private async Task DeleteItem(SftpFileViewModel file)
    {
        if (file == null || file.Name == "..") return;

        var result = MessageBox.Show($"Are you sure you want to delete {file.Name}?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            StatusText = $"Deleting {file.Name}...";
            await Task.Run(() =>
            {
                if (file.IsDirectory)
                    _sftpClient?.DeleteDirectory(file.FullName);
                else
                    _sftpClient?.DeleteFile(file.FullName);
            });
            StatusText = $"Deleted {file.Name}";
            await RefreshFiles();
        }
        catch (Exception ex)
        {
            StatusText = $"Delete failed: {ex.Message}";
            MessageBox.Show($"Could not delete item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
