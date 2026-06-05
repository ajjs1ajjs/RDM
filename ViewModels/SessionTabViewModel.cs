using CommunityToolkit.Mvvm.ComponentModel;
using RemoteManager.Controls;
using RemoteManager.Helpers;
using RemoteManager.Models;

namespace RemoteManager.ViewModels;

public partial class SessionTabViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private string _header = "";
    [ObservableProperty] private Guid _connectionId;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _statusText = L.Status_Disconnected;
    [ObservableProperty] private string _sessionInfo = "";
    [ObservableProperty] private bool _isSelected;

    public event EventHandler? CloseRequested;

    protected void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Connect() { }
    public virtual Task ConnectAsync() => Task.CompletedTask;
    public virtual void Disconnect() { }

    public virtual string? GetPassword() => null;

    public virtual string TypeIcon => "\uE713";

    public virtual void Dispose() { }
}
