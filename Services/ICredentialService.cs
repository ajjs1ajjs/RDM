using System;

namespace RemoteManager.Services;

public interface ICredentialService
{
    string CredentialDir { get; }
    void Save(Guid connectionId, string password);
    string? Load(Guid connectionId);
    void Delete(Guid connectionId);
    void SaveAdditional(Guid connectionId, string key, string value);
    string? LoadAdditional(Guid connectionId, string key);
    void DeleteAdditional(Guid connectionId, string key);
    void SaveDomainCredential(string domain, string username, string password);
    (string Username, string Password)? LoadDomainCredential(string domain);
    void DeleteDomainCredential(string domain);
}
