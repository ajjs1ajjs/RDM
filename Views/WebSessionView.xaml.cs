using System;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
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

            var env = await CoreWebView2Environment.CreateAsync(null, null, new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = vm.IgnoreCertificateErrors ? "--ignore-certificate-errors" : ""
            });

            await WebView.EnsureCoreWebView2Async(env);
            WebView.CoreWebView2.Navigate(vm.Url);
        }
    }
}
