namespace ProcessedPostsApi.Models;

public class CheckDuplicateRequest
{
    public string Link { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
}
