using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using KpiMgmtApi.Models;

namespace KpiMgmtApi.Services
{
    public class CsvFileReader
    {
        public CsvFileResponse ParseCsv(byte[] csvBytes, string filePath)
        {
            // Create a memory stream from the byte array
            using (var memoryStream = new MemoryStream(csvBytes))
            using (var reader = new StreamReader(memoryStream, Encoding.UTF8))
            {
                // Set up the CsvConfiguration
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true, // Assumes headers are present in CSV
                    IgnoreBlankLines = true, // Optionally ignore blank lines
                    TrimOptions = TrimOptions.Trim // Optionally trim fields to remove extra whitespace
                };

                // Create a CsvReader instance with the configuration
                using (var csv = new CsvReader(reader, config))
                {
                    // Read and map the CSV records to the AdUserData model
                    var records = csv.GetRecords<AdUserData>().ToList();

                    // Return the response containing both the records and the count
                    return new CsvFileResponse
                    {
                        Records = records,
                        RecordCount = records.Count
                    };
                }
            }
        }
    }

    // Response model to include both the records and the count
    public class CsvFileResponse
    {
        public List<AdUserData> Records { get; set; }
        public int RecordCount { get; set; }
    }
}
