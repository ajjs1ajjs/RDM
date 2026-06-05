using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RemoteManager.ViewModels;

public partial class SftpFileViewModel : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Length { get; set; }
    public DateTime LastWriteTime { get; set; }
    public string Permissions { get; set; } = string.Empty;

    public string Icon => IsDirectory ? "📁" : "📄";
    public string SizeString => IsDirectory ? "" : FormatSize(Length);

    private static string FormatSize(long bytes)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB" };
        if (bytes == 0)
            return "0 B";
        long bytesAbs = Math.Abs(bytes);
        int place = Convert.ToInt32(Math.Floor(Math.Log(bytesAbs, 1024)));
        double num = Math.Round(bytesAbs / Math.Pow(1024, place), 1);
        return (Math.Sign(bytes) * num).ToString() + " " + suf[place];
    }
}
