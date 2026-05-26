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
            else if (e.OldItems?.Count > 0)
                vm.SelectedTab = vm.OpenTabs.Count > 0 ? vm.OpenTabs[^1] : null;
        };
    }
}
