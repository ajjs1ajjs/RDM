using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RemoteManager.Models;

namespace RemoteManager.ViewModels;

public abstract partial class TreeEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isExpanded = false;
}

public partial class GroupViewModel : TreeEntryViewModel
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public ObservableCollection<TreeEntryViewModel> Children { get; set; } = new();
}

public partial class ConnectionItemViewModel : TreeEntryViewModel
{
    public Connection? Connection { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public ConnectionType Type { get; set; }
    public string Description { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    public string TypeIcon => Type == ConnectionType.RDP ? "\uE7F4" : "\uE9A9";
    public string DisplayText => $"{Name} ({Host}:{Port})";
}
