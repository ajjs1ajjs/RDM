using RemoteManager.Services;

namespace RemoteManager.Helpers;

public static class CredentialResolver
{
    public static (string Username, string Password) ResolveCredentials(ICredentialService credentialService, Models.Connection? connection, Guid connectionId)
    {
        string username = connection?.Username ?? "";
        string password = credentialService.Load(connectionId) ?? "";

        if (connection != null && !string.IsNullOrEmpty(username))
        {
            string? domain = ExtractDomain(username);

            if (!string.IsNullOrEmpty(domain))
            {
                var matchedCred = credentialService.LoadDomainCredential(domain);

                if (matchedCred != null)
                {
                    if (string.IsNullOrEmpty(password))
                    {
                        password = matchedCred.Value.Password;
                    }

                    bool isJustDomain = username.EndsWith("\\") ||
                                        username.EndsWith("@") ||
                                        string.Equals(username, domain, System.StringComparison.OrdinalIgnoreCase);

                    if (isJustDomain && !string.IsNullOrEmpty(matchedCred.Value.Username))
                    {
                        username = matchedCred.Value.Username;
                    }
                }
            }
        }

        return (username, password);
    }

    internal static string? ExtractDomain(string username)
    {
        if (username.Contains('\\'))
            return username.Split('\\')[0].Trim();
        if (username.Contains('@'))
            return username.Split('@')[1].Trim();
        return null;
    }
}
