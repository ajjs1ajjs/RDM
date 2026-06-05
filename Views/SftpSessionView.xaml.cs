using System.Windows.Controls;

namespace RemoteManager.Views;

public partial class SftpSessionView : UserControl
{
    public SftpSessionView()
    {
        InitializeComponent();
    }

    private void DataGrid_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files != null && files.Length > 0 && DataContext is RemoteManager.ViewModels.SftpSessionViewModel vm)
            {
                // Upload the first dropped file (for simplicity we only upload one file at a time)
                _ = vm.UploadFile(files[0]);
            }
        }
    }
}
