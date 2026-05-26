using System.Windows;
using RemoteManager.ViewModels;

namespace RemoteManager.Views;

public partial class ConnectionEditDialog : Window
{
    private readonly ConnectionEditViewModel _vm;

    public ConnectionEditDialog(ConnectionEditViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Owner = Application.Current.MainWindow;

        // Pre-populate secure password fields
        PasswordBox.Password = _vm.Password;
        SshJumpHostPasswordBox.Password = _vm.SshJumpHostPassword;
        SshKeyPassphraseBox.Password = _vm.SshKeyPassphrase;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _vm.Password = PasswordBox.Password;
        _vm.SshJumpHostPassword = SshJumpHostPasswordBox.Password;
        _vm.SshKeyPassphrase = SshKeyPassphraseBox.Password;
        _vm.SaveCommand.Execute(null);

        if (!string.IsNullOrEmpty(_vm.ValidationError))
        {
            System.Windows.MessageBox.Show(_vm.ValidationError, "Validation Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
