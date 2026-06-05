using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace RemoteManager.Services;

public static class Log
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RemoteManager",
        "logs");

    private static string LogFile => Path.Combine(LogDir, $"remote-manager-{DateTime.UtcNow:yyyy-MM-dd}.log");
    private static readonly object _lock = new();

    private static bool _initialized;

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        try
        {
            Directory.CreateDirectory(LogDir);
            _initialized = true;
        }
        catch
        {
        }
    }

    public static void Info(string message, [CallerMemberName] string? caller = null) =>
        Write("INFO", message, caller);

    public static void Warn(string message, [CallerMemberName] string? caller = null) =>
        Write("WARN", message, caller);

    public static void Error(string message, Exception? ex = null, [CallerMemberName] string? caller = null) =>
        Write("ERROR", ex != null ? $"{message}: {ex.Message}" : message, caller);

    public static void Debug(string message, [CallerMemberName] string? caller = null) =>
        Write("DEBUG", message, caller);

    private static void Write(string level, string message, string? caller)
    {
        try
        {
            EnsureInitialized();
            var line = $"[{DateTime.UtcNow:HH:mm:ss.fff}] [{level}] [{caller}] {message}";
            System.Diagnostics.Debug.WriteLine(line);

            lock (_lock)
            {
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}
