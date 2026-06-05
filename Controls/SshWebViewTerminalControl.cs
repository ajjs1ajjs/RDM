using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using RemoteManager.Models;
using RemoteManager.Services;
using Renci.SshNet;

namespace RemoteManager.Controls;

public class SshWebViewTerminalControl : TerminalControl
{
    private readonly Microsoft.Web.WebView2.Wpf.WebView2 _webView;
    private SshClient? _client;
    private ShellStream? _shell;
    private CancellationTokenSource? _readCts;
    private bool _disconnectRequested;
    private bool _isWebViewReady;
    private uint _columns = 120;
    private uint _rows = 40;

    public event EventHandler<string>? ConnectionClosed;

    public SshWebViewTerminalControl()
    {
        _webView = new Microsoft.Web.WebView2.Wpf.WebView2();
        Content = _webView;
        _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RemoteManager.WebView2"));
            await _webView.EnsureCoreWebView2Async(env);
            
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            
            var htmlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Terminal", "index.html");
            if (System.IO.File.Exists(htmlPath))
            {
                _webView.CoreWebView2.Navigate(htmlPath);
                _isWebViewReady = true;
            }
            else
            {
                Log.Error("xterm.js HTML file not found: " + htmlPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to initialize WebView2", ex);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson);
            if (msg.TryGetProperty("type", out var typeProp))
            {
                var type = typeProp.GetString();
                if (type == "data" && msg.TryGetProperty("content", out var contentProp))
                {
                    var content = contentProp.GetString();
                    if (!string.IsNullOrEmpty(content))
                    {
                        SendText(content);
                    }
                }
                else if (type == "resize" && msg.TryGetProperty("cols", out var colsProp) && msg.TryGetProperty("rows", out var rowsProp))
                {
                    _columns = (uint)colsProp.GetInt32();
                    _rows = (uint)rowsProp.GetInt32();
                    // SSH.NET ShellStream does not support dynamic resize without custom hack
                }
            }
        }
        catch { }
    }

    public async Task<bool> ConnectAsync(string host, int port, string user, string pass, SSHSettings? settings)
    {
        while (!_isWebViewReady)
        {
            await Task.Delay(100);
        }

        try
        {
            var connectionInfo = settings?.AuthType == SshAuthType.Key && !string.IsNullOrWhiteSpace(settings.PrivateKeyPath)
                ? new ConnectionInfo(host, port, user, new PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(settings.PrivateKeyPath, settings.PrivateKeyPassphrase ?? pass)))
                : new ConnectionInfo(host, port, user, new PasswordAuthenticationMethod(user, pass));

            connectionInfo.Timeout = TimeSpan.FromSeconds(15);

            _client = new SshClient(connectionInfo);
            await Task.Run(() => _client.Connect());
            _disconnectRequested = false;

            if (settings?.PortForwarding?.Enabled == true)
            {
                try
                {
                    var fwd = new ForwardedPortLocal("127.0.0.1", settings.PortForwarding.LocalPort, settings.PortForwarding.RemoteHost, settings.PortForwarding.RemotePort);
                    _client.AddForwardedPort(fwd);
                    fwd.Start();
                    Log.Info($"Started local port forwarding: {fwd.BoundHost}:{fwd.BoundPort} -> {fwd.Host}:{fwd.Port}");
                }
                catch (Exception pex)
                {
                    Log.Warn("Port forwarding failed: " + pex.Message);
                }
            }

            _columns = (uint)(settings?.TerminalColumns ?? 120);
            _rows = (uint)(settings?.TerminalRows ?? 40);
            _shell = _client.CreateShellStream("xterm-256color", _columns, _rows, 0, 0, 8192);
            _shell.Closed += OnShellClosed;

            _readCts = new CancellationTokenSource();
            _ = Task.Run(() => ReadOutputLoopAsync(_readCts.Token));

            _ = Dispatcher.BeginInvoke(() => _webView.Focus());

            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("SSH connect error: " + ex.Message);
            ConnectionClosed?.Invoke(this, ex.Message);
            return false;
        }
    }

    public override void Disconnect()
    {
        _disconnectRequested = true;
        _readCts?.Cancel();

        try { _shell?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }

        _shell = null;
        _client = null;
    }

    public override void Clear()
    {
        // Clear terminal via xterm.js
        if (_isWebViewReady && _webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.ExecuteScriptAsync("term.clear();");
        }
    }

    public override void SendText(string input)
    {
        if (_shell == null || !_client?.IsConnected == true) return;
        try
        {
            _shell.Write(input);
        }
        catch (Exception ex)
        {
            Log.Warn("SSH send input error: " + ex.Message);
        }
    }

    private void OnShellClosed(object? sender, EventArgs e)
    {
        if (!_disconnectRequested)
        {
            Dispatcher.Invoke(() => ConnectionClosed?.Invoke(this, "Connection closed by remote host."));
        }
        Disconnect();
    }

    private async Task ReadOutputLoopAsync(CancellationToken token)
    {
        var buffer = new byte[8192];
        while (!token.IsCancellationRequested && _shell != null)
        {
            try
            {
                int read = await _shell.ReadAsync(buffer, 0, buffer.Length, token);
                if (read > 0)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, read);
                    Dispatcher.Invoke(() =>
                    {
                        if (_isWebViewReady && _webView.CoreWebView2 != null)
                        {
                            var jsString = JsonSerializer.Serialize(text);
                            _webView.CoreWebView2.ExecuteScriptAsync($"window.writeToTerminal({jsString});");
                        }
                    });
                }
                else
                {
                    break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Debug("SSH read loop error: " + ex.Message);
                break;
            }
        }
    }
}
