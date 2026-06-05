using System.Windows;
using RemoteManager.Helpers;

namespace RemoteManager.Views;

public partial class MasterPasswordWindow : Window
{
    private readonly string _expectedHash;
    private readonly Action<string> _onSuccess;

    public MasterPasswordWindow(string expectedHash, Action<string> onSuccess)
    {
        InitializeComponent();
        _expectedHash = expectedHash;
        _onSuccess = onSuccess;
        PasswordBox.Focus();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        if (CryptoHelper.VerifyPassword(password, _expectedHash))
        {
            _onSuccess(password);
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Invalid master password.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
