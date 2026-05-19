using System.Windows.Controls;
using RemoteManager.ViewModels;

namespace RemoteManager.Views;

public partial class SshSessionView : UserControl
{
    public SshSessionView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is SshSessionViewModel vm)
            {
                vm.Terminal = TerminalControl;
                if (!vm.IsConnected) await vm.ConnectAsync();
            }
        };
    }
}
