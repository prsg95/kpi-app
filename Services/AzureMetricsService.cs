using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using KpiMgmtApi.Helpers;
using KpiMgmtApi.Models;
using KpiMgmtApi.Models.VmStats;
using Microsoft.Extensions.Configuration;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Microsoft.Extensions.Configuration;
using Azure.Core;
using Azure.Identity;
using Azure;


namespace KpiMgmtApi.Services
{
    public class AzureMetricsService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        public AzureMetricsService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        public async Task<string> GetVMMetricsAsync(string metricName, string startTime, string endTime, string subscriptionId, string resourceGroup, string vmName)
        {
            var tenantId = _config["AzureLogAnalytics:TenantId"];
            var clientId = _config["AzureLogAnalytics:ClientId"];
            var clientSecret = _config["AzureLogAnalytics:ClientSecret"];

            // Metric name → Azure metric + aggregation mapping
            var metricMapping = new Dictionary<string, (string AzureMetric, string Aggregation)>
            {
                { "CPU", ("Percentage CPU", "Maximum") },
                { "Memory", ("Available Memory Percentage", "Minimum") }
            };

            if (!metricMapping.TryGetValue(metricName, out var metricInfo))
            {
                throw new Exception("Invalid metric name.");
            }

            string token = await AzureAuthHelper.GetAzureAccessToken(tenantId, clientId, clientSecret);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            DateTime startDateTime = DateTime.ParseExact(startTime, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            DateTime endDateTime = DateTime.ParseExact(endTime, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

            string formattedStartTime = startDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string formattedEndTime = endDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

            string url = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}" +
                         $"/providers/Microsoft.Compute/virtualMachines/{vmName}/providers/microsoft.insights/metrics" +
                         $"?timespan={formattedStartTime}/{formattedEndTime}" +
                         $"&metricnames={metricInfo.AzureMetric}" +
                         $"&aggregation={metricInfo.Aggregation}&api-version=2021-05-01";

            HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Azure API call failed: {response.StatusCode}");
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            Console.WriteLine(jsonResponse);

            var metricsResponse = JsonSerializer.Deserialize<VmMetricsResponse>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (metricsResponse?.Value == null || metricsResponse.Value.Count == 0)
            {
                return JsonSerializer.Serialize(new VmMetricResponse());
            }

            var hourlyData = new Dictionary<string, List<double>>();

            foreach (var metric in metricsResponse.Value)
            {
                foreach (var series in metric.Timeseries)
                {
                    if (series.Data == null) continue;

                    foreach (var dataPoint in series.Data)
                    {
                        double? value = metricInfo.Aggregation == "Maximum"
                            ? dataPoint.Maximum
                            : dataPoint.Minimum;

                        if (!value.HasValue) continue;

                        string recordHour = dataPoint.TimeStamp.ToString("dd-MM-yyyy HH");

                        if (!hourlyData.ContainsKey(recordHour))
                        {
                            hourlyData[recordHour] = new List<double>();
                        }

                        hourlyData[recordHour].Add(value.Value);
                    }
                }
            }

            var aggregatedResponse = new VmMetricResponse();

            foreach (var entry in hourlyData)
            {
                double aggregateValue = metricInfo.Aggregation == "Maximum"
                    ? entry.Value.Max()
                    : entry.Value.Min();

                var aggregatedRecord = new VmMetricRecord
                {
                    Record_Hour = entry.Key,
                    Record_Count = aggregateValue
                };

                aggregatedResponse.FullResponse.Add(aggregatedRecord);
            }

            return JsonSerializer.Serialize(aggregatedResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }



        public async Task<VmDetailsResponse> GetVMDetailsAsync(string subscriptionId, string resourceGroup, string vmName)
        {
            var tenantId = _config["AzureLogAnalytics:TenantId"];
            var clientId = _config["AzureLogAnalytics:ClientId"];
            var clientSecret = _config["AzureLogAnalytics:ClientSecret"];

            string token = await AzureAuthHelper.GetAzureAccessToken(tenantId, clientId, clientSecret);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Fetch basic VM details
            string vmUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}" +
                           $"/providers/Microsoft.Compute/virtualMachines/{vmName}?api-version=2021-07-01";

            HttpResponseMessage vmResponse = await _httpClient.GetAsync(vmUrl);
            if (!vmResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Azure VM Details API call failed: {vmResponse.StatusCode}");
            }

            string vmJson = await vmResponse.Content.ReadAsStringAsync();
            var vmDetails = JsonSerializer.Deserialize<VmDetailsResponse>(vmJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Fetch instanceView details (for provisioning state, power state, etc.)
            string instanceViewUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}" +
                                     $"/providers/Microsoft.Compute/virtualMachines/{vmName}/instanceView?api-version=2021-07-01";

            HttpResponseMessage instanceResponse = await _httpClient.GetAsync(instanceViewUrl);
            if (instanceResponse.IsSuccessStatusCode)
            {
                string instanceJson = await instanceResponse.Content.ReadAsStringAsync();
                var instanceView = JsonSerializer.Deserialize<VmInstanceView>(instanceJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                vmDetails.InstanceView = instanceView;

                // Extract current status from instanceView
                vmDetails.CurrentStatus = instanceView?.Statuses?.FirstOrDefault(s => s.Code.StartsWith("PowerState/"))?.DisplayStatus
                                          ?? instanceView?.Statuses?.FirstOrDefault()?.DisplayStatus
                                          ?? "Unknown";
            }
            else
            {
                vmDetails.CurrentStatus = "Unknown";  // Fallback if instanceView API fails
            }

            return vmDetails;
        }

        public async Task<string> RunScriptOnVMAsync(string subscriptionId, string resourceGroup, string vmName)
        {
            // Authenticate using client credentials
            var tenantId = _config["AzureLogAnalytics:TenantId"];
            var clientId = _config["AzureLogAnalytics:ClientId"];
            var clientSecret = _config["AzureLogAnalytics:ClientSecret"];

            TokenCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

            // Create the ARM client
            ArmClient armClient = new ArmClient(credential);

            // Build the VM resource identifier
            ResourceIdentifier vmResourceId = VirtualMachineResource.CreateResourceIdentifier(subscriptionId, resourceGroup, vmName);
            VirtualMachineResource virtualMachine = armClient.GetVirtualMachineResource(vmResourceId);

            // Prepare the script to run
            var input = new RunCommandInput("RunPowerShellScript")
            {
                Script =
                {
                    // 1. Drive Info
                    "Write-Host \"===== DRIVE INFORMATION =====\"",
                    "Get-PSDrive -PSProvider FileSystem | ForEach-Object {",
                    "    $drive = $_",
                    "    if ($drive.Used -ne $null) {",
                    "        $totalSpace = [Math]::Round(($drive.Used + $drive.Free) / 1GB, 2)",
                    "        $freeSpace = [Math]::Round($drive.Free / 1GB, 2)",
                    "        Write-Host \"Drive $($drive.Name): Total: $totalSpace GB, Free: $freeSpace GB\"",
                    "    }",
                    "}",

                    // 2. System Uptime
                    "Write-Host \"`n===== SYSTEM UPTIME =====\"",
                    "$os = Get-WmiObject Win32_OperatingSystem",
                    "$uptime = (Get-Date) - $os.ConvertToDateTime($os.LastBootUpTime)",
                    "Write-Host \"Uptime: $($uptime.Days) days, $($uptime.Hours) hours, $($uptime.Minutes) minutes\"",

                    // 3. Application Version
                    "Write-Host \"`n===== APPLICATION VERSION =====\"",
                    "$version = (Get-ItemProperty -Path 'C:\\SimcorpGAIN\\DataBrowser\\DataBrowser.exe').VersionInfo.ProductVersion",
                    "Write-Host \"DataBrowser.exe ProductVersion: $version\""
                }
            };

            // Execute the RunCommand
            ArmOperation<VirtualMachineRunCommandResult> operation = await virtualMachine.RunCommandAsync(WaitUntil.Completed, input);
            VirtualMachineRunCommandResult result = operation.Value;

            // Extract output from result
            if (result.Value.Count > 0 && !string.IsNullOrWhiteSpace(result.Value[0].Message))
            {
                return result.Value[0].Message;
            }

            return "RunCommand executed successfully, but no output message was returned.";

        }



    }


    public class VmMetricResponse
    {
        public List<VmMetricRecord> FullResponse { get; set; } = new List<VmMetricRecord>();
    }

    public class VmMetricRecord
    {
        public string Record_Hour { get; set; }
        public double Record_Count { get; set; }
    }

    public class FullVmResponse
    {
        public List<VmDetailsResponse> FullResponse { get; set; }
    }

    public class VmDetailsResponse
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public string Type { get; set; }
        public Dictionary<string, string> Tags { get; set; }

        [JsonPropertyName("instanceView")]
        public VmInstanceView InstanceView { get; set; }  // This handles the instanceView API response
        public string CurrentStatus { get; set; }  // Holds the extracted current VM status

    }

    public class VmInstanceView
    {
        [JsonPropertyName("statuses")]
        public List<InstanceStatus> Statuses { get; set; }
    }

    public class InstanceStatus
    {
        public string Code { get; set; }
        public string Level { get; set; }
        public string DisplayStatus { get; set; }
        public string Message { get; set; }
        public string Time { get; set; }
    }

}
