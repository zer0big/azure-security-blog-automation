using Azure;
using Azure.Data.Tables;

namespace ProcessedPostsApi.Models;

public class ProcessedPostEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime ProcessedDate { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
