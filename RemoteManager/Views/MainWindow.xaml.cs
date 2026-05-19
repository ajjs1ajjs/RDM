using System.Windows;
using RemoteManager.Services;
using RemoteManager.ViewModels;

namespace RemoteManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public void Initialize(MainViewModel vm)
    {
        DataContext = vm;

        vm.OpenTabs.CollectionChanged += (s, e) =>
        {
            if (e.NewItems?.Count > 0)
                vm.SelectedTab = e.NewItems[0];
            else if (e.OldItems?.Count > 0 && e.OldItems[0] == vm.SelectedTab)
                vm.SelectedTab = e.OldItems.Count > 0 ? e.OldItems[e.OldItems.Count - 1] : null;
        };
    }
}
