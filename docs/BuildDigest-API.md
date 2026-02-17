# BuildDigest API 상세 가이드

## 개요

`BuildDigest`는 Azure Security Blog Automation 시스템의 핵심 통합 처리 엔진입니다. RSS 피드 수집, 웹 스크래핑, AOAI 분석, 중복 제거, 이메일 생성을 단일 HTTP 엔드포인트로 처리합니다.

## 아키텍처

```
┌─────────────────────────────────────────────────────────────┐
│                      BuildDigest API                         │
│  (HTTP POST /api/BuildDigest)                               │
└─────────────────────────────────────────────────────────────┘
                            │
        ┌───────────────────┼───────────────────┐
        │                   │                   │
        ▼                   ▼                   ▼
┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│ RSS Feed     │   │ Web Scraping │   │ AOAI         │
│ Collection   │   │ (First Para) │   │ Insights     │
└──────────────┘   └──────────────┘   └──────────────┘
        │                   │                   │
        └───────────────────┼───────────────────┘
                            │
                            ▼
                   ┌──────────────┐
                   │ Deduplication│
                   │ (Table Store)│
                   └──────────────┘
                            │
                            ▼
                   ┌──────────────┐
                   │ Email HTML   │
                   │ Generation   │
                   └──────────────┘
```

## 주요 기능

### 1. RSS 피드 병렬 수집

```csharp
// 모든 피드를 병렬로 수집 (Task.WhenAll)
var feedTasks = input.RssFeedUrls.Select(async feed =>
{
    try
    {
        var items = await FetchFeedAsync(feed.Url!, cutoff, cancellationToken);
        return (feed, items, ok: true);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Feed fetch failed: {Url}", feed.Url);
        return (feed, items: Array.Empty<Post>(), ok: false);
    }
}).ToList();

var results = await Task.WhenAll(feedTasks);

foreach (var (feed, items, ok) in results)
{
    if (!ok) { feedStatuses.Add(/* FAIL 상태 */); continue; }
    // OK 피드만 처리
}
```

**특징**:
- 병렬 처리 (Task.WhenAll)로 7개 피드 ~40-60초 완료
- FAIL 피드 자동 제외 (`continue`)
- 각 피드 독립적 에러 처리
- Retry 로직: 최대 2회, exponential backoff

### 2. 웹 스크래핑 (첫 문단 추출)

```csharp
private static async Task<string> ScrapeFirstParagraphAsync(string url, CancellationToken ct)
{
    try
    {
        var response = await RssHttp.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);
        
        // Microsoft Security Blog 전용 패턴 우선
        var patterns = new[]
        {
            @"<p[^>]*class=[""']wp-block-paragraph[""'][^>]*>(.*?)</p>",  // 우선
            @"<div[^>]*class=[""']entry-content[""'][^>]*>(.*?)</div>",  // fallback
            @"<article[^>]*>(.*?)</article>",                            // fallback
            @"<div[^>]*class=[""']post-content[""'][^>]*>(.*?)</div>"   // fallback
        };
        
        // 첫 번째 매칭 패턴 사용
        // HTML 태그 제거, max 800자
    }
    catch
    {
        return string.Empty; // 실패 시 빈 문자열 (fallback 체인으로)
    }
}
```

**Fallback 체인**:
1. 웹 스크래핑 (`ScrapeFirstParagraphAsync`)
2. RSS description 파싱 (`ExtractFirstParagraph`)
3. 원본 RSS summary

**특징**:
- User-Agent 설정으로 봇 차단 완화
- 12초 timeout
- 예외 발생 시 자동 fallback

### 3. AOAI 한국어 핵심 인사이트

```csharp
private async Task<TranslationDiagnostics> TryTranslateKoreanBatchAsync(
    List<Post> posts, CancellationToken ct)
{
    var systemPrompt = 
        "You are an expert technical analyst who extracts key insights from blog posts.\n" +
        "Your task: Read the English summary of each blog post and extract 3 key insights in Korean.\n" +
        "Rules:\n" +
        "- Provide exactly 3 Korean bullet points per item that capture the CORE INSIGHTS.\n" +
        "- Focus on actionable takeaways, key concepts, or important implications.\n" +
        "- Do NOT simply translate. Instead, ANALYZE and SYNTHESIZE the key insights.\n" +
        "- Write in natural, professional Korean.";
    
    // JSON 마크다운 블록 제거
    var cleanContent = content.Trim();
    if (cleanContent.StartsWith("```json")) cleanContent = cleanContent.Substring(7);
    if (cleanContent.StartsWith("```")) cleanContent = cleanContent.Substring(3);
    if (cleanContent.EndsWith("```")) cleanContent = cleanContent.Substring(0, cleanContent.Length - 3);
    
    // JSON 파싱 및 한국어 인사이트 매핑
}
```

**특징**:
- System prompt: "expert technical analyst" (단순 번역 제거)
- 18초 timeout
- Fallback 메시지: "핵심 인사이트를 생성하려면 AOAI 설정이 필요합니다"
- 상세한 에러 로깅 (raw content, cleaned content, stack trace)

### 4. 24시간 필터링 및 hasNew 판별

```csharp
// 신규 게시물: newCutoff(기본 24시간) 이내
var newCutoff = DateTimeOffset.UtcNow.AddHours(-newWindowHours);

// displayPosts: 신규 있으면 newPosts, 없으면 최근 10개
var displayPosts = newPosts.Count > 0
    ? newPosts
    : allItems.Where(p => p.PublishDateParsed >= newCutoff).Take(10).ToList();

// hasNew: displayPosts의 newCutoff 이내 항목 카운트
var recentPostsCount = displayPosts.Count(p => p.PublishDateParsed >= newCutoff);
var hasNew = recentPostsCount > 0;

// 제목 정확성: recentPostsCount 사용
var subject = hasNew
    ? $"[Microsoft Azure 업데이트] 새 게시글 {recentPostsCount}개"
    : "[Microsoft Azure 업데이트] 최근 게시글 요약 (신규 없음)";
```

**개선 사항**:
- 이전: `newPosts.Count` 기준 (중복 제거 전, 부정확)
- 현재: `displayPosts`의 `recentPostsCount` 기준 (실제 표시 개수)
- 제목 숫자와 이메일 내용 일치

### 5. 중복 제거 (Table Storage)

```csharp
// Best-effort 방식: Storage 실패해도 계속 진행
if (table != null)
{
    try
    {
        var pk = MakePartitionKey(p.SourceName ?? "Unknown");
        var rk = MakeRowKey(p.Link ?? string.Empty); // SHA256 해시
        _ = await table.GetEntityAsync<TableEntity>(pk, rk, cancellationToken);
        isDup = true; // 존재하면 중복
    }
    catch (RequestFailedException rfe) when (rfe.Status == 404)
    {
        isDup = false; // 404면 신규
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Dedup check failed; treating as new");
        isDup = false;
        table = null; // Storage 장애 시 더 이상 호출 안함
    }
}
```

**특징**:
- PartitionKey: `SourceName` (공백 제거)
- RowKey: `SHA256(Link)` Base64 URL-safe 인코딩
- Best-effort: 실패해도 이메일 발송 (중복 > 누락)

### 6. 이메일 HTML 생성

```csharp
// 블로그 첫 문단
if (!string.IsNullOrWhiteSpace(p.FirstParagraph))
{
    sb.AppendLine("<div style=\"margin-top:12px;font-weight:700;color:#333;\">📝 블로그 첫 문단</div>");
    sb.AppendLine($"<div style=\"margin-top:6px;color:#444;font-style:italic;border-left:3px solid #0078d4;padding-left:12px;\">{WebUtility.HtmlEncode(p.FirstParagraph)}</div>");
}

// 한국어 핵심 인사이트만 표시 (영문 제거)
sb.AppendLine("<div style=\"margin-top:14px;font-weight:700;color:#333;\">💡 핵심 인사이트 (AI Summary)</div>");
sb.AppendLine("<ul class=\"bullets\">");
foreach (var b in koBullets.Take(BulletCount))
{
    sb.AppendLine($"<li>{WebUtility.HtmlEncode(SanitizeBullet(b))}</li>");
}
sb.AppendLine("</ul>");
```

**특징**:
- 첫 문단: 이탤릭, 좌측 파란색 border
- 핵심 인사이트: 한국어 3줄만 표시
- Fallback 메시지: AOAI 설정 필요 안내

## API 요청/응답

### Request

```json
{
  "rssFeedUrls": [
    {
      "url": "https://www.microsoft.com/en-us/security/blog/feed/",
      "sourceName": "Microsoft Security Blog",
      "emoji": "🛡️"
    }
  ],
  "daysSince": 30,
  "maxItems": 12,
  "newWindowHours": 24,
  "scheduleText": "매일 07:00, 19:00 (KST)에 새로운 게시글을 확인합니다."
}
```

### Response

```json
{
  "subject": "[Microsoft Azure 업데이트] 새 게시글 3개",
  "html": "<html>...</html>",
  "stats": {
    "daysSince": 30,
    "newWindowHours": 24,
    "cutoff": "2025-12-15T00:00:00Z",
    "translation": {
      "totalPosts": 3,
      "successCount": 3,
      "error": null
    },
    "feeds": [
      {
        "sourceName": "Microsoft Security Blog",
        "url": "https://...",
        "ok": true,
        "items": 5,
        "items24h": 3,
        "elapsedMs": 1234
      }
    ],
    "allItems": 5,
    "newPosts": 3,
    "displayPosts": 3,
    "elapsedMs": 25000
  }
}
```

## 성능 및 타임아웃

| 작업 | Timeout | 비고 |
|------|---------|------|
| RSS HTTP | 12초 | `RssHttp.Timeout` |
| 웹 스크래핑 HTTP | 12초 | `RssHttp` 공유 |
| AOAI HTTP | 18초 | `AoaiHttp.Timeout` |
| AOAI CancellationToken | 18초 | `cts.CancelAfter` |
| RSS Retry | 2초, 4초 | Exponential backoff |
| 전체 실행 시간 | 40-60초 | 병렬 처리, 웹 스크래핑 오버헤드 포함 |

## 에러 처리 및 Fallback

| 상황 | 처리 방법 | 영향 |
|------|-----------|------|
| RSS 피드 실패 | `continue`로 skip | 다른 피드 정상 처리 |
| 웹 스크래핑 실패 | ExtractFirstParagraph 호출 | RSS description 사용 |
| ExtractFirstParagraph 실패 | 원본 summary 사용 | 요약 텍스트 표시 |
| AOAI 실패 | Fallback 메시지 표시 | "AOAI 설정 필요" |
| Table Storage 실패 | 계속 진행 | 중복 발송 가능 (허용) |
| JSON 파싱 실패 | Markdown 제거 후 재시도 | AOAI 응답 정리 |

## 모니터링 및 로깅

### Application Insights 로그

```csharp
// RSS 피드 실패
_logger.LogWarning(ex, "Feed fetch failed: {Url}", feed.Url);

// AOAI 실패
_logger.LogWarning("AOAI translate failed: {Status} {Body}", resp.StatusCode, TruncateForLogging(raw));

// AOAI 성공
_logger.LogInformation("AOAI raw content length: {Length}, preview: {Preview}", 
    content?.Length ?? 0, TruncateForLogging(content));

// 중복 체크 실패
_logger.LogWarning(ex, "Dedup check failed; treating as new: {Link}", p.Link);
```

### 권장 쿼리

```kusto
// 최근 24시간 에러 조회
traces 
| where timestamp > ago(24h) and severityLevel >= 3 
| project timestamp, message, severityLevel 
| order by timestamp desc

// BuildDigest 실행 시간 통계
requests 
| where name == "BuildDigest" 
| summarize avg(duration), max(duration), count() by bin(timestamp, 1h)

// RSS 피드 실패율
traces 
| where message contains "Feed fetch failed" 
| summarize count() by tostring(customDimensions.Url)
```

## 배포 및 운영

### 배포 방법

```bash
# 1. 빌드
cd functions
dotnet build -c Release

# 2. 배포 (full build 필수)
func azure functionapp publish func-dev-security-blog-automation

# 3. 검증
curl -X POST https://func-dev-security-blog-automation.azurewebsites.net/api/BuildDigest \
  -H "Content-Type: application/json" \
  -H "x-functions-key: YOUR_FUNCTION_KEY" \
  -d @request.json
```

### 환경 변수 설정

```bash
# AOAI 설정
az functionapp config appsettings set \
  --name func-dev-security-blog-automation \
  --resource-group rg-security-blog-automation-dev \
  --settings \
    AOAI_ENDPOINT=https://aoai-xxx.cognitiveservices.azure.com/ \
    AOAI_DEPLOYMENT=gpt-4o \
    AOAI_API_VERSION=2024-12-01-preview

# Storage 설정
az functionapp config appsettings set \
  --name func-dev-security-blog-automation \
  --resource-group rg-security-blog-automation-dev \
  --settings \
    AzureWebJobsStorage="DefaultEndpointsProtocol=https;..."
```

## 문제 해결

### 문제: AOAI fallback 메시지만 표시

**증상**: "핵심 인사이트를 생성하려면 AOAI 설정이 필요합니다"

**원인**:
1. AOAI 환경 변수 미설정
2. JSON 파싱 실패
3. Timeout 초과

**해결**:
```bash
# 1. 환경 변수 확인
az functionapp config appsettings list \
  --name func-dev-security-blog-automation \
  --resource-group rg-security-blog-automation-dev \
  | grep AOAI

# 2. Application Insights 로그 확인
az monitor app-insights query \
  --app func-dev-security-blog-automation \
  --resource-group rg-security-blog-automation-dev \
  --analytics-query "traces | where message contains 'AOAI' | order by timestamp desc | take 10"

# 3. --no-build 제거하고 재배포
func azure functionapp publish func-dev-security-blog-automation
```

### 문제: 블로그 첫 문단이 RSS description

**증상**: "Learn how Microsoft unites..." (RSS description)

**원인**:
1. 웹 스크래핑 실패
2. 잘못된 Regex 패턴
3. 봇 차단

**해결**:
```bash
# 1. 블로그 페이지 HTML 구조 확인
curl https://www.microsoft.com/en-us/security/blog/2026/01/13/... \
  | grep -A 5 "wp-block-paragraph"

# 2. User-Agent 헤더 확인 (BuildDigest.cs 생성자)
# 3. Application Insights에서 scraping 로그 확인
```

### 문제: hasNew가 "신규 없음"이지만 RSS에 new 표시

**증상**: RSS "1 new" vs 이메일 "신규 없음"

**원인**: `newPosts.Count`와 `displayPosts` 불일치 (이미 수정됨)

**검증**:
```csharp
// BuildDigest.cs 라인 303-309 확인
var recentPostsCount = displayPosts.Count(p => p.PublishDateParsed >= newCutoff);
var hasNew = recentPostsCount > 0;
```

## 참고 문서

- [아키텍처 가이드](아키텍처.md)
- [배포 가이드](배포-가이드.md)
- [Logic App 워크플로우](Logic-App-워크플로우.md)
- [CHANGELOG](../CHANGELOG.md)
