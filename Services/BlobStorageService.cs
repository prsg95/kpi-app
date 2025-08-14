using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using KpiMgmtApi.Data;
using KpiMgmtApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace KpiMgmtApi.Services
{
    public class BlobStorageService
    {
        private readonly TenantInfoContext _context;
        private readonly string _containerName = "gain-data";

        public BlobStorageService(TenantInfoContext context)
        {
            _context = context;
        }

        // Method to get CSV file content and full file path
        public async Task<(byte[] fileBytes, string filePath)> GetCsvFileAsync(string tenantShortName, string productName)
        {
            // Get Tenant and Product information
            var tenant = await _context.Tenants
                .Include(t => t.TenantProducts)
                .ThenInclude(tp => tp.Product)
                .FirstOrDefaultAsync(t => t.TShortName == tenantShortName &&
                                          t.TenantProducts.Any(tp => tp.Product.PName == productName));

            if (tenant == null)
                throw new Exception("Tenant or Product not found.");

            // Build connection string and initialize BlobServiceClient
            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={tenant.TAccountName};AccountKey={tenant.TAccountKey};EndpointSuffix=core.windows.net";
            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

            // Find the latest folder in ADGroup
            var adGroupPrefix = "ADGroup/";
            var latestFolder = await GetLatestFolderAsync(containerClient, adGroupPrefix);
            if (latestFolder == null)
                throw new Exception("No folders found in ADGroup.");

            var latestFolderPrefix = $"{adGroupPrefix}{latestFolder}/";

            // Fetch the CSV file from the folder
            var blobItems = new List<BlobItem>();

            await foreach (var item in containerClient.GetBlobsAsync(prefix: latestFolderPrefix))
            {
                blobItems.Add(item);
            }

            var blobItem = blobItems.FirstOrDefault();
            if (blobItem == null)
                throw new Exception("No CSV file found.");

            // Get the content of the blob
            var blobClient = containerClient.GetBlobClient(blobItem.Name);
            var download = await blobClient.DownloadContentAsync();

            // Return the file content and the full file path
            string filePath = $"{latestFolderPrefix}{blobItem.Name}";
            return (download.Value.Content.ToArray(), filePath);
        }

        // Method to get the latest folder in ADGroup
        private async Task<string> GetLatestFolderAsync(BlobContainerClient containerClient, string adGroupPrefix)
        {
            var folders = new List<string>();

            await foreach (var blobHierarchyItem in containerClient.GetBlobsByHierarchyAsync(prefix: adGroupPrefix, delimiter: "/"))
            {
                if (blobHierarchyItem.IsPrefix)
                {
                    var folderName = blobHierarchyItem.Prefix.TrimEnd('/').Split('/').Last();
                    if (DateTime.TryParseExact(folderName, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                    {
                        folders.Add(folderName);
                    }
                }
            }

            return folders.OrderByDescending(f => f).FirstOrDefault();
        }
    }
}
