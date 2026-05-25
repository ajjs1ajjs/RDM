using System.Drawing;
using System.Windows;
using RemoteManager.Models;
using RemoteManager.Services;
using RemoteManager.ViewModels;
using RemoteManager.Views;

namespace RemoteManager;

public partial class App : Application
{
    private NotifyIcon? _trayIcon;
    private DatabaseService? _db;
    private IntPtr _hIcon = IntPtr.Zero;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = new SettingsService();
        _db = new DatabaseService();

        try
        {
            ApplyTheme(settings.Current.Theme);

            var mainVm = new MainViewModel(_db, settings);
            var mainWindow = new MainWindow();
            mainWindow.Initialize(mainVm);
            mainWindow.Show();

            UpdateTrayIcon(mainWindow, settings.Current.MinimizeToTray);

            mainWindow.StateChanged += (s, args) =>
            {
                if (settings.Current.MinimizeToTray && mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.Hide();
                    _trayIcon?.ShowBalloonTip(1000, "Remote Manager",
                        "Application minimized to tray", ToolTipIcon.Info);
                }
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.ToString());
            MessageBox.Show(
                $"Startup failed: {ex.Message}\n\nSee debug output for details.",
                "Remote Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public static void ApplyTheme(string theme)
    {
        var app = Current;
        if (app == null)
            return;

        var themeUri = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "Resources/Styles/ThemeLight.xaml"
            : "Resources/Styles/Theme.xaml";

        var existingTheme = app.Resources.MergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.StartsWith("Resources/Styles/Theme", StringComparison.OrdinalIgnoreCase) == true);

        var newDictionary = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Relative) };

        if (existingTheme == null)
        {
            app.Resources.MergedDictionaries.Insert(0, newDictionary);
            return;
        }

        var index = app.Resources.MergedDictionaries.IndexOf(existingTheme);
        app.Resources.MergedDictionaries[index] = newDictionary;
    }

    public void UpdateTrayIcon(Window window, bool enabled)
    {
        if (enabled)
        {
            if (_trayIcon == null)
            {
                _trayIcon = new NotifyIcon
                {
                    Text = "Remote Manager",
                    Visible = true
                };

                try
                {
                    using var bmp = new Bitmap(16, 16);
                    using var g = Graphics.FromImage(bmp);
                    g.Clear(System.Drawing.Color.Transparent);
                    using var brush = new SolidBrush(System.Drawing.Color.FromArgb(0, 120, 215));
                    g.FillEllipse(brush, 0, 0, 15, 15);
                    g.DrawString("R", new Font("Segoe UI", 9, System.Drawing.FontStyle.Bold),
                        System.Drawing.Brushes.White, 3, 2);
                    var hIcon = bmp.GetHicon();
                    _hIcon = hIcon;
                    _trayIcon.Icon = Icon.FromHandle(hIcon);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load tray icon: {ex.Message}");
                }

                _trayIcon.DoubleClick += (s, args) =>
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                };

                _trayIcon.BalloonTipClicked += (s, args) =>
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                };
            }
        }
        else
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            if (_hIcon != IntPtr.Zero)
            {
                try
                {
                    DestroyIcon(_hIcon);
                }
                catch { }
                _hIcon = IntPtr.Zero;
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
        _db?.Dispose();
        base.OnExit(e);
    }
}
