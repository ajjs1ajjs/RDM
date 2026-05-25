using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using RemoteManager.Services;
using RemoteManager.Models;

namespace RemoteManager.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IDatabaseService _db;
    private readonly ImportExportService _importExport;

    public SettingsViewModel(SettingsService settings, IDatabaseService db)
    {
        _settings = settings;
        _db = db;
        _importExport = new ImportExportService(db);

        _currentDbPath = settings.Current.DatabasePath;
        _selectedTheme = settings.Current.Theme;
        _autoReconnect = settings.Current.AutoReconnect;
        _minimizeToTray = settings.Current.MinimizeToTray;
        _defaultRdpPort = settings.Current.DefaultRdpPort;
        _defaultSshPort = settings.Current.DefaultSshPort;
        _backupFolderPath = settings.Current.BackupFolderPath;

        if (settings.Current.DomainCredentials != null)
        {
            foreach (var cred in settings.Current.DomainCredentials)
            {
                var encryptedCred = CredentialManager.LoadDomainCredential(cred.Domain);
                _domainCredentials.Add(new DomainCredentialViewModel
                {
                    Domain = cred.Domain,
                    Username = cred.Username,
                    Password = encryptedCred?.Password ?? string.Empty,
                    OriginalDomain = cred.Domain
                });
            }
        }
    }

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

    public string[] Themes { get; } = ["Dark", "Light"];

    [RelayCommand]
    private void ChangeDatabasePath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Database files (*.db)|*.db|All files (*.*)|*.*",
            Title = "Select database location",
            FileName = "RemoteManager.db"
        };

        if (dialog.ShowDialog() == true)
        {
            var newPath = dialog.FileName;

            if (!string.Equals(newPath, CurrentDbPath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(CurrentDbPath))
            {
                var result = System.Windows.MessageBox.Show(
                    "Move existing database to the new location?",
                    "Migrate Database",
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
                        System.Windows.MessageBox.Show($"Migration failed: {ex.Message}", "Error",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        return;
                    }
                }
            }

            CurrentDbPath = newPath;
            _settings.ChangeDatabasePath(newPath);
            System.Windows.MessageBox.Show("Database path updated! Restart the application for changes to take full effect.",
                "Settings", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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
                CredentialManager.DeleteDomainCredential(vm.OriginalDomain);
            }

            if (!string.IsNullOrEmpty(vm.Password))
            {
                CredentialManager.SaveDomainCredential(vm.Domain, vm.Username, vm.Password);
            }

            vm.OriginalDomain = vm.Domain;
        }

        _settings.Save();

        App.ApplyTheme(SelectedTheme);

        if (System.Windows.Application.Current is App app && app.MainWindow != null)
        {
            app.UpdateTrayIcon(app.MainWindow, MinimizeToTray);
        }

        System.Windows.MessageBox.Show("Settings saved!", "Success",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ChangeBackupFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select backup folder"
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
                CredentialManager.DeleteDomainCredential(cred.Domain);
            }
            DomainCredentials.Remove(cred);
        }
    }

    [RelayCommand]
    private void ImportConnections()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "All Supported Files (*.json, *.xml, *.rdm)|*.json;*.xml;*.rdm|JSON files (*.json)|*.json|RDM XML files (*.xml, *.rdm)|*.xml;*.rdm|All files (*.*)|*.*",
            Title = "Import connections"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var preview = _importExport.PreviewImport(dialog.FileName);
                var groupsPreview = preview.Groups.Count <= 8
                    ? string.Join("\n", preview.Groups)
                    : string.Join("\n", preview.Groups.Take(8)) + $"\n... (and {preview.Groups.Count - 8} more)";

                var connsPreview = preview.Connections.Count <= 12
                    ? string.Join("\n", preview.Connections)
                    : string.Join("\n", preview.Connections.Take(12)) + $"\n... (and {preview.Connections.Count - 12} more)";

                var result = System.Windows.MessageBox.Show(
                    $"Found {preview.GroupCount} groups and {preview.ConnectionCount} connections.\n\n" +
                    $"Groups Preview:\n{groupsPreview}\n\n" +
                    $"Connections Preview:\n{connsPreview}\n\n" +
                    $"Do you want to import all of them?",
                    "Import Preview",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question
                );

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    _importExport.ImportFromFile(dialog.FileName);
                    ImportCompleted?.Invoke();
                    System.Windows.MessageBox.Show("Import completed successfully!", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Import failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void ExportConnections()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Export connections",
            FileName = $"RemoteManager_export_{System.DateTime.Now:yyyyMMdd}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _importExport.ExportToFile(dialog.FileName);
                System.Windows.MessageBox.Show("Export completed!", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void ExportEncryptedBackup()
    {
        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Encrypted Backup files (*.enc)|*.enc|All files (*.*)|*.*",
            Title = "Export Secure Backup (with Passwords)",
            FileName = $"RemoteManager_backup_{System.DateTime.Now:yyyyMMdd}.enc"
        };

        if (saveDialog.ShowDialog() == true)
        {
            var passwordDialog = new Views.InputDialog("Enter a password to encrypt your connection profiles and passwords:")
            {
                Title = "Set Backup Password"
            };

            if (passwordDialog.ShowDialog() == true)
            {
                var password = passwordDialog.Value;
                if (string.IsNullOrEmpty(password))
                {
                    System.Windows.MessageBox.Show("Password cannot be empty. Export canceled.", "Warning", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    _importExport.ExportEncrypted(saveDialog.FileName, password);
                    System.Windows.MessageBox.Show("Secure backup completed successfully!", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (System.Exception ex)
                {
                    System.Windows.MessageBox.Show($"Secure backup failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
    }

    [RelayCommand]
    private void ImportEncryptedBackup()
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Encrypted Backup files (*.enc)|*.enc|All files (*.*)|*.*",
            Title = "Import Secure Backup"
        };

        if (openDialog.ShowDialog() == true)
        {
            var passwordDialog = new Views.InputDialog("Enter the password to decrypt the backup file:")
            {
                Title = "Enter Backup Password"
            };

            if (passwordDialog.ShowDialog() == true)
            {
                var password = passwordDialog.Value;
                try
                {
                    var preview = _importExport.PreviewImportEncrypted(openDialog.FileName, password);

                    var groupsPreview = preview.Groups.Count <= 8
                        ? string.Join("\n", preview.Groups)
                        : string.Join("\n", preview.Groups.Take(8)) + $"\n... (and {preview.Groups.Count - 8} more)";

                    var connsPreview = preview.Connections.Count <= 12
                        ? string.Join("\n", preview.Connections)
                        : string.Join("\n", preview.Connections.Take(12)) + $"\n... (and {preview.Connections.Count - 12} more)";

                    var result = System.Windows.MessageBox.Show(
                        $"Decrypted successfully!\nFound {preview.GroupCount} groups and {preview.ConnectionCount} connections.\n\n" +
                        $"Groups Preview:\n{groupsPreview}\n\n" +
                        $"Connections Preview:\n{connsPreview}\n\n" +
                        $"Do you want to import all of them?",
                        "Import Preview",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question
                    );

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        _importExport.ImportEncrypted(openDialog.FileName, password);
                        ImportCompleted?.Invoke();
                        System.Windows.MessageBox.Show("Secure import completed successfully!", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    System.Windows.MessageBox.Show("Failed to decrypt the file. Please verify the password.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                catch (System.Exception ex)
                {
                    System.Windows.MessageBox.Show($"Import failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
