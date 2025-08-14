using KpiMgmtApi.Models.DataVendorStats;
using KpiMgmtApi.Models.UserActivityStats;
using System.Globalization;

namespace KpiMgmtApi.Helpers
{
    public class MetricStatsHelper
    {
        public List<object> ProcessFullResponse(object response)
        {
            if(response is VendorStatResponse vednorResponse)
            {
                return vednorResponse.Result.Select(vendorStat => new
                {
                    vendorStat.Record_Hour,
                    vendorStat.Incoming_Source_Name,
                    vendorStat.Record_Count,
                    Processed_Date = DateTime.ParseExact(vendorStat.Record_Hour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture).ToString("dd.MM.yyyy"),
                    Processed_Hour = $"{DateTime.ParseExact(vendorStat.Record_Hour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture).Hour}-{DateTime.ParseExact(vendorStat.Record_Hour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture).Hour + 1}"
                }).ToList<object>();
            }
            else if(response is UserTaskCountResponse userResponse)
            {
                return userResponse.Result.Select(userTask => new
                {
                    userTask.Record_Hour,
                    userTask.UserName,
                    userTask.Record_Count,
                    Processed_Date = DateTime.ParseExact(userTask.Record_Hour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture).ToString("dd.MM.yyyy"),
                    Processed_Hour = $"{DateTime.ParseExact(userTask.Record_Hour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture).Hour}-{DateTime.ParseExact(userTask.Record_Hour, "dd.MM.yyyy HH", CultureInfo.InvariantCulture).Hour + 1}"
                }).ToList<object>();
            }
            return new List<object>();
        }

        public List<object> GetTop5Records(object response)
        {
            if (response is VendorStatResponse vendorResponse)
            {
                return vendorResponse.Result
                    .GroupBy(v => v.Incoming_Source_Name)
                    .Select(g => new
                    {
                        Name = g.Key,
                        Total_Count = g.Sum(v => v.Record_Count)
                    })
                    .OrderByDescending(v => v.Total_Count)
                    .Take(5)
                    .ToList<object>();
            }
            else if (response is UserTaskCountResponse userResponse)
            {
                return userResponse.Result
                            .GroupBy(u => u.UserName)
                            .Select(g => new
                            {
                                Name = g.Key,
                                Total_Count = g.Sum(u => u.Record_Count)
                            })
                            .OrderByDescending(u => u.Total_Count)
                            .Take(5)
                            .ToList<object>();
            }
            return new List<object>();
        }
    }
}
