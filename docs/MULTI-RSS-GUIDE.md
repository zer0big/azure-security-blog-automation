# 다중 RSS 피드 지원 확장 가이드

## ✅ 완료된 작업

### 1. 헤더 색상 문제 해결
- **변경 전**: 흰색 텍스트 (`#fff`) → 보이지 않음
- **변경 후**: Azure 파란색 (`#0078d4`) → 명확하게 보임
- **배경**: 밝은 파란색 gradient (`#e3f2fd → #bbdefb`)
- **테두리**: 3px 파란색 하단 테두리 추가

### 2. 다중 RSS 지원 구조 구현
새로운 워크플로 파일: `security-blog-multi-rss.json`

**주요 변경사항**:
- ✅ `rssFeedUrl` (단일) → `rssFeedUrls` (배열)로 변경
- ✅ 2중 ForEach 루프: RSS 피드별 → 아이템별
- ✅ SourceName 추가로 출처 구분
- ✅ Function에 SourceName 표시 기능 추가 (파란 배지)

## 📋 다중 RSS 추가 방법

### Option 1: 기존 단일 RSS 유지 (현재 운영 중)
**파일**: `security-blog-consolidated.json`
- 하나의 RSS 피드만 처리
- 안정적이고 검증됨
- 현재 배포된 상태

### Option 2: 다중 RSS로 전환 (새로운 구조)
**파일**: `security-blog-multi-rss.json`

#### 2-1. 워크플로 배포
```powershell
# 1. 현재 Logic App 속성 가져오기
az rest --method get `
  --uri "https://management.azure.com/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-security-blog-automation-dev/providers/Microsoft.Logic/workflows/logic-dev-security-blog-automation?api-version=2019-05-01" `
  --query "{id:id, name:name, location:location, properties:properties}" > current-props.json

# 2. 다중 RSS 워크플로 정의 적용
$props = Get-Content -Path current-props.json -Raw | ConvertFrom-Json
$workflow = Get-Content -Path workflows\security-blog-multi-rss.json -Raw | ConvertFrom-Json
$props.properties.definition = $workflow

# 3. 전체 payload 생성 및 배포
$fullPayload = @{
    location = $props.location
    properties = $props.properties
} | ConvertTo-Json -Depth 100 -Compress

$fullPayload | Out-File -FilePath full-deploy-multi.json -Encoding UTF8

az rest --method put `
  --uri "$($props.id)?api-version=2019-05-01" `
  --body '@full-deploy-multi.json'
```

#### 2-2. RSS 피드 추가/수정
Logic App Parameters에서 `rssFeedUrls` 배열 수정:

```json
{
  "rssFeedUrls": {
    "value": [
      {
        "url": "https://www.microsoft.com/en-us/security/blog/feed/",
        "sourceName": "Microsoft Security"
      },
      {
        "url": "https://azure.microsoft.com/en-us/blog/topics/security/feed/",
        "sourceName": "Azure Security"
      },
      {
        "url": "https://techcommunity.microsoft.com/t5/security-compliance-and-identity/bg-p/MicrosoftSecurityandCompliance/rss",
        "sourceName": "Tech Community"
      }
    ]
  }
}
```

## 🎯 추천 RSS 피드 목록

### Microsoft 보안 관련
1. **Microsoft Security Blog**
   - URL: `https://www.microsoft.com/en-us/security/blog/feed/`
   - 설명: Microsoft 공식 보안 블로그

2. **Azure Security Blog**
   - URL: `https://azure.microsoft.com/en-us/blog/topics/security/feed/`
   - 설명: Azure 보안 관련 업데이트

3. **Microsoft Defender**
   - URL: `https://techcommunity.microsoft.com/t5/microsoft-defender-for-endpoint/bg-p/MicrosoftDefenderATPBlog/rss`
   - 설명: Defender 제품군 업데이트

4. **Microsoft Sentinel**
   - URL: `https://techcommunity.microsoft.com/t5/microsoft-sentinel-blog/bg-p/MicrosoftSentinelBlog/rss`
   - 설명: Sentinel SIEM 업데이트

5. **Security Compliance & Identity**
   - URL: `https://techcommunity.microsoft.com/t5/security-compliance-and-identity/bg-p/MicrosoftSecurityandCompliance/rss`
   - 설명: 보안, 컴플라이언스, ID 관리

### Azure 아키텍처/개발
6. **Azure Updates**
   - URL: `https://azure.microsoft.com/en-us/updates/feed/`
   - 설명: Azure 전체 업데이트

7. **Azure Architecture**
   - URL: `https://azure.microsoft.com/en-us/blog/topics/architecture/feed/`
   - 설명: Azure 아키텍처 모범사례

## 🔄 이메일에서의 표시

다중 RSS 사용 시 각 게시글에 **출처 배지**가 표시됩니다:

```
[Microsoft Security] Microsoft 365 보안 업데이트
[Azure Security] Azure Firewall 새 기능 출시
[Tech Community] Zero Trust 구현 가이드
```

## 📊 현재 상태 요약

| 항목 | 단일 RSS (현재) | 다중 RSS (준비완료) |
|------|----------------|-------------------|
| **워크플로** | `security-blog-consolidated.json` | `security-blog-multi-rss.json` |
| **배포상태** | ✅ 운영 중 | ⏳ 대기 (파일 준비됨) |
| **RSS 개수** | 1개 | 무제한 |
| **출처 표시** | ❌ 없음 | ✅ 배지로 표시 |
| **확장성** | 제한적 | 매우 높음 |
| **복잡도** | 낮음 | 중간 |

## 🚀 다음 단계 (사용자 선택)

### 시나리오 A: 현재 유지
- 아무 작업 불필요
- 단일 RSS로 안정적 운영
- 필요 시 나중에 전환 가능

### 시나리오 B: 다중 RSS 전환
1. 위 배포 스크립트 실행
2. RSS 피드 목록 결정 (위 추천 목록 참조)
3. Logic App Parameters 업데이트
4. 테스트 실행
5. 정상 확인 후 스케줄 활성화

## ⚠️ 주의사항

### 다중 RSS 사용 시
- **실행 시간 증가**: RSS 피드 개수 × 평균 처리 시간
- **중복 체크**: 각 RSS별로 독립적으로 처리
- **비용**: Action 실행 횟수 증가 (RSS 개수에 비례)
- **권장**: 최대 5-7개 RSS 피드 (성능 최적화)

### Storage Table 구조
- PartitionKey: `{SourceName}-{YYYYMM}` (예: `MicrosoftSecurity-202512`)
- RowKey: SHA256 hash of link
- 자동으로 출처별 파티션 분리

## 📧 이메일 미리보기

### 새로운 디자인
```
┌────────────────────────────────────────┐
│  🔒 Microsoft 보안 블로그 업데이트       │
│       새로운 게시글 15개                 │ ← 파란색 텍스트 (#0078d4)
│  ────────────────────────────────────  │ ← 파란색 배경 (gradient)
│                                        │
│  [Microsoft Security] 제목1             │ ← 파란 배지
│  📅 2025년 12월 22일                    │
│  요약 내용...                           │
│  [전체 글 읽기 →]                       │
│                                        │
│  [Azure Security] 제목2                 │
│  📅 2025년 12월 21일                    │
│  요약 내용...                           │
│  [전체 글 읽기 →]                       │
└────────────────────────────────────────┘
```

## 🔧 트러블슈팅

### Q: 특정 RSS 피드가 작동하지 않는다면?
A: RSS 커넥터에서 해당 URL을 수동으로 테스트해보세요.

### Q: 너무 많은 이메일이 온다면?
A: RSS 피드 개수를 줄이거나 필터링 로직 추가를 고려하세요.

### Q: 출처 배지 색상을 바꾸고 싶다면?
A: `GenerateEmailHtml.cs`의 `sourceTag` 스타일 변경:
```csharp
background: #28a745; // 녹색
background: #dc3545; // 빨간색
background: #ffc107; // 노란색
```

## 📝 변경 이력

- **2025-12-22**: 다중 RSS 지원 구조 구현
- **2025-12-22**: 헤더 색상 파란색으로 변경
- **2025-12-21**: 한글 제목 인코딩 문제 해결
- **2025-12-21**: HTML 태그 제거 기능 추가
- **2025-12-21**: 통합 이메일 기능 구현
