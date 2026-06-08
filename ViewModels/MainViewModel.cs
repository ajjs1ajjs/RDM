using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RemoteManager.Helpers;
using RemoteManager.Models;
using RemoteManager.Services;
using RemoteManager.Views;

namespace RemoteManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly ISettingsService _settings;
    private readonly IImportExportService _importExport;
    private readonly ICredentialService _credentialService;
    private readonly IPingService _pingService;
    private string? _pendingImportPath;
    private string? _pendingImportPassword;

    public MainViewModel(IDatabaseService db, ISettingsService settings, IImportExportService importExport, ICredentialService credentialService, IPingService pingService)
    {
        _db = db;
        _settings = settings;
        _importExport = importExport;
        _credentialService = credentialService;
        _pingService = pingService;

        if (settings.IsFirstRun)
        {
            PerformFirstRunSetup(settings);
        }

        _db.Initialize(settings.Current.DatabasePath);

        SyncTabSelectionCommand = new RelayCommand(OnSyncTabSelection);
        LoadData();
        _ = ImportPendingAsync();
        _pingService.StartMonitoring(OnPingStatusUpdated);

        OpenTabs.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(IsDashboardVisible));
            OnPropertyChanged(nameof(ActiveSessionsCount));
            OnPropertyChanged(nameof(PendingSessionsCount));
            OnPropertyChanged(nameof(Latency));
            OnPropertyChanged(nameof(ActiveSessionsAngle));
            OnPropertyChanged(nameof(PendingSessionsAngle));
            OnPropertyChanged(nameof(LatencyAngle));

            if (e.NewItems != null)
            {
                foreach (SessionTabViewModel tab in e.NewItems)
                {
                    tab.CloseRequested += OnTabCloseRequested;
                    tab.PropertyChanged += OnTabPropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (SessionTabViewModel tab in e.OldItems)
                {
                    tab.CloseRequested -= OnTabCloseRequested;
                    tab.PropertyChanged -= OnTabPropertyChanged;
                }
            }
        };
    }

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is SessionTabViewModel tab)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                CloseTab(tab);
            });
        }
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionTabViewModel.IsConnected) ||
            e.PropertyName == nameof(SessionTabViewModel.IsConnecting))
        {
            OnPropertyChanged(nameof(ActiveSessionsCount));
            OnPropertyChanged(nameof(PendingSessionsCount));
            OnPropertyChanged(nameof(Latency));
            OnPropertyChanged(nameof(ActiveSessionsAngle));
            OnPropertyChanged(nameof(PendingSessionsAngle));
            OnPropertyChanged(nameof(LatencyAngle));
        }
    }

    private void OnPingStatusUpdated(Guid connectionId, PingStatus status, long latency)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var connItem = FindConnectionItem(connectionId);
            if (connItem != null)
            {
                connItem.PingStatus = status;
                connItem.PingLatency = latency;
            }

            var recentItem = RecentConnections.FirstOrDefault(r => r.Connection?.Id == connectionId);
            if (recentItem != null)
            {
                recentItem.PingStatus = status;
                recentItem.PingLatency = latency;
            }
        });
    }

    private ConnectionItemViewModel? FindConnectionItem(Guid id)
    {
        foreach (var group in Groups)
        {
            var item = FindInGroup(group, id);
            if (item != null) return item;
        }
        return null;
    }

    private ConnectionItemViewModel? FindInGroup(TreeEntryViewModel entry, Guid id)
    {
        if (entry is ConnectionItemViewModel c && c.Connection?.Id == id) return c;
        if (entry is GroupViewModel g)
        {
            foreach (var child in g.Children)
            {
                var found = FindInGroup(child, id);
                if (found != null) return found;
            }
        }
        return null;
    }

    public bool IsDashboardVisible => OpenTabs.Count == 0;
    public int ActiveSessionsCount => OpenTabs.Count(t => t.IsConnected);
    public int PendingSessionsCount => OpenTabs.Count(t => t.IsConnecting);
    public int Latency
    {
        get
        {
            if (ActiveSessionsCount == 0 && !Groups.Any())
                return 0;

            long maxLatency = 0;
            foreach (var group in Groups)
            {
                CollectMaxLatency(group, ref maxLatency);
            }
            return (int)Math.Min(maxLatency, int.MaxValue);
        }
    }

    private static void CollectMaxLatency(TreeEntryViewModel entry, ref long maxLatency)
    {
        if (entry is ConnectionItemViewModel c && c.PingStatus == PingStatus.Online)
        {
            if (c.PingLatency > maxLatency)
                maxLatency = c.PingLatency;
        }
        if (entry is GroupViewModel g)
        {
            foreach (var child in g.Children)
                CollectMaxLatency(child, ref maxLatency);
        }
    }

    public double ActiveSessionsAngle => Math.Clamp(-90.0 + (ActiveSessionsCount * 22.5), -90.0, 90.0);
    public double PendingSessionsAngle => Math.Clamp(-90.0 + (PendingSessionsCount * 45.0), -90.0, 90.0);
    public double LatencyAngle => Math.Clamp(-90.0 + (Latency * 3.0), -90.0, 90.0);

    [ObservableProperty]
    private ObservableCollection<ConnectionItemViewModel> _recentConnections = new();

    private void RefreshRecentConnections()
    {
        RecentConnections.Clear();
        var recent = _db.GetAllConnections()
            .Where(c => c.LastConnectedAt.HasValue)
            .OrderByDescending(c => c.LastConnectedAt)
            .Take(3);

        foreach (var conn in recent)
        {
            RecentConnections.Add(new ConnectionItemViewModel
            {
                Connection = conn,
                Name = conn.Name,
                Host = conn.Host,
                Port = conn.Port,
                Type = conn.Type,
                Description = conn.Description,
                Tags = conn.Tags ?? new System.Collections.Generic.List<string>()
            });
        }
    }

    [ObservableProperty]
    private ObservableCollection<GroupViewModel> _groups = new();

    [ObservableProperty]
    private ObservableCollection<SessionTabViewModel> _openTabs = new();

    [ObservableProperty]
    private object? _selectedTab;

    partial void OnSelectedTabChanged(object? value)
    {
        foreach (var tab in OpenTabs)
        {
            tab.IsSelected = (tab == value);
        }
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    [ObservableProperty]
    private bool _isSearchEnabled = true;

    partial void OnSearchTextChanged(string value)
    {
        FilterConnections(value);
    }

    private void LoadData()
    {
        var expandedState = new Dictionary<Guid, bool>();
        foreach (var group in Groups)
            CollectExpandedState(group, expandedState);

        Groups.Clear();
        var allGroups = _db.GetAllGroups();
        var allConnections = _db.GetAllConnections();

        var groupMap = new Dictionary<Guid, GroupViewModel>();

        var ungroupedVm = new GroupViewModel { Id = Guid.Empty, Name = L.Group_Ungrouped, IsVisible = false };

        foreach (var group in allGroups)
        {
            var vm = new GroupViewModel
            {
                Id = group.Id,
                Name = group.Name,
                ParentId = group.ParentId,
                IsExpanded = expandedState.TryGetValue(group.Id, out var expanded) && expanded
            };
            groupMap[group.Id] = vm;
        }

        foreach (var group in allGroups)
        {
            var vm = groupMap[group.Id];
            if (group.ParentId.HasValue && groupMap.TryGetValue(group.ParentId.Value, out var parentVm))
            {
                parentVm.Children.Add(vm);
            }
            else
            {
                Groups.Add(vm);
            }
        }

        foreach (var conn in allConnections)
        {
            var itemVm = new ConnectionItemViewModel
            {
                Connection = conn,
                Name = conn.Name,
                Host = conn.Host,
                Port = conn.Port,
                Type = conn.Type,
                Description = conn.Description,
                Tags = conn.Tags ?? new System.Collections.Generic.List<string>()
            };

            _pingService.RegisterConnection(conn.Id, conn.Host, conn.Port);

            if (conn.GroupId != Guid.Empty && groupMap.TryGetValue(conn.GroupId, out var groupVm))
            {
                groupVm.Children.Add(itemVm);
            }
            else
            {
                ungroupedVm.Children.Add(itemVm);
                ungroupedVm.IsVisible = true;
            }
        }

        if (ungroupedVm.IsVisible)
        {
            Groups.Add(ungroupedVm);
        }

        RefreshRecentConnections();
    }

    private void CollectExpandedState(GroupViewModel group, Dictionary<Guid, bool> state)
    {
        state[group.Id] = group.IsExpanded;
        foreach (var child in group.Children.OfType<GroupViewModel>())
            CollectExpandedState(child, state);
    }

    private void FilterConnections(string search)
    {
        foreach (var group in Groups)
        {
            FilterEntry(group, search);
        }
    }

    private bool FilterEntry(TreeEntryViewModel entry, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            entry.IsVisible = true;
            if (entry is GroupViewModel group)
            {
                foreach (var child in group.Children)
                {
                    FilterEntry(child, search);
                }
            }
            return true;
        }

        if (entry is ConnectionItemViewModel connItem)
        {
            var matches = (connItem.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                       || (connItem.Host ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                       || (connItem.Description ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                       || (connItem.Tags != null && connItem.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase)));
            connItem.IsVisible = matches;
            return matches;
        }

        if (entry is GroupViewModel g)
        {
            var anyChildVisible = false;
            foreach (var child in g.Children)
            {
                if (FilterEntry(child, search))
                {
                    anyChildVisible = true;
                }
            }

            var groupMatches = (g.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase);
            g.IsVisible = groupMatches || anyChildVisible;

            if (groupMatches && !anyChildVisible)
            {
                foreach (var child in g.Children)
                {
                    MakeAllVisible(child);
                }
            }

            return g.IsVisible;
        }

        return false;
    }

    private void MakeAllVisible(TreeEntryViewModel entry)
    {
        entry.IsVisible = true;
        if (entry is GroupViewModel group)
        {
            foreach (var child in group.Children)
            {
                MakeAllVisible(child);
            }
        }
    }

    [RelayCommand]
    private void OpenConnection(ConnectionItemViewModel? item)
    {
        if (item?.Connection == null) return;

        lock (_openTabsLock)
        {
            var existing = OpenTabs.FirstOrDefault(t => t.ConnectionId == item.Connection.Id);
            if (existing != null)
            {
                SelectedTab = existing;
                return;
            }

            SessionTabViewModel tab;
            if (item.Connection.Type == ConnectionType.RDP)
            {
                tab = new RdpSessionViewModel(_db, _credentialService, _settings, item.Connection);
            }
            else if (item.Connection.Type == ConnectionType.Web)
            {
                tab = new WebSessionViewModel(item.Connection);
            }
            else
            {
                tab = new SshSessionViewModel(_db, _credentialService, _settings, item.Connection);
            }

            PropertyChangedEventHandler handler = (s, e) =>
            {
                if (e.PropertyName == nameof(SessionTabViewModel.IsConnected))
                    item.IsConnected = tab.IsConnected;
            };

            tab.PropertyChanged += handler;
            _tabHandlers[tab] = (item, handler);

            // Update LastConnectedAt
            item.Connection.LastConnectedAt = DateTime.UtcNow;
            _db.SaveConnection(item.Connection);

            RefreshRecentConnections();

            OpenTabs.Add(tab);
            SelectedTab = tab;
        }
    }

    [RelayCommand]
    private void OpenSftp(ConnectionItemViewModel? item)
    {
        if (item?.Connection == null || item.Connection.Type != ConnectionType.SSH) return;

        lock (_openTabsLock)
        {
            var existing = OpenTabs.OfType<SftpSessionViewModel>().FirstOrDefault(t => t.ConnectionId == item.Connection.Id);
            if (existing != null)
            {
                SelectedTab = existing;
                return;
            }

            var tab = new SftpSessionViewModel(_db, _credentialService, item.Connection);

            PropertyChangedEventHandler handler = (s, e) =>
            {
                // SFTP tab connection state doesn't necessarily dictate the main connection state,
                // but we can bind it if we want. For now, no strict binding required.
            };

            tab.PropertyChanged += handler;
            _tabHandlers[tab] = (item, handler);

            OpenTabs.Add(tab);
            SelectedTab = tab;
            _ = tab.ConnectAsync(); // auto connect SFTP
        }
    }

    private readonly Dictionary<SessionTabViewModel, (ConnectionItemViewModel Item, PropertyChangedEventHandler Handler)> _tabHandlers = new();

    private readonly object _openTabsLock = new();

    [RelayCommand]
    private void CloseTab(SessionTabViewModel? tab)
    {
        if (tab == null) return;
        if (tab.IsConnected)
        {
            var confirm = System.Windows.MessageBox.Show(
                L.Get("Tab_CloseConfirm", tab.Header),
                L.Tab_CloseTitle,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
        }

        // Unsubscribe PropertyChanged handler to prevent memory leak
        if (_tabHandlers.Remove(tab, out var entry))
        {
            tab.PropertyChanged -= entry.Handler;
            entry.Item.IsConnected = false;
        }

        tab.Disconnect();
        tab.Dispose();

        var idx = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);
        if (OpenTabs.Count > 0)
            SelectedTab = OpenTabs[Math.Min(idx, OpenTabs.Count - 1)];
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var settingsVm = new SettingsViewModel(_settings, _db, _importExport, _credentialService);
        settingsVm.ImportCompleted += () => { LoadData(); };
        OpenTabs.Add(new SettingsTabViewModel(settingsVm));
        SelectedTab = OpenTabs.Last();
    }

    [RelayCommand]
    private void AddGroup()
    {
        var dialog = new InputDialog(L.Group_AddDialog_Message, $"Group {Groups.Count + 1}")
        {
            Title = L.Group_AddDialog_Title
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Value))
        {
            var group = new ConnectionGroup
            {
                Name = dialog.Value.Trim(),
                SortOrder = Groups.Count
            };
            _db.SaveGroup(group);

            Groups.Add(new GroupViewModel
            {
                Id = group.Id,
                Name = group.Name,
                IsExpanded = false
            });
        }
    }

    [RelayCommand]
    private void AddConnection(string? typeStr)
    {
        var type = typeStr == "SSH" ? ConnectionType.SSH : (typeStr == "Web" ? ConnectionType.Web : ConnectionType.RDP);
        var vm = new ConnectionEditViewModel(_db, _credentialService, _settings) { SelectedType = type };

        var dialog = new ConnectionEditDialog(vm);
        if (dialog.ShowDialog() == true)
        {
            RefreshTree();
        }
    }

    [RelayCommand]
    private async Task WakeUp(ConnectionItemViewModel? item)
    {
        if (item?.Connection == null || string.IsNullOrWhiteSpace(item.Connection.MacAddress))
        {
            System.Windows.MessageBox.Show(L.WoL_NotConfigured, L.WoL_Title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            await WakeOnLanService.WakeUpAsync(item.Connection.MacAddress);
            System.Windows.MessageBox.Show(L.Get("WoL_Success", item.Connection.MacAddress), L.WoL_Title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(L.Get("WoL_Failed", ex.Message), L.Title_Error,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void EditConnection(ConnectionItemViewModel? item)
    {
        if (item?.Connection == null) return;
        var freshConn = _db.GetConnection(item.Connection.Id);
        if (freshConn == null) return;

        var vm = new ConnectionEditViewModel(_db, _credentialService, _settings, freshConn);
        var dialog = new ConnectionEditDialog(vm);
        if (dialog.ShowDialog() == true)
        {
            RefreshTree();
        }
    }

    [RelayCommand]
    private void DeleteConnection(ConnectionItemViewModel? item)
    {
        if (item?.Connection == null) return;

        var result = System.Windows.MessageBox.Show(
            L.Get("Conn_DeleteConfirm", item.Name),
            L.Conn_DeleteTitle,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _pingService.UnregisterConnection(item.Connection.Id);
            _db.DeleteConnection(item.Connection.Id);
            RefreshTree();
        }
    }

    [RelayCommand]
    private void DuplicateConnection(ConnectionItemViewModel? item)
    {
        if (item?.Connection == null) return;
        var original = _db.GetConnection(item.Connection.Id);
        if (original == null) return;

        // Use Newtonsoft.Json for a quick "deep copy" to avoid missing new properties
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(original);
        var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<Connection>(json);

        if (copy == null) return;

        copy.Id = Guid.NewGuid();
        copy.Name += L.Conn_DuplicateSuffix;
        copy.CreatedAt = DateTime.UtcNow;
        copy.ModifiedAt = DateTime.UtcNow;

        _db.SaveConnection(copy);

        // Copy credential if it exists
        var existingPassword = _credentialService.Load(original.Id);
        if (existingPassword != null)
        {
            _credentialService.Save(copy.Id, existingPassword);
        }

        // Copy SSH additional credentials (passphrase, jump host password)
        if (original.Type == ConnectionType.SSH && original.SshSettings != null)
        {
            var passphrase = _credentialService.LoadAdditional(original.Id, "passphrase");
            if (passphrase != null)
                _credentialService.SaveAdditional(copy.Id, "passphrase", passphrase);

            var jumpHostPassword = _credentialService.LoadAdditional(original.Id, "jumphost_password");
            if (jumpHostPassword != null)
                _credentialService.SaveAdditional(copy.Id, "jumphost_password", jumpHostPassword);
        }

        RefreshTree();
    }

    [RelayCommand]
    private void RenameGroup(GroupViewModel? group)
    {
        if (group == null || group.Id == Guid.Empty) return;
        var dbGroup = _db.GetGroup(group.Id);
        if (dbGroup == null) return;

        var dialog = new InputDialog(L.Group_RenameDialog, group.Name);
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Value)
            && dialog.Value != group.Name)
        {
            dbGroup.Name = dialog.Value;
            _db.SaveGroup(dbGroup);
            RefreshTree();
        }
    }

    [RelayCommand]
    private void DeleteGroup(GroupViewModel? group)
    {
        if (group == null || group.Id == Guid.Empty) return;

        var result = System.Windows.MessageBox.Show(
            L.Get("Group_DeleteConfirm", group.Name),
            L.Group_DeleteTitle,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _db.DeleteGroup(group.Id);
            RefreshTree();
        }
    }

    public IDatabaseService GetDb() => _db;

    [RelayCommand]
    private void Refresh()
    {
        RefreshTree();
    }

    [RelayCommand]
    private void FocusQuickConnect()
    {
        // Handled in view code-behind
    }

    [RelayCommand]
    private void CloseCurrentTab()
    {
        if (SelectedTab is SessionTabViewModel tab)
            CloseTab(tab);
    }

    private void RefreshTree()
    {
        LoadData();
    }

    private async Task ImportPendingAsync()
    {
        if (string.IsNullOrEmpty(_pendingImportPath))
            return;

        try
        {
            if (!string.IsNullOrEmpty(_pendingImportPassword))
                await _importExport.ImportEncryptedAsync(_pendingImportPath, _pendingImportPassword);
            else
                await _importExport.ImportFromFileAsync(_pendingImportPath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(L.Get("Import_Failed", ex.Message), L.Title_Error, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    public ICommand SyncTabSelectionCommand { get; private set; }

    private void OnSyncTabSelection()
    {
        // Automatically select the most recently opened tab or keep the current selection
        if (OpenTabs.Count > 0)
        {
            // If SelectedTab is not in OpenTabs, select the last one
            if (OpenTabs.FirstOrDefault(t => t == SelectedTab) == null)
            {
                SelectedTab = OpenTabs.Last();
            }
        }
    }

    [RelayCommand]
    private void NewRdp()
    {
        var conn = new Connection
        {
            Name = "Quick RDP",
            Host = "192.168.1.100",
            Port = 3389,
            Type = ConnectionType.RDP,
            RdpSettings = new RDPSettings()
        };

        var tab = new RdpSessionViewModel(_db, _credentialService, _settings, conn);
        OpenTabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void NewSsh()
    {
        var conn = new Connection
        {
            Name = "Quick SSH",
            Host = "192.168.1.100",
            Port = 22,
            Type = ConnectionType.SSH,
            SshSettings = new SSHSettings()
        };

        var tab = new SshSessionViewModel(_db, _credentialService, _settings, conn);
        OpenTabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private async Task ShowImport()
    {
        var dialog = new OpenFileDialog
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
                    LoadData();
                    System.Windows.MessageBox.Show(L.Import_Success, L.Title_Success, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(L.Get("Import_Failed", ex.Message), L.Title_Error, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async Task ShowExport()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = L.Export_FileDialogTitle,
            FileName = $"RemoteManager_export_{DateTime.Now:yyyyMMdd}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _importExport.ExportToFileAsync(dialog.FileName);
                System.Windows.MessageBox.Show(L.Export_Success, L.Title_Success, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(L.Get("Export_Failed", ex.Message), L.Title_Error, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    private void PerformFirstRunSetup(ISettingsService settings)
    {
        var importType = System.Windows.MessageBox.Show(
            L.FirstRun_Message,
            L.FirstRun_Title,
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question);

        bool restoredFromFolder = false;

        if (importType == System.Windows.MessageBoxResult.Yes)
        {
            var folderDlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = L.FirstRun_RestoreFolderTitle
            };
            if (folderDlg.ShowDialog() == true)
            {
                try
                {
                    SettingsService.RestoreBackup(folderDlg.FolderName, settings);
                    restoredFromFolder = true;
                    System.Windows.MessageBox.Show(
                        L.FirstRun_RestoreSuccess,
                        L.Title_Success,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        L.Get("FirstRun_RestoreFailed", ex.Message),
                        L.Title_Error,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
        }
        else if (importType == System.Windows.MessageBoxResult.No)
        {
            var openDlg = new OpenFileDialog
            {
                Filter = L.FirstRun_ImportFilter,
                Title = L.FirstRun_ImportTitle
            };

            if (openDlg.ShowDialog() == true)
            {
                var fileName = openDlg.FileName;
                if (fileName.EndsWith(".enc", System.StringComparison.OrdinalIgnoreCase))
                {
                    var pwdDlg = new InputDialog(L.FirstRun_DecryptDialog)
                    {
                        Title = L.FirstRun_DecryptTitle
                    };
                    if (pwdDlg.ShowDialog() == true)
                    {
                        _pendingImportPath = fileName;
                        _pendingImportPassword = pwdDlg.Value;
                    }
                }
                else
                {
                    _pendingImportPath = fileName;
                }
            }
        }

        if (!restoredFromFolder)
        {
            var backupSetup = System.Windows.MessageBox.Show(
                L.FirstRun_BackupMessage,
                L.FirstRun_BackupTitle,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (backupSetup == System.Windows.MessageBoxResult.Yes)
            {
                var folderDlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = L.FirstRun_BackupFolderTitle
                };
                if (folderDlg.ShowDialog() == true)
                {
                    settings.Current.BackupFolderPath = folderDlg.FolderName;
                    settings.Save();

                    settings.BackupData();

                    System.Windows.MessageBox.Show(
                        L.Get("FirstRun_BackupConfigured", settings.Current.BackupFolderPath),
                        L.FirstRun_BackupConfiguredTitle,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
        }
    }
}
