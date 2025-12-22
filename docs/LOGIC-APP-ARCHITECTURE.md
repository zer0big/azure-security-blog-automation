# Logic App 워크플로우 아키텍처 문서

## 📋 문서 정보

- **시스템명**: Azure Security Blog Automation
- **Logic App**: logic-dev-security-blog-automation
- **버전**: 1.0.0
- **최종 업데이트**: 2025-12-22
- **작성자**: Azure MVP Team
- **환경**: Development (Korea Central)

---

## 🎯 시스템 개요

### 목적
Microsoft 보안 블로그(3개 RSS 피드)에서 최신 보안 소식을 자동으로 수집하고, Azure OpenAI GPT-4o를 활용하여 3줄 핵심 인사이트(영문/한글)를 생성한 후, 매일 아침 종합 리포트를 이메일로 발송하는 자동화 시스템

### 주요 기능
1. **다중 RSS 피드 수집**: 3개 Microsoft 보안 블로그 동시 모니터링
2. **지능형 중복 제거**: Azure Table Storage 기반 게시물 중복 체크
3. **AI 기반 요약**: Azure OpenAI GPT-4o를 활용한 3줄 인사이트 생성 (영문/한글)
4. **스마트 컨텐츠 선택**: 
   - 24시간 이내 신규 게시물 우선
   - 신규 없을 시 최근 5개 게시물 표시 (30일 이내만)
5. **일일 자동 리포트**: Office 365 이메일로 종합 리포트 발송

### 실행 스케줄
- **빈도**: 매일 (Daily)
- **시간**: 오전 7:00 AM
- **시간대**: 한국 표준시 (Korea Standard Time, UTC+9)
- **트리거**: Recurrence

---

## 🏗️ 시스템 아키텍처

### 전체 구성도

```
┌─────────────────────────────────────────────────────────────────┐
│                     Logic App (Consumption)                      │
│                logic-dev-security-blog-automation                │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌──────────────┐                                                │
│  │  Recurrence  │ ──────► 매일 07:00 KST                        │
│  └──────┬───────┘                                                │
│         │                                                         │
│         ▼                                                         │
│  ┌────────────────────┐                                          │
│  │ Initialize_All_Posts│ ──► allPosts = []                       │
│  └──────┬─────────────┘                                          │
│         │                                                         │
│         ▼                                                         │
│  ┌───────────────────────────────────────────────────┐          │
│  │         For_Each_RSS_Feed (순차, 3개)             │          │
│  │  ┌─────────────────────────────────────────────┐ │          │
│  │  │ List_RSS_Feed_Items (since: -1 day)         │ │          │
│  │  └─────┬───────────────────────────────────────┘ │          │
│  │        │                                           │          │
│  │        ▼                                           │          │
│  │  ┌─────────────────────────────────────────────┐ │          │
│  │  │   For_Each_RSS_Item (순차)                  │ │          │
│  │  │   ┌───────────────────────────────────────┐ │ │          │
│  │  │   │ Check_Duplicate (Azure Function)      │ │ │          │
│  │  │   └─────┬─────────────────────────────────┘ │ │          │
│  │  │         │                                     │ │          │
│  │  │         ▼                                     │ │          │
│  │  │   ┌────────────────────────────────────┐    │ │          │
│  │  │   │ Condition_Is_New                    │    │ │          │
│  │  │   │ (isDuplicate == false)             │    │ │          │
│  │  │   └─────┬──────────────────────────────┘    │ │          │
│  │  │         │ YES                                 │ │          │
│  │  │         ▼                                     │ │          │
│  │  │   ┌────────────────────────────────────┐    │ │          │
│  │  │   │ Summarize_Post (Azure Function)    │    │ │          │
│  │  │   │ → GPT-4o 3줄 영문/한글 요약        │    │ │          │
│  │  │   └─────┬──────────────────────────────┘    │ │          │
│  │  │         │                                     │ │          │
│  │  │         ▼                                     │ │          │
│  │  │   ┌────────────────────────────────────┐    │ │          │
│  │  │   │ Append_To_All_Posts                │    │ │          │
│  │  │   └─────┬──────────────────────────────┘    │ │          │
│  │  │         │                                     │ │          │
│  │  │         ▼                                     │ │          │
│  │  │   ┌────────────────────────────────────┐    │ │          │
│  │  │   │ Insert_To_Table_Storage            │    │ │          │
│  │  │   └────────────────────────────────────┘    │ │          │
│  │  └───────────────────────────────────────────┘ │          │
│  └───────────────────────────────────────────────┘          │
│         │                                                         │
│         ▼                                                         │
│  ┌───────────────────────────────────────────────────┐          │
│  │     Get_All_Recent_Posts (순차, 3개)              │          │
│  │  ┌─────────────────────────────────────────────┐ │          │
│  │  │ List_Recent_Items (전체 조회)               │ │          │
│  │  └─────┬───────────────────────────────────────┘ │          │
│  │        │                                           │          │
│  │        ▼                                           │          │
│  │  ┌─────────────────────────────────────────────┐ │          │
│  │  │ Filter_Recent_Posts_Within_30Days           │ │          │
│  │  │ → Take 5, publishDate >= -30 days           │ │          │
│  │  └─────┬───────────────────────────────────────┘ │          │
│  │        │                                           │          │
│  │        ▼                                           │          │
│  │  ┌─────────────────────────────────────────────┐ │          │
│  │  │   Add_Top5_To_All_Posts (순차)              │ │          │
│  │  │   ┌───────────────────────────────────────┐ │ │          │
│  │  │   │ Summarize_Recent_Post (GPT-4o)        │ │ │          │
│  │  │   └─────┬─────────────────────────────────┘ │ │          │
│  │  │         │                                     │ │          │
│  │  │         ▼                                     │ │          │
│  │  │   ┌────────────────────────────────────┐    │ │          │
│  │  │   │ Append_Recent_Post                 │    │ │          │
│  │  │   └────────────────────────────────────┘    │ │          │
│  │  └───────────────────────────────────────────┘ │          │
│  └───────────────────────────────────────────────┘          │
│         │                                                         │
│         ▼                                                         │
│  ┌────────────────────┐                                          │
│  │ Generate_Email_HTML│ ──► Azure Function                       │
│  └──────┬─────────────┘                                          │
│         │                                                         │
│         ▼                                                         │
│  ┌────────────────────────┐                                      │
│  │ Send_Consolidated_Email│ ──► Office 365                       │
│  └────────────────────────┘                                      │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘

         ▲                          ▲                    ▲
         │                          │                    │
    ┌────┴────┐              ┌─────┴─────┐        ┌────┴─────┐
    │   RSS   │              │  Azure    │        │ Office   │
    │  Feeds  │              │ Functions │        │   365    │
    │  (3개)  │              │  (4개)    │        │ Connector│
    └─────────┘              └───────────┘        └──────────┘
```

### 외부 시스템 연동

#### 1. RSS 피드 소스
- **Microsoft Security Blog**: `https://www.microsoft.com/en-us/security/blog/feed/`
- **Azure Security Blog**: `https://azure.microsoft.com/en-us/blog/topics/security/feed/`
- **MS Security - Threat Intelligence**: `https://www.microsoft.com/en-us/security/blog/topic/threat-intelligence/feed/`

#### 2. Azure Functions (.NET 8)
- **CheckDuplicate**: 게시물 중복 여부 확인 (Table Storage 조회)
- **SummarizePost**: Azure OpenAI GPT-4o 기반 3줄 요약 생성 (영문/한글)
- **InsertProcessed**: 처리된 게시물 Table Storage 저장
- **GenerateEmailHtml**: HTML 이메일 템플릿 생성

#### 3. Azure Table Storage
- **Storage Account**: stdevsecblogauto
- **Table**: ProcessedPosts
- **용도**: 게시물 중복 체크용 영구 저장소
- **키 구조**: PartitionKey=sourceName, RowKey=link (URL 해시)

#### 4. Azure OpenAI
- **Endpoint**: aoai-knowledge-base-demo.cognitiveservices.azure.com
- **Model**: gpt-4o
- **용도**: 보안 게시물 핵심 인사이트 추출 및 한국어 번역

#### 5. Office 365 Connector
- **수신자**: azure-mvp@zerobig.kr
- **형식**: HTML 이메일
- **중요도**: Normal

---

## 📊 워크플로우 상세 설명

### 1. Trigger: Recurrence

**타입**: `Recurrence`

**설정**:
```json
{
  "frequency": "Day",
  "interval": 1,
  "schedule": {
    "hours": ["7"],
    "minutes": [0]
  },
  "timeZone": "Korea Standard Time"
}
```

**설명**: 
- 매일 오전 7시(KST)에 워크플로우 자동 실행
- 주말 포함 연중무휴 실행
- 한국 시간대 기준으로 정확한 실행 보장

**참고 문서**: [Recurrence Trigger](https://learn.microsoft.com/azure/logic-apps/logic-apps-workflow-actions-triggers#recurrence-trigger)

---

### 2. Initialize_All_Posts

**타입**: `InitializeVariable`

**설정**:
```json
{
  "variables": [{
    "name": "allPosts",
    "type": "array",
    "value": []
  }]
}
```

**목적**: 
- 모든 RSS 피드에서 수집된 게시물을 담을 배열 변수 초기화
- 신규 게시물과 최근 게시물을 모두 포함

**데이터 구조**:
```json
{
  "title": "게시물 제목",
  "link": "게시물 URL",
  "publishDate": "2024-12-22T10:00:00Z",
  "summary": "게시물 원문 요약",
  "sourceName": "Microsoft Security Blog",
  "englishSummary": ["insight 1", "insight 2", "insight 3"],
  "koreanSummary": ["인사이트 1", "인사이트 2", "인사이트 3"]
}
```

---

### 3. For_Each_RSS_Feed

**타입**: `Foreach`

**설정**:
```json
{
  "foreach": "@parameters('rssFeedUrls')",
  "runtimeConfiguration": {
    "concurrency": {
      "repetitions": 1
    }
  }
}
```

**동작**:
- 3개 RSS 피드를 **순차적으로** 처리 (concurrency=1)
- 각 피드별로 최근 24시간 이내 게시물 조회

**처리 순서**:
1. Microsoft Security Blog
2. Azure Security Blog
3. MS Security - Threat Intelligence

**⚠️ 개선 필요**: 
- 현재 순차 처리로 전체 실행 시간 증가
- 병렬 처리 가능하나 Azure Functions 과부하 우려

---

### 4. List_RSS_Feed_Items

**타입**: `ApiConnection` (RSS Connector)

**설정**:
```json
{
  "path": "/ListFeedItems",
  "queries": {
    "feedUrl": "@{items('For_Each_RSS_Feed')['url']}",
    "since": "@{addDays(utcNow(), -1)}"
  }
}
```

**동작**:
- RSS 피드에서 최근 24시간 이내 게시물만 조회
- `since` 파라미터로 시간 필터링

**반환 데이터**:
- title, primaryLink, publishDate, summary 등
- RSS 표준 항목 포함

**참고 문서**: [RSS Connector](https://learn.microsoft.com/connectors/rss/)

---

### 5. For_Each_RSS_Item

**타입**: `Foreach`

**설정**:
```json
{
  "foreach": "@body('List_RSS_Feed_Items')",
  "runtimeConfiguration": {
    "concurrency": {
      "repetitions": 1
    }
  }
}
```

**동작**:
- 각 RSS 아이템을 **순차적으로** 처리
- 중복 체크 → 신규 판정 → AI 요약 → 저장 순서 보장

**처리 로직**:
1. CheckDuplicate 호출
2. 중복이 아니면 Summarize → Append → Insert
3. 중복이면 Skip

---

### 6. Check_Duplicate

**타입**: `Http` (Azure Function 호출)

**엔드포인트**: 
```
POST https://func-dev-security-blog-automation.azurewebsites.net/api/CheckDuplicate
```

**요청 Body**:
```json
{
  "link": "https://...",
  "sourceName": "Microsoft Security Blog"
}
```

**응답**:
```json
{
  "isDuplicate": false
}
```

**동작**:
- Azure Table Storage의 ProcessedPosts 테이블 조회
- PartitionKey=sourceName, RowKey=link 해시값으로 검색
- 기존 게시물이면 `isDuplicate: true` 반환

**⚠️ 개선 필요**:
- Timeout 미설정 (무한 대기 가능)
- Retry Policy 없음 (실패 시 즉시 중단)

**참고 코드**: `functions/Functions/CheckDuplicate.cs`

---

### 7. Condition_Is_New

**타입**: `If`

**조건**:
```json
{
  "and": [{
    "equals": [
      "@body('Check_Duplicate')?['isDuplicate']",
      false
    ]
  }]
}
```

**분기**:
- **True (신규)**: Summarize → Append → Insert 실행
- **False (중복)**: Skip

**⚠️ 개선 필요**:
- False 분기 처리 로직 없음
- CheckDuplicate 실패 시 에러 핸들링 부재

---

### 8. Summarize_Post

**타입**: `Http` (Azure Function 호출)

**엔드포인트**:
```
POST https://func-dev-security-blog-automation.azurewebsites.net/api/SummarizePost
```

**요청 Body**:
```json
{
  "title": "게시물 제목",
  "content": "게시물 본문"
}
```

**응답**:
```json
{
  "englishSummary": [
    "A critical Windows vulnerability enables remote code execution.",
    "Microsoft has issued an emergency patch to address the issue.",
    "Users should update their systems immediately to stay protected."
  ],
  "koreanSummary": [
    "심각한 Windows 취약점이 원격 코드 실행을 가능하게 합니다.",
    "Microsoft가 문제 해결을 위한 긴급 패치를 배포했습니다.",
    "사용자는 즉시 시스템을 업데이트하여 보호를 유지해야 합니다."
  ]
}
```

**Azure OpenAI 설정**:
- **Model**: gpt-4o
- **Temperature**: 0.3 (일관성 있는 요약)
- **Max Tokens**: 1000
- **System Prompt**: "You are an expert security analyst. Summarize in 3 concise bullet points, each under 150 characters."

**⚠️ 개선 필요**:
- Timeout 미설정 (OpenAI 응답 지연 시 무한 대기)
- Retry Policy 없음 (429 Rate Limit 에러 시 즉시 실패)

**참고 코드**: `functions/Functions/SummarizePost.cs`

---

### 9. Append_To_All_Posts

**타입**: `AppendToArrayVariable`

**설정**:
```json
{
  "name": "allPosts",
  "value": {
    "title": "@{items('For_Each_RSS_Item')?['title']}",
    "link": "@{items('For_Each_RSS_Item')?['primaryLink']}",
    "publishDate": "@{items('For_Each_RSS_Item')?['publishDate']}",
    "summary": "@{items('For_Each_RSS_Item')?['summary']}",
    "sourceName": "@{items('For_Each_RSS_Feed')['sourceName']}",
    "englishSummary": "@body('Summarize_Post')?['englishSummary']",
    "koreanSummary": "@body('Summarize_Post')?['koreanSummary']"
  }
}
```

**동작**:
- allPosts 배열에 신규 게시물 추가
- AI 요약 결과 포함
- 이메일 생성 시 사용될 최종 데이터

---

### 10. Insert_To_Table_Storage

**타입**: `Http` (Azure Function 호출)

**엔드포인트**:
```
POST https://func-dev-security-blog-automation.azurewebsites.net/api/InsertProcessed
```

**요청 Body**:
```json
{
  "link": "https://...",
  "title": "게시물 제목",
  "publishDate": "2024-12-22T10:00:00Z",
  "sourceName": "Microsoft Security Blog"
}
```

**동작**:
- Azure Table Storage의 ProcessedPosts 테이블에 저장
- 향후 중복 체크 시 사용
- PartitionKey=sourceName, RowKey=link 해시

**⚠️ 개선 필요**:
- 저장 실패 시 재시도 없음
- 에러 로깅 부족

**참고 코드**: `functions/Functions/InsertProcessed.cs`

---

### 11. Get_All_Recent_Posts

**타입**: `Foreach`

**목적**: 
- 24시간 이내 신규 게시물이 **없을 경우**를 대비
- 각 RSS 피드별로 최근 5개 게시물 표시 (30일 이내만)

**설정**:
```json
{
  "foreach": "@parameters('rssFeedUrls')",
  "runtimeConfiguration": {
    "concurrency": {
      "repetitions": 1
    }
  }
}
```

**실행 시점**: `For_Each_RSS_Feed` 완료 후 항상 실행

---

### 12. List_Recent_Items

**타입**: `ApiConnection` (RSS Connector)

**설정**:
```json
{
  "path": "/ListFeedItems",
  "queries": {
    "feedUrl": "@{items('Get_All_Recent_Posts')['url']}"
  }
}
```

**차이점**:
- `since` 파라미터 **없음** → 전체 게시물 조회
- 최신 순으로 정렬된 결과 반환

---

### 13. Filter_Recent_Posts_Within_30Days

**타입**: `Query`

**설정**:
```json
{
  "from": "@take(body('List_Recent_Items'), 5)",
  "where": "@greaterOrEquals(item()?['publishDate'], addDays(utcNow(), -30))"
}
```

**동작**:
1. 최신 5개 게시물만 추출 (`take(5)`)
2. 30일 이내 게시물만 필터링
3. 30일 이상 오래된 게시물은 제외

**목적**: 
- 너무 오래된 게시물은 이메일에 표시하지 않음
- 사용자 요청 사항 반영

**참고 문서**: [Query Action](https://learn.microsoft.com/azure/logic-apps/logic-apps-perform-data-operations#filter-array-action)

---

### 14. Add_Top5_To_All_Posts

**타입**: `Foreach`

**설정**:
```json
{
  "foreach": "@body('Filter_Recent_Posts_Within_30Days')",
  "runtimeConfiguration": {
    "concurrency": {
      "repetitions": 1
    }
  }
}
```

**동작**:
- 필터링된 최근 게시물 각각에 대해 AI 요약 생성
- allPosts 배열에 추가

**⚠️ 개선 검토**:
- 현재 순차 처리 (concurrency=1)
- 병렬 처리 시 속도 향상 가능 (repetitions: 3 권장)

---

### 15. Summarize_Recent_Post

**타입**: `Http` (Azure Function 호출)

**동작**: `Summarize_Post`와 동일
- 최근 게시물에 대해서도 AI 요약 생성
- GPT-4o 3줄 영문/한글 인사이트 제공

---

### 16. Append_Recent_Post

**타입**: `AppendToArrayVariable`

**동작**: `Append_To_All_Posts`와 동일
- 최근 게시물을 allPosts 배열에 추가
- AI 요약 포함

---

### 17. Generate_Email_HTML

**타입**: `Http` (Azure Function 호출)

**엔드포인트**:
```
POST https://func-dev-security-blog-automation.azurewebsites.net/api/GenerateEmailHtml
```

**요청 Body**:
```json
{
  "posts": "@variables('allPosts')"
}
```

**응답**:
```json
{
  "subject": "🔐 Azure Security Updates - 2024-12-22",
  "html": "<html>...</html>"
}
```

**동작**:
- allPosts 배열 전체를 받아 HTML 이메일 생성
- 각 게시물별로:
  - 제목, 링크, 발행일
  - 원문 요약
  - 💡 Key Insights (AI Summary) - 영문
  - 🇰🇷 핵심 인사이트 (한국어 요약)
- RSS 소스별로 그룹핑하여 표시

**참고 코드**: `functions/Functions/GenerateEmailHtml.cs`

---

### 18. Send_Consolidated_Email

**타입**: `ApiConnection` (Office 365 Connector)

**설정**:
```json
{
  "To": "@parameters('emailRecipient')",
  "Subject": "@{body('Generate_Email_HTML').subject}",
  "Body": "@{body('Generate_Email_HTML').html}",
  "Importance": "Normal",
  "IsHtml": true
}
```

**동작**:
- Office 365 계정으로 이메일 발송
- HTML 형식 지원
- 첨부 파일 없음

**수신자**: azure-mvp@zerobig.kr

**참고 문서**: [Office 365 Outlook Connector](https://learn.microsoft.com/connectors/office365/)

---

## 🔧 파라미터 설정

### rssFeedUrls (Array)

**기본값**:
```json
[
  {
    "url": "https://www.microsoft.com/en-us/security/blog/feed/",
    "sourceName": "Microsoft Security Blog"
  },
  {
    "url": "https://azure.microsoft.com/en-us/blog/topics/security/feed/",
    "sourceName": "Azure Security Blog"
  },
  {
    "url": "https://www.microsoft.com/en-us/security/blog/topic/threat-intelligence/feed/",
    "sourceName": "MS Security - Threat Intelligence"
  }
]
```

**용도**: 모니터링할 RSS 피드 목록

---

### emailRecipient (String)

**기본값**: `azure-mvp@zerobig.kr`

**용도**: 일일 리포트 수신자 이메일 주소

---

### functionsAppUrl (String)

**기본값**: `https://func-dev-security-blog-automation.azurewebsites.net`

**용도**: Azure Functions 엔드포인트 URL

---

### functionKey (SecureString)

**타입**: SecureString (암호화)

**용도**: Azure Functions 인증 키

**⚠️ 보안 개선 필요**: 
- 현재 파라미터로 저장
- Azure Key Vault 참조로 변경 권장
- Managed Identity 사용 권장

---

### $connections (Object)

**용도**: API 커넥션 참조

**포함 항목**:
- `rss`: RSS Connector 연결 정보
- `office365`: Office 365 Outlook 연결 정보

---

## 📈 데이터 플로우

### 신규 게시물 처리 플로우

```
RSS Feed
   ↓
List_RSS_Feed_Items (since: -1 day)
   ↓
For_Each_RSS_Item
   ↓
Check_Duplicate (Azure Function)
   ↓
isDuplicate == false?
   ↓ YES
Summarize_Post (GPT-4o)
   ↓
{
  englishSummary: [...],
  koreanSummary: [...]
}
   ↓
Append_To_All_Posts
   ↓
Insert_To_Table_Storage
   ↓
allPosts[] 배열에 추가
```

### 최근 게시물 처리 플로우

```
RSS Feed
   ↓
List_Recent_Items (전체 조회)
   ↓
Filter_Recent_Posts_Within_30Days
   ↓ take(5) + publishDate >= -30days
For_Each (filtered items)
   ↓
Summarize_Recent_Post (GPT-4o)
   ↓
Append_Recent_Post
   ↓
allPosts[] 배열에 추가
```

### 최종 이메일 생성 플로우

```
allPosts[] (신규 + 최근)
   ↓
Generate_Email_HTML (Azure Function)
   ↓
{
  subject: "🔐 Azure Security Updates - 2024-12-22",
  html: "<html>...</html>"
}
   ↓
Send_Consolidated_Email (Office 365)
   ↓
azure-mvp@zerobig.kr 수신
```

---

## ⚠️ 개선 계획 (To-Be)

### Critical Priority (즉시 개선 필요)

#### 1. [WI 145] HTTP 액션 재시도 정책 추가

**현재 문제**:
- 모든 HTTP 액션에 retry policy 미설정
- Azure Functions 일시적 장애 시 즉시 실패
- 네트워크 타임아웃 발생 시 복구 불가

**개선 방안**:
```json
{
  "retry": {
    "type": "exponential",
    "count": 3,
    "interval": "PT10S",
    "maximumInterval": "PT1M",
    "minimumInterval": "PT5S"
  }
}
```

**적용 대상**:
- Check_Duplicate
- Summarize_Post
- Summarize_Recent_Post
- Insert_To_Table_Storage
- Generate_Email_HTML

**예상 효과**: 일시적 장애 자동 복구율 90% 이상

**참고**: [Retry Policies](https://learn.microsoft.com/azure/logic-apps/logic-apps-exception-handling#retry-policies)

---

#### 2. [WI 146] HTTP 액션 타임아웃 설정

**현재 문제**:
- 모든 HTTP 액션 timeout 미지정
- Azure OpenAI 응답 지연 시 무한 대기
- 워크플로우 전체 중단 가능

**개선 방안**:
```json
{
  "timeout": "PT2M"  // 일반 Function
}
```

```json
{
  "timeout": "PT3M"  // SummarizePost (AI 처리 고려)
}
```

**적용 기준**:
- CheckDuplicate: PT2M (Table Storage 조회는 빠름)
- SummarizePost: PT3M (GPT-4o 응답 시간 고려)
- InsertProcessed: PT2M
- GenerateEmailHtml: PT2M

**예상 효과**: 워크플로우 최대 실행 시간 예측 가능

**참고**: [HTTP Limits](https://learn.microsoft.com/azure/logic-apps/logic-apps-limits-and-config#http-limits)

---

#### 3. [WI 147] Function Key를 Azure Key Vault로 이관

**현재 문제**:
- Function Key가 Logic App 파라미터로 저장
- SecureString이지만 배포 시 노출 가능
- 키 교체 시 Logic App 재배포 필요

**개선 방안**:
```json
{
  "functionKey": {
    "type": "securestring",
    "value": "@Microsoft.KeyVault(SecretUri=https://kv-xxx.vault.azure.net/secrets/FunctionKey)"
  }
}
```

**구현 단계**:
1. Azure Key Vault 리소스 생성
2. Function Key를 Secret으로 저장
3. Logic App Managed Identity 활성화
4. Key Vault Access Policy 설정
5. Logic App 파라미터 업데이트

**예상 효과**: 
- 키 관리 중앙화
- 감사 로그 자동 기록
- 키 순환 간소화

**참고**: [Secure Parameters](https://learn.microsoft.com/azure/logic-apps/logic-apps-securing-a-logic-app#secure-parameters)

---

### High Priority (단계적 개선)

#### 4. [WI 148] 에러 핸들링 개선

**현재 문제**:
- Condition_Is_New의 True 분기만 구현
- HTTP 액션 실패 시 후속 처리 없음
- 실패한 RSS 피드 추적 불가

**개선 방안**:

**1) Scope 액션으로 그룹핑**:
```json
{
  "Scope_Process_RSS_Feed": {
    "type": "Scope",
    "actions": {
      "List_RSS_Feed_Items": {...},
      "For_Each_RSS_Item": {...}
    },
    "runAfter": {}
  }
}
```

**2) 실패 처리 분기 추가**:
```json
{
  "Send_Error_Notification": {
    "type": "ApiConnection",
    "runAfter": {
      "Scope_Process_RSS_Feed": ["Failed", "TimedOut"]
    }
  }
}
```

**3) 에러 로깅**:
```json
{
  "Log_Error": {
    "type": "Http",
    "inputs": {
      "uri": "https://func-xxx.azurewebsites.net/api/LogError",
      "body": {
        "error": "@result('Scope_Process_RSS_Feed')",
        "timestamp": "@utcNow()"
      }
    }
  }
}
```

**예상 효과**: 
- 부분 실패 시에도 이메일 발송 가능
- 에러 원인 추적 용이
- 운영 안정성 향상

**참고**: [Error Handling](https://learn.microsoft.com/azure/logic-apps/logic-apps-exception-handling)

---

#### 5. [WI 149] 모니터링 강화 (trackedProperties)

**현재 문제**:
- trackedProperties 미설정
- 실행 기록에서 특정 게시물 검색 불가
- 어떤 RSS 소스에서 에러 발생했는지 파악 어려움

**개선 방안**:

**각 액션에 trackedProperties 추가**:
```json
{
  "Check_Duplicate": {
    "type": "Http",
    "trackedProperties": {
      "sourceName": "@{items('For_Each_RSS_Feed')['sourceName']}",
      "postTitle": "@{items('For_Each_RSS_Item')?['title']}",
      "postLink": "@{items('For_Each_RSS_Item')?['primaryLink']}"
    }
  }
}
```

**Application Insights 연동**:
```json
{
  "Log_Custom_Event": {
    "type": "Http",
    "inputs": {
      "uri": "https://func-xxx.azurewebsites.net/api/LogEvent",
      "body": {
        "eventName": "PostProcessed",
        "properties": {
          "sourceName": "@{items('For_Each_RSS_Feed')['sourceName']}",
          "postCount": "@length(variables('allPosts'))"
        }
      }
    }
  }
}
```

**예상 효과**:
- 실행 기록 검색 가능
- 게시물별 처리 시간 추적
- KPI 대시보드 구축 가능

**참고**: [Tracked Properties](https://learn.microsoft.com/azure/logic-apps/monitor-logic-apps#tracked-properties)

---

#### 6. [WI 150] 병렬 처리 제한 최적화

**현재 상태**:
- For_Each_RSS_Feed: concurrency = 1 (순차)
- For_Each_RSS_Item: concurrency = 1 (순차)
- Add_Top5_To_All_Posts: concurrency = 1 (순차)

**개선 방안**:

**1) For_Each_RSS_Feed**: 
```json
{
  "runtimeConfiguration": {
    "concurrency": {
      "repetitions": 3  // 3개 피드 동시 처리
    }
  }
}
```
- 3개 RSS 피드 병렬 처리
- 전체 실행 시간 1/3로 단축

**2) For_Each_RSS_Item**: 
```json
{
  "runtimeConfiguration": {
    "concurrency": {
      "repetitions": 1  // 유지
    }
  }
}
```
- 순차 처리 유지 (중복 체크 정확성 보장)

**3) Add_Top5_To_All_Posts**:
```json
{
  "runtimeConfiguration": {
    "concurrency": {
      "repetitions": 3  // 병렬 처리
    }
  }
}
```
- 최대 3개 게시물 동시 요약
- Azure Functions 부하 분산 필요

**⚠️ 주의사항**:
- Azure Functions Consumption Plan 제한 확인
- Application Insights 모니터링 필수
- 단계적 적용 (1→2→3 순)

**예상 효과**:
- 전체 실행 시간: 약 10분 → 4분 단축
- 사용자 경험 개선 (더 빠른 이메일 수신)

**참고**: [Concurrency Control](https://learn.microsoft.com/azure/logic-apps/logic-apps-workflow-actions-triggers#foreach-action)

---

## 📚 참고 자료

### Azure Logic Apps

- [Logic Apps Documentation](https://learn.microsoft.com/azure/logic-apps/)
- [Workflow Definition Language](https://learn.microsoft.com/azure/logic-apps/logic-apps-workflow-definition-language)
- [Built-in Actions](https://learn.microsoft.com/azure/logic-apps/logic-apps-workflow-actions-triggers)
- [Managed Connectors](https://learn.microsoft.com/connectors/)

### Azure Functions

- [Azure Functions Documentation](https://learn.microsoft.com/azure/azure-functions/)
- [HTTP Trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-http-webhook-trigger)
- [Table Storage Bindings](https://learn.microsoft.com/azure/azure-functions/functions-bindings-storage-table)

### Azure OpenAI

- [Azure OpenAI Service](https://learn.microsoft.com/azure/ai-services/openai/)
- [GPT-4o Model](https://learn.microsoft.com/azure/ai-services/openai/concepts/models#gpt-4o)
- [Best Practices](https://learn.microsoft.com/azure/ai-services/openai/concepts/advanced-prompt-engineering)

### Azure Well-Architected Framework

- [Reliability Patterns](https://learn.microsoft.com/azure/well-architected/reliability/principles)
- [Security Best Practices](https://learn.microsoft.com/azure/well-architected/security/overview)
- [Operational Excellence](https://learn.microsoft.com/azure/well-architected/operational-excellence/overview)

### RSS Specification

- [RSS 2.0 Specification](https://www.rssboard.org/rss-specification)
- [RSS Best Practices](https://www.rssboard.org/rss-profile)

---

## 🔄 버전 히스토리

### v1.0.0 (2024-12-22)

**초기 구현**:
- 3개 RSS 피드 모니터링
- Azure OpenAI GPT-4o 기반 3줄 요약 (영문/한글)
- 중복 제거 로직 (Table Storage)
- 30일 필터링
- 일일 이메일 리포트

**알려진 제한사항**:
- HTTP 재시도 정책 없음
- 타임아웃 미설정
- Function Key 하드코딩
- 에러 핸들링 부족
- 모니터링 제한적
- 순차 처리로 성능 저하

**개선 예정**: 6개 Issue (WI 145~150)

---

## 📧 문의

**프로젝트**: Azure Security Blog Automation  
**환경**: Development (Korea Central)  
**담당자**: Azure MVP Team  
**이메일**: azure-mvp@zerobig.kr  
**ADO 프로젝트**: https://dev.azure.com/azure-mvp/azure-secu-updates-notification

---

*본 문서는 Logic App의 현재 상태(As-Is)를 기준으로 작성되었으며, 개선사항(To-Be)은 ADO Work Item으로 관리됩니다.*
