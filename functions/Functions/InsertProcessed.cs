using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ProcessedPostsApi.Models;
using ProcessedPostsApi.Services;

namespace ProcessedPostsApi.Functions;

public class InsertProcessed
{
    private readonly TableStorageService _tableService;
    private readonly ILogger<InsertProcessed> _logger;

    public InsertProcessed(TableStorageService tableService, ILogger<InsertProcessed> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    [Function("InsertProcessed")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("InsertProcessed API called");

        try
        {
            var request = await req.ReadFromJsonAsync<InsertProcessedRequest>();

            if (request == null || string.IsNullOrEmpty(request.Link) || 
                string.IsNullOrEmpty(request.Title) || string.IsNullOrEmpty(request.SourceName))
            {
                _logger.LogWarning("Invalid request: Link, Title, and SourceName are required");
                return new BadRequestObjectResult(new { error = "Link, Title, and SourceName are required" });
            }

            var partitionKey = _tableService.GeneratePartitionKey(request.SourceName);
            var rowKey = _tableService.GenerateRowKey(request.Link);

            _logger.LogInformation("Inserting entity - PartitionKey: {PartitionKey}, RowKey: {RowKey}", partitionKey, rowKey);

            var entity = new ProcessedPostEntity
            {
                PartitionKey = partitionKey,
                RowKey = rowKey,
                Link = request.Link,
                Title = request.Title,
                ProcessedDate = DateTime.UtcNow
            };

            var success = await _tableService.InsertEntityAsync(entity);

            if (success)
            {
                _logger.LogInformation("Entity inserted successfully - Link: {Link}", request.Link);
                return new OkObjectResult(new InsertProcessedResponse
                {
                    Success = true,
                    RowKey = rowKey
                });
            }
            else
            {
                _logger.LogError("Failed to insert entity - Link: {Link}", request.Link);
                return new ObjectResult(new { error = "Failed to insert entity" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in InsertProcessed API");
            return new ObjectResult(new { error = ex.Message })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
