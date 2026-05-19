using System.Windows;

namespace RemoteManager.Views;

public partial class InputDialog : Window
{
    public string Message { get; }
    public string Value { get; set; } = "";

    public InputDialog(string message, string defaultValue = "")
    {
        InitializeComponent();
        Message = message;
        Value = defaultValue;
        DataContext = this;
        InputBox.Focus();
        InputBox.SelectAll();
        Owner = Application.Current.MainWindow;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Value = InputBox.Text;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
