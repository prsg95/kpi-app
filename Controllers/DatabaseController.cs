using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KpiMgmtApi.Data;
using KpiMgmtApi.Dto;
using KpiMgmtApi.Services;
using System.Globalization;
using System.Collections;
using Microsoft.AspNetCore.Authorization;
using KpiMgmtApi.Models.DataVendorStats;
using KpiMgmtApi.Models.DatabaseStats;
using KpiMgmtApi.Models.TaskOverview;
using KpiMgmtApi.Models;
using KpiMgmtApi.Models.UserActivityStats;
using KpiMgmtApi.Helpers;
using System.Text.RegularExpressions;
using Microsoft.Graph;
using System.Linq;
using System.Threading.Tasks;

namespace TestApiConnection.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class DatabaseController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly TenantInfoContext _context;
        private readonly BlobStorageService _blobStorageService;
        private readonly CsvFileReader _csvFileReader;
        private readonly LogAnalyticsService _logAnalyticsService;
        private readonly AzureMetricsService _azureMetricsService;
        private readonly GraphAuthProvider _graphAuthProvider;


        public DatabaseController(IHttpClientFactory httpClientFactory, TenantInfoContext context, BlobStorageService blobStorageService, CsvFileReader csvFileReader, LogAnalyticsService logAnalyticsService, AzureMetricsService azureMetricsService, GraphAuthProvider graphAuthProvider)
        {
            _httpClient = httpClientFactory.CreateClient("ApiClient");
            _context = context;
            _blobStorageService = blobStorageService;
            _csvFileReader = csvFileReader;
            _logAnalyticsService = logAnalyticsService;
            _azureMetricsService = azureMetricsService;
            _graphAuthProvider = graphAuthProvider;
        }

        [HttpPost]
        [Route("get-oem-data")]
        public async Task<IActionResult> GetOemDataForMetrics(string tShortName, string envName, string pName, string metricName, string startDate, string endDate)
        {
            var environment = await _context.Envs
                .Include(e => e.Oem) // Include related Oem entity
                .Include(e => e.Tenant) // Include related Tenant entity
                .Include(e => e.Product) // Include related Product entity
                .FirstOrDefaultAsync(e => e.Tenant.TShortName == tShortName &&
                                          e.EName == envName &&
                                          e.Product.PName == pName);

            if (environment == null)
                return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");

            if (environment.Oem == null)
                return BadRequest("No OEM linked with the environment.");

            var subMetric = await _context.SubMetrics.FirstOrDefaultAsync(sm => sm.Sub_MetricName == metricName);

            if (subMetric == null)
                return BadRequest($"Metric '{metricName}' not found in Sub_Metrics.");

            string queryFromSqlServer = subMetric.Query;
            string schemaName = environment.SchemaName;

            if (!string.IsNullOrEmpty(schemaName))
            {
                queryFromSqlServer = queryFromSqlServer.Replace("{{Schema_Name}}", schemaName);
            }

            if (!DateTime.TryParseExact(startDate, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDateDt))
                return BadRequest("Invalid startDate format. Expected format is dd.MM.yyyy HH:mm");

            if (!DateTime.TryParseExact(endDate, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDateDt))
                return BadRequest("Invalid endDate format. Expected format is dd.MM.yyyy HH:mm");

            queryFromSqlServer += $" WHERE record_hour IS NOT NULL " +
                                  $"AND REGEXP_LIKE(TRIM(record_hour), '^[0-9]{{2}}\\.[0-9]{{2}}\\.[0-9]{{4}} [0-9]{{2}}$') " +
                                  $"AND record_hour BETWEEN '{startDateDt:dd.MM.yyyy HH}' AND '{endDateDt.AddHours(-1):dd.MM.yyyy HH}' " +
                                  $"AND TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI') >= TO_DATE('{startDateDt:dd.MM.yyyy HH:mm}', 'dd.mm.yyyy HH24:MI') " +
                                  $"AND TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI') < TO_DATE('{endDateDt:dd.MM.yyyy HH:mm}', 'dd.mm.yyyy HH24:MI') " +
                                  $"ORDER BY TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI')";

            var requestBody = new
            {
                sqlStatement = queryFromSqlServer,
                targetName = environment.Oem.PDBName,
                targetType = "oracle_pdb",
                credential = new
                {
                    DBCredsMonitoring = environment.Oem.NamedCred
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/target/query/v1", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Error: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var modelMapper = new MetricModelMapper();
            var modelType = modelMapper.GetModelTypeByMetric(metricName);
            var deserializedResponse = JsonSerializer.Deserialize(content, modelType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (deserializedResponse == null)
            {
                return BadRequest("Failed to parse the response.");
            }

            var resultProperty = modelType.GetProperty("Result");
            if (resultProperty == null)
            {
                return BadRequest("No 'Result' property found in the deserialized response.");
            }

            var resultList = (resultProperty.GetValue(deserializedResponse) as IEnumerable<object>)?.ToList();
            if (resultList == null)
            {
                return BadRequest("Result list is null.");
            }

            // Add Processed_Date and Processed_Hour to each record
            var fullResponseWithProcessedInfo = resultList.Select(record =>
            {
                var recordHourProperty = record.GetType().GetProperty("Record_Hour");
                var recordHour = recordHourProperty?.GetValue(record)?.ToString();

                DateTime parsedRecordHour;
                if (DateTime.TryParseExact(recordHour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedRecordHour))
                {
                    var processedDate = parsedRecordHour.ToString("dd.MM.yyyy");
                    var processedHour = $"{parsedRecordHour.Hour}-{parsedRecordHour.Hour + 1}";

                    // Use reflection to dynamically add new properties to the record
                    var newRecord = record.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(record));
                    newRecord.Add("Processed_Date", processedDate);
                    newRecord.Add("Processed_Hour", processedHour);

                    return newRecord;
                }

                return record;
            }).ToList();

            // Initialize counts
            int createdCount = 0;
            int completedCount = 0;
            int pendingCount = 0;

            // Check if metric is "taskinfo"
            if (metricName == "taskinfo")
            {
                var taskCounts = resultList
                    .Where(record => record.GetType().GetProperty("Task_Type") != null)
                    .GroupBy(record => record.GetType().GetProperty("Task_Type")?.GetValue(record)?.ToString())
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(record =>
                        {
                            var taskCountProperty = record.GetType().GetProperty("Task_Count");
                            return (int)(taskCountProperty?.GetValue(record) ?? 0);
                        })
                    );

                // Assign values
                createdCount = taskCounts.GetValueOrDefault("Created", 0);
                completedCount = taskCounts.GetValueOrDefault("Completed", 0);
                pendingCount = taskCounts.GetValueOrDefault("Pending", 0);
            }

            int total_count = resultList?.Sum(record =>
            {
                // Attempt to get Task_Count property value
                var taskCountProperty = record.GetType().GetProperty("Task_Count");
                if (taskCountProperty != null)
                {
                    return (int)(taskCountProperty.GetValue(record) ?? 0);
                }

                // Fallback to Record_Count if Task_Count is not present
                var recordCountProperty = record.GetType().GetProperty("Record_Count");
                return (int)(recordCountProperty?.GetValue(record) ?? 0);
            }) ?? 0;


            // Create an instance of TasksCount model
            var taskCountsResponse = new TasksCount
            {
                Total_Count = total_count,
                Created = createdCount,
                Completed = completedCount,
                Pending = pendingCount
            };

            var responseModel = new
            {
                FullResponse = fullResponseWithProcessedInfo,
                Count = metricName == "taskinfo"
                    ? new[] { taskCountsResponse }
                    : new[] { new TasksCount { Total_Count = total_count } } // Create a TasksCount object even if it's just total_count
            };

            return Ok(responseModel);
        }

        [HttpPost]
        [Route("get-propertiesChanged")]
        public async Task<IActionResult> GetPropertiesChanged(string tShortName, string envName, string pName, string metricName, string startDate, string endDate)
        {
            var environment = await _context.Envs
                .Include(e => e.Oem) // Include related Oem entity
                .Include(e => e.Tenant) // Include related Tenant entity
                .Include(e => e.Product) // Include related Product entity
                .FirstOrDefaultAsync(e => e.Tenant.TShortName == tShortName &&
                                          e.EName == envName &&
                                          e.Product.PName == pName);

            if (environment == null)
                return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");

            if (environment.Oem == null)
                return BadRequest("No OEM linked with the environment.");

            var subMetric = await _context.SubMetrics.FirstOrDefaultAsync(sm => sm.Sub_MetricName == metricName);

            if (subMetric == null)
                return BadRequest($"Metric '{metricName}' not found in Sub_Metrics.");

            string queryFromSqlServer = subMetric.Query;
            string schemaName = environment.SchemaName;

            if (!string.IsNullOrEmpty(schemaName))
            {
                queryFromSqlServer = queryFromSqlServer.Replace("{{Schema_Name}}", schemaName);
            }

            if (!DateTime.TryParseExact(startDate, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDateDt))
                return BadRequest("Invalid startDate format. Expected format is dd.MM.yyyy HH:mm");

            if (!DateTime.TryParseExact(endDate, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDateDt))
                return BadRequest("Invalid endDate format. Expected format is dd.MM.yyyy HH:mm");

            queryFromSqlServer += $" WHERE record_hour BETWEEN '{startDateDt:dd.MM.yyyy HH}' AND '{endDateDt.AddHours(-1):dd.MM.yyyy HH}' " +
                                  $"AND TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI') >= TO_DATE('{startDateDt:dd.MM.yyyy HH:mm}', 'dd.mm.yyyy HH24:MI') " +
                                  $"AND TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI') < TO_DATE('{endDateDt:dd.MM.yyyy HH:mm}', 'dd.mm.yyyy HH24:MI') " +
                                  $"ORDER BY TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI')";

            var requestBody = new
            {
                sqlStatement = queryFromSqlServer,
                targetName = environment.Oem.PDBName,
                targetType = "oracle_pdb",
                credential = new
                {
                    DBCredsMonitoring = environment.Oem.NamedCred
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/target/query/v1", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Error: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var modelMapper = new MetricModelMapper();
            var modelType = modelMapper.GetModelTypeByMetric(metricName);
            var deserializedResponse = JsonSerializer.Deserialize(content, modelType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (deserializedResponse == null)
            {
                return BadRequest("Failed to parse the response.");
            }

            var resultProperty = modelType.GetProperty("Result");
            if (resultProperty == null)
            {
                return BadRequest("No 'Result' property found in the deserialized response.");
            }

            var resultList = (resultProperty.GetValue(deserializedResponse) as IEnumerable<object>)?.ToList();
            if (resultList == null)
            {
                return BadRequest("Result list is null.");
            }

            // Add Processed_Date and Processed_Hour to each record
            var fullResponseWithProcessedInfo = resultList.Select(record =>
            {
                var recordHourProperty = record.GetType().GetProperty("Record_Hour");
                var recordHour = recordHourProperty?.GetValue(record)?.ToString();

                DateTime parsedRecordHour;
                if (DateTime.TryParseExact(recordHour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedRecordHour))
                {
                    var processedDate = parsedRecordHour.ToString("dd.MM.yyyy");
                    var processedHour = $"{parsedRecordHour.Hour}-{parsedRecordHour.Hour + 1}";

                    // Use reflection to dynamically add new properties to the record
                    var newRecord = record.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(record));
                    newRecord.Add("Processed_Date", processedDate);
                    newRecord.Add("Processed_Hour", processedHour);

                    return newRecord;
                }

                return record;
            }).ToList();

         
            int total_count = resultList?.Sum(record =>
            {
                var recordCountProperty = record.GetType().GetProperty("Record_Count");
                return (int)(recordCountProperty?.GetValue(record) ?? 0);
            }) ?? 0;


            var responseModel = new
            {
                FullResponse = fullResponseWithProcessedInfo,
                Total_Count = total_count
            };

            return Ok(responseModel);
        }

        [HttpPost]
        [Route("get-propertiesCount")]
        public async Task<IActionResult> GetPropertiesCount(string tShortName, string envName, string pName, string metricName, string startDate, string endDate)
        {
            var environment = await _context.Envs
                .Include(e => e.Oem) // Include related Oem entity
                .Include(e => e.Tenant) // Include related Tenant entity
                .Include(e => e.Product) // Include related Product entity
                .FirstOrDefaultAsync(e => e.Tenant.TShortName == tShortName &&
                                          e.EName == envName &&
                                          e.Product.PName == pName);

            if (environment == null)
                return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");

            if (environment.Oem == null)
                return BadRequest("No OEM linked with the environment.");

            var subMetric = await _context.SubMetrics.FirstOrDefaultAsync(sm => sm.Sub_MetricName == metricName);

            if (subMetric == null)
                return BadRequest($"Metric '{metricName}' not found in Sub_Metrics.");

            string queryFromSqlServer = subMetric.Query;
            string schemaName = environment.SchemaName;

            if (!string.IsNullOrEmpty(schemaName))
            {
                queryFromSqlServer = queryFromSqlServer.Replace("{{Schema_Name}}", schemaName);
            }

            if (!DateTime.TryParseExact(startDate, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDateDt))
                return BadRequest("Invalid startDate format. Expected format is dd.MM.yyyy HH:mm");

            if (!DateTime.TryParseExact(endDate, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDateDt))
                return BadRequest("Invalid endDate format. Expected format is dd.MM.yyyy HH:mm");

            queryFromSqlServer += $" WHERE record_hour BETWEEN '{startDateDt:dd.MM.yyyy HH}' AND '{endDateDt.AddHours(-1):dd.MM.yyyy HH}' " +
                                  $"AND TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI') >= TO_DATE('{startDateDt:dd.MM.yyyy HH:mm}', 'dd.mm.yyyy HH24:MI') " +
                                  $"AND TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI') < TO_DATE('{endDateDt:dd.MM.yyyy HH:mm}', 'dd.mm.yyyy HH24:MI') " +
                                  $"ORDER BY TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI')";

            var requestBody = new
            {
                sqlStatement = queryFromSqlServer,
                targetName = environment.Oem.PDBName,
                targetType = "oracle_pdb",
                credential = new
                {
                    DBCredsMonitoring = environment.Oem.NamedCred
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/target/query/v1", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Error: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var modelMapper = new MetricModelMapper();
            var modelType = modelMapper.GetModelTypeByMetric(metricName);
            var deserializedResponse = JsonSerializer.Deserialize(content, modelType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (deserializedResponse == null)
            {
                return BadRequest("Failed to parse the response.");
            }

            var resultProperty = modelType.GetProperty("Result");
            if (resultProperty == null)
            {
                return BadRequest("No 'Result' property found in the deserialized response.");
            }

            var resultList = (resultProperty.GetValue(deserializedResponse) as IEnumerable<object>)?.ToList();
            if (resultList == null)
            {
                return BadRequest("Result list is null.");
            }

            // Calculate total count across all records
            int totalCount = resultList?.Sum(record =>
            {
                var recordCountProperty = record.GetType().GetProperty("Record_Count");
                return (int)(recordCountProperty?.GetValue(record) ?? 0);
            }) ?? 0;

            // Group by Record_Hour and sum Record_Count
            var groupedResponse = resultList
                .Select(record =>
                {
                    var recordHourProperty = record.GetType().GetProperty("Record_Hour");
                    var recordCountProperty = record.GetType().GetProperty("Record_Count");

                    var recordHour = recordHourProperty?.GetValue(record)?.ToString();
                    var recordCount = recordCountProperty?.GetValue(record) as int? ?? 0;

                    return new
                    {
                        Record_Hour = recordHour,
                        Record_Count = recordCount
                    };
                })
                .GroupBy(r => r.Record_Hour)
                .Select(g => new
                {
                    Record_Hour = g.Key,
                    Record_Count = g.Sum(x => x.Record_Count)
                })
                .ToList();

            // Create response model with FullResponse and Total_Count
            var responseModel = new
            {
                FullResponse = groupedResponse,
                Total_Count = totalCount
            };

            return Ok(responseModel);

        }

        /*[HttpPost]
        [Route("get-datavendorstats")]
        public async Task<IActionResult> GetDataVendorStats(string tShortName, string envName, string pName, string metricName, string startDate, string endDate)
        {
            // Fetch the environment and related OEM based on tShortName and envName
            var environment = await _context.Envs
                .Include(e => e.Oem) // Include related Oem entity
                .Include(e => e.Tenant) // Include related Tenant entity
                .Include(e => e.Product) // Include related Product entity
                .FirstOrDefaultAsync(e => e.Tenant.TShortName == tShortName &&
                                          e.EName == envName &&
                                          e.Product.PName == pName);

            if (environment == null)
                return NotFound("Environment not found for the given TShortName and EnvName.");

            if (environment.Oem == null)
                return BadRequest("No OEM linked with the environment.");

            // Check for Sub_MetricName
            var subMetric = await _context.SubMetrics.FirstOrDefaultAsync(sm => sm.Sub_MetricName == metricName);

            if (subMetric == null)
            {
                return BadRequest($"Sub_Metric '{metricName}' not found.");
            }

            // Use the query from the Sub_Metrics table
            string queryFromSqlServer = subMetric.Query;

            string schemaName = environment.SchemaName;

            if (!string.IsNullOrEmpty(schemaName))
            {
                queryFromSqlServer = queryFromSqlServer.Replace("{{Schema_Name}}", schemaName);
            }

            DateTime startDateDt;
            DateTime endDateDt;

            if (!DateTime.TryParseExact(startDate, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out startDateDt))
            {
                return BadRequest("Invalid startDate format.");
            }

            if (!DateTime.TryParseExact(endDate, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out endDateDt))
            {
                return BadRequest("Invalid endDate format.");
            }

            queryFromSqlServer += $" WHERE record_hour IS NOT NULL " +
                                  $"AND REGEXP_LIKE(TRIM(record_hour), '^[0-9]{{2}}\\.[0-9]{{2}}\\.[0-9]{{4}} [0-9]{{2}}$') " +
                                  $"AND TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI') >= TO_DATE('{startDateDt:dd.MM.yyyy HH:mm}', 'dd.mm.yyyy HH24:MI') " +
                                  $"AND TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI') < TO_DATE('{endDateDt:dd.MM.yyyy HH:mm}', 'dd.mm.yyyy HH24:MI') " +
                                  $"ORDER BY TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI')";

            var requestBody = new
            {
                sqlStatement = queryFromSqlServer,
                targetName = environment.Oem.PDBName,
                targetType = "oracle_pdb",
                credential = new
                {
                    DBCredsMonitoring = environment.Oem.NamedCred
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/target/query/v1", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Error: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            // Log the response content
            Console.WriteLine("Response Content: " + content); // Replace with your preferred logging method

            var deserializedResponse = JsonSerializer.Deserialize<VendorStatResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (deserializedResponse == null)
            {
                return BadRequest("Failed to parse the response.");
            }

            // Process the FullResponse to include Processed Date and Processed Hour
            var fullResponseWithProcessedInfo = deserializedResponse.Result.Select(vendorStat => new
            {
                vendorStat.Record_Hour,
                vendorStat.Incoming_Source_Name,
                vendorStat.Record_Count,
                Processed_Date = DateTime.ParseExact(vendorStat.Record_Hour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture).ToString("dd.MM.yyyy"),
                Processed_Hour = $"{DateTime.ParseExact(vendorStat.Record_Hour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture).Hour}-{DateTime.ParseExact(vendorStat.Record_Hour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture).Hour + 1}"
            }).ToList();

            // Aggregate the Record_Count for each Incoming_Source_Name
            var aggregatedVendors = deserializedResponse.Result
                .GroupBy(v => v.Incoming_Source_Name)
                .Select(g => new
                {
                    Incoming_Source_Name = g.Key,
                    Total_File_Count = g.Sum(v => v.Record_Count)
                })
                .OrderByDescending(v => v.Total_File_Count)
                .ToList();

            // Get top 5 based on cumulative Record_Count
            var top5Vendors = aggregatedVendors.Take(5).ToList();

            return Ok(new
            {
                FullResponse = fullResponseWithProcessedInfo,
                Top5Vendors = top5Vendors
            });
        }*/

        [HttpPost]
        [Route("Top5Records")]
        public async Task<IActionResult> GetMetricStats(string tShortName, string envName, string pName, string metricName, string startDate, string endDate)
        {
            var environment = await _context.Envs
                .Include(e => e.Oem)
                .Include(e => e.Tenant)
                .Include(e => e.Product)
                .FirstOrDefaultAsync(e => e.Tenant.TShortName == tShortName &&
                                          e.EName == envName &&
                                          e.Product.PName == pName);

            if (environment == null)
                return NotFound("Environment not found for the given TShortName and EnvName.");

            if (environment.Oem == null)
                return BadRequest("No OEM linked with the environment.");

            var subMetric = await _context.SubMetrics.FirstOrDefaultAsync(sm => sm.Sub_MetricName == metricName);
            if (subMetric == null)
                return BadRequest($"Sub_Metric '{metricName}' not found.");

            string queryFromSqlServer = subMetric.Query;
            if (!string.IsNullOrEmpty(environment.SchemaName))
            {
                queryFromSqlServer = queryFromSqlServer.Replace("{{Schema_Name}}", environment.SchemaName);
            }

            if (!DateTime.TryParseExact(startDate, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDateDt) ||
                !DateTime.TryParseExact(endDate, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDateDt))
            {
                return BadRequest("Invalid date format.");
            }

            queryFromSqlServer += $" WHERE record_hour IS NOT NULL " +
                                  $"AND REGEXP_LIKE(TRIM(record_hour), '^[0-9]{{2}}\\.[0-9]{{2}}\\.[0-9]{{4}} [0-9]{{2}}$') " +
                                  $"AND TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI') >= TO_DATE('{startDateDt:dd.MM.yyyy HH:mm}', 'dd.mm.yyyy HH24:MI') " +
                                  $"AND TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI') < TO_DATE('{endDateDt:dd.MM.yyyy HH:mm}', 'dd.mm.yyyy HH24:MI') " +
                                  $"ORDER BY TO_DATE(record_hour || ':00', 'dd.mm.yyyy HH24:MI')";

            var requestBody = new
            {
                sqlStatement = queryFromSqlServer,
                targetName = environment.Oem.PDBName,
                targetType = "oracle_pdb",
                credential = new { DBCredsMonitoring = environment.Oem.NamedCred }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/target/query/v1", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Error: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine("Response Content: " + content);

            // Deserialize dynamically based on metric type
            object deserializedResponse;
            if (metricName == "vendorstats")
            {
                deserializedResponse = JsonSerializer.Deserialize<VendorStatResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            else if (metricName == "usertaskcount")
            {
                deserializedResponse = JsonSerializer.Deserialize<UserTaskCountResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            else
            {
                return BadRequest("Unsupported metric type.");
            }

            if (deserializedResponse == null)
                return BadRequest("Failed to parse the response.");

            var metricStatsHelper = new MetricStatsHelper();

            var fullResponseWithProcessedInfo = metricStatsHelper.ProcessFullResponse(deserializedResponse);
            var top5Results = metricStatsHelper.GetTop5Records(deserializedResponse);

            return Ok(new
            {
                FullResponse = fullResponseWithProcessedInfo,
                Top5Results = top5Results
            });
        }


        [HttpPost]
        [Route("get-database-stats")]
        public async Task<IActionResult> GetDatabaseData(string tShortName, string envName, string pName, string metricName)
        {
            // Fetch the environment details
            var environment = await _context.Envs
                .Include(e => e.Oem)
                .Include(e => e.Tenant)
                .Include(e => e.Product)
                .FirstOrDefaultAsync(e => e.Tenant.TShortName == tShortName &&
                                          e.EName == envName &&
                                          e.Product.PName == pName);

            if (environment == null)
                return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");

            if (environment.Oem == null)
                return BadRequest("No OEM linked with the environment.");

            // Fetch the query for the given metric name
            var subMetric = await _context.SubMetrics.FirstOrDefaultAsync(sm => sm.Sub_MetricName == metricName);
            if (subMetric == null)
                return BadRequest($"Metric '{metricName}' not found in Sub_Metrics.");

            string queryFromSqlServer = subMetric.Query;

            // Replace schema name placeholder in the query
            string schemaName = environment.SchemaName;
            if (!string.IsNullOrEmpty(schemaName))
            {
                queryFromSqlServer = queryFromSqlServer.Replace("{{Schema_Name}}", schemaName);
            }

            // Prepare the request body
            var requestBody = new
            {
                sqlStatement = queryFromSqlServer,
                targetName = environment.Oem.PDBName,
                targetType = "oracle_pdb",
                credential = new
                {
                    DBCredsMonitoring = environment.Oem.NamedCred
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            // Send the request to OEM API
            var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/target/query/v1", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Error: {errorContent}");
            }

            // Process the response
            var content = await response.Content.ReadAsStringAsync();
            var modelMapper = new MetricModelMapper();
            var modelType = modelMapper.GetModelTypeByMetric(metricName);
            var deserializedResponse = JsonSerializer.Deserialize(content, modelType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (deserializedResponse == null)
            {
                return BadRequest("Failed to parse the response.");
            }

            var resultProperty = modelType.GetProperty("Result");
            if (resultProperty == null)
            {
                return BadRequest("No 'Result' property found in the deserialized response.");
            }

            var resultList = (resultProperty.GetValue(deserializedResponse) as IEnumerable<object>)?.ToList();

            // Processed_Date and Processed_Hour Logic
            if (resultList != null && resultList.Any())
            {
                resultList = resultList.Select(record =>
                {
                    var recordHourProperty = record.GetType().GetProperty("Record_Hour");
                    var recordHour = recordHourProperty?.GetValue(record)?.ToString();

                    if (DateTime.TryParseExact(recordHour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedRecordHour))
                    {
                        var processedDate = parsedRecordHour.ToString("dd.MM.yyyy");
                        var processedHour = $"{parsedRecordHour.Hour}-{parsedRecordHour.Hour + 1}";

                        // Add Processed_Date and Processed_Hour dynamically
                        var newRecord = record.GetType().GetProperties()
                                              .ToDictionary(p => p.Name, p => p.GetValue(record));

                        newRecord.Add("Processed_Date", processedDate);
                        newRecord.Add("Processed_Hour", processedHour);

                        return newRecord;
                    }

                    return record; // Return original if parsing fails
                }).ToList();
            }

            // Check if Record_Count exists in the response and calculate total count
            int totalCount = 0;
            if (resultList != null && resultList.Any())
            {
                totalCount = resultList.Sum(item =>
                {
                    if (item is Dictionary<string, object> recordDict && recordDict.TryGetValue("Record_Count", out var countValue))
                    {
                        return Convert.ToInt32(countValue);
                    }
                    return 0;
                });
            }

            // Construct response
            var responseModel = new
            {
                FullResponse = resultList,
                Count = new[] { new { Total_Count = totalCount } }
            };


            return Ok(responseModel);
        }


        [HttpPost]
        [Route("get-tablespace-stats")]
        public async Task<IActionResult> GetTablespaceStats(string tShortName, string envName, string pName, string metricName)
        {
            var environment = await _context.Envs
                .Include(e => e.Oem)
                .Include(e => e.Tenant)
                .Include(e => e.Product)
                .FirstOrDefaultAsync(e => e.Tenant.TShortName == tShortName &&
                                          e.EName == envName &&
                                          e.Product.PName == pName);

            if (environment == null)
                return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");

            if (environment.Oem == null)
                return BadRequest("No OEM linked with the environment.");

            var subMetric = await _context.SubMetrics.FirstOrDefaultAsync(sm => sm.Sub_MetricName == metricName);
            if (subMetric == null)
                return BadRequest($"Metric '{metricName}' not found in Sub_Metrics.");

            string queryFromSqlServer = subMetric.Query;
            string schemaName = environment.SchemaName;

            if (!string.IsNullOrEmpty(schemaName))
            {
                queryFromSqlServer = queryFromSqlServer.Replace("{{Schema_Name}}", schemaName);
            }

            var requestBody = new
            {
                sqlStatement = queryFromSqlServer,
                targetName = environment.Oem.PDBName,
                targetType = "oracle_pdb",
                credential = new
                {
                    DBCredsMonitoring = environment.Oem.NamedCred
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/target/query/v1", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Error: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var responseModel = JsonSerializer.Deserialize<TablespaceResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (responseModel == null || responseModel.Result == null || !responseModel.Result.Any())
            {
                return BadRequest("Failed to parse the response or no data found.");
            }

            // Calculate Total_Size, Total_Used_Size, PercentUsed, and PercentFree
            double totalSize = 0;
            double totalUsedSize = 0;

            foreach (var ts in responseModel.Result)
            {
                if (ts.Ts_Size > 0)
                {
                    ts.PercentUsed = Math.Round((ts.Us_Size / ts.Ts_Size) * 100, 2);
                    ts.PercentFree = Math.Round((ts.Fr_Size / ts.Ts_Size) * 100, 2);
                }
                else
                {
                    ts.PercentUsed = 0;
                    ts.PercentFree = 0;
                }

                totalSize += ts.Ts_Size;
                totalUsedSize += ts.Us_Size;
            }

            var resultList = responseModel.Result;

            // Create the new response format
            var formattedResponse = new
            {
                FullResponse = resultList,
                Count = new[]
                {
            new { Total_Size = Math.Round(totalSize, 2), Total_Used_Size = Math.Round(totalUsedSize, 2) }
        }
            };

            return Ok(formattedResponse);
        }

        [HttpPost]
        [Route("get-database-info")]
        public async Task<IActionResult> GetDatabaseStatus(string tShortName, string envName, string pName, string metricName)
        {
            // Fetch the environment details
            var environment = await _context.Envs
                .Include(e => e.Oem) // Include related Oem entity
                .Include(e => e.Tenant) // Include related Tenant entity
                .Include(e => e.Product) // Include related Product entity
                .FirstOrDefaultAsync(e => e.Tenant.TShortName == tShortName &&
                                          e.EName == envName &&
                                          e.Product.PName == pName);

            if (environment == null)
                return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");

            if (environment.Oem == null)
                return BadRequest("No OEM linked with the environment.");

            // Fetch the query for the given metric name
            var subMetric = await _context.SubMetrics.FirstOrDefaultAsync(sm => sm.Sub_MetricName == metricName);
            if (subMetric == null)
                return BadRequest($"Metric '{metricName}' not found in Sub_Metrics.");

            string queryFromSqlServer = subMetric.Query;

            // Replace schema name placeholder in the query
            string schemaName = environment.SchemaName;
            if (!string.IsNullOrEmpty(schemaName))
            {
                queryFromSqlServer = queryFromSqlServer.Replace("{{Schema_Name}}", schemaName);
            }

            // Prepare the request body
            var requestBody = new
            {
                sqlStatement = queryFromSqlServer,
                targetName = environment.Oem.PDBName,
                targetType = "oracle_pdb",
                credential = new
                {
                    DBCredsMonitoring = environment.Oem.NamedCred
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            // Send the request to OEM API
            var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/target/query/v1", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Error: {errorContent}");
            }

            // Process the response
            var content = await response.Content.ReadAsStringAsync();
            var modelMapper = new MetricModelMapper();
            var modelType = modelMapper.GetModelTypeByMetric(metricName);
            var deserializedResponse = JsonSerializer.Deserialize(content, modelType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (deserializedResponse == null)
            {
                return BadRequest("Failed to parse the response.");
            }

            var resultProperty = modelType.GetProperty("Result");
            if (resultProperty == null)
            {
                return BadRequest("No 'Result' property found in the deserialized response.");
            }

            var resultList = (resultProperty.GetValue(deserializedResponse) as IEnumerable<dynamic>)?.ToList();

            if (resultList == null)
            {
                return BadRequest("No results returned from the query.");
            }

            // Calculate the Status field
            var enhancedResultList = resultList.Select(item =>
            {
                string openMode = item.Open_Mode?.ToString().ToUpper();
                string restricted = item.Restricted?.ToString().ToUpper();
                string status = openMode switch
                {
                    "READ WRITE" when restricted == "NO" => "Running",
                    "READ WRITE" when restricted == "YES" => "Running in restricted mode",
                    "MOUNTED" => "Stopped",
                    _ => "Unknown"
                };

                // Add the calculated Status field
                return new
                {
                    item.Name,
                    item.Open_Mode,
                    item.Restricted,
                    item.Creation_Time,
                    item.Database_Uptime,
                    item.Total_Size_GB,
                    Status = status
                };
            }).ToList();

            // Return the enhanced response
            var responseModel = new
            {
                FullResponse = enhancedResultList
            };

            return Ok(responseModel);
        }


        [HttpGet]
        [Route("get-target-details")]
        public async Task<IActionResult> GetTargetDetails(string tShortName, string envName, string pName)
        {
            // Fetch environment details
            var environment = await _context.Envs
                .Include(e => e.Oem)
                .Include(e => e.Tenant)
                .Include(e => e.Product)
                .FirstOrDefaultAsync(e => e.Tenant.TShortName == tShortName &&
                                          e.EName == envName &&
                                          e.Product.PName == pName);

            if (environment == null)
                return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");

            if (environment.Oem == null)
                return BadRequest("No OEM linked with the environment.");

            string targetId = environment.Oem.TargetId;
            if (string.IsNullOrEmpty(targetId))
                return BadRequest("Target ID not found for the specified OEM.");

            try
            {
                // Fetch target details (BaseAddress is already set)
                var targetDetailsResponse = await _httpClient.GetAsync($"api/targets/{targetId}");

                if (!targetDetailsResponse.IsSuccessStatusCode)
                {
                    var errorContent = await targetDetailsResponse.Content.ReadAsStringAsync();
                    return StatusCode((int)targetDetailsResponse.StatusCode, $"Error fetching target details: {errorContent}");
                }

                var targetDetails = await targetDetailsResponse.Content.ReadAsStringAsync();

                // Fetch target properties
                var targetPropertiesResponse = await _httpClient.GetAsync($"api/targets/{targetId}/properties");

                if (!targetPropertiesResponse.IsSuccessStatusCode)
                {
                    var errorContent = await targetPropertiesResponse.Content.ReadAsStringAsync();
                    return StatusCode((int)targetPropertiesResponse.StatusCode, $"Error fetching target properties: {errorContent}");
                }

                var targetProperties = await targetPropertiesResponse.Content.ReadAsStringAsync();

                // Return the combined response
                var responseModel = new
                {
                    TargetDetails = JsonSerializer.Deserialize<object>(targetDetails),
                    TargetProperties = JsonSerializer.Deserialize<object>(targetProperties)
                };

                return Ok(responseModel);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }

        [HttpGet("list-AdUserData")]
        public async Task<IActionResult> ListBlobs([FromQuery] string tenantShortName, [FromQuery] string productName)
        {
            try
            {
                // Get CSV file from Blob Storage
                var (csvFileBytes, filePath) = await _blobStorageService.GetCsvFileAsync(tenantShortName, productName);

                // Parse the CSV file
                var csvFileResponse = _csvFileReader.ParseCsv(csvFileBytes, filePath);

                return Ok(new
                {
                    Data = csvFileResponse.Records,
                    Count = csvFileResponse.RecordCount,
                    FilePath = filePath
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [Route("get-servicestatus")]
        public async Task<IActionResult> GetServicStatus(string tShortName, string pName, string Env)
        {
            try
            {
                // Validate selection criteria
                if (tShortName != "All Clients")
                {
                    return BadRequest("Only 'All Clients' is supported for this API.");
                }

                // Ensure the environment value is valid (either 'PROD' or 'NON-PROD')
                if (Env != "PROD" && Env != "NON-PROD")
                {
                    return BadRequest("Invalid environment value. Allowed values: 'PROD' or 'NON-PROD'.");
                }

                // Check if the Product is linked to the Tenant
                var tenantProduct = await _context.TenantProducts
                    .Include(tp => tp.Tenant)
                    .Include(tp => tp.Product)
                    .FirstOrDefaultAsync(tp => tp.Tenant.TShortName == tShortName && tp.Product.PName == pName);

                if (tenantProduct == null)
                {
                    return NotFound($"No association found between Tenant '{tShortName}' and Product '{pName}'.");
                }

                // Fetch the workspace ID based on the environment
                var workspaces = await _context.ClientWorkspaces
                   .Where(cw => cw.Environment == Env)
                   .Select(cw => cw.WorkspaceID)
                   .ToListAsync();

                if (!workspaces.Any())
                {
                    return NotFound($"No workspaces found for the specified environment: {Env}.");
                }

                var logTasks = workspaces.Select(workspace => _logAnalyticsService.GetLogsAsync(workspace));
                var results = await System.Threading.Tasks.Task.WhenAll(logTasks);

                // 🔹 Filter out empty responses and merge valid ones into a single response
                var mergedResults = results
                    .Where(result => result is not null && ((dynamic)result).FullResponse.Count > 0)
                    .SelectMany(result => ((List<Dictionary<string, object>>)((dynamic)result).FullResponse))
                    .ToList();

                return Ok(new { FullResponse = mergedResults });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving logs", error = ex.Message });
            }
        }

        [HttpPost]
        [Route("get-servicelogs")]
        public async Task<IActionResult> GetServicelogs(string tShortName, string pName, string Env)
        {
            try
            {
                // Validate selection criteria
                if (tShortName != "All Clients")
                {
                    return BadRequest("Only 'All Clients' is supported for this API.");
                }

                // Ensure the environment value is valid (either 'PROD' or 'NON-PROD')
                if (Env != "PROD" && Env != "NON-PROD")
                {
                    return BadRequest("Invalid environment value. Allowed values: 'PROD' or 'NON-PROD'.");
                }

                // Check if the Product is linked to the Tenant
                var tenantProduct = await _context.TenantProducts
                    .Include(tp => tp.Tenant)
                    .Include(tp => tp.Product)
                    .FirstOrDefaultAsync(tp => tp.Tenant.TShortName == tShortName && tp.Product.PName == pName);

                if (tenantProduct == null)
                {
                    return NotFound($"No association found between Tenant '{tShortName}' and Product '{pName}'.");
                }
                // Fetch the workspace ID based on the environment
                var workspaces = await _context.ClientWorkspaces
                   .Where(cw => cw.Environment == Env)
                   .Select(cw => cw.WorkspaceID)
                   .ToListAsync();

                if (!workspaces.Any())
                {
                    return NotFound($"No workspaces found for the specified environment: {Env}.");
                }

                // Fetch logs if criteria match
                var logTasks = workspaces.Select(workspace => _logAnalyticsService.GetLogsAsync2(workspace));
                var results = await System.Threading.Tasks.Task.WhenAll(logTasks);

                // 🔹 Filter out empty responses and merge valid ones into a single response
                var mergedResults = results
                    .Where(result => result is not null && ((dynamic)result).FullResponse.Count > 0)
                    .SelectMany(result => ((List<Dictionary<string, object>>)((dynamic)result).FullResponse))
                    .ToList();

                return Ok(new { FullResponse = mergedResults });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving logs", error = ex.Message });
            }
        }

        [HttpGet("vmmetrics")]
        public async Task<IActionResult> GetVMMetrics(string tShortName, string pName, string Env, [FromQuery] string metric, [FromQuery] string startTime, [FromQuery] string endTime)
        {
            try
            {
                var environment = await _context.Envs
                                          .Include(e => e.Tenant)  // Include related Tenant entity
                                          .Include(e => e.Product) // Include related Product entity
                                          .Where(e => e.Tenant.TShortName == tShortName &&
                                                      e.EName == Env &&
                                                      e.Product.PName == pName)
                                          .Select(e => new
                                          {
                                              SubscriptionId = e.TID,  // Fetching Subscription ID from TID
                                              ResourceGroup = e.RgName,  // Fetching Resource Group from RgName
                                              VmName = e.EName  // Fetching VM Name from EName
                                          })
                                          .FirstOrDefaultAsync();

                if (environment == null)
                    return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");

                string result = await _azureMetricsService.GetVMMetricsAsync(metric, startTime, endTime, environment.SubscriptionId, environment.ResourceGroup, environment.VmName);

                // Deserialize the raw JSON string into an object
                var formattedResponse = JsonSerializer.Deserialize<object>(result);

                // Return the object with proper JSON formatting
                return Ok(formattedResponse);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("vm/details")]
        public async Task<IActionResult> GetVMDetails(string tShortName, string pName, string Env)
        {
            try
            {
                var environment = await _context.Envs
                    .Include(e => e.Tenant)  // Include related Tenant entity
                    .Include(e => e.Product) // Include related Product entity
                    .Where(e => e.Tenant.TShortName == tShortName &&
                                e.EName == Env &&
                                e.Product.PName == pName)
                    .Select(e => new
                    {
                        SubscriptionId = e.TID,        // Fetching Subscription ID from TID
                        ResourceGroup = e.RgName,      // Fetching Resource Group from RgName
                        VmName = e.EName               // Fetching VM Name from EName
                    })
                    .FirstOrDefaultAsync();

                if (environment == null)
                    return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");

                // Call the service method that returns a single VmDetailsResponse
                VmDetailsResponse vmDetail = await _azureMetricsService.GetVMDetailsAsync(
                    environment.SubscriptionId,
                    environment.ResourceGroup,
                    environment.VmName
                );

                // Wrap it inside FullVmResponse
                var result = new FullVmResponse
                {
                    FullResponse = new List<VmDetailsResponse> { vmDetail }
                };

                return Ok(result); // Return the wrapped response
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("vmscript")]
        public async Task<IActionResult> GetVMScriptDetails(string tShortName, string pName, string Env)
        {
            try
            {
                var environment = await _context.Envs
                    .Include(e => e.Tenant)
                    .Include(e => e.Product)
                    .Where(e => e.Tenant.TShortName == tShortName &&
                                e.EName == Env &&
                                e.Product.PName == pName)
                    .Select(e => new
                    {
                        SubscriptionId = e.TID,
                        ResourceGroup = e.RgName,
                        VmName = e.EName
                    })
                    .FirstOrDefaultAsync();

                if (environment == null)
                {
                    return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");
                }

                string scriptOutput = await _azureMetricsService.RunScriptOnVMAsync(
                    environment.SubscriptionId,
                    environment.ResourceGroup,
                    environment.VmName
                );

                var outputLines = scriptOutput?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

                // Format the result
                var driveUtil = new Dictionary<string, object>();
                int days = 0, hours = 0, minutes = 0;
                string appVersion = "";

                foreach (var line in outputLines)
                {
                    if(line.StartsWith("Drive "))
{
                        var match = Regex.Match(line, @"Drive (\w:)\sTotal:\s([\d.]+)\sGB,\sFree:\s([\d.]+)\sGB");
                        if (match.Success)
                        {
                            var name = match.Groups[1].Value;
                            var total = double.Parse(match.Groups[2].Value);
                            var free = double.Parse(match.Groups[3].Value);
                            var percent = total == 0 ? 0 : Math.Round((free / total) * 100, 2);

                            driveUtil[name] = new
                            {
                                TotalGB = total,
                                FreeGB = free,
                                FreePercent = percent
                            };
                        }
                    }
                    else if (line.StartsWith("Uptime:"))
                    {
                        // Example: Uptime: 1 days, 1 hours, 3 minutes
                        var match = Regex.Match(line, @"Uptime:\s(?<d>\d+)\sdays,\s(?<h>\d+)\shours,\s(?<m>\d+)\sminutes");
                        if (match.Success)
                        {
                            days = int.Parse(match.Groups["d"].Value);
                            hours = int.Parse(match.Groups["h"].Value);
                            minutes = int.Parse(match.Groups["m"].Value);
                        }
                    }
                    else if (line.StartsWith("DataBrowser.exe"))
                    {
                        var parts = line.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            appVersion = parts[1].Trim();
                        }
                    }
                }

                return Ok(new
                {
                    FullResponse = new[]
                   {
                    new
                        {
                            VmName = environment.VmName,
                            DriveUtilisation = driveUtil,
                            Uptime = new { Days = days, Hours = hours, Minutes = minutes },
                            AppVersion = appVersion
                        }
                    }
                });

            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }


        [HttpPost]
        [Route("get-tenant-groups")]
        public async Task<IActionResult> GetTenantGroups(string tShortName, string pName, string Env)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tShortName) || string.IsNullOrWhiteSpace(pName) || string.IsNullOrWhiteSpace(Env))
                {
                    return BadRequest("Missing or invalid input parameters.");
                }

                var environment = await _context.Envs
                    .Include(e => e.Tenant)
                    .Include(e => e.Product)
                    .Where(e => e.Tenant.TShortName == tShortName &&
                                e.EName == Env &&
                                e.Product.PName == pName)
                    .Select(e => new
                    {
                        SubscriptionId = e.TID,
                        VmName = e.EName
                    })
                    .FirstOrDefaultAsync();

                if (environment == null)
                {
                    return NotFound("Environment not found for the given TShortName, EnvName, and Product Name.");
                }

                var mappedEnv = MapEnvironment(Env);
                if (mappedEnv != "PROD" && mappedEnv != "NON-PROD")
                {
                    return BadRequest("Invalid environment value. Allowed values: 'PROD' or 'NON-PROD'.");
                }

                var allGroups = await _graphAuthProvider.GetGainSecurityGroupsAsync(tShortName);

                if (allGroups == null || !allGroups.Any())
                {
                    return NotFound($"No groups found for Tenant '{tShortName}'.");
                }

                var filteredGroups = allGroups
                    .Where(g => g.Environment.Equals(mappedEnv, StringComparison.OrdinalIgnoreCase))
                    .ToList();


                if (!filteredGroups.Any())
                {
                    return NotFound($"No {Env.ToUpper()} groups found for Tenant '{tShortName}'.");
                }

                var tenantGroups = filteredGroups.Select(g => new
                {
                    g.DisplayName,
                    Tenant = tShortName.ToUpper(),
                    Environment = g.Environment.ToUpper(),
                    g.Members
                }).OrderByDescending(g => g.Members != null && g.Members.Any())
                  .ToList();

                var uniqueMemberCount = tenantGroups
                    .SelectMany(g => g.Members)
                    .Where(m => !string.IsNullOrWhiteSpace(m.Email))
                    .Select(m => m.Email.ToLower())
                    .Distinct()
                    .Count();

                var finalResponse = new
                {
                    FullResponse = new[]
                    {
                        new
                        {
                            Count = new[]
                            {
                                new
                                {
                                    TotalGroups = tenantGroups.Count,
                                    TotalUniqueMembers = uniqueMemberCount,
                                }
                            },
                            Groups = tenantGroups
                        }
                    }
                };

                return Ok(finalResponse);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving groups", error = ex.ToString() });
            }
        }


        private string MapEnvironment(string env)
        {
            if (string.IsNullOrWhiteSpace(env))
                return null;

            var lowerEnv = env.ToLower();

            if (lowerEnv.Contains("prd") || lowerEnv.Contains("prod"))
                return "PROD";

            // Treat all other valid inputs as NON-PROD
            return "NON-PROD";
        }


        [HttpPost]
        [Route("check-swiftmessage")]
        public async Task<IActionResult> CheckSwiftMessage(string tShortName, string envName, string pName)
        {
            // Fetch environment details
            var environment = await _context.Envs
                .Include(e => e.Oem)
                .Include(e => e.Tenant)
                .Include(e => e.Product)
                .FirstOrDefaultAsync(e => e.Tenant.TShortName == tShortName &&
                                          e.EName == envName &&
                                          e.Product.PName == pName);

            if (environment == null)
                return NotFound("Environment not found for the given TShortName and EnvName.");

            if (environment.Oem == null)
                return BadRequest("No OEM linked with the environment.");
            
            string schemaName = environment.SchemaName;
            // Define the SQL query
            string query = "SELECT COUNT(*) AS COUNT FROM {{schemaName}}.SA_SW_RESPONSE";
            query = query.Replace("{{schemaName}}", schemaName);

            // Create request body
            var requestBody = new
            {
                sqlStatement = query,
                targetName = environment.Oem.PDBName,
                targetType = "oracle_pdb",
                credential = new
                {
                    DBCredsMonitoring = environment.Oem.NamedCred
                }
            };

            var jsonContent = new StringContent(
             JsonSerializer.Serialize(requestBody),
             Encoding.UTF8,
             "application/json"
            );

            var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/target/query/v1", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Error: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine("Response Content: " + content);

            try
            {
                var deserializedResponse = JsonSerializer.Deserialize<CountResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (deserializedResponse == null || deserializedResponse.Result == null || deserializedResponse.Result.Count == 0)
                {
                    return BadRequest("Failed to parse the response or no data found.");
                }

                return Ok(new { Count = deserializedResponse.Result[0].COUNT });
            }
            catch (JsonException ex)
            {
                return BadRequest($"JSON Parsing Error: {ex.Message}");
            }
        }

        [HttpPost]
        [Route("check-restorePoints")]
        public async Task<IActionResult> CheckRestorePoints(string tShortName, string pName)
        {
            if (tShortName != "All Clients")
            {
                return BadRequest("Only 'All Clients' is supported for this API.");
            }

            // Check if the Product is linked to the Tenant
            var tenantProduct = await _context.TenantProducts
                .Include(tp => tp.Tenant)
                .Include(tp => tp.Product)
                .FirstOrDefaultAsync(tp => tp.Tenant.TShortName == tShortName && tp.Product.PName == pName);

            if (tenantProduct == null)
            {
                return NotFound($"No association found between Tenant '{tShortName}' and Product '{pName}'.");
            }

            string query = "SELECT NAME, SCN, TIME, DATABASE_INCARNATION# AS Database_Incarnation, GUARANTEE_FLASHBACK_DATABASE, STORAGE_SIZE FROM V$RESTORE_POINT";

            // Fetch all PDBs
            var pdbsList = await _context.Pdbs.ToListAsync();

            if (pdbsList == null || !pdbsList.Any())
            {
                return NotFound("No PDBs found.");
            }

            var results = new List<object>();

            foreach (var pdb in pdbsList)
            {
                var requestBody = new
                {
                    sqlStatement = query,
                    targetName = pdb.PdbName,
                    targetType = "oracle_pdb",
                    credential = new
                    {
                        DBCredsMonitoring = pdb.NamedCred
                    }
                };

                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                try
                {
                    var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/target/query/v1", jsonContent);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var deserializedResponse = JsonSerializer.Deserialize<RestorePointResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        // If no restore points are found, explicitly add a message
                        if (deserializedResponse?.Result == null || !deserializedResponse.Result.Any())
                        {
                            results.Add(new { PdbName = pdb.PdbName, Status = "No", Checked = "Yes", RestorePoints = "No restore points found." });
                        }
                        else
                        {
                            var formattedRestorePoints = deserializedResponse.Result.Select(rp => new
                            {
                                rp.Name,
                                rp.Scn,
                                rp.Time,
                                rp.Database_Incarnation,
                                rp.Guarantee_Flashback_Database,
                                StorageSizeGB = rp.StorageSizeGB,  // Include converted size
                                rp.ConvertedTime
                            }).ToList();

                            results.Add(new { PdbName = pdb.PdbName, Status = "Yes", Checked = "Yes", RestorePoints = formattedRestorePoints });
                        }
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        results.Add(new { PdbName = pdb.PdbName, Status = $"Error: {errorContent}", Checked = "No" });
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new { PdbName = pdb.PdbName, Status = $"Exception: {ex.Message}" });
                }
            }

            return Ok(results);
        }


        /*[HttpPost]
        [Route("get-user-details")]
        public async Task<IActionResult> GetUserDetailsAsync([FromBody] ConnectionRequest request)
        {
            var environment = await _context.Envs
                .Include(e => e.Oem)
                .Include(e => e.Tenant)
                .FirstOrDefaultAsync(e => e.EName == request.EName && e.Tenant.TShortName == request.TShortName);

            if (environment == null)
            {
                return NotFound("Environment not found for the given TShortName and EName.");
            }

            var requestBody = new
            {
                sqlStatement = $"select * from all_users where user_id = @userId",
                targetName = environment.Oem.PDBName,
                targetType = "oracle_pdb",
                DBCredsMonitoring = environment.Oem.NamedCred
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("websvcs/restful/emws/oracle.sysman.db/executesql/repository/query/v1", jsonContent);
            //response.EnsureSuccessStatusCode();
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Error: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var userResponse = JsonSerializer.Deserialize<UserResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (userResponse != null && userResponse.Result.Count > 0)
            {
                var user = userResponse.Result[0];
                return Ok(user);
            }

            return NotFound("User not found");
        }*/

        [HttpGet]
        [Route("get-envs-by-shortname/{tShortName}/{pName}")]
        public async Task<IActionResult> GetEnvsByTenantShortNameAndProductNameAsync(string tShortName, string pName)
        {
            var tenants = await _context.Tenants
                .Where(t => t.TShortName == tShortName)
                .ToListAsync();

            if (!tenants.Any())
            {
                return NotFound("No tenant found with the specified short name.");
            }

            var product = await _context.Products
                .Where(p => p.PName == pName)
                .FirstOrDefaultAsync();

            if (product == null)
            {
                return NotFound("No product found with the specified name.");
            }

            var tenantId = tenants.First().TID; // Get the first matching tenant ID

            var envs = await _context.Envs
                .Where(e => e.TID == tenantId && e.Pid == product.PID)
                .Select(e => new EnvDto // Use the DTO to avoid circular references
                {
                    EID = e.EID,
                    Name = e.EName
                    // Map other properties as needed
                })
                .ToListAsync();

            if (!envs.Any())
            {
                return NotFound("No environments found for the specified tenant and product.");
            }

            return Ok(envs);
        }


        [HttpGet]
        [Route("get-all-clients")]
        public async Task<IActionResult> GetAllTShortNamesAsync()
        {
            // Fetch TID and TShortName from the database
            var clients = await _context.Tenants
                .Select(t => new ClientDto
                {
                    id = t.TID,         
                    name = t.TName ?? " ", 
                    shortName = t.TShortName
                })
                .ToListAsync();

            // Check if there are no clients found
            if (clients == null || !clients.Any())
            {
                return NotFound("No Clients found.");
            }

            // Return the transformed list of clients
            return Ok(clients);
        }

        [HttpGet]
        [Route("get-all-products")]
        public async Task<IActionResult> GetAllProductsAsync()
        {
            var products = await _context.Products
                .Select(p => new ProductDto
                {
                    id = p.PID,
                    name = p.PName
                })
                .ToListAsync();
           
            // Check if there are no products found
            if(products == null || !products.Any())
            {
                return NotFound("No Products found.");
            }
            return Ok(products);
        }

        [HttpGet]
        [Route("get-products-from-tenant/{tShortName}")]
        public async Task<IActionResult> GetProductsFromTenantAsync(string tShortName)
        {
            // Validate if the Tenant Short Name exists
            var tenantExists = await _context.Tenants
                .AnyAsync(t => t.TShortName == tShortName);

            if (!tenantExists)
            {
                return NotFound($"No tenant found with the short name: {tShortName}");
            }

            // Query Tenant, TenantProduct, and Product to get the list of products for the given tShortName
            var products = await _context.Tenants
                .Where(t => t.TShortName == tShortName)  // Filter by Tenant Short Name
                .Join(
                    _context.TenantProducts,
                    t => t.TID,  // Join with TenantProducts based on TID
                    tp => tp.TID,
                    (t, tp) => new { t, tp }
                )
                .Join(
                    _context.Products,
                    tp => tp.tp.PID,  // Join with Products based on PID
                    p => p.PID,
                    (t, p) => new ProductDto
                    {
                        id = p.PID,
                        name = p.PName
                    }
                )
                .ToListAsync();

            // Check if no products are found for the given Tenant Short Name
            if (!products.Any())
            {
                return NotFound($"No products found for Tenant Short Name: {tShortName}");
            }

            return Ok(products);
        }

        [HttpGet]
        [Route("get-tenants-from-product/{pName}")]
        public async Task<IActionResult> GetTenantsFromProductAsync(string pName)
        {
            // Validate if the Product Name exists
            var productExists = await _context.Products
                .AnyAsync(p => p.PName == pName);

            if (!productExists)
            {
                return NotFound($"No product found with the name: {pName}");
            }

            // Query Product, TenantProduct, and Tenant to get the list of tenants for the given pName
            var tenants = await _context.Products
                .Where(p => p.PName == pName)  // Filter by Product Name
                .Join(
                    _context.TenantProducts,
                    p => p.PID,  // Join with TenantProducts based on PID
                    tp => tp.PID,
                    (p, tp) => new { p, tp }
                )
                .Join(
                    _context.Tenants,
                    tp => tp.tp.TID,  // Join with Tenants based on TID
                    t => t.TID,
                    (p, t) => new ClientDto
                    {
                        id = t.TID,
                        name = t.TName,
                        shortName = t.TShortName
                    }
                )
                .ToListAsync();

            // Check if no tenants are found for the given Product Name
            if (!tenants.Any())
            {
                return NotFound($"No tenants found for Product Name: {pName}");
            }

            return Ok(tenants);
        }


        [HttpGet]
        [Route("get-all-app-metrics")]
        public async Task<IActionResult> GetAllAppMetricsAsync()
        {
            var metrics = await _context.Metrics
                .Where(m => m.MetricType == "Application" )
                .Select(m => new MetricDto
                {
                    Id = m.ID,
                    Name = m.MetricName,
                    MetricType = m.MetricType,
                    CreatedAt = m.CreatedAt
                })
                .ToListAsync();

            // Check if there are no metrics found
            if (metrics == null || !metrics.Any())
            {
                return NotFound("No App Metrics found.");
            }

            // Return the transformed list of metrics
            return Ok(metrics);
        }

        [HttpGet]
        [Route("get-all-infra-metrics")]
        public async Task<IActionResult> GetAllInfraMetricsAsync()
        {
            var metrics = await _context.Metrics
                .Where(m => m.MetricType == "Infrastructure")
                .Select(m => new MetricDto
                {
                    Id = m.ID,
                    Name = m.MetricName,
                    MetricType = m.MetricType,
                    CreatedAt = m.CreatedAt
                })
                .ToListAsync();

            // Check if there are no metrics found
            if (metrics == null || !metrics.Any())
            {
                return NotFound("No App Metrics found.");
            }

            // Return the transformed list of metrics
            return Ok(metrics);
        }

    }

    public class ConnectionRequest
    {
        public string TShortName { get; set; }
        public string EName { get; set; }
    }
}
