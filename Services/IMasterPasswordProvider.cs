namespace RemoteManager.Services;

public interface IMasterPasswordProvider
{
    string? CurrentMasterPassword { get; set; }
}
