using System.Windows;
using System.Windows.Controls;

namespace RemoteManager.Helpers;

public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    public static readonly DependencyProperty BindPasswordProperty =
        DependencyProperty.RegisterAttached("BindPassword", typeof(bool), typeof(PasswordBoxHelper),
            new PropertyMetadata(false, OnBindPasswordChanged));

    public static bool GetBindPassword(DependencyObject d) => (bool)d.GetValue(BindPasswordProperty);
    public static void SetBindPassword(DependencyObject d, bool value) => d.SetValue(BindPasswordProperty, value);

    private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox passwordBox)
        {
            var wasBound = (bool)e.OldValue;
            var needToBind = (bool)e.NewValue;

            if (wasBound)
            {
                passwordBox.PasswordChanged -= OnPasswordChanged;
            }

            if (needToBind)
            {
                passwordBox.PasswordChanged += OnPasswordChanged;
            }
        }
    }

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox passwordBox && passwordBox.Password != (string)e.NewValue)
        {
            passwordBox.PasswordChanged -= OnPasswordChanged;
            passwordBox.Password = (string)e.NewValue;
            passwordBox.PasswordChanged += OnPasswordChanged;
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            SetBoundPassword(passwordBox, passwordBox.Password);
        }
    }
}
