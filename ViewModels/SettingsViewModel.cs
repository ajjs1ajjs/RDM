using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using RemoteManager.Helpers;
using RemoteManager.Models;
using RemoteManager.Services;

namespace RemoteManager.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDatabaseService _db;
    private readonly IImportExportService _importExport;
    private readonly ICredentialService _credentialService;
    private readonly IMasterPasswordProvider _masterPasswordProvider;

    public SettingsViewModel(ISettingsService settings, IDatabaseService db, IImportExportService importExport, ICredentialService credentialService, IMasterPasswordProvider? masterPasswordProvider = null)
    {
        _settings = settings;
        _db = db;
        _importExport = importExport;
        _credentialService = credentialService;
        _masterPasswordProvider = masterPasswordProvider ?? new MasterPasswordProvider();

        _currentDbPath = settings.Current.DatabasePath;
        _selectedTheme = settings.Current.Theme;
        _autoReconnect = settings.Current.AutoReconnect;
        _minimizeToTray = settings.Current.MinimizeToTray;
        _defaultRdpPort = settings.Current.DefaultRdpPort;
        _defaultSshPort = settings.Current.DefaultSshPort;
        _backupFolderPath = settings.Current.BackupFolderPath;
        _useMasterPassword = settings.Current.UseMasterPassword;

        if (settings.Current.DomainCredentials != null)
        {
            foreach (var cred in settings.Current.DomainCredentials)
            {
                var encryptedCred = _credentialService.LoadDomainCredential(cred.Domain);
                _domainCredentials.Add(new DomainCredentialViewModel
                {
                    Domain = cred.Domain,
                    Username = cred.Username,
                    Password = encryptedCred?.Password ?? string.Empty,
                    OriginalDomain = cred.Domain
                });
            }
        }

        foreach (var snippet in _db.GetAllSnippets())
        {
            _snippets.Add(new SnippetViewModel(snippet));
        }
    }

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<SnippetViewModel> _snippets = new();

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<DomainCredentialViewModel> _domainCredentials = new();

    [ObservableProperty]
    private string _currentDbPath = string.Empty;

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private bool _autoReconnect;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private string _defaultRdpPort = "3389";

    [ObservableProperty]
    private string _defaultSshPort = "22";

    [ObservableProperty]
    private string _backupFolderPath = string.Empty;

    [ObservableProperty]
    private bool _useMasterPassword;

    public string[] Themes { get; } = ["Dark", "Light", "System"];

    [RelayCommand]
    private void ChangeDatabasePath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Database files (*.db)|*.db|All files (*.*)|*.*",
            Title = L.Settings_DbSelectTitle,
            FileName = "RemoteManager.db"
        };

        if (dialog.ShowDialog() == true)
        {
            var newPath = dialog.FileName;

            if (!string.Equals(newPath, CurrentDbPath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(CurrentDbPath))
            {
                var result = System.Windows.MessageBox.Show(
                    L.Settings_DbMigrateMessage,
                    L.Settings_DbMigrateTitle,
                    System.Windows.MessageBoxButton.YesNoCancel,
                    System.Windows.MessageBoxImage.Question
                );

                if (result == System.Windows.MessageBoxResult.Cancel)
                    return;

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    try
                    {
                        _db.MigrateDatabase(newPath);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show(L.Get("Settings_MigrationFailed", ex.Message), L.Title_Error,
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        return;
                    }
                }
            }

            CurrentDbPath = newPath;
            _settings.ChangeDatabasePath(newPath);
            System.Windows.MessageBox.Show(L.Settings_DbPathUpdated,
                L.Title_Settings, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settings.Current.Theme = SelectedTheme;
        _settings.Current.AutoReconnect = AutoReconnect;
        _settings.Current.MinimizeToTray = MinimizeToTray;
        _settings.Current.DefaultRdpPort = DefaultRdpPort;
        _settings.Current.DefaultSshPort = DefaultSshPort;
        _settings.Current.BackupFolderPath = BackupFolderPath;
        _settings.Current.UseMasterPassword = UseMasterPassword;

        _settings.Current.DomainCredentials.Clear();
        foreach (var vm in DomainCredentials)
        {
            _settings.Current.DomainCredentials.Add(new DomainCredential
            {
                Domain = vm.Domain,
                Username = vm.Username
            });

            if (!string.IsNullOrEmpty(vm.OriginalDomain) && vm.Domain != vm.OriginalDomain)
            {
                _credentialService.DeleteDomainCredential(vm.OriginalDomain);
            }

            if (!string.IsNullOrEmpty(vm.Password))
            {
                _credentialService.SaveDomainCredential(vm.Domain, vm.Username, vm.Password);
            }

            vm.OriginalDomain = vm.Domain;
        }

        _settings.Save();

        // Save Snippets
        var existingSnippets = _db.GetAllSnippets();
        var currentIds = Snippets.Select(s => s.Id).ToList();

        // Delete removed snippets
        foreach (var existing in existingSnippets)
        {
            if (!currentIds.Contains(existing.Id))
            {
                _db.DeleteSnippet(existing.Id);
            }
        }

        // Save/Update current snippets
        foreach (var snippetVm in Snippets)
        {
            _db.SaveSnippet(snippetVm.ToModel());
        }

        App.ApplyTheme(SelectedTheme);

        if (System.Windows.Application.Current is App app && app.MainWindow != null)
        {
            app.UpdateTrayIcon(app.MainWindow, MinimizeToTray);
        }

        System.Windows.MessageBox.Show(L.Settings_Saved, L.Title_Success,
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ChangeMasterPassword(System.Windows.Controls.PasswordBox pwdBox)
    {
        var pwd = pwdBox?.Password;
        if (string.IsNullOrEmpty(pwd))
        {
            System.Windows.MessageBox.Show(L.MasterPwd_Empty, L.Title_Error, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        _settings.Current.MasterPasswordHash = RemoteManager.Helpers.CryptoHelper.HashPassword(pwd);
        _masterPasswordProvider.CurrentMasterPassword = pwd;
        UseMasterPassword = true;
        _settings.Current.UseMasterPassword = true;
        _settings.Save();

        System.Windows.MessageBox.Show(L.MasterPwd_Changed, L.Title_Success, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        pwdBox?.Clear();
    }

    [RelayCommand]
    private void ChangeBackupFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = L.Settings_BackupFolderTitle
        };

        if (dialog.ShowDialog() == true)
        {
            BackupFolderPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void ClearBackupFolder()
    {
        BackupFolderPath = string.Empty;
    }

    public event System.Action? ImportCompleted;

    [RelayCommand]
    private void AddDomainCredential()
    {
        DomainCredentials.Add(new DomainCredentialViewModel { Domain = "DOMAIN", Username = "username", Password = "" });
    }

    [RelayCommand]
    private void DeleteDomainCredential(DomainCredentialViewModel cred)
    {
        if (cred != null)
        {
            if (!string.IsNullOrEmpty(cred.Domain))
            {
                _credentialService.DeleteDomainCredential(cred.Domain);
            }
            DomainCredentials.Remove(cred);
        }
    }

    [RelayCommand]
    private void AddSnippet()
    {
        Snippets.Add(new SnippetViewModel(new Snippet { Name = "New Snippet", Command = "echo 'Hello'" }));
    }

    [RelayCommand]
    private void DeleteSnippet(SnippetViewModel snippet)
    {
        if (snippet != null)
        {
            Snippets.Remove(snippet);
        }
    }

    [RelayCommand]
    private async Task ImportConnections()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "All Supported Files (*.json, *.xml, *.rdm)|*.json;*.xml;*.rdm|JSON files (*.json)|*.json|RDM XML files (*.xml, *.rdm)|*.xml;*.rdm|All files (*.*)|*.*",
            Title = L.Import_FileDialogTitle
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var preview = await _importExport.PreviewImportAsync(dialog.FileName);
                var (groupsPreview, connsPreview) = Helpers.ImportPreviewHelper.BuildPreviewParts(preview);

                var msg = L.Get("Import_PreviewMessage",
                    preview.GroupCount, preview.ConnectionCount, groupsPreview, connsPreview);
                var result = System.Windows.MessageBox.Show(msg,
                    L.Import_PreviewTitle,
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question
                );

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await _importExport.ImportFromFileAsync(dialog.FileName);
                    ImportCompleted?.Invoke();
                    System.Windows.MessageBox.Show(L.Import_SuccessWithDetails, L.Title_Success, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show(L.Get("Import_Failed", ex.Message), L.Title_Error, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async Task ExportConnections()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = L.Export_FileDialogTitle,
            FileName = $"RemoteManager_export_{System.DateTime.Now:yyyyMMdd}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _importExport.ExportToFileAsync(dialog.FileName);
                System.Windows.MessageBox.Show(L.Export_Success, L.Title_Success, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show(L.Get("Export_Failed", ex.Message), L.Title_Error, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async Task ExportEncryptedBackup()
    {
        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Encrypted Backup files (*.enc)|*.enc|All files (*.*)|*.*",
            Title = L.Backup_ExportTitle,
            FileName = $"RemoteManager_backup_{System.DateTime.Now:yyyyMMdd}.enc"
        };

        if (saveDialog.ShowDialog() == true)
        {
            var passwordDialog = new Views.InputDialog(L.Backup_PasswordDialog)
            {
                Title = L.Backup_PasswordDialogTitle
            };

            if (passwordDialog.ShowDialog() == true)
            {
                var password = passwordDialog.Value;
                if (string.IsNullOrEmpty(password))
                {
                    System.Windows.MessageBox.Show(L.Backup_PasswordEmpty, L.Title_Warning, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    await _importExport.ExportEncryptedAsync(saveDialog.FileName, password);
                    System.Windows.MessageBox.Show(L.Backup_ExportSuccess, L.Title_Success, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (System.Exception ex)
                {
                    System.Windows.MessageBox.Show(L.Get("Backup_ExportFailed", ex.Message), L.Title_Error, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
    }

    [RelayCommand]
    private async Task ImportEncryptedBackup()
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Encrypted Backup files (*.enc)|*.enc|All files (*.*)|*.*",
            Title = L.Backup_ImportTitle
        };

        if (openDialog.ShowDialog() == true)
        {
            var passwordDialog = new Views.InputDialog(L.Backup_ImportPasswordDialog)
            {
                Title = L.Backup_ImportPasswordTitle
            };

            if (passwordDialog.ShowDialog() == true)
            {
                var password = passwordDialog.Value;
                try
                {
                    var preview = await _importExport.PreviewImportEncryptedAsync(openDialog.FileName, password);
                    var (groupsPreview, connsPreview) = Helpers.ImportPreviewHelper.BuildPreviewParts(preview);

                    var msg = L.Get("Import_PreviewEncryptedMessage",
                        preview.GroupCount, preview.ConnectionCount, groupsPreview, connsPreview);
                    var result = System.Windows.MessageBox.Show(msg,
                        L.Import_PreviewTitle,
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question
                    );

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        await _importExport.ImportEncryptedAsync(openDialog.FileName, password);
                        ImportCompleted?.Invoke();
                        System.Windows.MessageBox.Show(L.Backup_ImportSuccess, L.Title_Success, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    System.Windows.MessageBox.Show(L.Backup_DecryptFailed, L.Title_Error, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                catch (System.Exception ex)
                {
                    System.Windows.MessageBox.Show(L.Get("Import_Failed", ex.Message), L.Title_Error, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
    }
}

public partial class DomainCredentialViewModel : ObservableObject
{
    [ObservableProperty]
    private string _domain = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    public string? OriginalDomain { get; set; }
}
