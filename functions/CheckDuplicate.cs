using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace ProcessedPostsApi
{
    public class CheckDuplicate
    {
        private readonly ILogger<CheckDuplicate> _logger;
        private readonly TableServiceClient _tableServiceClient;

        public CheckDuplicate(ILogger<CheckDuplicate> logger)
        {
            _logger = logger;
            var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            _tableServiceClient = new TableServiceClient(storageConnectionString);
        }

        [Function("CheckDuplicate")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            _logger.LogInformation("CheckDuplicate function processing request");

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                CheckDuplicateRequest? data = null;
                if (!string.IsNullOrWhiteSpace(requestBody))
                {
                    try
                    {
                        data = JsonSerializer.Deserialize<CheckDuplicateRequest>(
                            requestBody,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Invalid JSON body for CheckDuplicate");
                        var badJson = req.CreateResponse(HttpStatusCode.BadRequest);
                        await badJson.WriteAsJsonAsync(new { error = "Invalid JSON body" });
                        return badJson;
                    }
                }

                if (string.IsNullOrEmpty(data?.Link) || string.IsNullOrEmpty(data?.SourceName))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Link and SourceName are required" });
                    return badResponse;
                }

                var tableClient = _tableServiceClient.GetTableClient("ProcessedPosts");
                await tableClient.CreateIfNotExistsAsync();

                // PartitionKey: SourceName, RowKey: URL hash
                var partitionKey = data.SourceName.Replace(" ", "");
                var rowKey = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(data.Link)
                    )
                ).Replace("/", "_").Replace("+", "-");

                try
                {
                    var entity = await tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
                    var response = req.CreateResponse(HttpStatusCode.OK);
                    await response.WriteAsJsonAsync(new { isDuplicate = true });
                    return response;
                }
                catch (Azure.RequestFailedException ex) when (ex.Status == 404)
                {
                    var response = req.CreateResponse(HttpStatusCode.OK);
                    await response.WriteAsJsonAsync(new { isDuplicate = false });
                    return response;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking duplicate");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }
    }

    public class CheckDuplicateRequest
    {
        public string? Link { get; set; }
        public string? SourceName { get; set; }
    }
}
