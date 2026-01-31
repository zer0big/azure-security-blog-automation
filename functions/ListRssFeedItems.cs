using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.ServiceModel.Syndication;
using System.Xml;

namespace ProcessedPostsApi
{
    public class ListRssFeedItems
    {
        private readonly ILogger<ListRssFeedItems> _logger;
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static ListRssFeedItems()
        {
            // Microsoft Security 블로그의 User-Agent 차단을 우회하기 위한 브라우저 User-Agent 설정
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/rss+xml, application/xml, text/xml, */*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,ko;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        }

        public ListRssFeedItems(ILogger<ListRssFeedItems> logger)
        {
            _logger = logger;
        }

        [Function("ListRssFeedItems")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("ListRssFeedItems function processing request");

            try
            {
                // Parse request parameters
                string? feedUrl = null;
                int daysSince = 1;
                bool useTestData = false;

                // GET 방식: query string에서 파라미터 추출
                if (req.Method == "GET")
                {
                    var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                    feedUrl = query["feedUrl"];
                    var daysSinceStr = query["daysSince"] ?? query["since"];
                    if (!string.IsNullOrEmpty(daysSinceStr) && int.TryParse(daysSinceStr, out var days))
                    {
                        daysSince = days;
                    }
                }
                // POST 방식: request body에서 파라미터 추출
                else if (req.Method == "POST")
                {
                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    _logger.LogInformation($"Request body: {requestBody}");
                    if (!string.IsNullOrEmpty(requestBody))
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };
                        var requestData = JsonSerializer.Deserialize<ListRssFeedItemsRequest>(requestBody, options);
                        feedUrl = requestData?.FeedUrl;
                        daysSince = requestData?.DaysSince ?? 1;
                        useTestData = requestData?.UseTestData ?? false;
                    }
                }

                if (string.IsNullOrEmpty(feedUrl))
                {
                    _logger.LogWarning("feedUrl parameter is missing");
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "feedUrl parameter is required" });
                    return badRequest;
                }

                _logger.LogInformation($"Fetching RSS feed from: {feedUrl}, daysSince: {daysSince}, useTestData: {useTestData}");

                List<RssFeedItem> items;

                // 테스트 데이터 모드: 파이프라인 검증용
                if (useTestData)
                {
                    _logger.LogInformation("Using test data mode");
                    items = new List<RssFeedItem>
                    {
                        new RssFeedItem
                        {
                            Title = "[테스트] Azure Security 주요 업데이트 - 제로 트러스트 아키텍처",
                            PrimaryLink = "https://www.microsoft.com/security/blog/test-1",
                            Summary = "Microsoft는 제로 트러스트 보안 모델의 최신 업데이트를 발표했습니다. 이번 업데이트는 ID 기반 접근 제어, 최소 권한 원칙, 그리고 지속적인 검증을 강화합니다.",
                            PublishDate = DateTime.UtcNow.AddHours(-2).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            Categories = new List<string> { "Security", "Zero Trust", "Azure" }
                        },
                        new RssFeedItem
                        {
                            Title = "[테스트] Microsoft Defender 위협 인텔리전스 리포트 2024",
                            PrimaryLink = "https://www.microsoft.com/security/blog/test-2",
                            Summary = "2024년 상반기 사이버 위협 동향 분석 리포트가 공개되었습니다. 랜섬웨어 공격이 35% 증가했으며, AI를 활용한 피싱 공격이 새로운 트렌드로 부상했습니다.",
                            PublishDate = DateTime.UtcNow.AddHours(-5).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            Categories = new List<string> { "Threat Intelligence", "Defender", "Report" }
                        },
                        new RssFeedItem
                        {
                            Title = "[테스트] Azure Sentinel SOAR 기능 강화 발표",
                            PrimaryLink = "https://www.microsoft.com/security/blog/test-3",
                            Summary = "Azure Sentinel의 보안 오케스트레이션 자동화 응답(SOAR) 기능이 대폭 개선되었습니다. 새로운 플레이북 템플릿과 커넥터가 추가되어 위협 대응 자동화가 더욱 강력해졌습니다.",
                            PublishDate = DateTime.UtcNow.AddHours(-8).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            Categories = new List<string> { "Sentinel", "SOAR", "Automation" }
                        }
                    };
                }
                else
                {
                    // RSS 피드 다운로드 및 파싱
                    items = await FetchRssFeedAsync(feedUrl, daysSince);
                }

                _logger.LogInformation($"Successfully parsed {items.Count} items from RSS feed");
                _logger.LogInformation($"Items summary: {string.Join(", ", items.Select(i => $"'{i.Title.Substring(0, Math.Min(30, i.Title.Length))}...'"))}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(items);
                return response;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"HTTP request failed: {ex.Message}");
                // Logic App expects an array, so return empty array instead of error object
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new List<RssFeedItem>());
                return response;
            }
            catch (XmlException ex)
            {
                _logger.LogError(ex, $"XML parsing failed: {ex.Message}");
                // Logic App expects an array, so return empty array instead of error object
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new List<RssFeedItem>());
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error: {ex.Message}");
                // Logic App expects an array, so return empty array instead of error object
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new List<RssFeedItem>());
                return response;
            }
        }

        private async Task<List<RssFeedItem>> FetchRssFeedAsync(string feedUrl, int daysSince)
        {
            var items = new List<RssFeedItem>();
            var cutoffDate = DateTime.UtcNow.AddDays(-daysSince);

            _logger.LogInformation($"Cutoff date for filtering: {cutoffDate:yyyy-MM-dd HH:mm:ss} UTC (daysSince={daysSince})");

            try
            {
                _logger.LogInformation($"Downloading RSS feed from: {feedUrl}");
                
                // RSS 피드 다운로드 (재시도 로직 포함)
                var response = await RetryAsync(async () =>
                {
                    var httpResponse = await _httpClient.GetAsync(feedUrl);
                    _logger.LogInformation($"HTTP Response: {httpResponse.StatusCode}");
                    httpResponse.EnsureSuccessStatusCode();
                    return httpResponse;
                }, maxRetries: 3);

                _logger.LogInformation("HTTP request succeeded, parsing RSS feed...");

                // RSS 피드 파싱
                using var stream = await response.Content.ReadAsStreamAsync();
                using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    IgnoreWhitespace = true,
                    IgnoreComments = true,
                    Async = true
                });

                var feed = SyndicationFeed.Load(xmlReader);
                _logger.LogInformation($"Feed title: {feed.Title?.Text ?? "Unknown"}");
                _logger.LogInformation($"Total items in feed: {feed.Items.Count()}");

                foreach (var item in feed.Items)
                {
                    // Many feeds (especially Atom) may not populate PublishDate.
                    // Fall back to LastUpdatedTime to avoid filtering out all items.
                    var publishDate = item.PublishDate.UtcDateTime;
                    if (publishDate == DateTime.MinValue || publishDate.Year < 2000)
                    {
                        publishDate = item.LastUpdatedTime.UtcDateTime;
                    }

                    // 최근 N일 이내의 게시물만 필터링
                    if (publishDate >= cutoffDate)
                    {
                        var link = item.Links.FirstOrDefault()?.Uri?.ToString() ?? string.Empty;
                        var summary = item.Summary?.Text ?? string.Empty;
                        
                        // HTML 태그 제거 (간단한 정리)
                        summary = System.Text.RegularExpressions.Regex.Replace(summary, "<.*?>", string.Empty);
                        summary = System.Net.WebUtility.HtmlDecode(summary);

                        items.Add(new RssFeedItem
                        {
                            Title = item.Title?.Text ?? "No title",
                            PrimaryLink = link,
                            Summary = summary.Trim(),
                            PublishDate = publishDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            Categories = item.Categories?.Select(c => c.Name).ToList() ?? new List<string>()
                        });

                        _logger.LogInformation($"Added item: {item.Title?.Text} (Published: {publishDate:yyyy-MM-dd HH:mm:ss})");
                    }
                    else
                    {
                        _logger.LogDebug($"Skipped old item: {item.Title?.Text} (Published: {publishDate:yyyy-MM-dd HH:mm:ss})");
                    }
                }

                _logger.LogInformation($"Filtered {items.Count} items published after {cutoffDate:yyyy-MM-dd HH:mm:ss}");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogError($"HTTP 403 Forbidden error for {feedUrl}. The RSS feed may be blocking requests.");
                throw new HttpRequestException($"Access denied (HTTP 403) for RSS feed: {feedUrl}. The server may be blocking automated requests.", ex, HttpStatusCode.Forbidden);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"Failed to download RSS feed from {feedUrl}: {ex.Message}");
                throw;
            }
            catch (XmlException ex)
            {
                _logger.LogError(ex, $"Failed to parse XML from {feedUrl}: {ex.Message}");
                throw;
            }

            return items;
        }

        private async Task<T> RetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                    _logger.LogWarning($"Attempt {attempt} failed: {ex.Message}. Retrying in {delay.TotalSeconds} seconds...");
                    await Task.Delay(delay);
                }
            }

            // 마지막 시도
            return await operation();
        }
    }

    public class ListRssFeedItemsRequest
    {
        public string? FeedUrl { get; set; }
        public int DaysSince { get; set; } = 1;
        public bool UseTestData { get; set; } = false;
    }

    public class RssFeedItem
    {
        public string Title { get; set; } = string.Empty;
        public string PrimaryLink { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string PublishDate { get; set; } = string.Empty;
        public List<string> Categories { get; set; } = new List<string>();
    }
}
