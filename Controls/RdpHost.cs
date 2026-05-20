using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using RemoteManager.Models;

namespace RemoteManager.Controls;

public partial class RdpHost : TerminalControl
{
    private dynamic? _client;
    private WindowsFormsHost? _wfh;

    // Pending connection (if layout not ready yet)
    private string? _pendingHost;
    private int _pendingPort;
    private string? _pendingUser;
    private string? _pendingPass;
    private RDPSettings? _pendingSettings;

    private System.Windows.Threading.DispatcherTimer? _stateTimer;
    private bool _wasConnected;

    public event EventHandler? Connected;
    public event EventHandler<string>? Disconnected;
    public event EventHandler<string>? ErrorOccurred;

    public RdpHost()
    {
        Background = System.Windows.Media.Brushes.Black;
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch;

        InitializeRdp();

        SizeChanged += OnSizeChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _stateTimer?.Stop();
        _stateTimer = null;

        if (_wfh != null)
        {
            _wfh.Child = null;
            _wfh.Dispose();
            _wfh = null;
        }
        if (_client != null)
        {
            try
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(_client);
            }
            catch { }
            _client = null;
        }
        Content = null;
    }

    /// <summary>
    /// Converts WPF DIPs to physical screen pixels using the current DPI scaling factor.
    /// This is critical because the RDP ActiveX control works in physical pixels,
    /// but WPF ActualWidth/ActualHeight are in device-independent pixels (DIPs).
    /// On a 125% scaled display: 1536 DIPs × 1.25 = 1920 physical pixels.
    /// </summary>
    private (int width, int height) GetPhysicalPixelSize()
    {
        double dpiScaleX = 1.0;
        double dpiScaleY = 1.0;

        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }
        }
        catch { }

        int w = Math.Max(800, (int)(ActualWidth * dpiScaleX));
        int h = Math.Max(600, (int)(ActualHeight * dpiScaleY));

        // RDP requires even dimensions
        if (w % 2 != 0) w++;
        if (h % 2 != 0) h++;

        return (w, h);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ActualWidth > 100 && ActualHeight > 100)
        {
            // If we had a pending connection waiting for layout, fire it now
            if (_pendingHost != null)
            {
                var host = _pendingHost;
                _pendingHost = null;
                Connect(host, _pendingPort, _pendingUser ?? "", _pendingPass ?? "", _pendingSettings);
                return;
            }

            // Keep SmartSizing active on resize
            if (_client != null && IsLoaded)
            {
                ApplySmartSizing();
            }
        }
    }

    private void ApplySmartSizing()
    {
        if (_client == null) return;
        try { _client.AdvancedSettings9.SmartSizing = true; } catch { }
        try { _client.AdvancedSettings2.SmartSizing = true; } catch { }
    }

    private void InitializeRdp()
    {
        if (Content != null) return;
        try
        {
            _wfh = new WindowsFormsHost
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Background = System.Windows.Media.Brushes.Black
            };

            var ax = new RdpAxHost("{8B918B82-7985-4C24-89DF-C33AD2BBFBCD}")
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                BackColor = System.Drawing.Color.Black
            };

            ((System.ComponentModel.ISupportInitialize)ax).BeginInit();
            _wfh.Child = ax;
            ((System.ComponentModel.ISupportInitialize)ax).EndInit();

            _client = ax.GetOcx();
            Content = _wfh;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("AxHost Init failed: " + ex.Message);
        }
    }

    public void Connect(string host, int port, string user, string pass, RDPSettings? s)
    {
        if (_client == null)
        {
            // Ensure we try to initialize if not done yet
            InitializeRdp();

            if (_client == null)
            {
                // Absolute fallback only if AxHost really failed to create
try { Process.Start("mstsc.exe", $@"/v:{host}:{port}"); Connected?.Invoke(this, EventArgs.Empty); }
catch (Exception) { ErrorOccurred?.Invoke(this, "Connection failed due to initialization error"); }
                return;
            }
        }

        // If layout hasn't happened yet, defer connection until SizeChanged fires
        if (ActualWidth < 100 || ActualHeight < 100)
        {
            _pendingHost = host;
            _pendingPort = port;
            _pendingUser = user;
            _pendingPass = pass;
            _pendingSettings = s;
            return;
        }

        // Get physical pixel dimensions (DPI-aware!)
        var (w, h) = GetPhysicalPixelSize();

        try
        {
            _client.Server = host;
            _client.UserName = user;

            // Set desktop resolution in PHYSICAL pixels, not WPF DIPs
            _client.DesktopWidth = w;
            _client.DesktopHeight = h;

            try
            {
                var adv = _client.AdvancedSettings9;
                if (adv != null)
                {
                    try { adv.RDPPort = port; } catch { }
                    try { adv.ClearTextPassword = pass; } catch { }
                    try { adv.EnableCredSSP = s?.UseCredSsp ?? true; } catch { }
                    try { adv.AuthenticationLevel = s?.NetworkLevelAuth == true ? 2 : 0; } catch { }
                    try { adv.RedirectClipboard = s?.RedirectClipboard ?? true; } catch { }
                    try { adv.RedirectDrives = s?.RedirectDrives ?? false; } catch { }
                    try { adv.RedirectPrinters = s?.RedirectPrinters ?? false; } catch { }
                    try { adv.AudioRedirectionMode = s?.AudioMode ?? 0; } catch { }
                    try { adv.SmartSizing = true; } catch { }
                }
            }
            catch { }

            ApplySmartSizing();
            try { _client.DisplayScrollBars = false; } catch { }

            _client.Connect();

            if (_stateTimer == null)
            {
                _stateTimer = new System.Windows.Threading.DispatcherTimer();
                _stateTimer.Interval = TimeSpan.FromMilliseconds(500);
                _stateTimer.Tick += OnStateTimerTick;
            }
            _stateTimer.Stop();
            _stateTimer.Start();
            _wasConnected = false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"RDP: {ex.Message}");
        }
    }

    private void OnStateTimerTick(object? sender, EventArgs e)
    {
        if (_client == null) return;

        try
        {
            int state = (int)_client.Connected;
            if (state == 1) // Connected
            {
                if (!_wasConnected)
                {
                    _wasConnected = true;
                    Connected?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (state == 0) // Disconnected
            {
                if (_wasConnected)
                {
                    _wasConnected = false;

                    int extReason = 0;
                    try { extReason = (int)_client.ExtendedDisconnectReason; } catch { }

                    string reasonMsg = extReason switch
                    {
                        1 => "APIInitiatedDisconnect",
                        2 => "APIInitiatedLogoff",
                        3 => "ServerIdleTimeout",
                        4 => "ServerLogonTimeout",
                        12 => "LogoffByUser",
                        _ => $"Code {extReason}"
                    };

                    Disconnected?.Invoke(this, reasonMsg);
                }
            }
        }
        catch { }
    }

    public override void Disconnect()
    {
        try
        {
            _stateTimer?.Stop();
            _stateTimer = null;
            _wasConnected = false;
            _client?.Disconnect();
            Disconnected?.Invoke(this, "Disconnected");
        }
        catch { }
    }

    public void ToggleFullScreen() { try { if (_client != null) { bool isFull = _client.FullScreen; _client.FullScreen = !isFull; } } catch { } }
    public void SendCtrlAltDel() { try { if (_client != null) { _client.SendKeys(17, 18, 46); } } catch { } }
}

internal class RdpAxHost : AxHost
{
    public RdpAxHost(string clsid) : base(clsid)
    {
        try { CreateControl(); } catch { }
    }
    public new object? GetOcx() => base.GetOcx();
}
