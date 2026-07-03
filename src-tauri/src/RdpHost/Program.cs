using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;

namespace RdpHost
{
    static class Program
    {
        static void Log(string message)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "rdp_host.log");
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
            }
            catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                MainImpl(args);
            }
            catch (Exception ex)
            {
                Log("FATAL UNHANDLED EXCEPTION: " + ex.ToString());
                Environment.Exit(1);
            }
        }

        static void MainImpl(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Log("RdpHost starting. Args: " + string.Join(" ", args));

            IntPtr parentHwnd = IntPtr.Zero;
            string host = "";
            int port = 3389;
            string user = "";
            string pass = "";
            int width = 800;
            int height = 600;
            int x = 0;
            int y = 0;
            bool smartSizing = true;
            string sessionId = "";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-parent" && i + 1 < args.Length)
                    parentHwnd = new IntPtr(long.Parse(args[i + 1]));
                else if (args[i] == "-host" && i + 1 < args.Length)
                    host = args[i + 1];
                else if (args[i] == "-port" && i + 1 < args.Length)
                    port = int.Parse(args[i + 1]);
                else if (args[i] == "-user" && i + 1 < args.Length)
                    user = args[i + 1];
                else if (args[i] == "-pass" && i + 1 < args.Length)
                    pass = args[i + 1];
                else if (args[i] == "-w" && i + 1 < args.Length)
                    width = int.Parse(args[i + 1]);
                else if (args[i] == "-h" && i + 1 < args.Length)
                    height = int.Parse(args[i + 1]);
                else if (args[i] == "-x" && i + 1 < args.Length)
                    x = int.Parse(args[i + 1]);
                else if (args[i] == "-y" && i + 1 < args.Length)
                    y = int.Parse(args[i + 1]);
                else if (args[i] == "-smart-sizing" && i + 1 < args.Length)
                    smartSizing = args[i + 1] == "1";
                else if (args[i] == "-session" && i + 1 < args.Length)
                    sessionId = args[i + 1];
            }

            if (parentHwnd == IntPtr.Zero || string.IsNullOrEmpty(host))
            {
                Log("Error: Missing parent HWND or Host.");
                return;
            }

            // Create a borderless form at the desired position
            Form form = new Form();
            form.Text = sessionId;
            form.FormBorderStyle = FormBorderStyle.None;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(x, y);
            form.Size = new Size(width, height);

            Log($"Form created at ({x},{y}) size {width}x{height}");

            // Create RDP ActiveX control
            MyRdpClient rdpClient = new MyRdpClient();
            rdpClient.Dock = DockStyle.Fill;
            form.Controls.Add(rdpClient);

            form.Load += (s, e) =>
            {
                Log("Form_Load event triggered.");
                try
                {
                    dynamic rdp = rdpClient.Ocx;
                    rdp.Server = host;
                    rdp.UserName = user;

                    dynamic advanced = rdp.AdvancedSettings9;
                    advanced.RDPPort = port;
                    advanced.SmartSizing = smartSizing;
                    advanced.EnableCredSspSupport = !string.IsNullOrEmpty(pass);
                    advanced.AuthenticationLevel = !string.IsNullOrEmpty(pass) ? 2 : 0;
                    advanced.NegotiateSecurityLayer = true;

                    if (!string.IsNullOrEmpty(pass))
                    {
                        advanced.ClearTextPassword = pass;
                    }

                    try
                    {
                        object ocxVal = rdpClient.Ocx;
                        if (ocxVal != null)
                        {
                            var t = ocxVal.GetType();
                            var onDisconnected = t.GetEvent("OnDisconnected");
                            if (onDisconnected != null)
                                onDisconnected.AddEventHandler(ocxVal, new EventHandler((_, _) =>
                                {
                                    Log("OnDisconnected event fired, closing form");
                                    form.Invoke(() => form.Close());
                                }));
                            var onConnected = t.GetEvent("OnConnected");
                            if (onConnected != null)
                                onConnected.AddEventHandler(ocxVal, new EventHandler((_, _) =>
                                {
                                    Log("OnConnected event fired");
                                }));
                            var onLoginComplete = t.GetEvent("OnLoginComplete");
                            if (onLoginComplete != null)
                                onLoginComplete.AddEventHandler(ocxVal, new EventHandler((_, _) =>
                                {
                                    Log("OnLoginComplete event fired");
                                }));
                            var onFatalError = t.GetEvent("OnFatalError");
                            if (onFatalError != null)
                                onFatalError.AddEventHandler(ocxVal, new EventHandler((_, _) =>
                                {
                                    Log("OnFatalError event fired, closing form");
                                    form.Invoke(() => form.Close());
                                }));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("Event subscription error: " + ex.Message);
                    }

                    Log("Calling rdp.Connect()...");
                    rdp.Connect();
                    Log("rdp.Connect() called successfully.");
                }
                catch (Exception ex)
                {
                    Log("Exception in Form_Load: " + ex.Message + "\n" + ex.StackTrace);
                }
            };

            Log("Entering Application.Run...");
            Application.Run(form);
            Log("RdpHost exiting.");
            Environment.Exit(0);
        }
    }

    public class MyRdpClient : AxHost
    {
        public MyRdpClient() : base("8b918b82-7985-4c24-89df-c33ad2bbfbcd")
        {
        }

        public object Ocx => this.GetOcx();
    }
}
