using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RemoteManager.ViewModels;

namespace RemoteManager.Services;

public class PingService : IPingService, IDisposable
{
    private class Target
    {
        public Guid Id { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public DateTime LastChecked { get; set; }
        public PingStatus CurrentStatus { get; set; } = PingStatus.Unknown;
    }

    private readonly ConcurrentDictionary<Guid, Target> _targets = new();
    private Action<Guid, PingStatus, long>? _statusCallback;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public void StartMonitoring(Action<Guid, PingStatus, long> statusCallback)
    {
        StopMonitoring();
        _statusCallback = statusCallback;
        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
    }

    public void StopMonitoring()
    {
        _cts?.Cancel();
        _monitorTask?.Wait(1000);
        _cts?.Dispose();
        _cts = null;
        _statusCallback = null;
    }

    public void RegisterConnection(Guid connectionId, string host, int port)
    {
        _targets[connectionId] = new Target
        {
            Id = connectionId,
            Host = host,
            Port = port
        };
    }

    public void UnregisterConnection(Guid connectionId)
    {
        _targets.TryRemove(connectionId, out _);
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var kvp in _targets)
            {
                if (token.IsCancellationRequested) break;

                var target = kvp.Value;
                if (DateTime.UtcNow - target.LastChecked > _checkInterval || target.CurrentStatus == PingStatus.Unknown)
                {
                    _ = CheckTargetAsync(target, token); // Fire and forget so we don't block other checks
                }
            }

            try
            {
                await Task.Delay(2000, token); // Brief pause before next cycle
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckTargetAsync(Target target, CancellationToken token)
    {
        target.LastChecked = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(target.Host)) return;

        _statusCallback?.Invoke(target.Id, PingStatus.Checking, -1);

        var sw = Stopwatch.StartNew();
        bool isOnline = false;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(3)); // 3 seconds timeout

            using var client = new TcpClient();
            await client.ConnectAsync(target.Host, target.Port, cts.Token);
            isOnline = true;
        }
        catch
        {
            isOnline = false;
        }
        finally
        {
            sw.Stop();
            var status = isOnline ? PingStatus.Online : PingStatus.Offline;
            var latency = isOnline ? sw.ElapsedMilliseconds : -1;
            target.CurrentStatus = status;

            if (!token.IsCancellationRequested)
            {
                _statusCallback?.Invoke(target.Id, status, latency);
            }
        }
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
