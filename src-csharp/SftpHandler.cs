using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;

namespace RDM
{
    public static class SftpHandler
    {
        private static ConnectionInfo GetConnectionInfo(string host, int port, string username, string? password, string? privateKey, string? passphrase)
        {
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
            return connInfo;
        }

        public static async Task<string> SftpLs(string host, int port, string username, string? password, string? privateKey, string? passphrase, string path)
        {
            var connInfo = GetConnectionInfo(host, port, username, password, privateKey, passphrase);
            using var client = new SshClient(connInfo);
            
            return await Task.Run(() =>
            {
                client.Connect();
                // Match the ls -la command argument format exactly
                using var cmd = client.CreateCommand($"ls -la \"{path.Replace("\"", "\\\"")}\"");
                string result = cmd.Execute();
                client.Disconnect();
                return result;
            });
        }

        public static async Task<string> SftpDownload(string host, int port, string username, string? password, string? privateKey, string? passphrase, string remotePath, string localPath)
        {
            var connInfo = GetConnectionInfo(host, port, username, password, privateKey, passphrase);
            using var client = new SftpClient(connInfo);
            
            await Task.Run(() =>
            {
                client.Connect();
                using (var fs = File.OpenWrite(localPath))
                {
                    client.DownloadFile(remotePath, fs);
                }
                client.Disconnect();
            });

            return "Download completed successfully";
        }

        public static async Task<string> SftpUpload(string host, int port, string username, string? password, string? privateKey, string? passphrase, string localPath, string remotePath)
        {
            var connInfo = GetConnectionInfo(host, port, username, password, privateKey, passphrase);
            using var client = new SftpClient(connInfo);

            await Task.Run(() =>
            {
                client.Connect();
                using (var fs = File.OpenRead(localPath))
                {
                    client.UploadFile(fs, remotePath);
                }
                client.Disconnect();
            });

            return "Upload completed successfully";
        }
    }
}
