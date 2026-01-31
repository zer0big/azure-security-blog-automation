using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace ProcessedPostsApi
{
    public class GetRecentPosts
    {
        private readonly ILogger<GetRecentPosts> _logger;
        private readonly TableServiceClient _tableServiceClient;

        public GetRecentPosts(ILogger<GetRecentPosts> logger)
        {
            _logger = logger;
            var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            _tableServiceClient = new TableServiceClient(storageConnectionString);
        }

        [Function("GetRecentPosts")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("GetRecentPosts function processing request");

            try
            {
                // Parse daysSince from query string (default = 1)
                var daysSinceStr = req.Url.Query.Contains("daysSince=") 
                    ? req.Url.Query.Split("daysSince=")[1].Split('&')[0] 
                    : "1";
                var daysSince = int.TryParse(daysSinceStr, out var days) ? days : 1;

                var limit = 15;
                if (req.Body != null)
                {
                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(requestBody))
                    {
                        try
                        {
                            var request = JsonSerializer.Deserialize<GetRecentPostsRequest>(
                                requestBody,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (request?.Limit is > 0)
                                limit = request.Limit;
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "Ignoring invalid JSON body for GetRecentPosts");
                        }
                    }
                }

                var tableClient = _tableServiceClient.GetTableClient("ProcessedPosts");
                await tableClient.CreateIfNotExistsAsync();

                var cutoff = DateTimeOffset.UtcNow.AddDays(-daysSince);
                var candidates = new List<(DateTimeOffset processedTime, object post)>();

                // Use Timestamp for server-side filtering; it's always present and queryable.
                // We'll sort in-memory to return the most recent items.
                await foreach (var entity in tableClient.QueryAsync<TableEntity>(
                    filter: $"Timestamp ge datetime'{cutoff.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}'"))
                {
                    // Prefer explicit ProcessedDate if present; otherwise fall back to entity.Timestamp.
                    DateTimeOffset processedTime = entity.Timestamp ?? DateTimeOffset.MinValue;
                    if (entity.TryGetValue("ProcessedDate", out var processedObj))
                    {
                        if (processedObj is DateTimeOffset dto)
                        {
                            processedTime = dto;
                        }
                        else if (processedObj is DateTime dt)
                        {
                            processedTime = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
                        }
                    }

                    var koreanSummary = entity.ContainsKey("KoreanSummary") 
                        ? JsonSerializer.Deserialize<List<string>>(entity.GetString("KoreanSummary") ?? "[]")
                        : new List<string>();

                    var englishSummary = entity.ContainsKey("EnglishSummary")
                        ? JsonSerializer.Deserialize<List<string>>(entity.GetString("EnglishSummary") ?? "[]")
                        : new List<string>();

                    candidates.Add((processedTime, new
                    {
                        title = entity.GetString("Title"),
                        link = entity.GetString("Link"),
                        sourceName = entity.GetString("SourceName"),
                        summary = entity.GetString("Summary"),
                        publishDate = entity.GetString("PublishDate"),
                        koreanSummary,
                        englishSummary,
                        emoji = GetEmojiForSource(entity.GetString("SourceName") ?? "")
                    }));
                }

                // Some Table endpoints can behave unexpectedly with Timestamp filters.
                // If we got no results, fall back to a bounded scan with client-side filtering.
                if (candidates.Count == 0)
                {
                    _logger.LogWarning("Timestamp filter returned 0 rows; falling back to bounded scan for GetRecentPosts");
                    var scanned = 0;
                    await foreach (var entity in tableClient.QueryAsync<TableEntity>())
                    {
                        scanned++;
                        if (scanned > 500) break;

                        DateTimeOffset processedTime = entity.Timestamp ?? DateTimeOffset.MinValue;
                        if (entity.TryGetValue("ProcessedDate", out var processedObj))
                        {
                            if (processedObj is DateTimeOffset dto)
                            {
                                processedTime = dto;
                            }
                            else if (processedObj is DateTime dt)
                            {
                                processedTime = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
                            }
                        }

                        if (processedTime < cutoff) continue;

                        var koreanSummary = entity.ContainsKey("KoreanSummary") 
                            ? JsonSerializer.Deserialize<List<string>>(entity.GetString("KoreanSummary") ?? "[]")
                            : new List<string>();

                        var englishSummary = entity.ContainsKey("EnglishSummary")
                            ? JsonSerializer.Deserialize<List<string>>(entity.GetString("EnglishSummary") ?? "[]")
                            : new List<string>();

                        candidates.Add((processedTime, new
                        {
                            title = entity.GetString("Title"),
                            link = entity.GetString("Link"),
                            sourceName = entity.GetString("SourceName"),
                            summary = entity.GetString("Summary"),
                            publishDate = entity.GetString("PublishDate"),
                            koreanSummary,
                            englishSummary,
                            emoji = GetEmojiForSource(entity.GetString("SourceName") ?? "")
                        }));
                    }
                }

                var posts = candidates
                    .OrderByDescending(x => x.processedTime)
                    .Take(limit)
                    .Select(x => x.post)
                    .ToList();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(posts);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent posts");
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new List<object>());
                return response;
            }
        }

        private static string GetEmojiForSource(string sourceName)
        {
            return sourceName switch
            {
                "Microsoft Security Blog" => "🛡️",
                "Azure Security Blog" => "🔐",
                "Microsoft Entra Blog" => "🔑",
                "Sentinel Blog" => "👁️",
                "Defender Blog" => "🛡️",
                "Azure DevOps Blog" => "🔧",
                "Azure Architecture Blog" => "📊",
                "Azure Infrastructure Blog" => "🏗️",
                "Azure Governance and Management Blog" => "🏢",
                "Azure DevOps Community" => "🔨",
                "Azure Integration Services Blog" => "⚡",
                _ => "📰"
            };
        }
    }

    public class GetRecentPostsRequest
    {
        public int Limit { get; set; } = 15;
    }
}
