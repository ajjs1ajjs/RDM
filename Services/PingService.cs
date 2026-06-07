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
    private readonly ISettingsService? _settings;
    private int _batchIndex;

    public PingService(ISettingsService? settings = null)
    {
        _settings = settings;
    }

    private TimeSpan CheckInterval => TimeSpan.FromSeconds(
        Math.Max(5, _settings?.Current?.PingIntervalSeconds ?? 30));

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
        try { _monitorTask?.Wait(1000); }
        catch (AggregateException) { /* task cancellation is expected */ }
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
            var interval = CheckInterval;
            var targets = _targets.Values.ToArray();

            // Process targets in batches to avoid network spikes
            int batchSize = Math.Max(5, targets.Length / 5);
            var batch = targets.Skip(_batchIndex * batchSize).Take(batchSize).ToList();
            _batchIndex = (_batchIndex + 1) % Math.Max(1, (targets.Length + batchSize - 1) / batchSize);

            foreach (var target in batch)
            {
                if (token.IsCancellationRequested) break;

                if (DateTime.UtcNow - target.LastChecked > interval || target.CurrentStatus == PingStatus.Unknown)
                {
                    _ = CheckTargetAsync(target, token);
                }
            }

            try
            {
                await Task.Delay(Math.Min(2000, (int)interval.TotalMilliseconds / 2), token);
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
