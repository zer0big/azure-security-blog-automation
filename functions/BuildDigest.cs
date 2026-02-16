using System.Diagnostics;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ProcessedPostsApi;

public sealed class BuildDigest
{
    private readonly ILogger<BuildDigest> _logger;

    private const int BulletCount = 3;

    private static readonly HttpClient RssHttp = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5
    })
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private static readonly HttpClient AoaiHttp = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    })
    {
        Timeout = TimeSpan.FromSeconds(18)
    };

    private static readonly TokenCredential Credential = new DefaultAzureCredential();

    public BuildDigest(ILogger<BuildDigest> logger)
    {
        _logger = logger;

        // Set a browser-ish UA once. Some feeds block default clients.
        if (!RssHttp.DefaultRequestHeaders.Contains("User-Agent"))
        {
            RssHttp.DefaultRequestHeaders.Clear();
            RssHttp.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            RssHttp.DefaultRequestHeaders.Add("Accept", "application/rss+xml, application/xml, text/xml, */*");
            RssHttp.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,ko;q=0.8");
            // We can now decompress Brotli too.
            RssHttp.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            RssHttp.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        }
    }

    [Function("BuildDigest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var swTotal = Stopwatch.StartNew();
        _logger.LogInformation("BuildDigest triggered");

        BuildDigestRequest? input;
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Request body is required" });
                return bad;
            }

            input = JsonSerializer.Deserialize<BuildDigestRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON body for BuildDigest");
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid JSON body" });
            return bad;
        }

        if (input?.RssFeedUrls == null || input.RssFeedUrls.Count == 0)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "rssFeedUrls is required" });
            return bad;
        }

        var daysSince = input.DaysSince is > 0 ? input.DaysSince : 30;
        var maxItems = input.MaxItems is > 0 ? Math.Min(input.MaxItems, 30) : 12;
        var newWindowHours = input.NewWindowHours is > 0 ? Math.Min(input.NewWindowHours, 168) : 24;
        var scheduleText = string.IsNullOrWhiteSpace(input.ScheduleText)
            ? "매일 07:00, 15:00, 22:00 (KST)에 새로운 게시글을 확인합니다."
            : input.ScheduleText!.Trim();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-daysSince);
        var newCutoff = DateTimeOffset.UtcNow.AddHours(-newWindowHours);

        // Best-effort storage for dedup. If this fails, we still produce an email.
        TableClient? table = null;
        var storageConn = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        if (!string.IsNullOrWhiteSpace(storageConn))
        {
            try
            {
                var tsc = new TableServiceClient(storageConn);
                table = tsc.GetTableClient("ProcessedPosts");
                await table.CreateIfNotExistsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Table init failed; continuing without dedup persistence");
                table = null;
            }
        }

        var feedStatuses = new List<FeedStatus>();
        var allItems = new List<Post>();

        // Fetch feeds sequentially with per-feed time budget. This avoids Logic App hangs.
        foreach (var feed in input.RssFeedUrls)
        {
            if (string.IsNullOrWhiteSpace(feed?.Url))
                continue;

            var sw = Stopwatch.StartNew();
            try
            {
                var items = await FetchFeedAsync(feed.Url!, cutoff, cancellationToken);
                foreach (var it in items)
                {
                    allItems.Add(new Post
                    {
                        Title = it.Title,
                        Link = it.Link,
                        Summary = it.Summary,
                        PublishDate = it.PublishDate,
                        FirstParagraph = it.FirstParagraph,
                        SourceName = string.IsNullOrWhiteSpace(feed.SourceName) ? "Unknown" : feed.SourceName!,
                        Emoji = feed.Emoji
                    });
                }

                var items24h = items.Count(p => p.PublishDateParsed >= newCutoff);

                feedStatuses.Add(new FeedStatus
                {
                    SourceName = feed.SourceName ?? "Unknown",
                    Url = feed.Url!,
                    Ok = true,
                    Items = items.Count,
                    Items24h = items24h,
                    ElapsedMs = sw.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Feed fetch failed: {Url}", feed.Url);
                feedStatuses.Add(new FeedStatus
                {
                    SourceName = feed.SourceName ?? "Unknown",
                    Url = feed.Url!,
                    Ok = false,
                    Items = 0,
                    Items24h = 0,
                    Error = ex.Message,
                    ElapsedMs = sw.ElapsedMilliseconds
                });
                // Skip FAIL feeds - don't add their items to allItems
                continue;
            }
        }

        // Dedup within this run by Link
        allItems = allItems
            .Where(p => !string.IsNullOrWhiteSpace(p.Link))
            .GroupBy(p => p.Link!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.PublishDateParsed).First())
            .OrderByDescending(p => p.PublishDateParsed)
            .ToList();

        // New posts are defined as items published within the last N hours (default 24).
        // This prevents old posts from being falsely treated as "new" when Table read/write is flaky.
        var newPosts = new List<Post>();
        var upserts = new List<TableEntity>();

        foreach (var p in allItems.Where(p => p.PublishDateParsed >= newCutoff))
        {
            if (newPosts.Count >= maxItems)
                break;

            var isDup = false;
            if (table != null)
            {
                try
                {
                    var pk = MakePartitionKey(p.SourceName ?? "Unknown");
                    var rk = MakeRowKey(p.Link ?? string.Empty);
                    _ = await table.GetEntityAsync<TableEntity>(pk, rk, cancellationToken: cancellationToken);
                    isDup = true;
                }
                catch (RequestFailedException rfe) when (rfe.Status == 404)
                {
                    isDup = false;
                }
                catch (Exception ex)
                {
                    // If storage is flaky, we still want a digest rather than failing the run.
                    _logger.LogWarning(ex, "Dedup check failed; treating as new: {Link}", p.Link);
                    isDup = false;
                    table = null; // stop hammering storage
                }
            }

            if (isDup)
                continue;

            newPosts.Add(p);

            // Prepare upsert entity (best-effort)
            if (table != null)
            {
                var pk = MakePartitionKey(p.SourceName ?? "Unknown");
                var rk = MakeRowKey(p.Link ?? string.Empty);

                var en = ExtractBullets(p.Summary);
                p.EnglishSummary = en;

                upserts.Add(new TableEntity(pk, rk)
                {
                    { "Title", p.Title },
                    { "Link", p.Link },
                    { "SourceName", p.SourceName },
                    { "Summary", p.Summary },
                    { "PublishDate", p.PublishDate },
                    { "EnglishSummary", JsonSerializer.Serialize(en) },
                    { "KoreanSummary", JsonSerializer.Serialize(p.KoreanSummary ?? Array.Empty<string>()) },
                    { "ProcessedDate", DateTime.UtcNow }
                });
            }
        }

        // Only show posts from the last 24 hours (newWindowHours)
        var displayPosts = newPosts.Count > 0
            ? newPosts
            : allItems.Where(p => p.PublishDateParsed >= newCutoff).Take(Math.Min(10, allItems.Count)).ToList();

        // Create bullet summaries (always) + try a single batch translation to Korean (optional).
        foreach (var p in displayPosts)
        {
            p.EnglishSummary = NormalizeBullets(p.EnglishSummary ?? ExtractBullets(p.Summary), BulletCount, new[]
            {
                "See the original post for details.",
                "Open the article link for more context.",
                "More information is available in the full post."
            });

            // Leave Korean empty unless translation succeeds.
            p.KoreanSummary ??= Array.Empty<string>();
        }

        var translation = await TryTranslateKoreanBatchAsync(displayPosts, cancellationToken);

        // Best-effort upsert: include translated Korean if we have it.
        if (table != null && upserts.Count > 0)
        {
            try
            {
                foreach (var p in newPosts)
                {
                    var pk = MakePartitionKey(p.SourceName ?? "Unknown");
                    var rk = MakeRowKey(p.Link ?? string.Empty);
                    var entity = new TableEntity(pk, rk)
                    {
                        { "Title", p.Title },
                        { "Link", p.Link },
                        { "SourceName", p.SourceName },
                        { "Summary", p.Summary },
                        { "PublishDate", p.PublishDate },
                        { "EnglishSummary", JsonSerializer.Serialize(p.EnglishSummary ?? Array.Empty<string>()) },
                        { "KoreanSummary", JsonSerializer.Serialize(p.KoreanSummary ?? Array.Empty<string>()) },
                        { "ProcessedDate", DateTime.UtcNow }
                    };
                    await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Table upsert failed; continuing");
            }
        }

        // hasNew는 displayPosts에 newCutoff 이내 항목이 있는지로 판단
        var recentPostsCount = displayPosts.Count(p => p.PublishDateParsed >= newCutoff);
        var hasNew = recentPostsCount > 0;
        var subject = hasNew
            ? $"[Microsoft Azure 업데이트] 새 게시글 {recentPostsCount}개"
            : "[Microsoft Azure 업데이트] 최근 게시글 요약 (신규 없음)";

        var html = RenderHtml(displayPosts, hasNew, recentPostsCount, scheduleText, feedStatuses, daysSince, newWindowHours);

        swTotal.Stop();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
        var jsonOpts = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };
        var responseBody = JsonSerializer.Serialize(new
        {
            subject,
            html,
            stats = new
            {
                daysSince,
                newWindowHours,
                cutoff = cutoff.ToString("o"),
                translation,
                feeds = feedStatuses,
                allItems = allItems.Count,
                newPosts = newPosts.Count,
                displayPosts = displayPosts.Count,
                elapsedMs = swTotal.ElapsedMilliseconds
            }
        }, jsonOpts);
        await ok.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(responseBody));
        return ok;
    }

    private static async Task<List<Post>> FetchFeedAsync(string feedUrl, DateTimeOffset cutoff, CancellationToken ct)
    {
        // Retry with exponential backoff
        HttpResponseMessage resp = await RetryAsync(async () =>
        {
            var r = await RssHttp.GetAsync(feedUrl, ct);
            r.EnsureSuccessStatusCode();
            return r;
        }, maxRetries: 2, ct);

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            Async = true
        });

        var feed = SyndicationFeed.Load(xmlReader);
        var results = new List<Post>();

        foreach (var item in feed.Items)
        {
            var published = item.PublishDate.UtcDateTime;
            if (published == DateTime.MinValue || published.Year < 2000)
            {
                published = item.LastUpdatedTime.UtcDateTime;
            }

            var publishedOffset = published == DateTime.MinValue
                ? DateTimeOffset.MinValue
                : new DateTimeOffset(DateTime.SpecifyKind(published, DateTimeKind.Utc));

            if (publishedOffset < cutoff)
                continue;

            var link = item.Links.FirstOrDefault()?.Uri?.ToString() ?? string.Empty;
            var summary = item.Summary?.Text ?? string.Empty;
            summary = Regex.Replace(summary, "<.*?>", string.Empty);
            summary = WebUtility.HtmlDecode(summary).Trim();

            // Scrape first paragraph from actual blog page
            var firstPara = await ScrapeFirstParagraphAsync(link, ct);
            // Fallback to RSS summary if scraping fails
            if (string.IsNullOrWhiteSpace(firstPara))
            {
                firstPara = ExtractFirstParagraph(summary);
                if (string.IsNullOrWhiteSpace(firstPara) && !string.IsNullOrWhiteSpace(summary))
                {
                    firstPara = summary.Length > 600 ? summary.Substring(0, 600) + "..." : summary;
                }
            }

            results.Add(new Post
            {
                Title = item.Title?.Text ?? "No title",
                Link = link,
                Summary = summary,
                PublishDate = publishedOffset == DateTimeOffset.MinValue
                    ? DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    : publishedOffset.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                FirstParagraph = firstPara
            });
        }

        return results;
    }

    private static async Task<T> RetryAsync<T>(Func<Task<T>> op, int maxRetries, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await op();
            }
            catch when (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay, ct);
            }
        }

        return await op();
    }

    private static string MakePartitionKey(string sourceName) => (sourceName ?? "Unknown").Replace(" ", string.Empty);

    private static string MakeRowKey(string link)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(link ?? string.Empty));
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-");
    }

    private static string[] ExtractBullets(string? summary)
    {
        var text = (summary ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new[]
            {
                "Summary is not available in the RSS feed.",
                "Open the article link for full details.",
                "This digest was generated without full-page scraping."
            };
        }

        // Split into sentences, keep first 3 meaningful ones.
        var parts = Regex.Split(text, @"(?<=[.!?])\s+")
            .Select(s => s.Trim())
            .Where(s => s.Length >= 20)
            .Take(3)
            .ToList();

        if (parts.Count == 0)
        {
            var one = text.Length > 140 ? text.Substring(0, 140) + "..." : text;
            return NormalizeBullets(new[] { one }, BulletCount, new[]
            {
                "See the original post for details.",
                "Open the article link for more context.",
                "More information is available in the full post."
            });
        }

        var trimmed = parts.Select(s => s.Length > 160 ? s.Substring(0, 160) + "..." : s).ToArray();
        return NormalizeBullets(trimmed, BulletCount, new[]
        {
            "See the original post for details.",
            "Open the article link for more context.",
            "More information is available in the full post."
        });
    }

    private static string[] NormalizeBullets(string[] bullets, int targetCount, string[] fillers)
    {
        var cleaned = (bullets ?? Array.Empty<string>())
            .Select(SanitizeBullet)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (cleaned.Count > targetCount)
            cleaned = cleaned.Take(targetCount).ToList();

        var fillerIndex = 0;
        while (cleaned.Count < targetCount)
        {
            var filler = fillers.Length == 0
                ? "See the original post for details."
                : fillers[Math.Min(fillerIndex, fillers.Length - 1)];
            cleaned.Add(filler);
            fillerIndex++;
        }

        return cleaned.ToArray();
    }

    private static string SanitizeBullet(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var t = s.Trim();
        // Remove legacy marker used in older emails.
        if (t.StartsWith("(번역 미구성)", StringComparison.OrdinalIgnoreCase))
        {
            t = t.Substring("(번역 미구성)".Length).Trim();
        }
        return t;
    }

    private async Task<TranslationDiagnostics> TryTranslateKoreanBatchAsync(List<Post> posts, CancellationToken ct)
    {
        // If AOAI isn't configured, do nothing.
        var endpointRaw = GetEnvFirst("AOAI_ENDPOINT", "AZURE_OPENAI_ENDPOINT")?.Trim();
        var deployment = GetEnvFirst("AOAI_DEPLOYMENT", "AZURE_OPENAI_DEPLOYMENT")?.Trim();
        var apiVersion = GetEnvFirst("AOAI_API_VERSION", "AZURE_OPENAI_API_VERSION")?.Trim();
        if (string.IsNullOrWhiteSpace(apiVersion)) apiVersion = "2024-12-01-preview";

        if (string.IsNullOrWhiteSpace(endpointRaw) || string.IsNullOrWhiteSpace(deployment))
        {
            _logger.LogInformation("AOAI not configured; skipping Korean translation");
            return new TranslationDiagnostics
            {
                Configured = false,
                Attempted = false,
                Succeeded = false,
                TranslatedPosts = 0,
                EndpointHost = string.IsNullOrWhiteSpace(endpointRaw) ? null : SafeHost(endpointRaw),
                Deployment = deployment,
                ApiVersion = apiVersion,
                Error = "AOAI_ENDPOINT/AOAI_DEPLOYMENT not set"
            };
        }

        var endpoint = NormalizeAoaiEndpoint(endpointRaw);

        var diag = new TranslationDiagnostics
        {
            Configured = true,
            Attempted = true,
            Succeeded = false,
            TranslatedPosts = 0,
            EndpointHost = SafeHost(endpoint),
            Deployment = deployment,
            ApiVersion = apiVersion
        };

        // Keep request small: translate up to 10 posts.
        var slice = posts.Take(10).ToList();

        var inputPayload = slice.Select((p, i) => new
        {
            index = i,
            title = p.Title,
            englishSummary = NormalizeBullets(p.EnglishSummary ?? Array.Empty<string>(), BulletCount, new[]
            {
                "See the original post for details.",
                "Open the article link for more context.",
                "More information is available in the full post."
            })
        }).ToArray();

        var systemPrompt = "You are an expert technical analyst who extracts key insights from blog posts.\n" +
                   "Your task: Read the English summary of each blog post and extract 3 key insights in Korean.\n" +
                   "Rules:\n" +
                   "- Provide exactly 3 Korean bullet points per item that capture the CORE INSIGHTS of the entire blog post.\n" +
                   "- Focus on actionable takeaways, key concepts, or important implications.\n" +
                   "- Do NOT simply translate the English text. Instead, ANALYZE and SYNTHESIZE the key insights.\n" +
                   "- Write in natural, professional Korean.\n" +
                   "- Output valid JSON only, with the exact schema: { items: [ { index: 0, koreanSummary: [ ... ] } ] }";

        var userPrompt = $"Extract key insights from this JSON payload:\n{JsonSerializer.Serialize(new { items = inputPayload })}";

        var requestPayload = new
        {
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 900,
            temperature = 0.2,
            response_format = new { type = "json_object" }
        };

        var requestUri = $"{endpoint}openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, requestUri);

        var apiKey = GetEnvFirst("AOAI_API_KEY", "AZURE_OPENAI_API_KEY")?.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpReq.Headers.Add("api-key", apiKey);
        }
        else
        {
            var token = await Credential.GetTokenAsync(new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }), ct);
            httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        }

        httpReq.Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(18));

        try
        {
            var resp = await AoaiHttp.SendAsync(httpReq, cts.Token);
            var raw = await resp.Content.ReadAsStringAsync(cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("AOAI translate failed: {Status} {Body}", resp.StatusCode, TruncateForLogging(raw));
                diag.Error = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                return diag;
            }

            // Parse AOAI response -> choices[0].message.content
            using var top = JsonDocument.Parse(raw);
            var content = top.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            _logger.LogInformation("AOAI raw content length: {Length}, preview: {Preview}", 
                content?.Length ?? 0, TruncateForLogging(content));

            if (string.IsNullOrWhiteSpace(content))
            {
                diag.Error = "Empty content in AOAI response";
                return diag;
            }

            // Clean up content - remove markdown code blocks if present
            var cleanContent = content.Trim();
            if (cleanContent.StartsWith("```json"))
            {
                cleanContent = cleanContent.Substring(7);
            }
            if (cleanContent.StartsWith("```"))
            {
                cleanContent = cleanContent.Substring(3);
            }
            if (cleanContent.EndsWith("```"))
            {
                cleanContent = cleanContent.Substring(0, cleanContent.Length - 3);
            }
            cleanContent = cleanContent.Trim();

            _logger.LogInformation("AOAI cleaned content: {Content}", TruncateForLogging(cleanContent));

            using var doc = JsonDocument.Parse(cleanContent);
            if (!doc.RootElement.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
            {
                diag.Error = "Missing 'items' array in AOAI JSON content";
                return diag;
            }

            foreach (var itemEl in itemsEl.EnumerateArray())
            {
                if (!itemEl.TryGetProperty("index", out var idxEl) || idxEl.ValueKind != JsonValueKind.Number)
                    continue;
                var idx = idxEl.GetInt32();
                if (idx < 0 || idx >= slice.Count) continue;

                if (!itemEl.TryGetProperty("koreanSummary", out var ksEl) || ksEl.ValueKind != JsonValueKind.Array)
                    continue;

                var ks = ksEl.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToArray();
                if (ks.Length == 0) continue;

                slice[idx].KoreanSummary = NormalizeBullets(ks, BulletCount, new[]
                {
                    "자세한 내용은 원문을 참고하세요.",
                    "추가 정보는 원문에 있습니다.",
                    "원문 링크에서 전체 내용을 확인하세요."
                });
            }

            diag.TranslatedPosts = slice.Count(p => p.KoreanSummary != null && p.KoreanSummary.Any(ContainsHangul));
            diag.Succeeded = diag.TranslatedPosts > 0;
            if (!diag.Succeeded)
            {
                diag.Error = "AOAI call succeeded but produced no Hangul output";
            }

            return diag;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AOAI translate failed - Type: {Type}, Message: {Message}, StackTrace: {StackTrace}", 
                ex.GetType().Name, ex.Message, ex.StackTrace);
            diag.Error = ex.GetType().Name + ": " + ex.Message;
            return diag;
        }
    }

    private static string? SafeHost(string endpoint)
    {
        try
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return null;
            return uri.Host;
        }
        catch
        {
            return null;
        }
    }

    private sealed class TranslationDiagnostics
    {
        public bool Configured { get; set; }
        public bool Attempted { get; set; }
        public bool Succeeded { get; set; }
        public int TranslatedPosts { get; set; }
        public string? EndpointHost { get; set; }
        public string? Deployment { get; set; }
        public string? ApiVersion { get; set; }
        public string? Error { get; set; }
    }

    private static string NormalizeAoaiEndpoint(string raw)
    {
        var e = raw.Trim();
        if (!e.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            e = "https://" + e;
        }
        if (!e.EndsWith("/", StringComparison.Ordinal))
        {
            e += "/";
        }
        return e;
    }

    private static string? GetEnvFirst(params string[] keys)
    {
        foreach (var k in keys)
        {
            var v = Environment.GetEnvironmentVariable(k);
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return null;
    }

    private static async Task<string> ScrapeFirstParagraphAsync(string url, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            
            var response = await RssHttp.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return string.Empty;
            
            var html = await response.Content.ReadAsStringAsync(ct);
            
            // Microsoft Security Blog: Look for wp-block-paragraph first (actual blog content)
            // Fallback to entry-content or article
            var patterns = new[]
            {
                @"<p[^>]*class=[""']wp-block-paragraph[""'][^>]*>(.*?)</p>",
                @"<div[^>]*class=[""']entry-content[""'][^>]*>(.*?)</div>",
                @"<article[^>]*>(.*?)</article>",
                @"<div[^>]*class=[""']post-content[""'][^>]*>(.*?)</div>"
            };
            
            string paragraph = string.Empty;
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    paragraph = match.Groups[1].Value;
                    break;
                }
            }
            
            if (string.IsNullOrWhiteSpace(paragraph)) return string.Empty;
            
            // Remove all HTML tags
            paragraph = Regex.Replace(paragraph, "<.*?>", string.Empty);
            paragraph = WebUtility.HtmlDecode(paragraph).Trim();
            
            // Return first paragraph, max 800 chars
            return paragraph.Length > 800 ? paragraph.Substring(0, 800) + "..." : paragraph;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractFirstParagraph(string? summary)
    {
        var text = (summary ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        
        // Find first sentence-ending pattern (. followed by space or newline)
        var match = Regex.Match(text, @"^(.+?\.)(?:\s|$)");
        if (match.Success)
        {
            var para = match.Groups[1].Value.Trim();
            // If too short, try to get more sentences
            if (para.Length < 100 && text.Length > para.Length)
            {
                var secondMatch = Regex.Match(text, @"^(.+?\.\s+.+?\.)(?:\s|$)");
                if (secondMatch.Success) para = secondMatch.Groups[1].Value.Trim();
            }
            return para.Length > 600 ? para.Substring(0, 600) + "..." : para;
        }
        
        // Fallback: take first 500 chars
        return text.Length > 500 ? text.Substring(0, 500) + "..." : text;
    }

    private static string BuildExcerpt(string? summary)
    {
        var text = (summary ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = Regex.Replace(text, "<.*?>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length <= 360) return text;
        return text.Substring(0, 360) + "...";
    }

    private static string TruncateForLogging(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= 400 ? s : s.Substring(0, 400) + "...";
    }

    private static string RenderHtml(
        List<Post> posts,
        bool hasNewPosts,
        int newPostsCount,
        string scheduleText,
        List<FeedStatus> feedStatuses,
        int daysSince,
        int newWindowHours)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\">");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Tahoma, sans-serif; line-height: 1.6; color: #222; max-width: 860px; margin: 0 auto; padding: 18px; background: #f4f6f8; }");
        sb.AppendLine(".container { background: #fff; border-radius: 10px; box-shadow: 0 2px 6px rgba(0,0,0,0.08); overflow: hidden; }");
        sb.AppendLine(".header { background: linear-gradient(135deg,#e3f2fd,#bbdefb); padding: 22px 26px; border-bottom: 3px solid #0078d4; }");
        sb.AppendLine(".header h1 { margin: 0; color: #0078d4; font-size: 26px; }");
        sb.AppendLine(".header .meta { margin-top: 6px; color: #005a9e; font-weight: 600; }");
        sb.AppendLine(".content { padding: 18px 26px; }");
        sb.AppendLine(".notice { background: #fff3cd; border-left: 4px solid #ffc107; padding: 12px 14px; border-radius: 6px; margin: 14px 0; color: #6b5500; }");
        sb.AppendLine(".post { background: #f8f9fa; border-left: 4px solid #0078d4; padding: 16px; border-radius: 6px; margin: 14px 0; }");
        sb.AppendLine(".post-title { font-size: 18px; font-weight: 700; margin: 0 0 6px 0; color: #0b3d91; }");
        sb.AppendLine(".tag { display:inline-block; background:#0078d4; color:#fff; padding: 2px 10px; border-radius: 999px; font-size: 12px; margin-right: 8px; }");
        sb.AppendLine(".date { color:#666; font-size: 13px; }");
        sb.AppendLine(".bullets { margin: 10px 0 0 0; padding-left: 18px; }");
        sb.AppendLine(".bullets li { margin: 4px 0; }");
        sb.AppendLine(".link { display:inline-block; margin-top: 10px; background:#0078d4; color:#fff !important; padding: 8px 14px; text-decoration:none; border-radius:6px; }");
        sb.AppendLine(".link:hover { background:#005a9e; }");
        sb.AppendLine(".footer { padding: 14px 26px; border-top: 1px solid #e6e6e6; color:#666; font-size: 12px; }");
        sb.AppendLine("table { width:100%; border-collapse: collapse; margin-top: 10px; }");
        sb.AppendLine("th, td { border: 1px solid #e6e6e6; padding: 8px; font-size: 12px; text-align:left; }");
        sb.AppendLine("th { background:#fafafa; }");
        sb.AppendLine(".ok { color:#1b5e20; font-weight:700; }");
        sb.AppendLine(".fail { color:#b71c1c; font-weight:700; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"container\">");
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine("<h1>☁️ Microsoft Azure 업데이트</h1>");
        sb.AppendLine($"<div class=\"meta\">{(hasNewPosts ? $"새 게시글 {newPostsCount}개 (최근 {newWindowHours}시간)" : $"신규 없음 (최근 {newWindowHours}시간)")}</div>");
        sb.AppendLine($"<div style=\"margin-top:6px;color:#2b4a6b;font-size:12px;\">{WebUtility.HtmlEncode(scheduleText)}</div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"content\">");

        // Per-feed 24h status summary (top of email)
        sb.AppendLine("<div style=\"margin-top:10px;\">");
        sb.AppendLine($"<div style=\"font-weight:700;color:#444;\">피드별 최근 {newWindowHours}시간 등록 현황</div>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><th>Source</th><th>최근 {newWindowHours}시간</th><th>상태</th></tr>");
        foreach (var fs in feedStatuses.OrderBy(f => f.SourceName, StringComparer.OrdinalIgnoreCase))
        {
            var label = fs.Items24h > 0 ? $"{fs.Items24h}개" : "없음";
            var status = fs.Ok ? "OK" : "FAIL";
            var cls = fs.Ok ? "ok" : "fail";
            sb.AppendLine($"<tr><td>{WebUtility.HtmlEncode(fs.SourceName)}</td><td>{WebUtility.HtmlEncode(label)}</td><td class=\"{cls}\">{status}</td></tr>");
        }
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");

        if (!hasNewPosts)
        {
            sb.AppendLine($"<div class=\"notice\">최근 {newWindowHours}시간 내 신규 게시글이 없어 최근 글을 요약해서 보냅니다. (라이브 RSS 기준)</div>");
        }

        if (posts.Count == 0)
        {
            sb.AppendLine("<div class=\"post\"><div class=\"post-title\">게시글을 가져오지 못했습니다.</div><div>RSS 연결/권한/피드 상태를 확인해 주세요.</div></div>");
        }

        foreach (var p in posts)
        {
            var emoji = string.IsNullOrWhiteSpace(p.Emoji) ? "📰" : p.Emoji;
            var src = WebUtility.HtmlEncode(p.SourceName ?? "Unknown");
            var title = WebUtility.HtmlEncode(p.Title ?? "(no title)");
            var link = p.Link ?? string.Empty;
            var excerpt = BuildExcerpt(p.Summary);

            sb.AppendLine("<div class=\"post\">");
            sb.AppendLine($"<div class=\"post-title\"><span class=\"tag\">{emoji} {src}</span>{title}</div>");

            if (DateTimeOffset.TryParse(p.PublishDate, out var dt))
            {
                sb.AppendLine($"<div class=\"date\">📅 {dt.ToLocalTime():yyyy-MM-dd HH:mm}</div>");
            }

            // 블로그 첫 문단 발췌
            if (!string.IsNullOrWhiteSpace(p.FirstParagraph))
            {
                sb.AppendLine("<div style=\"margin-top:12px;font-weight:700;color:#333;\">📝 블로그 첫 문단</div>");
                sb.AppendLine($"<div style=\"margin-top:6px;color:#444;font-style:italic;border-left:3px solid #0078d4;padding-left:12px;\">{WebUtility.HtmlEncode(p.FirstParagraph)}</div>");
            }

            // 한국어 핵심 인사이트만 표시 (AI가 전체 블로그에서 추출한 핵심)
            var koBulletsRaw = (p.KoreanSummary != null && p.KoreanSummary.Any(ContainsHangul))
                ? p.KoreanSummary!
                : Array.Empty<string>();

            var koBullets = NormalizeBullets(koBulletsRaw, BulletCount, new[]
            {
                "핵심 인사이트를 생성하려면 AOAI 설정이 필요합니다.",
                "현재는 영어 요약만 제공됩니다.",
                "원문 링크에서 전체 내용을 확인하세요."
            });

            sb.AppendLine("<div style=\"margin-top:14px;font-weight:700;color:#333;\">💡 핵심 인사이트 (AI Summary)</div>");
            sb.AppendLine("<ul class=\"bullets\">");
            foreach (var b in koBullets.Take(BulletCount))
            {
                sb.AppendLine($"<li>{WebUtility.HtmlEncode(SanitizeBullet(b))}</li>");
            }
            sb.AppendLine("</ul>");

            if (!string.IsNullOrWhiteSpace(link))
            {
                sb.AppendLine($"<a class=\"link\" href=\"{WebUtility.HtmlEncode(link)}\">전체 글 읽기 →</a>");
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("<div style=\"margin-top:18px;\">");
        sb.AppendLine("<div style=\"font-weight:700; color:#444;\">피드 상세 상태</div>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><th>Source</th><th>Status</th><th>최근 {newWindowHours}시간</th><th>최근 {daysSince}일</th><th>Elapsed</th><th>Error</th></tr>");
        foreach (var fs in feedStatuses)
        {
            var status = fs.Ok ? "OK" : "FAIL";
            var cls = fs.Ok ? "ok" : "fail";
            sb.AppendLine($"<tr><td>{WebUtility.HtmlEncode(fs.SourceName)}</td><td class=\"{cls}\">{status}</td><td>{fs.Items24h}</td><td>{fs.Items}</td><td>{fs.ElapsedMs} ms</td><td>{WebUtility.HtmlEncode(fs.Error ?? string.Empty)}</td></tr>");
        }
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"footer\">Generated by Azure Security Blog Automation</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static bool ContainsHangul(string s)
    {
        foreach (var ch in s)
        {
            if (ch >= '\uAC00' && ch <= '\uD7A3') return true;
        }
        return false;
    }

    private sealed class BuildDigestRequest
    {
        public List<FeedRef>? RssFeedUrls { get; set; }
        public int DaysSince { get; set; } = 30;
        public int MaxItems { get; set; } = 12;
        public int NewWindowHours { get; set; } = 24;
        public string? ScheduleText { get; set; }
    }

    private sealed class FeedRef
    {
        public string? SourceName { get; set; }
        public string? Url { get; set; }
        public string? Emoji { get; set; }
    }

    private sealed class FeedStatus
    {
        public string SourceName { get; set; } = "";
        public string Url { get; set; } = "";
        public bool Ok { get; set; }
        public int Items { get; set; }
        public int Items24h { get; set; }
        public long ElapsedMs { get; set; }
        public string? Error { get; set; }
    }

    private sealed class Post
    {
        public string? Title { get; set; }
        public string? Link { get; set; }
        public string? Summary { get; set; }
        public string? PublishDate { get; set; }
        public string? SourceName { get; set; }
        public string? Emoji { get; set; }
        public string? FirstParagraph { get; set; }

        public string[]? EnglishSummary { get; set; }
        public string[]? KoreanSummary { get; set; }

        public DateTimeOffset PublishDateParsed
        {
            get
            {
                if (DateTimeOffset.TryParse(PublishDate, out var dto)) return dto;
                return DateTimeOffset.MinValue;
            }
        }
    }
}
