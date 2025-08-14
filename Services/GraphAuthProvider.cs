using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class GraphAuthProvider
{
    private readonly GraphServiceClient _graphClient;

    public GraphAuthProvider(IConfiguration configuration)
    {
        var tenantId = configuration["AzureLogAnalytics:TenantId"];
        var clientId = configuration["AzureLogAnalytics:ClientId"];
        var clientSecret = configuration["AzureLogAnalytics:ClientSecret"];

        var clientSecretCredential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _graphClient = new GraphServiceClient(clientSecretCredential);
    }

    public async Task<List<GroupDetails>> GetGainSecurityGroupsAsync(string tShortName)
    {
        try
        {
            if (string.IsNullOrEmpty(tShortName))
                throw new ArgumentNullException(nameof(tShortName));

            var validKeywords = new[]
            {
            "gainprd", "gainprod", "gainabprod",
            "gaintest", "gaindev", "gainuat", "gainsit",
            "gainabdev", "gainabtest"
        };

            var allGroups = new List<Group>();
            var nextPage = await _graphClient.Groups.GetAsync(requestConfig =>
            {
                requestConfig.QueryParameters.Filter = "securityEnabled eq true";
                requestConfig.QueryParameters.Select = new[] { "id", "displayName" };
                requestConfig.QueryParameters.Top = 999;
            });

            while (nextPage?.Value != null)
            {
                allGroups.AddRange(nextPage.Value.Where(g => g != null));

                if (!string.IsNullOrEmpty(nextPage.OdataNextLink))
                {
                    nextPage = await _graphClient.Groups.WithUrl(nextPage.OdataNextLink).GetAsync();
                }
                else
                {
                    break;
                }
            }

            var filteredGroups = allGroups
                .Where(group =>
                    group != null &&
                    !string.IsNullOrEmpty(group.DisplayName) &&
                    group.DisplayName.StartsWith(tShortName, StringComparison.OrdinalIgnoreCase) &&
                    validKeywords.Any(keyword =>
                        group.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                )
                .Select(group => new GroupDetails
                {
                    DisplayName = group.DisplayName,
                    Tenant = tShortName.ToUpper(),
                    Environment = group.DisplayName.Contains("GAINPRD", StringComparison.OrdinalIgnoreCase) ||
                                  group.DisplayName.Contains("GAINPROD", StringComparison.OrdinalIgnoreCase) ||
                                  group.DisplayName.Contains("GAINABPROD", StringComparison.OrdinalIgnoreCase)
                                  ? "PROD" : "NON-PROD",
                    Product = "GAIN",
                    GroupId = group.Id
                })
                .ToList();

            var tasks = filteredGroups.Select(async group =>
            {
                try
                {
                    var membersPage = await _graphClient.Groups[group.GroupId].Members.GetAsync(memberConfig =>
                    {
                        memberConfig.QueryParameters.Select = new[] { "displayName", "mail" };
                    });

                    group.Members = membersPage?.Value?
                        .Where(member => member is User)
                        .Select(member =>
                        {
                            var user = member as User;
                            return new GroupMember
                            {
                                DisplayName = user?.DisplayName ?? "Unknown",
                                Email = user?.Mail ?? "No Email"
                            };
                        })
                        .ToList() ?? new List<GroupMember>();
                }
                catch
                {
                    group.Members = new List<GroupMember>();
                }

                return group;
            });

            return (await Task.WhenAll(tasks)).ToList();
        }
        catch
        {
            throw;
        }
    }


}

public class GroupDetails
{
    public string DisplayName { get; set; }
    public string Tenant { get; set; }
    public string Environment { get; set; }
    public string Product { get; set; } = "GAIN";
    public List<GroupMember> Members { get; set; } = new List<GroupMember>();
    public string GroupId { get; set; }
}

public class GroupMember
{
    public string DisplayName { get; set; }
    public string Email { get; set; }
}

public class TenantRequest
{
    public string Tenant { get; set; }
}

