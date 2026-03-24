using Azure.Core;
using Azure.Identity;

namespace MCP_MI;

public static class TokenProvider
{
    public static string GetIcmMcpApiToken()
    {
        // No pre-authorization needed - just acquire token and go!
        var credential = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = "3bc62a4d-a65e-48ed-af39-f70577ab184c" // PMEAPP ID
            });

        AccessToken accessToken = credential.GetToken(
            new TokenRequestContext(new[] { "api://icmmcpapi-ppe/.default" }));

        return accessToken.Token;
    }
}
