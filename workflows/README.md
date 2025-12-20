# Logic App Workflows

Azure Logic Apps 워크플로 정의 파일입니다.

## 📁 파일 구조

```
workflows/
├── security-blog-summarizer.json   # 보안 블로그 자동 요약 워크플로
└── README.md                        # 이 파일
```

## 📋 워크플로 구조

### security-blog-summarizer.json

Microsoft Security Blog RSS 피드를 읽고 Azure OpenAI로 요약하여 이메일로 발송하는 워크플로입니다.

#### 1. Trigger (트리거)

- **Type**: Recurrence (일정)
- **Frequency**: 매일
- **Schedule**: 09:00 KST (Korea Standard Time)
- **Purpose**: 매일 아침 자동 실행

#### 2. Actions (액션)

| 순서 | 액션 이름 | 유형 | 설명 |
|-----|----------|------|------|
| 1 | List_all_RSS_feed_items | API Connection (RSS) | Microsoft Security Blog RSS 피드 읽기 (지난 24시간) |
| 2 | Condition_Check_New_Posts | Condition | 새 게시물 존재 여부 확인 |
| 3 | For_each_RSS_Item | For each | 각 게시물 반복 처리 |
| 4 | Try_Summarize_and_Send | Scope (Try) | 요약 및 이메일 발송 (에러 처리) |
| 5 | HTTP_Call_Azure_OpenAI | HTTP | Azure OpenAI GPT-4 API 호출 (Managed Identity) |
| 6 | Send_an_email_(V2) | API Connection (Office 365) | HTML 이메일 발송 |
| 7 | Catch_Errors | Scope (Catch) | 에러 발생 시 알림 이메일 발송 |

#### 3. 에러 처리

- **Try-Catch 패턴**: Scope를 사용한 구조화된 에러 처리
- **재시도 정책**: HTTP 액션에 Exponential Backoff 적용
  - Count: 3회
  - Interval: 10초 → 최대 1분
- **에러 알림**: 실패 시 관리자에게 즉시 이메일 발송

#### 4. 보안 기능

- ✅ **Managed Identity**: OpenAI API 호출 시 인증 (API 키 하드코딩 방지)
- ✅ **Parameters**: 민감 정보 (이메일, 엔드포인트) 외부 파라미터화
- ✅ **API Connection**: Office 365, RSS 연결 분리 관리

## 🚀 배포 방법

### 1. Bicep 템플릿으로 배포

```bash
# 1. Bicep으로 Logic App 리소스 생성
az deployment group create \
  --resource-group rg-security-blog-automation-dev \
  --template-file ../infra/bicep/main.bicep \
  --parameters @../infra/bicep/parameters.dev.json

# 2. 워크플로 정의 업로드
az logic workflow update \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --definition @security-blog-summarizer.json
```

### 2. Azure Portal에서 Import

1. Azure Portal → Logic Apps → [Your Logic App] 열기
2. **Logic app designer** 클릭
3. **Code view** 클릭
4. `security-blog-summarizer.json` 내용 붙여넣기
5. **Save** 클릭

### 3. Parameters 설정

Logic App에서 다음 Parameters를 설정해야 합니다:

| Parameter | 예시 값 | 설명 |
|-----------|--------|------|
| `openAiEndpoint` | `https://your-openai.openai.azure.com/` | Azure OpenAI 엔드포인트 |
| `openAiDeploymentName` | `gpt-4` | GPT-4 배포 이름 |
| `emailRecipient` | `your-email@example.com` | 이메일 수신자 |
| `rssFeedUrl` | `https://www.microsoft.com/en-us/security/blog/feed/` | RSS 피드 URL |

## 🔧 배포 후 설정

### 1. API Connections 인증

#### Office 365 Outlook

```bash
# Azure Portal에서 수동 인증 필요
# 1. API Connections > office365-dev-security-blog-automation 열기
# 2. "Edit API connection" 클릭
# 3. "Authorize" 버튼 클릭
# 4. Microsoft 계정으로 로그인
```

#### RSS

- 별도 인증 불필요 (공개 피드)

### 2. Managed Identity 권한 부여

Logic App의 Managed Identity에 Azure OpenAI 리소스 접근 권한을 부여해야 합니다.

```bash
# 1. Logic App Managed Identity Principal ID 확인
PRINCIPAL_ID=$(az logic workflow show \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --query identity.principalId -o tsv)

# 2. Azure OpenAI 리소스에 역할 할당
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Cognitive Services OpenAI User" \
  --scope /subscriptions/{subscription-id}/resourceGroups/{openai-rg}/providers/Microsoft.CognitiveServices/accounts/{openai-name}
```

## 🧪 테스트

### 1. 수동 실행

```bash
# Azure Portal에서 Run 버튼 클릭
# 또는 Azure CLI
az logic workflow run trigger \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --trigger-name Recurrence
```

### 2. Run History 확인

```bash
# 최근 실행 이력 조회
az logic workflow run list \
  --resource-group rg-security-blog-automation-dev \
  --workflow-name logic-dev-security-blog-automation \
  --top 5 \
  --query "[].{RunId:name, Status:status, StartTime:startTime}" \
  --output table
```

### 3. 테스트 시나리오

- ✅ **정상 실행**: RSS 피드에 새 게시물이 있을 때
- ✅ **빈 결과**: RSS 피드에 새 게시물이 없을 때 (Terminate)
- ✅ **OpenAI 에러**: API 키 오류 또는 Rate Limit → 재시도 3회 → 실패 시 에러 이메일
- ✅ **이메일 발송 실패**: Office 365 연결 오류 → 에러 이메일

## 📊 모니터링

### 주요 메트릭

- **Run Success Rate**: 성공률 (목표: 95% 이상)
- **Run Duration**: 평균 실행 시간 (목표: 30초 이내)
- **Billable Actions**: 실행당 액션 수 (예상: 6-10개)
- **OpenAI Token Usage**: 요약당 토큰 사용량 (목표: 500 tokens 이하)

### Application Insights 쿼리

```kusto
// Logic App 실행 성공률
AzureDiagnostics
| where ResourceType == "WORKFLOWS"
| summarize 
    Total = count(),
    Succeeded = countif(status_s == "Succeeded"),
    Failed = countif(status_s == "Failed")
| extend SuccessRate = (Succeeded * 100.0) / Total
```

## 💰 비용 예상

### 실행당 비용

| 항목 | 수량 | 단가 | 비용 |
|-----|------|------|------|
| Logic App Actions | ~8개/실행 | $0.000025/액션 | $0.0002 |
| Azure OpenAI (GPT-4) | ~300 tokens | $0.00003/token | $0.009 |
| **실행당 총계** | | | **$0.0092** |

### 월간 비용 (매일 5개 게시물 가정)

- Logic App: $0.0002 × 8 × 5 × 30 = **$0.24/월**
- Azure OpenAI: $0.009 × 5 × 30 = **$1.35/월**
- **월 총계**: **$1.59/월**

## 🔒 보안 체크리스트

- ✅ Managed Identity 사용 (OpenAI API)
- ✅ API 키 하드코딩 방지 (Parameters 활용)
- ✅ Office 365 OAuth 인증
- ✅ 재시도 정책으로 일시적 오류 대응
- ✅ 에러 알림으로 장애 인지
- ✅ 민감 정보 로그 제외

## 📚 참고 자료

- [Logic Apps 워크플로 정의 언어](https://learn.microsoft.com/azure/logic-apps/logic-apps-workflow-definition-language)
- [Logic Apps 에러 처리](https://learn.microsoft.com/azure/logic-apps/logic-apps-exception-handling)
- [Azure OpenAI Chat Completions](https://learn.microsoft.com/azure/openai/how-to/chatgpt)
- [Logic Apps Managed Identity](https://learn.microsoft.com/azure/logic-apps/create-managed-service-identity)
