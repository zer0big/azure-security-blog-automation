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

            // Handle null or empty data gracefully - return empty summary instead of error
            if (data == null || string.IsNullOrEmpty(data.Title) || string.IsNullOrEmpty(data.Content))
            {
                _logger.LogWarning("RSS feed data is null or empty - returning empty summary");
                
                var emptySummary = new SummaryResult
                {
                    EnglishSummary = new[] { "[No content available]" },
                    KoreanSummary = new[] { "[사용 가능한 콘텐츠 없음]" }
                };
                
                var emptyResponse = req.CreateResponse(HttpStatusCode.OK);
                emptyResponse.Headers.Add("Content-Type", "application/json; charset=utf-8");
                var emptyJsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = false
                };
                var emptyJsonBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(emptySummary, emptyJsonOptions));
                await emptyResponse.Body.WriteAsync(emptyJsonBytes, 0, emptyJsonBytes.Length);
                return emptyResponse;
            }

            _logger.LogInformation($"Processing post: {data.Title}");

            // Call Azure OpenAI for summarization and translation
            var summary = await GenerateSummaryAndTranslation(data.Title, data.Content, cancellationToken);

            // Return result with explicit UTF-8 encoding
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            var jsonOptions = new JsonSerializerOptions
            {
                // Prevent Unicode escaping so Korean characters are preserved as-is
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false
            };
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(summary, jsonOptions));
            await response.Body.WriteAsync(jsonBytes, 0, jsonBytes.Length);

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
    1. Summarize the given security blog post in exactly {summaryPoints} concise bullet points (English).
    2. Provide an equivalent set of {summaryPoints} concise bullet points in Korean (translate naturally; do NOT output English in the Korean summary).

Hard constraints:
- Output exactly {summaryPoints} items for `englishSummary` and exactly {summaryPoints} items for `koreanSummary` in the JSON response, with no extra commentary or metadata.
- Do NOT include the sentence 'But point solutions can only go so far.' or any direct paraphrase or variant of that sentence; if the idea is relevant, restate it as a neutral, specific insight (for example: 'Integrated platforms reduce gaps between point solutions.') without using the banned wording.
- Avoid mid-word truncation or ellipses like 'cyberattac...'; always end at word boundaries.
- Keep each bullet under 140 characters and focused on insight or action.

If constraints cannot be met exactly, choose the closest valid output that satisfies JSON format and the constraints.";

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
            _logger.LogError("Azure OpenAI API error: {Status} - {Response}", response.StatusCode, TruncateForLogging(responseContent));
            throw new Exception($"Azure OpenAI API returned {response.StatusCode}: {responseContent}");
        }

        _logger.LogInformation("Azure OpenAI response received successfully (length={Length})", responseContent?.Length ?? 0);

        AzureOpenAIResponse? aoaiResponse = null;
        string contentText = "{}";
        try
        {
            aoaiResponse = JsonSerializer.Deserialize<AzureOpenAIResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            contentText = aoaiResponse?.Choices?[0]?.Message?.Content ?? "{}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize Azure OpenAI top-level response. Raw response (truncated): {Response}", TruncateForLogging(responseContent));
            // include raw contentText fallback so downstream parsing can be examined
            contentText = responseContent;
        }

        SummaryData? summaryData = null;
        try
        {
            summaryData = JsonSerializer.Deserialize<SummaryData>(contentText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse JSON payload from model. Payload (truncated): {Payload}", TruncateForLogging(contentText));
        }

        // Defensive parsing: model may return fields as JSON-encoded strings or differently-cased properties.
        if (summaryData == null)
        {
            try
            {
                using var doc = JsonDocument.Parse(contentText);
                var root = doc.RootElement;

                // If top-level is a JSON string (double-encoded), unwrap once
                if (root.ValueKind == JsonValueKind.String)
                {
                    var inner = root.GetString() ?? "{}";
                    using var innerDoc = JsonDocument.Parse(inner);
                    root = innerDoc.RootElement;
                }

                string[] ExtractArray(JsonElement el)
                {
                    if (el.ValueKind == JsonValueKind.Array)
                    {
                        return el.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();
                    }
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var s = el.GetString() ?? string.Empty;
                        // try parsing the string as JSON array
                        try
                        {
                            using var inner = JsonDocument.Parse(s);
                            if (inner.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                return inner.RootElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();
                            }
                        }
                        catch { }
                        // fallback: split on newlines or return single-item array
                        var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                        if (lines.Length > 1) return lines;
                        return new[] { s };
                    }
                    return Array.Empty<string>();
                }

                string[] english = Array.Empty<string>();
                string[] korean = Array.Empty<string>();

                if (root.TryGetProperty("englishSummary", out var engEl)) english = ExtractArray(engEl);
                else if (root.TryGetProperty("EnglishSummary", out engEl)) english = ExtractArray(engEl);

                if (root.TryGetProperty("koreanSummary", out var korEl)) korean = ExtractArray(korEl);
                else if (root.TryGetProperty("KoreanSummary", out korEl)) korean = ExtractArray(korEl);

                // if we found anything, construct a SummaryData
                if ((english?.Length ?? 0) > 0 || (korean?.Length ?? 0) > 0)
                {
                    summaryData = new SummaryData
                    {
                        EnglishSummary = english ?? Array.Empty<string>(),
                        KoreanSummary = korean ?? Array.Empty<string>()
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Defensive parsing of model payload failed. Raw payload (truncated): {Payload}", TruncateForLogging(contentText));
            }
        }

        var engCount = summaryData?.EnglishSummary?.Length ?? 0;
        var korCount = summaryData?.KoreanSummary?.Length ?? 0;
        _logger.LogInformation("Parsed summaries - English: {EngCount}, Korean: {KorCount}", engCount, korCount);

        if (korCount == 0)
        {
            _logger.LogWarning("Korean summary is empty or missing. Will return fallback Korean message. Raw payload (truncated): {Payload}", TruncateForLogging(contentText));
        }

        return new SummaryResult
        {
            EnglishSummary = summaryData?.EnglishSummary ?? new[] { "Summary not available" },
            KoreanSummary = summaryData?.KoreanSummary ?? new[] { "요약을 사용할 수 없습니다" }
        };
    }

    private static string TruncateForLogging(string? s, int max = 1000)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "...[truncated]";
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
