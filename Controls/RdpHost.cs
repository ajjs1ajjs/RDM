using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using RemoteManager.Models;
using RemoteManager.Services;

namespace RemoteManager.Controls;

public partial class RdpHost : TerminalControl, IDisposable
{
    private dynamic? _client;
    private WindowsFormsHost? _wfh;
    private bool _disposed;

    private string? _pendingHost;
    private int _pendingPort;
    private string? _pendingUser;
    private char[]? _pendingPass;
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
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

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
            try { _client.Disconnect(); } catch { }
            _client = null;
        }

        Content = null;
        GC.SuppressFinalize(this);
    }

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
        catch (Exception ex) { Log.Debug("DPI detection error: " + ex.Message); }

        int w = Math.Max(800, (int)(ActualWidth * dpiScaleX));
        int h = Math.Max(600, (int)(ActualHeight * dpiScaleY));

        if (w % 2 != 0) w++;
        if (h % 2 != 0) h++;

        return (w, h);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ActualWidth > 100 && ActualHeight > 100)
        {
            if (_pendingHost != null)
            {
                var host = _pendingHost;
                var passChars = _pendingPass;
                _pendingHost = null;
                _pendingPass = null;
                var pass = passChars != null ? new string(passChars) : "";
                if (passChars != null)
                    Array.Clear(passChars, 0, passChars.Length);
                Connect(host, _pendingPort, _pendingUser ?? "", pass, _pendingSettings);
                return;
            }

            if (_client != null && IsLoaded)
            {
                ApplySmartSizing();
            }
        }
    }

    private void ApplySmartSizing()
    {
        if (_client == null) return;
        try { _client.AdvancedSettings9.SmartSizing = true; } catch (Exception ex) { Log.Debug("SmartSizing9 error: " + ex.Message); }
        try { _client.AdvancedSettings2.SmartSizing = true; } catch (Exception ex) { Log.Debug("SmartSizing2 error: " + ex.Message); }
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
            Log.Warn("AxHost Init failed: " + ex.Message);
            Debug.WriteLine("AxHost Init failed: " + ex.Message);
        }
    }

    public void Connect(string host, int port, string user, string pass, RDPSettings? s)
    {
        if (_client == null)
        {
            InitializeRdp();

            if (_client == null)
            {
                try
                {
                    Process.Start("mstsc.exe", $@"/v:{host}:{port}");
                    Connected?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Log.Warn("mstsc fallback failed: " + ex.Message);
                    ErrorOccurred?.Invoke(this, "Connection failed due to initialization error");
                }
                return;
            }
        }

        if (ActualWidth < 100 || ActualHeight < 100)
        {
            _pendingHost = host;
            _pendingPort = port;
            _pendingUser = user;
            _pendingPass = pass.ToCharArray();
            _pendingSettings = s;
            return;
        }

        var (w, h) = GetPhysicalPixelSize();

        try
        {
            _client.Server = host;
            _client.UserName = user;

            _client.DesktopWidth = w;
            _client.DesktopHeight = h;

            try
            {
                var adv = _client.AdvancedSettings9;
                if (adv != null)
                {
                    try { adv.RDPPort = port; } catch (Exception ex) { Log.Debug("RDPPort error: " + ex.Message); }
                    try { adv.ClearTextPassword = pass; } catch (Exception ex) { Log.Debug("ClearTextPassword error: " + ex.Message); }
                    try { adv.EnableCredSSP = s?.UseCredSsp ?? true; } catch (Exception ex) { Log.Debug("EnableCredSSP error: " + ex.Message); }
                    try { adv.AuthenticationLevel = s?.NetworkLevelAuth == true ? 2 : 0; } catch (Exception ex) { Log.Debug("AuthenticationLevel error: " + ex.Message); }
                    try { adv.RedirectClipboard = s?.RedirectClipboard ?? true; } catch (Exception ex) { Log.Debug("RedirectClipboard error: " + ex.Message); }
                    try { adv.RedirectDrives = s?.RedirectDrives ?? true; } catch (Exception ex) { Log.Debug("RedirectDrives error: " + ex.Message); }
                    try { adv.RedirectPrinters = s?.RedirectPrinters ?? false; } catch (Exception ex) { Log.Debug("RedirectPrinters error: " + ex.Message); }
                    try { adv.AudioRedirectionMode = s?.AudioMode ?? 0; } catch (Exception ex) { Log.Debug("AudioMode error: " + ex.Message); }
                    try { adv.SmartSizing = true; } catch (Exception ex) { Log.Debug("SmartSizing error: " + ex.Message); }
                    try { adv.UseMultimon = s?.UseMultimon ?? false; } catch (Exception ex) { Log.Debug("UseMultimon error: " + ex.Message); }
                }
            }
            catch (Exception ex) { Log.Warn("AdvancedSettings9 access error: " + ex.Message); }

            ApplySmartSizing();
            try { _client.DisplayScrollBars = false; } catch (Exception ex) { Log.Debug("DisplayScrollBars error: " + ex.Message); }

            _client.Connect();

            ClearComPassword();

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
            Log.Warn("RDP connect error: " + ex.Message);
            ErrorOccurred?.Invoke(this, $"RDP: {ex.Message}");
        }
    }

    private void ClearComPassword()
    {
        try
        {
            if (_client != null)
            {
                var adv = _client.AdvancedSettings9;
                if (adv != null)
                {
                    adv.ClearTextPassword = "";
                }
            }
        }
        catch
        {
        }
    }

    private void OnStateTimerTick(object? sender, EventArgs e)
    {
        if (_client == null) return;

        try
        {
            int state = (int)_client.Connected;
            if (state == 1)
            {
                if (!_wasConnected)
                {
                    _wasConnected = true;
                    Connected?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (state == 0)
            {
                if (_wasConnected)
                {
                    _wasConnected = false;

                    int extReason = 0;
                    try { extReason = (int)_client.ExtendedDisconnectReason; } catch (Exception ex) { Log.Debug("ExtendedDisconnectReason error: " + ex.Message); }

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
        catch (Exception ex) { Log.Debug("State timer tick error: " + ex.Message); }
    }

    public override void Disconnect()
    {
        try
        {
            _stateTimer?.Stop();
            _stateTimer = null;
            _wasConnected = false;
            _client?.Disconnect();

            if (_client != null)
            {
                _client = null;
            }

            Disconnected?.Invoke(this, "Disconnected");
        }
        catch (Exception ex) { Log.Warn("Disconnect error: " + ex.Message); }
    }

    public void ToggleFullScreen()
    {
        try
        {
            if (_client != null)
            {
                bool isFull = _client.FullScreen;
                _client.FullScreen = !isFull;
            }
        }
        catch (Exception ex) { Log.Debug("ToggleFullScreen error: " + ex.Message); }
    }

    public void SendCtrlAltDel()
    {
        try
        {
            if (_client != null)
                _client.SendKeys(new object[] { 17, 18, 46 });
        }
        catch (Exception ex) { Log.Debug("SendCtrlAltDel error: " + ex.Message); }
    }
}

internal class RdpAxHost : AxHost
{
    public RdpAxHost(string clsid) : base(clsid)
    {
        try { CreateControl(); } catch (Exception ex) { Services.Log.Warn("RdpAxHost CreateControl failed: " + ex.Message); }
    }
    public new object? GetOcx() => base.GetOcx();
}
