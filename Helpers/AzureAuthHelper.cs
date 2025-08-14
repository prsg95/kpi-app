using Azure.Identity;
using Azure.Core;
using System;
using System.Threading.Tasks;

namespace KpiMgmtApi.Helpers
{
    public class AzureAuthHelper
    {
        public static async Task<string> GetAzureAccessToken(string tenantId, string clientId, string clientSecret)
        {
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            TokenRequestContext requestContext = new TokenRequestContext(new[] { "https://management.azure.com/.default" });
            AccessToken token = await credential.GetTokenAsync(requestContext);
            return token.Token;

        }
    }
}
