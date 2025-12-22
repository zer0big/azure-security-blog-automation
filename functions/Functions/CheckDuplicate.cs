using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ProcessedPostsApi.Models;
using ProcessedPostsApi.Services;

namespace ProcessedPostsApi.Functions;

public class CheckDuplicate
{
    private readonly TableStorageService _tableService;
    private readonly ILogger<CheckDuplicate> _logger;

    public CheckDuplicate(TableStorageService tableService, ILogger<CheckDuplicate> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    [Function("CheckDuplicate")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("CheckDuplicate API called");

        try
        {
            var request = await req.ReadFromJsonAsync<CheckDuplicateRequest>();

            if (request == null || string.IsNullOrEmpty(request.Link) || string.IsNullOrEmpty(request.SourceName))
            {
                _logger.LogWarning("Invalid request: Link and SourceName are required");
                return new BadRequestObjectResult(new { error = "Link and SourceName are required" });
            }

            var partitionKey = _tableService.GeneratePartitionKey(request.SourceName);
            var rowKey = _tableService.GenerateRowKey(request.Link);

            _logger.LogInformation("Checking duplicate - PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);

            var entity = await _tableService.GetEntityAsync(partitionKey, rowKey);

            if (entity != null)
            {
                // Duplicate found - return 200 OK with IsDuplicate=true
                _logger.LogInformation("Duplicate found - Link: {Link}", request.Link);
                return new OkObjectResult(new CheckDuplicateResponse
                {
                    IsDuplicate = true,
                    RowKey = rowKey
                });
            }
            else
            {
                // New item - return 200 OK with IsDuplicate=false
                _logger.LogInformation("New item - Link: {Link}", request.Link);
                return new OkObjectResult(new CheckDuplicateResponse
                {
                    IsDuplicate = false,
                    RowKey = rowKey
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CheckDuplicate API");
            return new ObjectResult(new { error = ex.Message })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
