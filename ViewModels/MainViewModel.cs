using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RemoteManager.Models;
using RemoteManager.Services;
using RemoteManager.Views;

namespace RemoteManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly SettingsService _settings;
    private readonly ImportExportService _importExport;

    public MainViewModel(IDatabaseService db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
        _importExport = new ImportExportService(db);

        _db.Initialize(settings.Current.DatabasePath);
        SyncTabSelectionCommand = new RelayCommand(OnSyncTabSelection);
        LoadData();

        OpenTabs.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (SessionTabViewModel tab in e.NewItems)
                {
                    tab.CloseRequested += OnTabCloseRequested;
                }
            }
            if (e.OldItems != null)
            {
                foreach (SessionTabViewModel tab in e.OldItems)
                {
                    tab.CloseRequested -= OnTabCloseRequested;
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

        var ungroupedVm = new GroupViewModel { Id = Guid.Empty, Name = "Ungrouped", IsVisible = false };

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
                Description = conn.Description
            };

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
            foreach (var item in group.Children)
            {
                if (string.IsNullOrWhiteSpace(search))
                {
                    item.IsVisible = true;
                }
                else
                {
                    if (item is ConnectionItemViewModel connItem)
                        item.IsVisible = connItem.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || connItem.Host.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || connItem.Description.Contains(search, StringComparison.OrdinalIgnoreCase);
                    else
                        item.IsVisible = item.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
                }
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
                tab = new RdpSessionViewModel(_db, item.Connection);
            }
            else
            {
                tab = new SshSessionViewModel(_db, item.Connection);
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

            OpenTabs.Add(tab);
            SelectedTab = tab;
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
                $"Disconnect '{tab.Header}' and close tab?",
                "Active Connection",
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
        if (tab is IDisposable disposable)
            disposable.Dispose();

        var idx = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);
        if (OpenTabs.Count > 0)
            SelectedTab = OpenTabs[Math.Min(idx, OpenTabs.Count - 1)];
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var settingsVm = new SettingsViewModel(_settings, _db);
        settingsVm.ImportCompleted += () => { LoadData(); };
        OpenTabs.Add(new SettingsTabViewModel(settingsVm));
        SelectedTab = OpenTabs.Last();
    }

    [RelayCommand]
    private void AddGroup()
    {
        var dialog = new InputDialog("Enter name for the new group:", $"Group {Groups.Count + 1}")
        {
            Title = "New Group"
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
        var type = typeStr == "SSH" ? ConnectionType.SSH : ConnectionType.RDP;
        var vm = new ConnectionEditViewModel(_db) { SelectedType = type };

        var dialog = new ConnectionEditDialog(vm);
        if (dialog.ShowDialog() == true)
        {
            RefreshTree();
        }
    }

    [RelayCommand]
    private void EditConnection(ConnectionItemViewModel? item)
    {
        if (item?.Connection == null) return;
        var freshConn = _db.GetConnection(item.Connection.Id);
        if (freshConn == null) return;

        var vm = new ConnectionEditViewModel(_db, freshConn);
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
            $"Delete connection '{item.Name}'?",
            "Confirm Delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
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
        copy.Name += " (copy)";
        copy.CreatedAt = DateTime.UtcNow;
        copy.ModifiedAt = DateTime.UtcNow;

        _db.SaveConnection(copy);

        // Copy credential if it exists
        var existingPassword = CredentialManager.Load(original.Id);
        if (existingPassword != null)
        {
            CredentialManager.Save(copy.Id, existingPassword);
        }

        RefreshTree();
    }

    [RelayCommand]
    private void RenameGroup(GroupViewModel? group)
    {
        if (group == null || group.Id == Guid.Empty) return;
        var dbGroup = _db.GetGroup(group.Id);
        if (dbGroup == null) return;

        var dialog = new InputDialog("Rename Group", group.Name);
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
            $"Delete group '{group.Name}' and all its connections?",
            "Confirm Delete",
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

        var tab = new RdpSessionViewModel(_db, conn);
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

        var tab = new SshSessionViewModel(_db, conn);
        OpenTabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void ShowImport()
    {
        var dialog = new OpenFileDialog
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
                    LoadData();
                    System.Windows.MessageBox.Show("Import completed!", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Import failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void ShowExport()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Export connections",
            FileName = $"RemoteManager_export_{DateTime.Now:yyyyMMdd}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _importExport.ExportToFile(dialog.FileName);
                System.Windows.MessageBox.Show("Export completed!", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
