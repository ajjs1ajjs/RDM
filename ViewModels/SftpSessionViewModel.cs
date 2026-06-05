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
        Header = L.Get("Sftp_TabHeader", string.IsNullOrWhiteSpace(connection.Host) ? connection.Name : connection.Host);
        SessionInfo = L.Get("SessionInfo_Sftp", connection.Username, connection.Host, connection.Port);
    }

    public override async Task ConnectAsync()
    {
        if (Connection == null || IsConnecting) return;
        IsConnecting = true;
        StatusText = L.Sftp_Connecting;

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
            StatusText = L.Get("Sftp_Connected", Host);
            await RefreshFiles();
        }
        catch (Exception ex)
        {
            StatusText = L.Get("Sftp_Failed", ex.Message);
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
        StatusText = L.Status_Disconnected;

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
        var client = _sftpClient;
        if (client == null || !IsConnected) return;

        try
        {
            StatusText = L.Sftp_Loading;
            var files = await Task.Run(() => client.ListDirectory(CurrentPath));
            
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
            StatusText = L.Get("Sftp_Ready", Files.Count);
        }
        catch (Exception ex)
        {
            StatusText = L.Get("Sftp_LoadFailed", ex.Message);
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
        var client = _sftpClient;
        if (file == null || file.IsDirectory || client == null) return;

        var dialog = new SaveFileDialog
        {
            FileName = file.Name,
            Title = L.Sftp_DownloadTitle
        };

        if (dialog.ShowDialog() == true)
        {
            await ExecuteTransferAsync($"Downloading {file.Name}", () =>
            {
                using var stream = File.OpenWrite(dialog.FileName);
                client.DownloadFile(file.FullName, stream, downloaded =>
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
        var client = _sftpClient;
        if (client == null || !IsConnected) return;

        if (string.IsNullOrEmpty(localPath))
        {
            var dialog = new OpenFileDialog { Title = L.Sftp_UploadTitle };
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
            client.UploadFile(stream, remotePath, uploaded =>
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
        TransferStatus = $"{actionName}...";
        StatusText = $"{actionName}...";

        try
        {
            await Task.Run(action);
            StatusText = L.Get("Sftp_TransferComplete", actionName);
        }
        catch (Exception ex)
        {
            StatusText = L.Get("Sftp_TransferFailed", actionName, ex.Message);
            MessageBox.Show(StatusText, L.Sftp_TransferErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
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

        var result = MessageBox.Show(L.Get("Sftp_DeleteConfirm", file.Name), L.Sftp_DeleteTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var deleteClient = _sftpClient;
        try
        {
            StatusText = L.Get("Sftp_Deleting", file.Name);
            await Task.Run(() =>
            {
                if (file.IsDirectory)
                    RecursiveDeleteDirectory(deleteClient!, file.FullName);
                else
                    deleteClient?.DeleteFile(file.FullName);
            });
            StatusText = L.Get("Sftp_Deleted", file.Name);
            await RefreshFiles();
        }
        catch (Exception ex)
        {
            StatusText = L.Get("Sftp_DeleteFailed", ex.Message);
            MessageBox.Show(L.Get("Sftp_DeleteError", ex.Message), L.Title_Error, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void RecursiveDeleteDirectory(Renci.SshNet.SftpClient client, string path)
    {
        foreach (var entry in client.ListDirectory(path))
        {
            if (entry.Name == "." || entry.Name == "..")
                continue;

            if (entry.IsDirectory)
                RecursiveDeleteDirectory(client, entry.FullName);
            else
                client.DeleteFile(entry.FullName);
        }
        client.DeleteDirectory(path);
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
