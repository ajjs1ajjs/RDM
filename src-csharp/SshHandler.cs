using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Renci.SshNet;
using Microsoft.Web.WebView2.Wpf;

namespace RDM
{
    public class SshSession
    {
        public string SessionId { get; set; } = "";
        public SshClient Client { get; set; } = null!;
        public ShellStream Stream { get; set; } = null!;
        public CancellationTokenSource Cts { get; set; } = new CancellationTokenSource();
    }

    public static class SshHandler
    {
        public static readonly ConcurrentDictionary<string, SshSession> Sessions = new();
        private static WebView2? _webView;

        public static void Initialize(WebView2 webView)
        {
            _webView = webView;
        }

        public static async Task ConnectSsh(
            WebView2 webView,
            string sessionId,
            string host,
            int port,
            string username,
            string? password,
            string? privateKey,
            string? passphrase,
            uint cols,
            uint rows
        )
        {
            _webView = webView;
            ConnectionInfo connInfo;

            if (!string.IsNullOrEmpty(privateKey))
            {
                using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKey));
                var pkeyFile = new PrivateKeyFile(keyStream, passphrase);
                connInfo = new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, pkeyFile));
            }
            else
            {
                connInfo = new ConnectionInfo(host, port, username, new PasswordAuthenticationMethod(username, password ?? ""));
            }

            connInfo.Timeout = TimeSpan.FromSeconds(15);

            var client = new SshClient(connInfo);
            await Task.Run(() => client.Connect());

            var stream = client.CreateShellStream("xterm-256color", (uint)cols, (uint)rows, 0, 0, 8192);

            var session = new SshSession
            {
                SessionId = sessionId,
                Client = client,
                Stream = stream
            };

            Sessions[sessionId] = session;

            // Start background reader loop
            _ = Task.Run(() => ReadLoop(webView, session));
        }

        private static async Task ReadLoop(WebView2 webView, SshSession session)
        {
            byte[] buffer = new byte[8192];
            var token = session.Cts.Token;

            try
            {
                while (!token.IsCancellationRequested && session.Client.IsConnected)
                {
                    // ShellStream.Read might block, so we use Task.Run to offload to thread pool
                    int bytesRead = await Task.Run(() => session.Stream.Read(buffer, 0, buffer.Length), token);
                    if (bytesRead > 0)
                    {
                        string text = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        // Post chunk to React frontend
                        await webView.Dispatcher.InvokeAsync(() =>
                        {
                            var payload = new
                            {
                                type = "event",
                                name = "ssh-output",
                                payload = new
                                {
                                    session_id = session.SessionId,
                                    data = text
                                }
                            };
                            webView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(payload));
                        });
                    }
                    else
                    {
                        await Task.Delay(10, token);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SSH Read error in session {session.SessionId}: {ex.Message}");
            }
            finally
            {
                DisconnectSsh(session.SessionId);
            }
        }

        public static void WriteSsh(string sessionId, string data)
        {
            if (Sessions.TryGetValue(sessionId, out var session))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                session.Stream.Write(bytes, 0, bytes.Length);
                session.Stream.Flush();
            }
        }

        public static void ResizeSsh(string sessionId, uint cols, uint rows)
        {
            if (Sessions.TryGetValue(sessionId, out var session))
            {
                try
                {
                    var channelField = typeof(ShellStream).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (channelField != null)
                    {
                        var channel = channelField.GetValue(session.Stream);
                        if (channel != null)
                        {
                            var method = channel.GetType().GetMethod("SendWindowChangeRequest", new[] { typeof(uint), typeof(uint), typeof(uint), typeof(uint) });
                            if (method != null)
                            {
                                method.Invoke(channel, new object[] { cols, rows, 0u, 0u });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SSH Resize error: {ex.Message}");
                }
            }
        }

        public static void DisconnectSsh(string sessionId)
        {
            if (Sessions.TryRemove(sessionId, out var session))
            {
                session.Cts.Cancel();
                try { session.Stream.Dispose(); } catch { }
                try { session.Client.Disconnect(); } catch { }
                try { session.Client.Dispose(); } catch { }

                if (_webView != null)
                {
                    _webView.Dispatcher.InvokeAsync(() =>
                    {
                        var payload = new
                        {
                            type = "event",
                            name = "ssh-closed",
                            payload = sessionId
                        };
                        _webView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(payload));
                    });
                }
            }
        }
    }
}
