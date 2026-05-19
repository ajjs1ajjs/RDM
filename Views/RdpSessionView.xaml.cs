using System.Windows.Controls;
using RemoteManager.ViewModels;

namespace RemoteManager.Views;

public partial class RdpSessionView : UserControl
{
    public RdpSessionView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is RdpSessionViewModel vm)
            {
                vm.RdpHost = RdpControl;
                if (!vm.IsConnected) vm.Connect();
            }
        };
    }
}
