using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RemoteManager.ViewModels;

namespace RemoteManager.Services;

public interface IPingService
{
    void StartMonitoring(Action<Guid, PingStatus, long> statusCallback);
    void StopMonitoring();
    void RegisterConnection(Guid connectionId, string host, int port);
    void UnregisterConnection(Guid connectionId);
}
