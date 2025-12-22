using Azure.Data.Tables;
using Azure.Identity;
using ProcessedPostsApi.Models;
using System.Security.Cryptography;
using System.Text;

namespace ProcessedPostsApi.Services;

public class TableStorageService
{
    private readonly TableClient _tableClient;

    public TableStorageService()
    {
        var storageAccountName = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_NAME") 
            ?? throw new InvalidOperationException("STORAGE_ACCOUNT_NAME environment variable is not set");
        var tableName = Environment.GetEnvironmentVariable("ProcessedPostsTable") ?? "ProcessedPosts";
        
        var tableServiceClient = new TableServiceClient(
            new Uri($"https://{storageAccountName}.table.core.windows.net"),
            new DefaultAzureCredential()
        );
        
        _tableClient = tableServiceClient.GetTableClient(tableName);
    }

    /// <summary>
    /// Generate PartitionKey: SourceName-YYYYMM (e.g., SecurityBlog-202512)
    /// </summary>
    public string GeneratePartitionKey(string sourceName)
    {
        return $"{sourceName}-{DateTime.UtcNow:yyyyMM}";
    }

    /// <summary>
    /// Generate RowKey: SHA256 hash of link → URL-safe Base64
    /// URL-safe Base64: Replace '+' with '-', '/' with '_', remove '='
    /// </summary>
    public string GenerateRowKey(string link)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(link));
        
        // URL-safe Base64: + → -, / → _, remove =
        var base64 = Convert.ToBase64String(hashBytes);
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>
    /// Get entity from Table Storage
    /// Returns null if entity does not exist (404)
    /// </summary>
    public async Task<ProcessedPostEntity?> GetEntityAsync(string partitionKey, string rowKey)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<ProcessedPostEntity>(partitionKey, rowKey);
            return response.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Insert entity into Table Storage
    /// Returns true if successful, false otherwise
    /// </summary>
    public async Task<bool> InsertEntityAsync(ProcessedPostEntity entity)
    {
        try
        {
            await _tableClient.AddEntityAsync(entity);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
