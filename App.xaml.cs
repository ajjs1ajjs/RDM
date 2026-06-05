using System.Drawing;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RemoteManager.Models;
using RemoteManager.Services;
using RemoteManager.ViewModels;
using RemoteManager.Views;

namespace RemoteManager;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private NotifyIcon? _trayIcon;
    private IDatabaseService? _db;
    private IntPtr _hIcon = IntPtr.Zero;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private void ConfigureServices(IServiceCollection services)
    {
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RemoteManager");
        var credentialsDir = Path.Combine(appDataDir, "credentials");

        services.AddSingleton<ISettingsService>(sp => new SettingsService(appDataDir));
        services.AddSingleton<ICredentialService>(sp => new CredentialService(credentialsDir, sp.GetRequiredService<ISettingsService>()));
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IImportExportService, ImportExportService>();
        services.AddSingleton<IPingService, PingService>();
        
        services.AddTransient<MainViewModel>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        System.Windows.Forms.Application.ThreadException += (sender, args) =>
        {
            Services.Log.Error("WinForms Thread Exception", args.Exception);
            if (args.Exception is System.Runtime.InteropServices.InvalidComObjectException ||
                args.Exception.Message.Contains("class factory") ||
                args.Exception is System.Runtime.InteropServices.COMException)
            {
                // Ignore COM-related interop exceptions during RDP tab closure
                return;
            }
        };

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            var settings = _serviceProvider.GetRequiredService<ISettingsService>();
            _db = _serviceProvider.GetRequiredService<IDatabaseService>();

            if (settings.Current.UseMasterPassword && !string.IsNullOrEmpty(settings.Current.MasterPasswordHash))
            {
                var pwdWindow = new MasterPasswordWindow(settings.Current.MasterPasswordHash);
                if (pwdWindow.ShowDialog() != true)
                {
                    Shutdown(0);
                    return;
                }
            }

            ApplyTheme(settings.Current.Theme);

            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
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
            Services.Log.Error("Startup failed", ex);
            System.Diagnostics.Debug.WriteLine(ex.ToString());
            MessageBox.Show(
                $"Startup failed: {ex.Message}\n\nSee debug output for details.",
                "Remote Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Services.Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Services.Log.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Services.Log.Error("Unhandled AppDomain exception", ex);
    }

    public static void ApplyTheme(string theme)
    {
        var app = Current;
        if (app == null)
            return;

        var themeUri = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "pack://application:,,,/RemoteManager;component/Resources/Styles/ThemeLight.xaml"
            : "pack://application:,,,/RemoteManager;component/Resources/Styles/Theme.xaml";

        var existingTheme = app.Resources.MergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("Resources/Styles/Theme", StringComparison.OrdinalIgnoreCase) == true);

        var newDictionary = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Absolute) };

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
                    Services.Log.Warn("Failed to create tray icon: " + ex.Message);
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
                catch (Exception ex)
                {
                    Services.Log.Warn("Failed to destroy tray icon handle: " + ex.Message);
                }
                _hIcon = IntPtr.Zero;
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        if (_hIcon != IntPtr.Zero)
        {
            try { DestroyIcon(_hIcon); }
            catch (Exception ex) { Services.Log.Warn("Failed to destroy icon on exit: " + ex.Message); }
            _hIcon = IntPtr.Zero;
        }
        _db?.Dispose();
        base.OnExit(e);
    }
}
