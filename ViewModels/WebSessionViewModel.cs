using System.Threading.Tasks;
using RemoteManager.Models;

namespace RemoteManager.ViewModels;

public class WebSessionViewModel : SessionTabViewModel
{
    private readonly Connection _connection;

    public string Url { get; }
    public bool IgnoreCertificateErrors { get; }

    public override string TypeIcon => "\uE12B";

    public WebSessionViewModel(Connection connection)
    {
        _connection = connection;
        ConnectionId = connection.Id;
        Header = string.IsNullOrWhiteSpace(connection.Name) ? connection.Host : connection.Name;
        Url = connection.WebSettings?.Url ?? "https://" + connection.Host;
        IgnoreCertificateErrors = connection.WebSettings?.IgnoreCertificateErrors ?? false;
        SessionInfo = $"Web Session: {Url}";
    }

    public override Task ConnectAsync()
    {
        IsConnecting = false;
        IsConnected = true;
        StatusText = $"Connected to {Url}";
        return Task.CompletedTask;
    }

    public override void Disconnect()
    {
        IsConnected = false;
        StatusText = "Disconnected";
    }
}
