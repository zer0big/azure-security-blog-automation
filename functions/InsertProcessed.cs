using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace ProcessedPostsApi
{
    public class InsertProcessed
    {
        private readonly ILogger<InsertProcessed> _logger;
        private readonly TableServiceClient _tableServiceClient;

        public InsertProcessed(ILogger<InsertProcessed> logger)
        {
            _logger = logger;
            var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            _tableServiceClient = new TableServiceClient(storageConnectionString);
        }

        [Function("InsertProcessed")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            _logger.LogInformation("InsertProcessed function processing request");

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                ProcessedPost? post = null;
                if (!string.IsNullOrWhiteSpace(requestBody))
                {
                    try
                    {
                        post = JsonSerializer.Deserialize<ProcessedPost>(
                            requestBody,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Invalid JSON body for InsertProcessed");
                        var badJson = req.CreateResponse(HttpStatusCode.BadRequest);
                        await badJson.WriteAsJsonAsync(new { error = "Invalid JSON body" });
                        return badJson;
                    }
                }

                if (string.IsNullOrEmpty(post?.Link) || string.IsNullOrEmpty(post?.SourceName))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Link and SourceName are required" });
                    return badResponse;
                }

                var tableClient = _tableServiceClient.GetTableClient("ProcessedPosts");
                await tableClient.CreateIfNotExistsAsync();

                // PartitionKey: SourceName, RowKey: URL hash
                var partitionKey = post.SourceName.Replace(" ", "");
                var rowKey = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(post.Link)
                    )
                ).Replace("/", "_").Replace("+", "-");

                var entity = new TableEntity(partitionKey, rowKey)
                {
                    { "Title", post.Title },
                    { "Link", post.Link },
                    { "SourceName", post.SourceName },
                    { "Summary", post.Summary },
                    { "PublishDate", post.PublishDate },
                    { "KoreanSummary", JsonSerializer.Serialize(post.KoreanSummary) },
                    { "EnglishSummary", JsonSerializer.Serialize(post.EnglishSummary) },
                    { "ProcessedDate", DateTime.UtcNow }
                };

                await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { success = true, partitionKey, rowKey });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting processed post");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }
    }

    public class ProcessedPost
    {
        public string? Title { get; set; }
        public string? Link { get; set; }
        public string? SourceName { get; set; }
        public string? Summary { get; set; }
        public string? PublishDate { get; set; }
        public List<string>? KoreanSummary { get; set; }
        public List<string>? EnglishSummary { get; set; }
    }
}
