using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ProcessedPostsApi.Functions;

public class SummarizePost
{
    private readonly ILogger<SummarizePost> _logger;
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(2)
    };
    private static readonly TokenCredential _credential = new DefaultAzureCredential();
    
    // Azure OpenAI Configuration
    private const int DefaultSummaryPoints = 3;
    private const int MinSummaryPoints = 1;
    private const int MaxSummaryPoints = 10;

    private const string DefaultAoaiApiVersion = "2024-12-01-preview";

    public SummarizePost(ILogger<SummarizePost> logger)
    {
        _logger = logger;
    }

    [Function("SummarizePost")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SummarizePost function triggered");

        try
        {
            // Parse request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<PostData>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || string.IsNullOrEmpty(data.Title) || string.IsNullOrEmpty(data.Content))
            {
                _logger.LogWarning("Invalid request: title or content is missing");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Title and Content are required");
                return badResponse;
            }

            _logger.LogInformation($"Processing post: {data.Title}");

            // Call Azure OpenAI for summarization and translation
            var summary = await GenerateSummaryAndTranslation(data.Title, data.Content, cancellationToken);

            // Return result
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(summary));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SummarizePost request");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Internal server error: {ex.Message}");
            return errorResponse;
        }
    }

    private async Task<SummaryResult> GenerateSummaryAndTranslation(string title, string content, CancellationToken cancellationToken)
    {
        var endpointRaw = Environment.GetEnvironmentVariable("AOAI_ENDPOINT")?.Trim();
        var deployment = Environment.GetEnvironmentVariable("AOAI_DEPLOYMENT")?.Trim();
        var apiVersion = Environment.GetEnvironmentVariable("AOAI_API_VERSION")?.Trim();
        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            apiVersion = DefaultAoaiApiVersion;
        }

        if (string.IsNullOrWhiteSpace(endpointRaw) || string.IsNullOrWhiteSpace(deployment))
        {
            throw new InvalidOperationException("AOAI_ENDPOINT and AOAI_DEPLOYMENT app settings must be set.");
        }

        var endpoint = NormalizeAoaiEndpoint(endpointRaw);

        var summaryPoints = GetSummaryPoints();

        var systemPrompt = $@"You are an expert security analyst. Your task is to:
    1. Summarize the given security blog post in exactly {summaryPoints} concise bullet points (English)
    2. Translate the summary into Korean ({summaryPoints} bullet points)

    Keep each bullet point under 150 characters. Focus on key insights, threats, and actionable information.";

        var englishExample = string.Join(", ", Enumerable.Range(1, summaryPoints).Select(i => $"\"point {i}\""));
        var koreanExample = string.Join(", ", Enumerable.Range(1, summaryPoints).Select(i => $"\"요점 {i}\""));

                var userPrompt = $@"Title: {title}

Content: {content}

Provide the output in the following JSON format:
{{
    ""englishSummary"": [{englishExample}],
    ""koreanSummary"": [{koreanExample}]
}}";

        var requestPayload = new
        {
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 1000,
            temperature = 0.3,
            response_format = new { type = "json_object" }
        };

        var requestJson = JsonSerializer.Serialize(requestPayload);
        var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var requestUri = $"{endpoint}openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
        
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri);

        // Prefer Entra ID (Managed Identity) for Azure OpenAI. Fall back to api-key for local/dev if provided.
        var apiKey = Environment.GetEnvironmentVariable("AOAI_API_KEY")?.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("api-key", apiKey);
        }
        else
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }),
                cancellationToken);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        }

        request.Content = requestContent;

        _logger.LogInformation($"Calling Azure OpenAI at {requestUri}");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError($"Azure OpenAI API error: {response.StatusCode} - {responseContent}");
            throw new Exception($"Azure OpenAI API returned {response.StatusCode}: {responseContent}");
        }

        _logger.LogInformation("Azure OpenAI response received successfully");

        var aoaiResponse = JsonSerializer.Deserialize<AzureOpenAIResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        var contentText = aoaiResponse?.Choices?[0]?.Message?.Content ?? "{}";

        var summaryData = JsonSerializer.Deserialize<SummaryData>(contentText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        _logger.LogInformation($"Parsed summaries - English: {summaryData?.EnglishSummary?.Length ?? 0}, Korean: {summaryData?.KoreanSummary?.Length ?? 0}");

        return new SummaryResult
        {
            EnglishSummary = summaryData?.EnglishSummary ?? new[] { "Summary not available" },
            KoreanSummary = summaryData?.KoreanSummary ?? new[] { "요약을 사용할 수 없습니다" }
        };
    }

    private static string NormalizeAoaiEndpoint(string raw)
    {
        // Accept either a base endpoint like:
        // - https://{resource}.openai.azure.com/
        // - https://{resource}.cognitiveservices.azure.com/
        // Or a full URL accidentally including path/query like:
        // - https://.../openai/deployments/.../chat/completions?api-version=...
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"AOAI_ENDPOINT is not a valid absolute URL: '{raw}'");
        }

        var builder = new UriBuilder(uri.Scheme, uri.Host)
        {
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Path = "/",
            Query = "",
            Fragment = ""
        };

        var normalized = builder.Uri.ToString();
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }

    private int GetSummaryPoints()
    {
        var raw = Environment.GetEnvironmentVariable("SUMMARY_POINTS");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultSummaryPoints;
        }

        if (!int.TryParse(raw.Trim(), out var points))
        {
            _logger.LogWarning("Invalid SUMMARY_POINTS value '{Value}'. Falling back to {Default}.", raw, DefaultSummaryPoints);
            return DefaultSummaryPoints;
        }

        if (points < MinSummaryPoints || points > MaxSummaryPoints)
        {
            _logger.LogWarning("SUMMARY_POINTS out of range ({Min}-{Max}): {Value}. Falling back to {Default}.", MinSummaryPoints, MaxSummaryPoints, points, DefaultSummaryPoints);
            return DefaultSummaryPoints;
        }

        return points;
    }

    // Request/Response DTOs
    public class PostData
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class SummaryResult
    {
        public string[] EnglishSummary { get; set; } = Array.Empty<string>();
        public string[] KoreanSummary { get; set; } = Array.Empty<string>();
    }

    private class SummaryData
    {
        public string[] EnglishSummary { get; set; } = Array.Empty<string>();
        public string[] KoreanSummary { get; set; } = Array.Empty<string>();
    }

    private class AzureOpenAIResponse
    {
        public Choice[]? Choices { get; set; }
    }

    private class Choice
    {
        public Message? Message { get; set; }
    }

    private class Message
    {
        public string? Content { get; set; }
    }
}
