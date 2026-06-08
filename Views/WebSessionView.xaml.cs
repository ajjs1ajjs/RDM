using System;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using RemoteManager.Helpers;
using RemoteManager.ViewModels;

namespace RemoteManager.Views;

public partial class WebSessionView : UserControl
{
    private WebSessionViewModel? _vm;

    public WebSessionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is WebSessionViewModel vm)
        {
            _vm = vm;

            try
            {
                var appDataDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RemoteManager",
                    "WebView2_Browser");

                var env = await CoreWebView2Environment.CreateAsync(null, appDataDir, new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = vm.IgnoreCertificateErrors ? "--ignore-certificate-errors" : ""
                });

                await WebView.EnsureCoreWebView2Async(env);
                WebView.CoreWebView2.Navigate(vm.Url);
            }
            catch (Exception ex)
            {
                Services.Log.Error("Failed to initialize WebView2 in WebSessionView", ex);
                System.Windows.MessageBox.Show(
                    L.Web_BrowserError + "\n\n" + ex.Message,
                    L.Web_BrowserErrorTitle,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
