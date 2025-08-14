using KpiMgmtApi.Models;
using KpiMgmtApi.Models.AdhocRequest;
using KpiMgmtApi.Models.BusinessProcess;
using KpiMgmtApi.Models.DatabaseStats;
using KpiMgmtApi.Models.DataVendorStats;
using KpiMgmtApi.Models.LongRunning;
using KpiMgmtApi.Models.TaskOverview;
using KpiMgmtApi.Models.UserActivityStats;

namespace KpiMgmtApi.Services
{
    public class MetricModelMapper
    {
        private readonly Dictionary<string, Type> _metricToModelMap = new Dictionary<string, Type>
    {
        { "user", typeof(UserResponse) },
        { "tablespace", typeof(TablespaceResponse) },
        { "vendorstats", typeof(VendorStatResponse) },
        { "filesprocessing", typeof(FilesProcessingResponse) },
        { "recordscount", typeof(RecordsCountResponse) },
        { "vendorrequestcount", typeof(VendorRequestCountResponse) },
        { "swiftmessages", typeof(SwiftMessagesResponse) },
        { "instrumentscrubbed", typeof(InstrumentScrubbedResponse) },
        { "processcompletiontime", typeof(ProcessCompletionTimeResponse) },
        { "topbusinessprocess", typeof(TopBusinessProcessResponse) },
        { "datafiles", typeof(DataFilesResponse) },
        { "top25tables", typeof(TopTablesReponse) },
        { "dbstatus", typeof(DatabaseStatusResponse) },
        { "dbindex", typeof(DbIndexResponse) },
        { "propertieschanged", typeof(PropertiesChangedResponse) },
        { "manualhandledcax", typeof(ManualhandledCaxResponse) },
        { "partieschanged", typeof(PartiesChangedResponse) },
        { "manualrequests", typeof(ManualRequestResponse) },
        { "caxplausibility", typeof(CaxPlausibilityResponse) },
        { "pricingplausibility", typeof(PricingPlausibilityResponse) },
        { "pricingexceptions", typeof(PricingExceptionsResponse) },
        { "pricingexceptionscount", typeof(PricingExceptionsCountResponse) },
        { "taskinfo", typeof(TaskInfoResponse) },
        { "oraclerversion", typeof(OracleVersionResponse) },
        { "totalpropertiescount", typeof(TotalPropertiesCountResponse) },
        { "longjobs", typeof(LongJobsResponse) },
        { "longquery", typeof(LongQueryResponse) },
        { "activesessions", typeof(ActiveSessionsResponse) },
        { "longquerydetails", typeof(QueryDetailsResponse) },
        { "longqueryperformance", typeof(QueryPerformanceResponse) },
        { "usertaskdetails", typeof(UserTaskDetailsResponse) },
        { "usertaskcount", typeof(UserTaskCountResponse) },


        // Add more mappings as necessary
    };

        public Type GetModelTypeByMetric(string metricName)
        {
            if (_metricToModelMap.TryGetValue(metricName.ToLower(), out var modelType))
            {
                return modelType;
            }
            throw new ArgumentException($"No model found for metric: {metricName}");
        }
    }

}
