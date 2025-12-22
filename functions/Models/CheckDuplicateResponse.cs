namespace ProcessedPostsApi.Models;

public class CheckDuplicateResponse
{
    public bool IsDuplicate { get; set; }
    public string? RowKey { get; set; }
}
