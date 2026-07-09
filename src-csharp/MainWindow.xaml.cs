using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Collections.Concurrent;
using Microsoft.Web.WebView2.Core;
using System.Security.Cryptography;
using System.Diagnostics;

namespace RDM
{
    public partial class MainWindow : Window
    {
        private readonly string _appDataDir;
        private readonly ConcurrentDictionary<string, System.Windows.Controls.Primitives.Popup> _rdpPopups = new();
        private readonly ConcurrentDictionary<string, MyRdpClient> _rdpClients = new();
        public static byte[]? MasterKey { get; private set; }

        private void Log(string message)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "wpf_rdp.log");
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
            }
            catch {}
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool CredWrite(ref CREDENTIAL pCredential, uint flags);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        private const uint CRED_TYPE_GENERIC = 1;
        private const uint CRED_PERSIST_SESSION = 2;
        private const uint CRED_PERSIST_LOCAL_MACHINE = 3;

        private static readonly IntPtr HWND_TOP = new IntPtr(0);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public MainWindow()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "wpf_rdp.log");
                if (File.Exists(path)) File.Delete(path);
            }
            catch {}
            
            InitializeComponent();

            // Synchronize overlay positions when main window moves or resizes
            LocationChanged += (s, e) => SendEvent("tauri://move", "");
            SizeChanged += (s, e) => SendEvent("tauri://move", "");
            StateChanged += (s, e) => {
                if (WindowState == WindowState.Minimized)
                {
                    foreach (var popup in _rdpPopups.Values) popup.IsOpen = false;
                }
                else
                {
                    foreach (var popup in _rdpPopups.Values) popup.IsOpen = true;
                    SendEvent("tauri://move", "");
                }
            };

            _appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "com.admin.rdm-manager"
            );

            // Initialize database
            Db.InitDb(_appDataDir);

            // Set up WebView2 initialization
            Loaded += OnMainWindowLoaded;
        }

        private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create custom environment to store WebView2 cache inside app data
                string cacheDir = Path.Combine(_appDataDir, "webview_cache");
                var env = await CoreWebView2Environment.CreateAsync(null, cacheDir);
                await webView.EnsureCoreWebView2Async(env);

                // Configure virtual host mapping if local wwwroot exists, otherwise load embedded assets from wwwroot.zip
                string localWwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
                if (Directory.Exists(localWwwroot))
                {
                    webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "rdm.local", 
                        localWwwroot, 
                        CoreWebView2HostResourceAccessKind.Allow
                    );
                    webView.Source = new Uri("http://rdm.local/index.html");
                }
                else
                {
                    // Register web resource requested filter for embedded zip
                    webView.CoreWebView2.AddWebResourceRequestedFilter(
                        "http://rdm.local/*",
                        Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All
                    );
                    webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
                    webView.Source = new Uri("http://rdm.local/index.html");
                }

                // Handle IPC messages from React JS bridge
                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "RDM Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string rawJson = e.WebMessageAsJson;
            try
            {
                var node = JsonNode.Parse(rawJson);
                if (node == null) return;

                string id = node["id"]?.ToString() ?? "";
                string cmd = node["cmd"]?.ToString() ?? "";
                var args = node["args"] as JsonObject;

                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(cmd)) return;

                // Execute command in a background thread to keep UI fluid
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await HandleCommand(cmd, args);
                        SendResponse(id, true, result, null);
                    }
                    catch (Exception ex)
                    {
                        SendResponse(id, false, null, ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to parse IPC message: {ex.Message}. Raw message: {rawJson}");
            }
        }

        private void SendResponse(string id, bool success, object? result, string? error)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var payload = new
                {
                    type = "response",
                    id = id,
                    success = success,
                    result = result,
                    error = error
                };
                string json = JsonSerializer.Serialize(payload);
                webView.CoreWebView2.PostWebMessageAsJson(json);
            });
        }

        private void SendEvent(string name, object? payload)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (webView.CoreWebView2 == null) return;
                var eventMsg = new
                {
                    type = "event",
                    name = name,
                    payload = payload
                };
                string json = JsonSerializer.Serialize(eventMsg);
                webView.CoreWebView2.PostWebMessageAsJson(json);
            });
        }

        private async Task<object?> HandleCommand(string cmd, JsonObject? args)
        {
            switch (cmd)
            {
                // Settings
                case "get_setting":
                    return Db.GetSetting(_appDataDir, args?["key"]?.ToString() ?? "");
                case "set_setting":
                    Db.SetSetting(_appDataDir, args?["key"]?.ToString() ?? "", args?["value"]?.ToString() ?? "");
                    return null;

                // Servers
                case "get_servers":
                    return Db.GetServers(_appDataDir);
                case "save_server":
                case "add_server":
                    var srv = JsonSerializer.Deserialize<Server>(args?["server"]?.ToJsonString() ?? "{}");
                    if (srv != null)
                    {
                        Db.AddServer(_appDataDir, srv);
                    }
                    return null;
                case "update_server":
                    var srvUpdate = JsonSerializer.Deserialize<Server>(args?["server"]?.ToJsonString() ?? "{}");
                    if (srvUpdate != null)
                    {
                        Db.UpdateServer(_appDataDir, srvUpdate);
                    }
                    return null;
                case "delete_server":
                    Db.DeleteServer(_appDataDir, args?["id"]?.ToString() ?? "");
                    return null;

                // Credentials
                case "get_credentials":
                    return Db.GetCredentials(_appDataDir);
                case "save_credential":
                case "add_credential":
                    var cred = JsonSerializer.Deserialize<Credential>(args?["credential"]?.ToJsonString() ?? "{}");
                    if (cred != null)
                    {
                        Db.AddCredential(_appDataDir, cred);
                    }
                    return null;
                case "update_credential":
                    var credUpdate = JsonSerializer.Deserialize<Credential>(args?["credential"]?.ToJsonString() ?? "{}");
                    if (credUpdate != null)
                    {
                        Db.UpdateCredential(_appDataDir, credUpdate);
                    }
                    return null;
                case "delete_credential":
                    Db.DeleteCredential(_appDataDir, args?["id"]?.ToString() ?? "");
                    return null;

                // History
                case "get_history":
                    return Db.GetHistory(_appDataDir, args?["serverId"]?.ToString() ?? "");
                case "add_history":
                    var hist = JsonSerializer.Deserialize<ConnectionHistory>(args?["history"]?.ToJsonString() ?? "{}");
                    if (hist != null) Db.AddHistory(_appDataDir, hist);
                    return null;

                // Crypto is handled internally using MasterKey — no key exposure to frontend

                // SSH Backend
                case "connect_ssh":
                    string sshSessionId = args?["sessionId"]?.ToString() ?? "";
                    string sshHost = args?["host"]?.ToString() ?? "";
                    int sshPort = args?["port"]?.GetValue<int>() ?? 22;
                    string sshUser = args?["username"]?.ToString() ?? "";
                    string? sshPass = args?["password"]?.ToString();
                    string? sshKey = args?["privateKey"]?.ToString();
                    string? sshPassphrase = args?["passphrase"]?.ToString();
                    string? sshServerId = args?["serverId"]?.ToString();
                    string? sshCredentialId = args?["credentialId"]?.ToString();
                    uint sshCols = args?["cols"]?.GetValue<uint>() ?? 80;
                    uint sshRows = args?["rows"]?.GetValue<uint>() ?? 24;

                    ResolveSshCredentials(sshServerId, sshCredentialId, ref sshUser, ref sshPass, ref sshKey, ref sshPassphrase);

                    await SshHandler.ConnectSsh(
                        webView, sshSessionId, sshHost, sshPort, sshUser, 
                        sshPass, sshKey, sshPassphrase, sshCols, sshRows
                    );
                    return null;

                case "write_ssh":
                case "write_ssh_input":
                    SshHandler.WriteSsh(args?["sessionId"]?.ToString() ?? "", args?["data"]?.ToString() ?? "");
                    return null;

                case "resize_ssh":
                case "resize_ssh_pty":
                    SshHandler.ResizeSsh(
                        args?["sessionId"]?.ToString() ?? "", 
                        args?["cols"]?.GetValue<uint>() ?? 80, 
                        args?["rows"]?.GetValue<uint>() ?? 24
                    );
                    return null;

                case "disconnect_ssh":
                    SshHandler.DisconnectSsh(args?["sessionId"]?.ToString() ?? "");
                    return null;

                // SFTP Backend
                case "sftp_ls":
                    string lsUser = args?["username"]?.ToString() ?? "";
                    string? lsPass = args?["password"]?.ToString();
                    string? lsKey = args?["privateKey"]?.ToString();
                    string? lsPassphrase = args?["passphrase"]?.ToString();
                    ResolveSshCredentials(args?["serverId"]?.ToString(), args?["credentialId"]?.ToString(), ref lsUser, ref lsPass, ref lsKey, ref lsPassphrase);
                    
                    return await SftpHandler.SftpLs(
                        args?["host"]?.ToString() ?? "",
                        args?["port"]?.GetValue<int>() ?? 22,
                        lsUser,
                        lsPass,
                        lsKey,
                        lsPassphrase,
                        args?["path"]?.ToString() ?? "/"
                    );

                case "sftp_download":
                    string dlUser = args?["username"]?.ToString() ?? "";
                    string? dlPass = args?["password"]?.ToString();
                    string? dlKey = args?["privateKey"]?.ToString();
                    string? dlPassphrase = args?["passphrase"]?.ToString();
                    ResolveSshCredentials(args?["serverId"]?.ToString(), args?["credentialId"]?.ToString(), ref dlUser, ref dlPass, ref dlKey, ref dlPassphrase);

                    return await SftpHandler.SftpDownload(
                        args?["host"]?.ToString() ?? "",
                        args?["port"]?.GetValue<int>() ?? 22,
                        dlUser,
                        dlPass,
                        dlKey,
                        dlPassphrase,
                        args?["remotePath"]?.ToString() ?? "",
                        args?["localPath"]?.ToString() ?? ""
                    );

                case "sftp_upload":
                    string ulUser = args?["username"]?.ToString() ?? "";
                    string? ulPass = args?["password"]?.ToString();
                    string? ulKey = args?["privateKey"]?.ToString();
                    string? ulPassphrase = args?["passphrase"]?.ToString();
                    ResolveSshCredentials(args?["serverId"]?.ToString(), args?["credentialId"]?.ToString(), ref ulUser, ref ulPass, ref ulKey, ref ulPassphrase);

                    return await SftpHandler.SftpUpload(
                        args?["host"]?.ToString() ?? "",
                        args?["port"]?.GetValue<int>() ?? 22,
                        ulUser,
                        ulPass,
                        ulKey,
                        ulPassphrase,
                        args?["localPath"]?.ToString() ?? "",
                        args?["remotePath"]?.ToString() ?? ""
                    );

                // Dialog compatibility
                case "show_open_dialog":
                    return await Dispatcher.InvokeAsync(() =>
                    {
                        var dialog = new Microsoft.Win32.OpenFileDialog();
                        return dialog.ShowDialog() == true ? dialog.FileName : null;
                    });

                case "show_save_dialog":
                    return await Dispatcher.InvokeAsync(() =>
                    {
                        var dialog = new Microsoft.Win32.SaveFileDialog();
                        return dialog.ShowDialog() == true ? dialog.FileName : null;
                    });

                // RDP Embedded Client (ActiveX overlay)
                case "connect_rdp_embedded":
                    await ConnectRdpEmbedded(args);
                    return null;

                case "resize_rdp_embedded":
                    await ResizeRdpEmbedded(args);
                    return null;

                case "disconnect_rdp_embedded":
                    await DisconnectRdpEmbedded(args?["sessionId"]?.ToString() ?? "");
                    return null;

                case "is_vault_setup":
                    return Db.GetSetting(_appDataDir, "vault_setup") != null;

                case "is_vault_unlocked":
                    return MasterKey != null;

                case "setup_master_password":
                    string setupPass = args?["password"]?.ToString() ?? "";
                    byte[] setupSalt = new byte[16];
                    RandomNumberGenerator.Fill(setupSalt);
                    Db.SetSetting(_appDataDir, "vault_salt", Convert.ToHexString(setupSalt));
                    byte[] derivedKey = Crypto.DeriveKey(setupPass, setupSalt);
                    EncryptedData verifyData = Crypto.EncryptSecret(derivedKey, "verify");
                    Db.SetSetting(_appDataDir, "vault_verify", JsonSerializer.Serialize(verifyData));
                    Db.SetSetting(_appDataDir, "vault_setup", "true");
                    MasterKey = derivedKey;
                    Log("setup_master_password: Vault initialized successfully.");
                    return null;

                case "unlock_vault":
                    string unlockPass = args?["password"]?.ToString() ?? "";
                    string? dbSaltHex = Db.GetSetting(_appDataDir, "vault_salt");
                    string? dbVerifyJson = Db.GetSetting(_appDataDir, "vault_verify");
                    if (string.IsNullOrEmpty(dbSaltHex) || string.IsNullOrEmpty(dbVerifyJson))
                    {
                        Log("unlock_vault: salt or verify token not found in database.");
                        return false;
                    }
                    try
                    {
                        byte[] dbSalt = Convert.FromHexString(dbSaltHex);
                        byte[] testKey = Crypto.DeriveKey(unlockPass, dbSalt);
                        var testVerifyData = JsonSerializer.Deserialize<EncryptedData>(dbVerifyJson);
                        if (testVerifyData == null) return false;
                        string decryptedVerify = Crypto.DecryptSecret(testKey, testVerifyData);
                        if (decryptedVerify == "verify")
                        {
                            MasterKey = testKey;
                            Log("unlock_vault: vault unlocked successfully.");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"unlock_vault decryption failure: {ex.Message}");
                    }
                    return false;

                case "lock_vault":
                    MasterKey = null;
                    Log("lock_vault called: MasterKey cleared from memory.");
                    return null;

                case "connect_rdp":
                    LaunchExternalRdp(args);
                    return null;

                case "bypass_rdp_warnings":
                    return null;

                default:
                    throw new NotSupportedException($"Command {cmd} is not supported in the C# WPF backend");
            }
        }

        private async Task ConnectRdpEmbedded(JsonObject? args)
        {
            if (args == null) return;
            string sessionId = args["sessionId"]?.ToString() ?? "";
            string serverId = args["serverId"]?.ToString() ?? "";
            string host = args["host"]?.ToString() ?? "";
            int port = args["port"]?.GetValue<int>() ?? 3389;
            string? username = args["username"]?.ToString();
            string? password = args["password"]?.ToString();

            double x = args["x"]?.GetValue<double>() ?? 0;
            double y = args["y"]?.GetValue<double>() ?? 0;
            double w = args["width"]?.GetValue<double>() ?? 800;
            double h = args["height"]?.GetValue<double>() ?? 600;
            double dpr = args["devicePixelRatio"]?.GetValue<double>() ?? 1.0;

            Log($"ConnectRdpEmbedded (Popup Mode) called. Session: {sessionId}, Host: {host}:{port}, User: {username}, Rect: {x},{y} ({w}x{h}), DPR: {dpr}");

            await Dispatcher.InvokeAsync(() =>
            {
                var rdpClient = new MyRdpClient();
                rdpClient.Dock = System.Windows.Forms.DockStyle.Fill;

                var rdpHost = new WindowsFormsHost();
                rdpHost.Child = rdpClient;

                var popup = new System.Windows.Controls.Primitives.Popup
                {
                    AllowsTransparency = false,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
                    PlacementTarget = webView, // Position relative to WebView2
                    Child = rdpHost,
                    IsOpen = false
                };

                _rdpPopups[sessionId] = popup;
                _rdpClients[sessionId] = rdpClient;
                Log("WPF Popup overlay configured with native ActiveX.");

                // Setup connection configurations
                rdpClient.HandleCreated += (s, e) =>
                {
                    Log($"rdpClient.HandleCreated triggered for {sessionId}");
                    try
                    {
                        dynamic rdp = rdpClient.Ocx;
                        rdp.Server = host;
                        rdp.DesktopWidth = (int)Math.Round(w * dpr);
                        rdp.DesktopHeight = (int)Math.Round(h * dpr);
                        if (!string.IsNullOrEmpty(username)) rdp.UserName = username;

                        dynamic advanced = rdp.AdvancedSettings9;
                        advanced.RDPPort = port;
                        advanced.SmartSizing = true;
                        advanced.AuthenticationLevel = 0; // Bypass warning prompts
                        advanced.EnableCredSspSupport = true;
                        try { advanced.BackgroundColor = 0x1B1818; } catch { }

                        if (!string.IsNullOrEmpty(password))
                        {
                            advanced.ClearTextPassword = password;
                            Log("Password configured on AdvancedSettings9");
                        }

                        // Subscribe to OnDisconnected to notify React frontend
                        rdpClient.OnDisconnected += (sender, args) =>
                        {
                            Log($"rdpClient.OnDisconnected callback received for {sessionId}");
                            // Trigger event callback to React
                            var payload = new
                            {
                                type = "event",
                                name = "rdp-closed",
                                payload = sessionId
                            };
                            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
                        };

                        Log("Calling rdp.Connect()...");
                        rdp.Connect();
                        Log("rdp.Connect() returned successfully.");
                    }
                    catch (Exception ex)
                    {
                        Log($"Exception in HandleCreated configuration: {ex.Message}\r\n{ex.StackTrace}");
                    }
                };
            });
        }

        private async Task ResizeRdpEmbedded(JsonObject? args)
        {
            if (args == null) return;
            string sessionId = args["sessionId"]?.ToString() ?? "";
            double x = args["x"]?.GetValue<double>() ?? 0;
            double y = args["y"]?.GetValue<double>() ?? 0;
            double w = args["width"]?.GetValue<double>() ?? 0;
            double h = args["height"]?.GetValue<double>() ?? 0;

            Log($"ResizeRdpEmbedded called for {sessionId}. Rect: {x},{y} ({w}x{h})");

            await Dispatcher.InvokeAsync(() =>
            {
                if (_rdpPopups.TryGetValue(sessionId, out var popup))
                {
                    if (w <= 0 || h <= 0)
                    {
                        popup.IsOpen = false;
                        Log($"Popup {sessionId} set to IsOpen = false (width/height <= 0)");
                    }
                    else
                    {
                        popup.Width = w;
                        popup.Height = h;
                        popup.HorizontalOffset = x;
                        popup.VerticalOffset = y;
                        
                        if (popup.Child is WindowsFormsHost hostControl)
                        {
                            hostControl.Width = w;
                            hostControl.Height = h;
                        }

                        popup.IsOpen = true;

                        // WPF Popup Offset refresh trick to force absolute coordinates recalcs on screen
                        var offset = popup.HorizontalOffset;
                        popup.HorizontalOffset = offset + 0.01;
                        popup.HorizontalOffset = offset;

                        Log($"Popup {sessionId} resized to {w}x{h} at {x},{y}. IsOpen set to true.");
                    }
                }
                else
                {
                    Log($"ResizeRdpEmbedded failed: Popup for {sessionId} not found in _rdpPopups");
                }
            });
        }

        private async Task DisconnectRdpEmbedded(string sessionId)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_rdpPopups.TryRemove(sessionId, out var popup))
                {
                    popup.IsOpen = false;
                    popup.Child = null;
                }
                _rdpClients.TryRemove(sessionId, out _);
                Log($"DisconnectRdpEmbedded complete for {sessionId}");
            });
        }

        private void LaunchExternalRdp(JsonObject? args)
        {
            if (args == null) return;
            string host = args["host"]?.ToString() ?? "";
            int port = args["port"]?.GetValue<int>() ?? 3389;
            string? username = args["username"]?.ToString();
            string? password = args["password"]?.ToString();

            // Store credential via Windows Credential Manager API (secure, no cmdline exposure)
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                StoreRdpCredential(host, username, password);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "mstsc",
                Arguments = $"/v:{host}:{port} /f",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }

        private void StoreRdpCredential(string host, string username, string password)
        {
            string target = $"TERMSRV/{host}";
            byte[] passwordBytes = System.Text.Encoding.Unicode.GetBytes(password);
            IntPtr blob = System.Runtime.InteropServices.Marshal.AllocHGlobal(passwordBytes.Length);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(passwordBytes, 0, blob, passwordBytes.Length);
                var cred = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = target,
                    CredentialBlobSize = (uint)passwordBytes.Length,
                    CredentialBlob = blob,
                    Persist = CRED_PERSIST_SESSION, // only persists for current logon session
                    UserName = username
                };
                CredWrite(ref cred, 0);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(blob);
            }
        }

        private void ResolveSshCredentials(
            string? serverId, 
            string? credentialId, 
            ref string username, 
            ref string? password, 
            ref string? privateKey, 
            ref string? passphrase)
        {
            try
            {
                if (MasterKey == null)
                {
                    Log("ResolveSshCredentials warning: MasterKey is null, cannot decrypt.");
                    return;
                }

                Credential? cred = null;

                if (!string.IsNullOrEmpty(credentialId))
                {
                    var credentials = Db.GetCredentials(_appDataDir);
                    cred = credentials.Find(c => c.id == credentialId);
                }
                else if (!string.IsNullOrEmpty(serverId))
                {
                    var servers = Db.GetServers(_appDataDir);
                    var server = servers.Find(s => s.id == serverId);
                    if (server != null)
                    {
                        if (!string.IsNullOrEmpty(server.username) && string.IsNullOrEmpty(username))
                        {
                            username = server.username;
                        }

                        if (!string.IsNullOrEmpty(server.credential_id))
                        {
                            var credentials = Db.GetCredentials(_appDataDir);
                            cred = credentials.Find(c => c.id == server.credential_id);
                        }
                        else if (!string.IsNullOrEmpty(server.encrypted_password))
                        {
                            var enc = JsonSerializer.Deserialize<EncryptedData>(server.encrypted_password);
                            if (enc != null)
                            {
                                password = Crypto.DecryptSecret(MasterKey, enc);
                                Log("Ssh password resolved from server encrypted_password.");
                            }
                        }
                    }
                }

                if (cred != null)
                {
                    if (string.IsNullOrEmpty(username))
                    {
                        username = cred.username;
                    }

                    if (!string.IsNullOrEmpty(cred.encrypted_secret))
                    {
                        var enc = JsonSerializer.Deserialize<EncryptedData>(cred.encrypted_secret);
                        if (enc != null)
                        {
                            string secret = Crypto.DecryptSecret(MasterKey, enc);
                            if (cred.type == "ssh_key")
                            {
                                privateKey = secret;
                                Log("Ssh privateKey resolved from credential.");
                            }
                            else
                            {
                                password = secret;
                                Log("Ssh password resolved from credential.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"ResolveSshCredentials exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static System.IO.Compression.ZipArchive? _wwwrootZip;

        private void OnWebResourceRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var uri = new Uri(e.Request.Uri);
                string path = uri.AbsolutePath.TrimStart('/');
                if (string.IsNullOrEmpty(path) || path == "/") path = "index.html";

                if (_wwwrootZip == null)
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var resourceStream = assembly.GetManifestResourceStream("RDM.wwwroot.zip");
                    if (resourceStream != null)
                    {
                        _wwwrootZip = new System.IO.Compression.ZipArchive(resourceStream);
                    }
                    else
                    {
                        Log("WebResourceRequested Error: RDM.wwwroot.zip resource stream not found in assembly.");
                    }
                }

                if (_wwwrootZip != null)
                {
                    var entry = _wwwrootZip.GetEntry(path);
                    if (entry != null)
                    {
                        using (var stream = entry.Open())
                        {
                            var ms = new MemoryStream();
                            stream.CopyTo(ms);
                            ms.Position = 0;

                            string mimeType = GetMimeType(path);
                            var response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                                ms, 
                                200, 
                                "OK", 
                                $"Content-Type: {mimeType}\r\nAccess-Control-Allow-Origin: *"
                            );
                            e.Response = response;
                            return;
                        }
                    }
                }

                e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                    null, 404, "Not Found", ""
                );
            }
            catch (Exception ex)
            {
                Log($"WebResourceRequested exception: {ex.Message}");
            }
        }

        private string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".html" or ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                _ => "application/octet-stream"
            };
        }
    }

    // Windows Forms ActiveX wrapper control
    public class MyRdpClient : System.Windows.Forms.AxHost
    {
        public event EventHandler? OnDisconnected;

        public object Ocx => GetOcx();

        public MyRdpClient() : base("8b918b82-7985-4c24-89df-c33ad2bbfbcd") // MsRdpClient9NotSafeForScripting
        {
        }

        protected override void AttachInterfaces()
        {
            // Subscribe to disconnect event dynamically
            try
            {
                dynamic ocx = Ocx;
                ocx.OnDisconnected += new Action<int>(OnDisconnectedCallback);
            }
            catch { }
        }

        private void OnDisconnectedCallback(int reason)
        {
            OnDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}
