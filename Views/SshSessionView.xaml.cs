using System.Windows.Controls;
using RemoteManager.ViewModels;

namespace RemoteManager.Views;

public partial class SshSessionView : UserControl
{
    public SshSessionView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is SshSessionViewModel vm)
            {
                vm.Terminal = TerminalControl;
                if (!vm.IsConnected)
                {
                    _ = vm.ConnectAsync().ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                            Services.Log.Error("SSH connect failed", t.Exception);
                    }, TaskScheduler.Default);
                }
            }
        };
    }
}
