using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcessedPostsApi.Functions;

public class GenerateEmailHtml
{
    private readonly ILogger<GenerateEmailHtml> _logger;

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
            var request = await req.ReadFromJsonAsync<GenerateEmailRequest>();

            if (request == null || request.Posts == null)
            {
                _logger.LogWarning("Invalid request: Posts array is required");
                return new BadRequestObjectResult(new { error = "Posts array is required" });
            }

            // 24시간 내 신규 게시물 여부 판단
            var hasNewPosts = request.Posts.Length > 0;
            
            var html = GenerateHtml(request.Posts, hasNewPosts);

            var subject = hasNewPosts 
                ? $"[Microsoft 보안 블로그] 새 게시글 {request.Posts.Length}개" 
                : "[Microsoft 보안 블로그] 최근 게시글 요약 (신규 없음)";
            
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

    private string GenerateHtml(BlogPost[] posts, bool hasNewPosts)
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
        sb.AppendLine("<h1>🔒 Microsoft 보안 블로그 업데이트</h1>");
        
        if (hasNewPosts)
        {
            sb.AppendLine($"<div class=\"count\">새로운 게시글 {posts.Length}개</div>");
        }
        else
        {
            sb.AppendLine($"<div class=\"count\">최근 게시글 {posts.Length}개</div>");
        }
        
        sb.AppendLine("</div>");
        
        if (!hasNewPosts)
        {
            sb.AppendLine("<div class=\"no-new-notice\">");
            sb.AppendLine("ℹ️ <strong>24시간 이내에 게시된 새 글이 없습니다.</strong><br>");
            sb.AppendLine("아래는 각 피드의 최근 5개 게시글입니다.");
            sb.AppendLine("</div>");
        }
        
        sb.AppendLine("<div class=\"content\">");

        foreach (var post in posts)
        {
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
            
            sb.AppendLine($"<div class=\"post-summary\">{System.Net.WebUtility.HtmlEncode(summary)}</div>");
            
            // Display AI-generated summaries if available
            if (post.EnglishSummary != null && post.EnglishSummary.Length > 0)
            {
                sb.AppendLine("<div style=\"background: #e8f4f8; padding: 15px; margin: 15px 0; border-radius: 4px; border-left: 3px solid #00a4ef;\">");
                sb.AppendLine("<div style=\"color: #00a4ef; font-weight: bold; margin-bottom: 10px;\">💡 Key Insights (AI Summary)</div>");
                sb.AppendLine("<ul style=\"margin: 5px 0; padding-left: 20px;\">");
                foreach (var point in post.EnglishSummary)
                {
                    sb.AppendLine($"<li style=\"margin: 5px 0;\">{System.Net.WebUtility.HtmlEncode(point)}</li>");
                }
                sb.AppendLine("</ul>");
                sb.AppendLine("</div>");
            }
            
            if (post.KoreanSummary != null && post.KoreanSummary.Length > 0)
            {
                sb.AppendLine("<div style=\"background: #f0f8ff; padding: 15px; margin: 15px 0; border-radius: 4px; border-left: 3px solid #0078d4;\">");
                sb.AppendLine("<div style=\"color: #0078d4; font-weight: bold; margin-bottom: 10px;\">🇰🇷 핵심 인사이트 (한국어 요약)</div>");
                sb.AppendLine("<ul style=\"margin: 5px 0; padding-left: 20px;\">");
                foreach (var point in post.KoreanSummary)
                {
                    sb.AppendLine($"<li style=\"margin: 5px 0;\">{System.Net.WebUtility.HtmlEncode(point)}</li>");
                }
                sb.AppendLine("</ul>");
                sb.AppendLine("</div>");
            }
            
            sb.AppendLine($"<a href=\"{System.Net.WebUtility.HtmlEncode(post.Link)}\" class=\"post-link\">전체 글 읽기 →</a>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"footer\">");
        sb.AppendLine("<p>이 메일은 Azure Security Blog Automation에 의해 자동으로 발송되었습니다.</p>");
        sb.AppendLine("<p>매일 오전 9시에 새로운 게시글을 확인합니다.</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
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
}

public class GenerateEmailRequest
{
    public BlogPost[]? Posts { get; set; }
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
