using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Collections.Concurrent;

namespace ProcessedPostsApi.Functions;

public class GenerateEmailHtml
{
    private readonly ILogger<GenerateEmailHtml> _logger;

    private static readonly HttpClient Http = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    // Cache of original article cleaned text to avoid re-fetching and to use as fallback for insights
    private static readonly ConcurrentDictionary<string, string> _originalTextCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public GenerateEmailHtml(ILogger<GenerateEmailHtml> logger)
    {
        _logger = logger;
    }

    [Function("GenerateEmailHtml")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("GenerateEmailHtml API called");

        try
        {
            // Read and parse the request body flexibly: some callers send `englishSummary`/`koreanSummary`
            // as JSON-encoded strings ("[\"a\",\"b\"]") while others send real arrays. We accept both.
            string bodyStr;
            using (var sr = new StreamReader(req.Body))
            {
                bodyStr = await sr.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(bodyStr))
            {
                _logger.LogWarning("Invalid request body");
                return new BadRequestObjectResult(new { error = "Request body is required" });
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(bodyStr);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse request JSON");
                return new BadRequestObjectResult(new { error = "Invalid JSON" });
            }

            BlogPost[] newPosts = Array.Empty<BlogPost>();
            BlogPost[] recentPosts = Array.Empty<BlogPost>();

            BlogPost[] ParsePostsElement(JsonElement el)
            {
                if (el.ValueKind != JsonValueKind.Array) return Array.Empty<BlogPost>();
                var list = new List<BlogPost>();
                foreach (var item in el.EnumerateArray())
                {
                    var bp = new BlogPost();
                    if (item.TryGetProperty("title", out var t) || item.TryGetProperty("Title", out t)) bp.Title = t.GetString();
                    if (item.TryGetProperty("link", out var l) || item.TryGetProperty("Link", out l)) bp.Link = l.GetString();
                    if (item.TryGetProperty("publishDate", out var pd) || item.TryGetProperty("PublishDate", out pd)) bp.PublishDate = pd.GetString();
                    if (item.TryGetProperty("summary", out var s) || item.TryGetProperty("Summary", out s)) bp.Summary = s.GetString();
                    if (item.TryGetProperty("sourceName", out var sn) || item.TryGetProperty("SourceName", out sn)) bp.SourceName = sn.GetString();

                    // EnglishSummary: accept array or JSON-encoded string
                    string[]? ParseStringArray(JsonElement prop)
                    {
                        try
                        {
                            if (prop.ValueKind == JsonValueKind.Array)
                            {
                                var arr = new List<string>();
                                foreach (var el2 in prop.EnumerateArray()) if (el2.ValueKind == JsonValueKind.String) arr.Add(el2.GetString() ?? string.Empty);
                                return arr.ToArray();
                            }
                            if (prop.ValueKind == JsonValueKind.String)
                            {
                                var raw = prop.GetString() ?? string.Empty;
                                // try parse as JSON array
                                try
                                {
                                    using (var pd2 = JsonDocument.Parse(raw))
                                    {
                                        if (pd2.RootElement.ValueKind == JsonValueKind.Array)
                                        {
                                            var arr = new List<string>();
                                            foreach (var el2 in pd2.RootElement.EnumerateArray()) if (el2.ValueKind == JsonValueKind.String) arr.Add(el2.GetString() ?? string.Empty);
                                            return arr.ToArray();
                                        }
                                    }
                                }
                                catch { }
                                // fallback: return single-element array with raw string
                                return new[] { raw };
                            }
                        }
                        catch { }
                        return null;
                    }

                    if (item.TryGetProperty("englishSummary", out var es) || item.TryGetProperty("EnglishSummary", out es)) bp.EnglishSummary = ParseStringArray(es);
                    if (item.TryGetProperty("koreanSummary", out var ks) || item.TryGetProperty("KoreanSummary", out ks)) bp.KoreanSummary = ParseStringArray(ks);

                    list.Add(bp);
                }
                return list.ToArray();
            }

            var root = doc.RootElement;
            if (root.TryGetProperty("newPosts", out var np) || root.TryGetProperty("NewPosts", out np) || root.TryGetProperty("posts", out np) || root.TryGetProperty("Posts", out np))
            {
                newPosts = ParsePostsElement(np);
            }
            if (root.TryGetProperty("recentPosts", out var rp) || root.TryGetProperty("RecentPosts", out rp))
            {
                recentPosts = ParsePostsElement(rp);
            }

            var hasNewPosts = newPosts.Length > 0;
            var displayPosts = hasNewPosts
                ? newPosts
                : (recentPosts.Length > 0 ? recentPosts : Array.Empty<BlogPost>());

            // Count actual new posts (excluding "No new posts" messages)
            var actualNewPostsCount = displayPosts.Count(p => p.Title != "No new posts in last 24 hours");

            var html = await GenerateHtmlAsync(displayPosts, hasNewPosts, actualNewPostsCount, recentPosts.Length);

            var subject = actualNewPostsCount > 0
                ? $"[Microsoft Azure 업데이트] 새 게시글 {actualNewPostsCount}개"
                : "[Microsoft Azure 업데이트] 최근 게시글 요약 (신규 없음)";
            
            return new OkObjectResult(new { html, subject });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GenerateEmailHtml API");
            return new ObjectResult(new { error = ex.Message })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private async Task<string> GenerateHtmlAsync(BlogPost[] posts, bool hasNewPosts, int newPostsCount, int recentPostsCount)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; max-width: 800px; margin: 0 auto; padding: 20px; background: #f4f4f4; }");
        sb.AppendLine(".container { background: #fff; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
        sb.AppendLine(".header { background: linear-gradient(135deg, #e3f2fd 0%, #bbdefb 100%); color: #0078d4 !important; padding: 30px; border-radius: 8px 8px 0 0; text-align: center; border-bottom: 3px solid #0078d4; }");
        sb.AppendLine(".header h1 { margin: 0 0 10px 0; font-size: 28px; color: #0078d4 !important; font-weight: bold; }");
        sb.AppendLine(".header .count { font-size: 18px; color: #005a9e !important; font-weight: 600; }");
        sb.AppendLine(".no-new-notice { background: #fff3cd; color: #856404; padding: 15px; margin: 20px; border-left: 4px solid #ffc107; border-radius: 4px; font-size: 16px; }");
        sb.AppendLine(".content { padding: 20px; }");
        sb.AppendLine(".post { background: #f8f9fa; padding: 20px; margin: 20px 0; border-left: 4px solid #0078d4; border-radius: 4px; }");
        sb.AppendLine(".post-title { color: #0078d4; font-size: 20px; font-weight: bold; margin: 0 0 10px 0; }");
        sb.AppendLine(".post-meta { color: #666; font-size: 14px; margin: 10px 0; }");
        sb.AppendLine(".post-summary { margin: 15px 0; color: #444; line-height: 1.8; }");
        sb.AppendLine(".post-link { display: inline-block; background: #0078d4; color: #fff !important; padding: 10px 20px; text-decoration: none; border-radius: 4px; margin-top: 10px; }");
        sb.AppendLine(".post-link:hover { background: #005a9e; }");
        sb.AppendLine(".footer { text-align: center; padding: 20px; color: #666; font-size: 14px; border-top: 2px solid #e0e0e0; margin-top: 20px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"container\">");
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine("<h1>☁️ Microsoft Azure 업데이트</h1>");
        
        sb.AppendLine($"<div class=\"count\">새로운 게시글 {newPostsCount}개</div>");
        
        sb.AppendLine("</div>");
        
        sb.AppendLine("<div class=\"content\">");

        if (!hasNewPosts && posts.Length == 0)
        {
            sb.AppendLine("<div class=\"post\">");
            sb.AppendLine("<div class=\"post-title\">최근 게시글을 가져오지 못했습니다.</div>");
            sb.AppendLine("<div class=\"post-summary\">RSS 연결/권한/커넥터 상태를 확인해 주세요.</div>");
            sb.AppendLine("</div>");
        }

        foreach (var post in posts)
        {
            // Check if this is a "No new posts" message
            if (post.Title == "No new posts in last 24 hours")
            {
                // Get emoji based on source name
                var emoji = SourceEmojiHelper.GetSourceEmoji(post.SourceName ?? "");
                var sourceName = !string.IsNullOrEmpty(post.SourceName) 
                    ? System.Net.WebUtility.HtmlEncode(post.SourceName) 
                    : "Unknown Source";
                sb.AppendLine($"<div style=\"padding: 10px 20px; margin: 10px 0; color: #666; font-size: 14px; border-left: 3px solid #ddd;\">{emoji} {sourceName}: No new posts in last 24 hours</div>");
                continue;
            }
            
            sb.AppendLine("<div class=\"post\">");
            
            // Show source name if available
            var sourceTag = "";
            if (!string.IsNullOrEmpty(post.SourceName))
            {
                sourceTag = $"<span style=\"display: inline-block; background: #0078d4; color: #fff; padding: 3px 10px; border-radius: 12px; font-size: 12px; font-weight: bold; margin-right: 10px;\">{System.Net.WebUtility.HtmlEncode(post.SourceName)}</span>";
            }
            
            sb.AppendLine($"<div class=\"post-title\">{sourceTag}{System.Net.WebUtility.HtmlEncode(post.Title)}</div>");
            sb.AppendLine("<div class=\"post-meta\">");
            
            if (DateTime.TryParse(post.PublishDate, out var publishDate))
            {
                sb.AppendLine($"<span>📅 {publishDate:yyyy년 MM월 dd일}</span>");
            }
            
            sb.AppendLine("</div>");
            
            var summary = StripHtmlTags(post.Summary ?? "");
            if (summary.Length > 400)
            {
                summary = summary.Substring(0, 400) + "...";
            }

            // Try to fetch the true original excerpt first (preferred). If we can obtain 3 non-duplicate lines
            // from the article page, we will show those and omit the RSS summary to avoid repeating content.
            var excerptLines = await TryGetOriginalExcerptLinesAsync(post.Link, 3);
            excerptLines = RemoveDuplicateLinesAgainstSummary(excerptLines, summary);

            // Remove known uninformative concluding lines (user reported variants of "But point solutions can only go so far.")
            excerptLines = excerptLines
                .Where(l => !ContainsUnwantedSentence(l))
                .ToList();

            // If not enough lines from the article (or all removed), fall back to RSS summary split into sentences.
            if (excerptLines.Count < 3)
            {
                var summaryCandidates = GetExcerptLines(summary, 6)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !ContainsUnwantedSentence(l))
                    .ToList();

                foreach (var c in summaryCandidates)
                {
                    if (excerptLines.Count >= 3) break;
                    if (!excerptLines.Any(e => string.Equals(e, c, StringComparison.OrdinalIgnoreCase)))
                    {
                        excerptLines.Add(c);
                    }
                }

                if (excerptLines.Count < 3 && !string.IsNullOrWhiteSpace(summary))
                {
                    var fallbackChunks = ChunkTextIntoLines(summary, 3 - excerptLines.Count, 300);
                    foreach (var c in fallbackChunks)
                    {
                        if (excerptLines.Count >= 3) break;
                        if (!ContainsUnwantedSentence(c)) excerptLines.Add(c);
                    }
                }
            }

            // Ensure exactly 3 lines: trim or pad as needed
            if (excerptLines.Count > 3)
            {
                excerptLines = excerptLines.Take(3).ToList();
            }

            // Render the three original/fallback lines without any heading/label and without bullets per user request.
            if (excerptLines.Count > 0)
            {
                sb.AppendLine("<div style=\"background: #ffffff; padding: 12px; margin: 15px 0; border-radius: 4px; border: 1px solid #e0e0e0;\">");
                for (int i = 0; i < 3; i++)
                {
                    var line = i < excerptLines.Count ? excerptLines[i] : string.Empty;
                    sb.AppendLine($"<div style=\"margin: 0;\">{System.Net.WebUtility.HtmlEncode(line)}</div>");
                }
                sb.AppendLine("</div>");
            }
            else
            {
                // Fallback: show the RSS summary if we couldn't get a good excerpt from the page.
                sb.AppendLine($"<div class=\"post-summary\">{System.Net.WebUtility.HtmlEncode(summary)}</div>");
            }
            
            // Always render EN/KR insight blocks. If AI-generated summaries are missing, fill using the best available
            // fallback: prefer the full cleaned article text (cached), then RSS summary, then joined excerpt lines.
            var fallbackForInsights = string.Empty;
            if (!string.IsNullOrWhiteSpace(post.Link) && _originalTextCache.TryGetValue(post.Link, out var origText) && !string.IsNullOrWhiteSpace(origText))
            {
                fallbackForInsights = origText;
            }
            else if (!string.IsNullOrWhiteSpace(StripHtmlTags(post.Summary ?? "")))
            {
                fallbackForInsights = StripHtmlTags(post.Summary ?? "");
            }
            else if (excerptLines != null && excerptLines.Count > 0)
            {
                fallbackForInsights = string.Join(" ", excerptLines);
            }

            // Pre-sanitize English summary lines: normalize quotes/ellipsis and strip unwanted sentence variants early
            string[]? sanitizedEnglish = null;
            if (post.EnglishSummary != null && post.EnglishSummary.Length > 0)
            {
                sanitizedEnglish = post.EnglishSummary
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => NormalizeWhitespaceAndPunctuation(s))
                    .Where(s => !ContainsUnwantedSentence(s))
                    .ToArray();
            }

            var enPoints = EnsureThreeInsightLines((sanitizedEnglish != null && sanitizedEnglish.Length > 0) ? sanitizedEnglish : null, fallbackForInsights);
            // Final filter as safeguard: remove banned variants and unwanted sentence fragments
            enPoints = enPoints.Where(p => !ContainsUnwantedSentence(p) && !IsBannedVariant(p)).ToList();
            // Ensure exactly 3 lines by padding from fallback if necessary
            enPoints = EnsureExactlyThreeInsightLines(enPoints, fallbackForInsights);
            if (enPoints.Count > 0)
            {
                sb.AppendLine("<div style=\"background: #e8f4f8; padding: 15px; margin: 15px 0; border-radius: 4px; border-left: 3px solid #00a4ef;\">");
                sb.AppendLine("<div style=\"color: #00a4ef; font-weight: bold; margin: 0;\">🔎 EN insights (3줄):</div>");
                foreach (var point in enPoints)
                {
                    var cleaned = CleanPointForRendering(point);
                    sb.AppendLine($"<div style=\"margin: 0;\">• {System.Net.WebUtility.HtmlEncode(cleaned)}</div>");
                }
                sb.AppendLine("</div>");
            }

            // If KoreanSummary is missing or empty, attempt a best-effort translation of the English points.
            string[]? krSource = post.KoreanSummary;
            // If provided KoreanSummary is actually English (no Hangul), ignore it to avoid duplicating English into KR block.
            if (krSource != null && krSource.Length > 0)
            {
                bool hasHangul = krSource.Any(s => !string.IsNullOrWhiteSpace(s) && s.Any(ch => ch >= '\uAC00' && ch <= '\uD7A3'));
                if (!hasHangul)
                {
                    krSource = null;
                }
            }
            if ((krSource == null || krSource.Length == 0) && enPoints.Count > 0)
            {
                try
                {
                    var translated = await TranslatePointsToKoreanAsync(enPoints);
                    if (translated != null && translated.Length > 0)
                    {
                        krSource = translated;
                    }
                }
                catch
                {
                    // swallow; we'll avoid falling back to English to prevent duplicate EN text in KR block
                }
            }

            List<string> krPoints;
            if (krSource == null || krSource.Length == 0)
            {
                // No Korean summaries available and translation failed/not configured: provide a concise Korean fallback set
                krPoints = new List<string>
                {
                    "한국어 요약을 사용할 수 없습니다.",
                    "AOAI 번역이 구성되지 않았거나 실패했습니다.",
                    string.IsNullOrWhiteSpace(post.Link) ? "원문을 확인하세요." : $"원문을 확인하세요: {post.Link}"
                };
            }
            else
            {
                krPoints = EnsureThreeInsightLines(krSource, fallbackForInsights);
                // Filter banned variants and ensure exactly 3 lines
                krPoints = krPoints.Where(p => !ContainsUnwantedSentence(p) && !IsBannedVariant(p)).ToList();
                krPoints = EnsureExactlyThreeInsightLines(krPoints, fallbackForInsights);
            }
            if (krPoints.Count > 0)
            {
                sb.AppendLine("<div style=\"background: #f0f8ff; padding: 15px; margin: 15px 0; border-radius: 4px; border-left: 3px solid #0078d4;\">");
                sb.AppendLine("<div style=\"color: #0078d4; font-weight: bold; margin: 0;\">💡 KR 인사이트 (3줄):</div>");
                foreach (var point in krPoints)
                {
                    var cleaned = CleanPointForRendering(point);
                    sb.AppendLine($"<div style=\"margin: 0;\">• {System.Net.WebUtility.HtmlEncode(cleaned)}</div>");
                }
                sb.AppendLine("</div>");
            }
            
            sb.AppendLine($"<a href=\"{System.Net.WebUtility.HtmlEncode(post.Link)}\" class=\"post-link\">전체 글 읽기 →</a>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"footer\">");
        sb.AppendLine("<p>이 메일은 Azure Security Blog Automation에 의해 자동으로 발송되었습니다.</p>");
        sb.AppendLine("<p>매일 07:00, 15:00, 22:00 (KST)에 새로운 게시글을 확인합니다.</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        var resultHtml = sb.ToString();
        // Remove a known unhelpful concluding sentence if it slipped into output. Use regex to match possible bullet/whitespace variants.
        try
        {
            // Remove any fragment containing the known unwanted phrase (catch HTML-encoded bullets/variants and nearby markup).
            resultHtml = Regex.Replace(resultHtml, @"(?is)[^<>]{0,200}?point solutions can only go so far\.?[^<>]{0,200}?", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            // swallow — best-effort cleanup
        }

        return resultHtml;
    }

    private async Task<List<string>> TryGetOriginalExcerptLinesAsync(string? url, int lineCount)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new List<string>();
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Azure-Security-Blog-Automation/1.0");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return new List<string>();
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string>();
            }

            var html = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(html))
            {
                return new List<string>();
            }

            // Prefer extracting the actual article body to avoid navigation/menus.
            var mainHtml = ExtractMainContentHtml(html);
            var text = ExtractReadableTextFromHtml(mainHtml);
            text = RemoveBoilerplateLines(text);
            // Cache cleaned article text for later use as a better fallback when generating insights
            try { _originalTextCache.AddOrUpdate(url, text ?? string.Empty, (_, __) => text ?? string.Empty); } catch { }
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            return GetExcerptLines(text, lineCount);
        }
        catch
        {
            // Best-effort only. Never fail the whole email generation because a page fetch failed.
            return new List<string>();
        }
    }

    private static string ExtractMainContentHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // Microsoft Security Blog pages commonly render article content under a div with class "entry-content".
        var idx = html.IndexOf("entry-content", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            // Start from the nearest opening <div before the marker.
            var start = html.LastIndexOf("<div", idx, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                start = idx;
            }

            // End at </article> if possible.
            var end = html.IndexOf("</article>", idx, StringComparison.OrdinalIgnoreCase);
            if (end > start)
            {
                return html.Substring(start, end - start);
            }

            // Otherwise, take a bounded slice.
            var take = Math.Min(60000, html.Length - start);
            return html.Substring(start, take);
        }

        // Generic fallback: try <article> ... </article>
        var articleStart = html.IndexOf("<article", StringComparison.OrdinalIgnoreCase);
        if (articleStart >= 0)
        {
            var articleEnd = html.IndexOf("</article>", articleStart, StringComparison.OrdinalIgnoreCase);
            if (articleEnd > articleStart)
            {
                return html.Substring(articleStart, articleEnd - articleStart);
            }
        }

        // Last resort: bounded slice from the start.
        return html.Length > 60000 ? html.Substring(0, 60000) : html;
    }

    private static string ExtractReadableTextFromHtml(string html)
    {
        // Remove scripts/styles
        var withoutScripts = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        var withoutStyles = Regex.Replace(withoutScripts, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        var withoutComments = Regex.Replace(withoutStyles, @"<!--.*?-->", " ", RegexOptions.Singleline);

        // Introduce line breaks for block-ish elements to preserve paragraph starts.
        var withBreaks = Regex.Replace(withoutComments, @"</(p|div|h1|h2|h3|li|br|article|section)>", "\n", RegexOptions.IgnoreCase);
        withBreaks = Regex.Replace(withBreaks, @"<(br|p|div|li|h1|h2|h3|article|section)[^>]*>", "\n", RegexOptions.IgnoreCase);

        // Strip tags
        var withoutTags = Regex.Replace(withBreaks, @"<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);

        // Normalize whitespace but keep line breaks
        decoded = decoded.Replace("\r", "\n");
        decoded = Regex.Replace(decoded, @"\n{3,}", "\n\n");
        decoded = Regex.Replace(decoded, @"[\t\f\v ]{2,}", " ");

        // Keep first chunk only to avoid gigantic pages
        decoded = decoded.Trim();
        if (decoded.Length > 8000)
        {
            decoded = decoded.Substring(0, 8000);
        }

        return decoded;
    }

    private static string RemoveBoilerplateLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var dropTokens = new[]
        {
            "Content types",
            "Best practices",
            "Products and services",
            "Microsoft Defender",
            "Microsoft Security Copilot",
            "Microsoft Sentinel",
            "Topics",
            "Skip to content",
            "Search",
            "All Microsoft",
        };

        var lines = text
            .Split('\n')
            .Select(l => (l ?? string.Empty).Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var cleaned = new List<string>();
        foreach (var line in lines)
        {
            // Drop common navigation blocks. These are typically short headings/menus.
            if (dropTokens.Any(t => line.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                continue;
            }

            // Drop title/header-like lines that often appear as "... | Microsoft Security Blog".
            if (line.IndexOf("| Microsoft Security Blog", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            cleaned.Add(line);
        }

        // Keep only a reasonable number of lines to reduce noise.
        return string.Join("\n", cleaned.Take(200));
    }

    private static List<string> RemoveDuplicateLinesAgainstSummary(List<string> lines, string summary)
    {
        if (lines == null || lines.Count == 0)
        {
            return new List<string>();
        }

        var normalizedSummary = (summary ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedSummary))
        {
            return lines;
        }

        var filtered = new List<string>();
        foreach (var line in lines)
        {
            var cleaned = (line ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(cleaned))
            {
                continue;
            }

            // If the RSS summary already contains this line verbatim-ish, skip it.
            if (normalizedSummary.IndexOf(cleaned, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            filtered.Add(cleaned);
        }

        return filtered;
    }

    private string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Remove HTML tags
        var withoutTags = Regex.Replace(html, @"<[^>]+>", "");
        
        // Decode HTML entities
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        
        // Remove extra whitespace
        decoded = Regex.Replace(decoded, @"\s+", " ").Trim();
        
        return decoded;
    }

    private static List<string> GetExcerptLines(string text, int lineCount)
    {
        var cleaned = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cleaned))
        {
            return new List<string>();
        }

        // Split into sentence-ish chunks first
        var sentenceCandidates = Regex
            .Split(cleaned, @"(?<=[\.\!\?]|\u3002|\uFF01|\uFF1F)\s+")
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var lines = new List<string>();
        foreach (var s in sentenceCandidates)
        {
            if (lines.Count >= lineCount)
            {
                break;
            }

            var clipped = s;
            if (clipped.Length > 300)
            {
                // Cut at the last whitespace before the limit to avoid breaking words
                var cut = clipped.Substring(0, 300);
                var lastSpace = cut.LastIndexOfAny(new[] { ' ', '\t', '\n', '\r' });
                if (lastSpace > 150)
                {
                    cut = cut.Substring(0, lastSpace);
                }
                clipped = cut.TrimEnd() + "…";
            }
            lines.Add(clipped);
        }

        // Fallback: chunk by length if sentence split didn't yield enough
        if (lines.Count < lineCount)
        {
            var remaining = cleaned;
            while (lines.Count < lineCount && remaining.Length > 0)
            {
                var take = Math.Min(300, remaining.Length);
                var chunk = remaining.Substring(0, take).Trim();
                // Avoid breaking the last word in the chunk
                var lastSpace = chunk.LastIndexOfAny(new[] { ' ', '\t', '\n', '\r' });
                if (lastSpace > 120 && lastSpace < chunk.Length - 1)
                {
                    chunk = chunk.Substring(0, lastSpace).Trim();
                }
                remaining = remaining.Substring(Math.Min(take, remaining.Length)).Trim();
                if (!string.IsNullOrEmpty(chunk))
                {
                    if (remaining.Length > 0)
                    {
                        chunk += "…";
                    }
                    lines.Add(chunk);
                }
            }
        }

        return lines.Take(lineCount).ToList();
    }

    // Return true if the line contains the known unwanted sentence or a close variant.
    private static bool ContainsUnwantedSentence(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var s = (line ?? string.Empty).Trim();
        // Normalize whitespace and remove bullet markers
        s = Regex.Replace(s, @"^[\s\u2022\u2023\u25E6\u2043\u2219\-\*]+", "");
        s = s.Replace("\u2026", "...");
        var lower = s.ToLowerInvariant();
        // Known English phrase to remove
        if (lower.Contains("point solutions can only go so far")) return true;
        // Slight variants
        if (lower.Contains("point solutions can only go so far.")) return true;
        if (lower.Contains("but point solutions" ) && lower.Contains("go so far")) return true;
        return false;
    }

    // Additional aggressive banned-variant detection for paraphrases.
    private static bool IsBannedVariant(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var s = (line ?? string.Empty).Trim();
        s = Regex.Replace(s, @"^[\s\u2022\u2023\u25E6\u2043\u2219\-\*]+", "");
        s = s.Replace("\u2026", "...");
        var lower = s.ToLowerInvariant();
        // Stricter check: only flag as banned when the text clearly combines a "point solution(s)" mention
        // with an explicit phrase that indicates limitation (e.g., "holding you back", "hold you back", "go so far").
        if ((lower.Contains("point solution") || lower.Contains("point solutions")) &&
            (lower.Contains("holding you back") || lower.Contains("hold you back") || lower.Contains("go so far")))
        {
            return true;
        }

        if (Regex.IsMatch(lower, @"point solutions?.*holding you back", RegexOptions.IgnoreCase)) return true;

        return false;
    }

    // Chunk text into N lines trying to respect word boundaries; returns up to 'count' chunks.
    private static List<string> ChunkTextIntoLines(string text, int count, int maxChunkLength)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text) || count <= 0) return result;
        var t = Regex.Replace(text, @"\s+", " ").Trim();
        int idx = 0;
        while (result.Count < count && idx < t.Length)
        {
            var remaining = t.Substring(idx);
            var take = Math.Min(maxChunkLength, remaining.Length);
            var chunk = remaining.Substring(0, take);
            // Try to cut at last space to avoid mid-word truncation
            var lastSpace = chunk.LastIndexOf(' ');
            if (lastSpace > Math.Max(80, take / 3))
            {
                chunk = chunk.Substring(0, lastSpace);
            }
            chunk = chunk.Trim();
            if (idx + chunk.Length < t.Length) chunk += "…";
            result.Add(chunk);
            idx += Math.Min(take, Math.Max(1, chunk.Length));
        }
        return result;
    }

    private static List<string> EnsureThreeInsightLines(string[]? points, string fallbackText)
    {
        var normalized = new List<string>();
        if (points != null)
        {
            foreach (var p in points)
            {
                var n = NormalizeBulletPoint(p);
                if (!string.IsNullOrWhiteSpace(n))
                {
                    normalized.Add(n);
                }
            }
        }
        var result = new List<string>();

        // Add normalized AI-provided points first, avoiding duplicates
        foreach (var n in normalized)
        {
            if (result.Count >= 3) break;
            if (result.Any(r => string.Equals(r, n, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(n);
        }

        if (result.Count < 3 && !string.IsNullOrWhiteSpace(fallbackText))
        {
            // Use sentence-based excerpting from the fallback text
            var excerpt = GetExcerptLines(fallbackText, 6); // ask for a few candidates
            foreach (var line in excerpt)
            {
                if (result.Count >= 3) break;

                // Avoid near-duplicates or substring overlaps
                var cleaned = line.Trim();
                if (string.IsNullOrEmpty(cleaned)) continue;

                if (result.Any(r => r.IndexOf(cleaned, StringComparison.OrdinalIgnoreCase) >= 0 || cleaned.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                result.Add(cleaned);
            }
        }

        // Final safeguard: if still short, pad with shorter chunks from fallback
        if (result.Count < 3 && !string.IsNullOrWhiteSpace(fallbackText))
        {
            var backup = GetExcerptLines(fallbackText, 3);
            foreach (var b in backup)
            {
                if (result.Count >= 3) break;
                if (result.Any(r => string.Equals(r, b, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(b);
            }
        }

        return result.Take(3).ToList();
    }

    // Ensure the supplied list has exactly three insight lines by padding from fallbackText when needed.
    private static List<string> EnsureExactlyThreeInsightLines(List<string> current, string fallbackText)
    {
        var result = current ?? new List<string>();
        // Remove banned variants/duplicates as a safety net
        result = result.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).Where(r => !ContainsUnwantedSentence(r) && !IsBannedVariant(r)).ToList();

        if (result.Count >= 3) return result.Take(3).ToList();

        if (!string.IsNullOrWhiteSpace(fallbackText))
        {
            // Get some candidate lines from fallback
            var candidates = GetExcerptLines(fallbackText, 8);
            foreach (var c in candidates)
            {
                if (result.Count >= 3) break;
                var cleaned = c.Trim();
                if (string.IsNullOrEmpty(cleaned)) continue;
                if (result.Any(r => r.IndexOf(cleaned, StringComparison.OrdinalIgnoreCase) >= 0 || cleaned.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (ContainsUnwantedSentence(cleaned) || IsBannedVariant(cleaned)) continue;
                result.Add(cleaned);
            }
        }

        // Last resort: chunk fallback text into short pieces
        if (result.Count < 3 && !string.IsNullOrWhiteSpace(fallbackText))
        {
            var more = ChunkTextIntoLines(fallbackText, 6, 160);
            foreach (var m in more)
            {
                if (result.Count >= 3) break;
                var cleaned = m.Trim();
                if (string.IsNullOrEmpty(cleaned)) continue;
                if (result.Any(r => r.IndexOf(cleaned, StringComparison.OrdinalIgnoreCase) >= 0 || cleaned.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (ContainsUnwantedSentence(cleaned) || IsBannedVariant(cleaned)) continue;
                result.Add(cleaned);
            }
        }

        // If still short, try a small set of safe, deterministic fallback bullets that avoid paraphrasing
        if (result.Count < 3)
        {
            var safeFallbacks = new[]
            {
                "See the linked article for additional context.",
                "Refer to the source article for full details.",
                "Check the original post for more information."
            };

            foreach (var fb in safeFallbacks)
            {
                if (result.Count >= 3) break;
                if (result.Any(r => string.Equals(r, fb, StringComparison.OrdinalIgnoreCase))) continue;
                if (ContainsUnwantedSentence(fb) || IsBannedVariant(fb)) continue;
                result.Add(fb);
            }
        }

        // Pad with empty strings if somehow still short to preserve layout
        while (result.Count < 3) result.Add(string.Empty);
        return result.Take(3).ToList();
    }

    private static string NormalizeBulletPoint(string? point)
    {
        var s = (point ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        // Remove common leading bullet/numbering markers.
        s = Regex.Replace(s, @"^\s*(?:[-\*\u2022\u2023\u25E6\u2043\u2219]|•)\s+", "");
        s = Regex.Replace(s, @"^\s*\d+\s*[\.|\)]\s+", "");
        return s.Trim();
    }

    private static string NormalizeAoaiEndpoint(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
        var v = raw.Trim();
        if (!v.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            v = "https://" + v;
        }
        if (!v.EndsWith("/")) v += "/";
        return v;
    }

    private static string NormalizeWhitespaceAndPunctuation(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        // Normalize common Unicode punctuation to ASCII equivalents for matching
           s = s.Replace("\u2018", "'").Replace("\u2019", "'")
               .Replace("\u201C", "\"").Replace("\u201D", "\"")
               .Replace("\u2013", "-").Replace("\u2014", "-")
               .Replace("\u2026", "...");
        // Normalize multiple whitespace
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static string CleanPointForRendering(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var t = s.Trim();
        // Remove common bullet characters that may appear inside the point
        t = Regex.Replace(t, "[\u2022\u2023\u25E6\u2043\u2219\u00B7\\u00B7â€¢]", "");
        // Remove any leading/trailing punctuation introduced
        t = t.Trim();
        // Collapse internal newlines to spaces
        t = Regex.Replace(t, "\r?\n", " ");
        // Normalize multiple whitespace
        t = Regex.Replace(t, "\\s+", " ").Trim();
        // Avoid mid-word ellipsis artifacts — ensure ellipsis is three dots
        t = t.Replace("…", "...");
        return t;
    }

    private async Task<string[]?> TranslatePointsToKoreanAsync(IEnumerable<string> englishPoints)
    {
        var endpointRaw = Environment.GetEnvironmentVariable("AOAI_ENDPOINT")?.Trim();
        var deployment = Environment.GetEnvironmentVariable("AOAI_DEPLOYMENT")?.Trim();
        var apiVersion = Environment.GetEnvironmentVariable("AOAI_API_VERSION")?.Trim() ?? "2024-12-01-preview";
        var apiKey = Environment.GetEnvironmentVariable("AOAI_API_KEY")?.Trim();
        var credential = new Azure.Identity.DefaultAzureCredential();

        if (string.IsNullOrWhiteSpace(endpointRaw) || string.IsNullOrWhiteSpace(deployment))
        {
            return null;
        }

        var endpoint = NormalizeAoaiEndpoint(endpointRaw);
        var pointsJsonArray = string.Join(",", englishPoints.Select(p => JsonSerializer.Serialize(p)));
        var systemPrompt = "You are a professional translator. Translate the provided English bullet points into Korean, preserving meaning and producing concise, natural Korean bullets (one sentence each). Return a JSON array of strings.";
        var userPrompt = $@"EnglishPoints: [{pointsJsonArray}]\n\nReturn JSON array only.";

        var requestPayload = new
        {
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 500,
            temperature = 0.2
        };

        var requestJson = JsonSerializer.Serialize(requestPayload);
        var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var requestUri = $"{endpoint}openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        // Try up to 3 attempts with small backoff and robust parsing
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = requestContent;

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Remove("api-key");
                request.Headers.Add("api-key", apiKey);
            }
            else
            {
                try
                {
                    var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }));
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
                }
                catch
                {
                    // Unable to acquire MSI token this attempt; try again later
                }
            }

            try
            {
                using var resp = await Http.SendAsync(request);
                if (!resp.IsSuccessStatusCode)
                {
                    // small backoff
                    await Task.Delay(300 * attempt);
                    continue;
                }

                var content = await resp.Content.ReadAsStringAsync();

                // 1) Try to find a JSON array directly in the response
                var match = Regex.Match(content, @"\[(?:.|\n)*?\]", RegexOptions.Singleline);
                if (match.Success)
                {
                    try
                    {
                        var arr = JsonSerializer.Deserialize<string[]>(match.Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (arr != null && arr.Length > 0) return arr;
                    }
                    catch { }
                }

                // 2) Try to parse as JSON and look for a string[] anywhere (choices.message.content etc.)
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    // Search for first string array node
                    var arrNode = FindFirstStringArray(doc.RootElement);
                    if (arrNode != null && arrNode.Count > 0)
                    {
                        return arrNode.ToArray();
                    }

                    // Try to extract choices[0].message.content and search within it
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    {
                        var first = choices[0];
                        if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contentNode))
                        {
                            var inner = contentNode.GetString() ?? string.Empty;
                            var m2 = Regex.Match(inner, @"\[(?:.|\n)*?\]", RegexOptions.Singleline);
                            if (m2.Success)
                            {
                                try
                                {
                                    var arr2 = JsonSerializer.Deserialize<string[]>(m2.Value);
                                    if (arr2 != null && arr2.Length > 0) return arr2;
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch
                {
                    // ignore parse errors and try again
                }

                // If we reach here, attempt backoff then retry
                await Task.Delay(300 * attempt);
            }
            catch
            {
                await Task.Delay(300 * attempt);
            }
        }

        return null;
    }

    // Recursively search a JsonElement for the first JSON array of strings and return it as List<string>.
    private static List<string>? FindFirstStringArray(JsonElement el)
    {
        try
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                var ok = true;
                var list = new List<string>();
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        list.Add(item.GetString() ?? string.Empty);
                    }
                    else
                    {
                        ok = false; break;
                    }
                }
                if (ok && list.Count > 0) return list;
            }

            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    var found = FindFirstStringArray(prop.Value);
                    if (found != null && found.Count > 0) return found;
                }
            }

            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                {
                    var found = FindFirstStringArray(item);
                    if (found != null && found.Count > 0) return found;
                }
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }
}

public class GenerateEmailRequest
{
    public BlogPost[]? Posts { get; set; }

    // Backward/forward compatible fields
    public BlogPost[]? NewPosts { get; set; }
    public BlogPost[]? RecentPosts { get; set; }
}

public class BlogPost
{
    public string? Title { get; set; }
    public string? Link { get; set; }
    public string? PublishDate { get; set; }
    public string? Summary { get; set; }
    public string? SourceName { get; set; }
    public string[]? EnglishSummary { get; set; }
    public string[]? KoreanSummary { get; set; }
}

public static class SourceEmojiHelper
{
    public static string GetSourceEmoji(string sourceName)
    {
        return sourceName switch
        {
            "Microsoft Security Blog" => "🔒",
            "Azure Security Blog" => "☁️",
            "MS Security - Threat Intelligence" => "🔍",
            "TC - Microsoft Defender" => "🛡️",
            "TC - Microsoft Sentinel" => "👁️",
            _ => "📰"
        };
    }
}
