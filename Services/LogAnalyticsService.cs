using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Collections;

namespace KpiMgmtApi.Services
{
    public class LogAnalyticsService
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public LogAnalyticsService(IConfiguration configuration)
        {
            _configuration = configuration;
            _httpClient = new HttpClient();
        }

        private async Task<string> GetAccessToken()
        {
            var tenantId = _configuration["AzureLogAnalytics:TenantId"];
            var clientId = _configuration["AzureLogAnalytics:ClientId"];
            var clientSecret = _configuration["AzureLogAnalytics:ClientSecret"];
            var url = $"https://login.microsoftonline.com/{tenantId}/oauth2/token";

            var requestData = new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "resource", "https://api.loganalytics.io" }
            };

            var requestContent = new FormUrlEncodedContent(requestData);
            var response = await _httpClient.PostAsync(url, requestContent);
            var responseString = await response.Content.ReadAsStringAsync();

            var jsonResponse = JsonSerializer.Deserialize<Dictionary<string, string>>(responseString);
            return jsonResponse["access_token"];
        }

        public async Task<object> GetLogsAsync(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId))
            {
                throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
            }

            var url = $"https://api.loganalytics.io/v1/workspaces/{workspaceId}/query";
            var token = await GetAccessToken();

            var kqlQuery = @"
                Event
                | where TimeGenerated >= ago(30d)
                | where Source == ""Service Control Manager""
                | where EventID in (7036, 7031, 7000)
                | where RenderedDescription has ""SimCorp""
                | parse RenderedDescription with * ""SimCorp Gain "" ServiceName: string "" "" *
                | project TimeGenerated, Computer, ServiceName, EventID, RenderedDescription
                | summarize arg_max(TimeGenerated, *) by Computer, ServiceName  // Get the latest log per service per machine
                | order by TimeGenerated desc";

            var requestBody = new { query = kqlQuery };
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.PostAsync(url, jsonContent);
            var responseString = await response.Content.ReadAsStringAsync();

            // 🔹 Instead of returning multiple responses, check for an empty response first
            var transformedResponse = TransformResponse(responseString);

            return transformedResponse ?? new { FullResponse = new List<object>() };
        }


        private object TransformResponse(string responseString)
        {
            var mergedFullResponse = new List<Dictionary<string, object>>();

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tables", out JsonElement tables) || tables.GetArrayLength() == 0)
                return null;  // 🔹 Instead of returning an empty response, return null

            var table = tables[0];

            if (!table.TryGetProperty("columns", out JsonElement columnsElement) ||
                !table.TryGetProperty("rows", out JsonElement rowsElement))
                return null;  // 🔹 Return null instead of multiple empty objects

            var columns = columnsElement.EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
            var rows = rowsElement.EnumerateArray();

            var allServices = new List<string> { "DataManagementService", "IntegrationService", "Message", "WM" };

            foreach (var row in rows)
            {
                var rowData = row.EnumerateArray().Select(value => value.ToString()).ToList();
                var logEntry = new Dictionary<string, object>();

                for (int i = 0; i < columns.Count; i++)
                {
                    logEntry[columns[i]] = rowData[i];
                }

                if (!logEntry.ContainsKey("Computer") || !logEntry.ContainsKey("ServiceName") || !logEntry.ContainsKey("RenderedDescription"))
                    continue;

                var computerName = logEntry["Computer"].ToString();
                var client = ExtractClientFromComputerName(computerName);
                var serviceName = logEntry["ServiceName"].ToString().Trim();
                var status = GetStatusFromRenderedDescription(logEntry["RenderedDescription"].ToString());

                var existingEntry = mergedFullResponse.FirstOrDefault(x => x["Client"].ToString() == client && x["EnvName"].ToString() == computerName.Split('.')[0]);

                if (existingEntry == null)
                {
                    var newEntry = new Dictionary<string, object>
            {
                { "Client", client },
                { "EnvName", computerName.Split('.')[0] }
            };

                    foreach (var svc in allServices)
                    {
                        newEntry[svc] = "NA";
                    }

                    newEntry[serviceName] = status;
                    mergedFullResponse.Add(newEntry);
                }
                else
                {
                    existingEntry[serviceName] = status;
                }
            }

            return mergedFullResponse.Count > 0 ? new { FullResponse = mergedFullResponse } : null; // 🔹 Return null if no valid data is found
        }



        public async Task<object> GetLogsAsync2(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId))
            {
                throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
            }

            var url = $"https://api.loganalytics.io/v1/workspaces/{workspaceId}/query";
            var token = await GetAccessToken();

            var kqlQuery = @"
                Event
                | where TimeGenerated >= ago(30d)
                | where Source == ""Service Control Manager""
                | where EventID in (7036, 7031, 7000)
                | where RenderedDescription has ""SimCorp""
                | parse RenderedDescription with * ""SimCorp Gain "" ServiceName: string "" "" *
                | project TimeGenerated, Computer, ServiceName, EventID, RenderedDescription
                | summarize arg_max(TimeGenerated, *) by Computer, ServiceName  // Get the latest log per service per machine
                | order by TimeGenerated desc";

            var requestBody = new { query = kqlQuery };
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.PostAsync(url, jsonContent);
            var responseString = await response.Content.ReadAsStringAsync();

            // 🔹 Instead of returning multiple responses, check for an empty response first
            var transformedResponse = TransformResponse2(responseString);

            return transformedResponse ?? new { FullResponse = new List<object>() };
        }


        private object TransformResponse2(string responseString)
        {
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tables", out var tables) || tables.GetArrayLength() == 0)
            {
                return new { FullResponse = new List<object>() };
            }

            var table = tables[0];
            var columns = table.GetProperty("columns").EnumerateArray()
                .Select(c => c.GetProperty("name").GetString()).ToList();
            var rows = table.GetProperty("rows").EnumerateArray();

            var logEntries = new List<Dictionary<string, object>>();

            foreach (var row in rows)
            {
                var rowData = row.EnumerateArray().Select(value => value.ToString()).ToList();
                var logEntry = new Dictionary<string, object>();

                for (int i = 0; i < columns.Count; i++)
                {
                    logEntry[columns[i]] = rowData[i];
                }

                // Safely get values from the dictionary
                if (!logEntry.TryGetValue("Computer", out var computerName) || string.IsNullOrEmpty(computerName.ToString()))
                {
                    continue; // Skip this row if "Computer" is missing
                }

                var client = ExtractClientFromComputerName(computerName.ToString());

                logEntries.Add(new Dictionary<string, object>
        {
            { "Computer", computerName.ToString().Split('.')[0] }, // Extract first part of the computer name
            { "Client", client }, // Extracted client dynamically
            { "ServiceName", logEntry.TryGetValue("ServiceName", out var serviceName) ? serviceName : "Unknown" },
            { "TimeGenerated", logEntry.TryGetValue("TimeGenerated", out var timeGenerated) ? timeGenerated : "N/A" },
            { "EventID", logEntry.TryGetValue("EventID", out var eventID) ? eventID : "N/A" },
            { "Status", logEntry.TryGetValue("RenderedDescription", out var renderedDesc) ?
                GetStatusFromRenderedDescription(renderedDesc.ToString()) : "Unknown" },
            { "RenderedDescription", logEntry.TryGetValue("RenderedDescription", out var description) ? description : "N/A" }
        });
            }

            return new { FullResponse = logEntries };
        }


        private string ExtractClientFromComputerName(string computerName)
        {
            var parts = computerName.Split('.');
            return parts.Length > 1 ? parts[1] : "unknown";
        }

        private string GetStatusFromRenderedDescription(string renderedDescription)
        {
            if (string.IsNullOrEmpty(renderedDescription)) return "Unknown";
            var lowerDescription = renderedDescription.ToLower();

            if (lowerDescription.Contains("running")) return "Running";
            if (lowerDescription.Contains("stopped") || lowerDescription.Contains("terminated") || lowerDescription.Contains("failed to start")) return "Stopped";
            return "Unknown";
        }
    }
}
