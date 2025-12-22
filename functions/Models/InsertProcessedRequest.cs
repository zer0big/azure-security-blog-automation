namespace ProcessedPostsApi.Models;

public class InsertProcessedRequest
{
    public string Link { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
}
