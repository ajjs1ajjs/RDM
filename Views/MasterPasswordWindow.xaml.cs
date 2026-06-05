using System.Windows;
using RemoteManager.Helpers;
using RemoteManager.Services;

namespace RemoteManager.Views;

public partial class MasterPasswordWindow : Window
{
    private readonly string _expectedHash;
    public bool IsUnlocked { get; private set; }

    public MasterPasswordWindow(string expectedHash)
    {
        InitializeComponent();
        _expectedHash = expectedHash;
        PasswordBox.Focus();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        if (CryptoHelper.HashPassword(password) == _expectedHash)
        {
            MasterPasswordContext.CurrentMasterPassword = password;
            IsUnlocked = true;
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
