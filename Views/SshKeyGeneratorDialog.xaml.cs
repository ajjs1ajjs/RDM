using System.Windows;

namespace RemoteManager.Views;

public partial class SshKeyGeneratorDialog : Window
{
    public SshKeyGeneratorDialog()
    {
        InitializeComponent();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as ViewModels.SshKeyGeneratorViewModel;
        if (vm != null)
        {
            vm.SaveKeysCommand.Execute(null);
            if (string.IsNullOrEmpty(vm.ErrorMessage))
            {
                DialogResult = true;
                Close();
            }
        }
    }
}
