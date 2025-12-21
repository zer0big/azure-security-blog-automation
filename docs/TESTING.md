# Testing Guide

Azure Logic Apps 보안 블로그 자동 요약 시스템의 테스트 가이드입니다.

## 📋 목차

- [테스트 환경 설정](#테스트-환경-설정)
- [단위 테스트](#단위-테스트)
- [통합 테스트](#통합-테스트)
- [성능 테스트](#성능-테스트)
- [보안 테스트](#보안-테스트)
- [트러블슈팅](#트러블슈팅)

## 🔧 테스트 환경 설정

### 1. 사전 준비

```bash
# Azure CLI 로그인
az login
az account set --subscription {subscription-id}

# Resource Group 확인
az group show --name rg-security-blog-automation-dev
```

### 2. 테스트 데이터 준비

- **RSS 피드**: Microsoft Security Blog RSS URL
- **테스트 이메일**: 수신 가능한 이메일 주소
- **Azure OpenAI**: GPT-4 배포 및 API 키

### 3. Logic App 상태 확인

```bash
# Logic App 상태 확인
az logic workflow show \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --query "state" -o tsv

# 출력: Enabled
```

## 🧪 단위 테스트

### 1. RSS 피드 읽기 테스트

**목적**: RSS 피드에서 최신 게시물을 정상적으로 읽어오는지 확인

**절차**:
1. Azure Portal → Logic App Designer 열기
2. "List all RSS feed items" 액션 선택
3. "Test" 버튼 클릭
4. Run History에서 Output 확인

**예상 결과**:
```json
{
  "statusCode": 200,
  "body": [
    {
      "title": "Security Update: ...",
      "publishDate": "2025-12-20T...",
      "primaryLink": "https://...",
      "summary": "..."
    }
  ]
}
```

**검증 항목**:
- ✅ `statusCode`: 200
- ✅ `body`: 배열 형태
- ✅ `title`, `publishDate`, `primaryLink` 존재
- ✅ 지난 24시간 내 게시물만 포함

### 2. Azure OpenAI 요약 테스트

**목적**: GPT-4가 게시물을 한국어로 정상 요약하는지 확인

**절차**:
1. Logic App Designer → "HTTP_Call_Azure_OpenAI" 액션
2. Test 버튼으로 샘플 게시물 입력
3. Response 확인

**테스트 입력**:
```json
{
  "messages": [
    {
      "role": "system",
      "content": "당신은 보안 전문가입니다. 제공된 보안 블로그 게시물을 한국어로 3-5문장으로 요약해주세요."
    },
    {
      "role": "user",
      "content": "제목: Microsoft Security Update\n\n내용: This article describes..."
    }
  ],
  "max_tokens": 500,
  "temperature": 0.3
}
```

**예상 결과**:
```json
{
  "choices": [
    {
      "message": {
        "content": "마이크로소프트가 새로운 보안 업데이트를 발표했습니다. ..."
      }
    }
  ],
  "usage": {
    "total_tokens": 350
  }
}
```

**검증 항목**:
- ✅ 한국어 요약 생성
- ✅ 3-5문장 길이
- ✅ 토큰 사용량 500 이하
- ✅ 응답 시간 5초 이내

### 3. 이메일 발송 테스트

**목적**: Office 365로 HTML 이메일이 정상 발송되는지 확인

**절차**:
1. Logic App Designer → "Send an email (V2)" 액션
2. Test 버튼으로 샘플 이메일 발송
3. 수신 확인

**테스트 입력**:
```json
{
  "To": "test@example.com",
  "Subject": "[Test] Security Alert",
  "Body": "<html><body><h2>테스트 이메일</h2></body></html>",
  "Importance": "Normal"
}
```

**검증 항목**:
- ✅ 이메일 수신 확인 (5분 이내)
- ✅ HTML 형식 정상 렌더링
- ✅ 헤더 색상 가시성 (밝은 파란색 배경 + Azure 파란색 텍스트)
- ✅ 제목, 본문, 링크 포함
- ✅ 스팸 폴더가 아닌 받은편지함
- ✅ 소스 배지 표시 (Multi-RSS 적용 시)

### 4. 자동화 테스트 스크립트 ⭐ NEW

**목적**: 전체 워크플로를 자동으로 테스트

**스크립트**: `test-blue-header.ps1`

**기능**:
1. ProcessedPosts 테이블 정리 (10개 엔티티 삭제)
2. Logic App 워크플로 REST API 트리거
3. 30초 대기
4. 최신 실행 상태 확인
5. 컬러 코딩된 결과 출력

**사용법**:
```powershell
# 저장소 루트 디렉토리에서 실행
.\test-blue-header.ps1
```

**예상 출력**:
```
⏳ Deleting 10 entities from ProcessedPosts table...
✓ Deleted: 1tVUus8OgEyjLQSgO4v-YJXXERG_80w4sYLN11WzzSM
✓ Deleted: 4PW-DokHGTP_e0jvgstySNFCWhunXS6l0SzJhe6iBKo
...

✅ Table cleaned! Triggering workflow...
⏳ Waiting 30 seconds for workflow to complete...

Latest Run:
  Name: 08584352749674094743258665769CU01
  Status: Succeeded
  Start: 2025-12-21T15:45:18Z
  End: 2025-12-21T15:45:36Z

✅ Workflow succeeded! Check your email for the new blue header!
```

**검증 항목**:
- ✅ 모든 엔티티 삭제 성공
- ✅ 워크플로 트리거 성공
- ✅ 실행 상태: Succeeded
- ✅ 이메일 수신 확인

**효과**:
- 수동 테스트 12단계 → 자동 1단계
- 테스트 시간: ~5분 → ~1분
- 일관된 테스트 절차

## 🔗 통합 테스트

### 시나리오 1: 정상 실행 (새 게시물 있음)

**목적**: RSS → OpenAI → Email 전체 흐름 검증

**절차**:
1. Azure Portal → Logic App → "Run Trigger" 클릭
2. Run History 모니터링 (30-60초 대기)
3. 각 단계 상태 확인
4. 이메일 수신 확인

**예상 결과**:
```
Step 1: List_all_RSS_feed_items → Succeeded (2초)
Step 2: Condition_Check_New_Posts → Succeeded (0.5초)
Step 3: For_each_RSS_Item → Succeeded (20초)
  └─ Try_Summarize_and_Send → Succeeded
      ├─ HTTP_Call_Azure_OpenAI → Succeeded (5초)
      └─ Send_an_email_(V2) → Succeeded (3초)
```

**검증 항목**:
- ✅ 전체 실행 시간: 30초 이내
- ✅ 모든 단계 Succeeded
- ✅ 이메일 수신 (HTML 형식)
- ✅ 요약 품질 확인 (3-5문장, 한국어)

### 시나리오 2: 새 게시물 없음

**목적**: RSS 피드가 비어있을 때 정상 종료 확인

**절차**:
1. RSS 피드 URL을 테스트용으로 변경 (빈 피드)
2. Logic App 수동 실행
3. Run History 확인

**예상 결과**:
```
Step 1: List_all_RSS_feed_items → Succeeded
Step 2: Condition_Check_New_Posts → Succeeded
  └─ Terminate_No_New_Posts → Succeeded
Status: Succeeded (Message: "새 게시물이 없습니다.")
```

**검증 항목**:
- ✅ Condition이 False로 분기
- ✅ Terminate 액션 실행
- ✅ 이메일 미발송
- ✅ 전체 상태 Succeeded

### 시나리오 3: OpenAI API 에러 (429 Rate Limit)

**목적**: API Rate Limit 발생 시 재시도 정책 검증

**절차**:
1. OpenAI API 키를 잘못된 값으로 변경
2. Logic App 수동 실행
3. Run History에서 재시도 확인

**예상 결과**:
```
Try 1: HTTP_Call_Azure_OpenAI → Failed (401)
  └─ Wait 10 seconds (Exponential Backoff)
Try 2: HTTP_Call_Azure_OpenAI → Failed (401)
  └─ Wait 20 seconds
Try 3: HTTP_Call_Azure_OpenAI → Failed (401)
  └─ Catch_Errors → Send_Error_Notification → Succeeded
```

**검증 항목**:
- ✅ 재시도 3회 실행
- ✅ Exponential Backoff 간격 (10초 → 20초 → 40초)
- ✅ Catch 블록 실행
- ✅ 에러 알림 이메일 발송

### 시나리오 4: Office 365 연결 오류

**목적**: Office 365 API Connection 만료 시 동작 확인

**절차**:
1. Azure Portal → API Connections → office365 연결 삭제
2. Logic App 수동 실행
3. Run History 확인

**예상 결과**:
```
HTTP_Call_Azure_OpenAI → Succeeded
Send_an_email_(V2) → Failed (Unauthorized)
  └─ Catch_Errors → Send_Error_Notification → Failed
```

**검증 항목**:
- ✅ Send email 액션 실패
- ✅ 에러 로그 기록
- ✅ Run History에 에러 메시지 표시

## 📊 성능 테스트

### 1. 부하 테스트 (동시 실행)

**목적**: 여러 게시물 동시 처리 시 성능 확인

**절차**:
1. RSS 피드에 10개 이상 게시물 존재하도록 설정
2. Logic App 수동 실행
3. Run History 확인

**측정 항목**:
- **전체 실행 시간**: 목표 60초 이내
- **게시물당 처리 시간**: 평균 5-7초
- **동시 실행 수**: For each concurrency = 1 (순차 처리)
- **Billable Actions**: 게시물당 6-8개

**성능 기준**:
| 게시물 수 | 예상 실행 시간 | Billable Actions |
|----------|--------------|------------------|
| 1개 | 10초 | 6개 |
| 5개 | 35초 | 30개 |
| 10개 | 65초 | 60개 |

### 2. 토큰 사용량 측정

**목적**: Azure OpenAI 토큰 비용 최적화

**절차**:
1. Application Insights → Logs
2. 다음 쿼리 실행:

```kusto
traces
| where message contains "OpenAI"
| extend tokens = toint(customDimensions.total_tokens)
| summarize 
    AvgTokens = avg(tokens),
    MaxTokens = max(tokens),
    TotalTokens = sum(tokens)
| project AvgTokens, MaxTokens, TotalTokens
```

**최적화 기준**:
- 평균 토큰: 300-400 tokens
- 최대 토큰: 500 tokens 이하
- 비용: $0.009/요약 (GPT-4 기준)

## 🔒 보안 테스트

### 1. Managed Identity 검증

**목적**: Logic App이 Managed Identity로 OpenAI 접근하는지 확인

**절차**:
1. Logic App → Identity → Status: On 확인
2. Azure OpenAI → Access Control (IAM) → Role assignments 확인
3. Logic App Principal ID에 "Cognitive Services OpenAI User" 역할 부여 확인

**검증 방법**:
```bash
# Logic App Principal ID 확인
PRINCIPAL_ID=$(az logic workflow show \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --query identity.principalId -o tsv)

# Role Assignment 확인
az role assignment list \
  --assignee $PRINCIPAL_ID \
  --query "[?roleDefinitionName=='Cognitive Services OpenAI User'].{Role:roleDefinitionName, Scope:scope}" \
  --output table
```

### 2. API 키 하드코딩 검사

**목적**: 코드에 민감 정보가 하드코딩되지 않았는지 확인

**검증 항목**:
- ✅ OpenAI API 키: Parameters로 관리
- ✅ 이메일 주소: Parameters로 관리
- ✅ RSS URL: Parameters로 관리
- ✅ 워크플로 JSON에 민감 정보 없음

**자동 검사**:
```bash
# Git History에서 민감 정보 검색
git log -p | grep -i "api.*key"
git log -p | grep -i "password"
```

### 3. 진단 로그 확인

**목적**: Application Insights에 민감 정보가 로깅되지 않는지 확인

**절차**:
1. Application Insights → Logs
2. 다음 쿼리 실행:

```kusto
traces
| where message contains "API" or message contains "Key"
| project timestamp, message
| take 10
```

**검증 항목**:
- ✅ API 키 노출 없음
- ✅ 개인정보 (이메일 본문) 마스킹
- ✅ 민감한 HTTP Headers 제외

## 🛠️ 트러블슈팅

### 문제 1: RSS 피드 읽기 실패

**증상**:
```
Error: The request failed with status code '404'
```

**원인**:
- RSS URL 오류
- 네트워크 연결 문제
- RSS 피드 서비스 장애

**해결**:
```bash
# RSS URL 유효성 검사
curl -I https://www.microsoft.com/en-us/security/blog/feed/

# Logic App에서 URL 확인
az logic workflow show \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --query "definition.parameters.rssFeedUrl.defaultValue"
```

### 문제 2: OpenAI API 429 에러

**증상**:
```
Error: Rate limit reached for requests
```

**원인**:
- OpenAI API Quota 초과
- 너무 빈번한 요청

**해결**:
1. Azure Portal → Azure OpenAI → Quotas 확인
2. 재시도 간격 증가:
   ```json
   "retryPolicy": {
     "type": "exponential",
     "interval": "PT30S",  // 10초 → 30초로 증가
     "maximumInterval": "PT2M"
   }
   ```

### 문제 3: 이메일 미수신

**증상**:
- Logic App Run Succeeded
- 이메일 수신되지 않음

**원인**:
- 스팸 폴더 분류
- Office 365 연결 만료
- 이메일 주소 오류

**해결**:
1. 스팸 폴더 확인
2. Office 365 API Connection 재인증:
   ```bash
   # Azure Portal → API Connections → office365 → Edit → Authorize
   ```
3. 이메일 주소 확인:
   ```bash
   az logic workflow show \
     --resource-group rg-security-blog-automation-dev \
     --name logic-dev-security-blog-automation \
     --query "definition.parameters.emailRecipient.defaultValue"
   ```

### 문제 4: 워크플로 실행 시간 초과

**증상**:
```
Error: Workflow run time exceeded the maximum allowed time
```

**원인**:
- For each 루프에서 너무 많은 게시물 처리
- OpenAI API 응답 지연

**해결**:
1. RSS 피드 필터 강화 (지난 24시간 → 12시간)
2. For each concurrency 증가 (순차 → 동시 2개):
   ```json
   "runtimeConfiguration": {
     "concurrency": {
       "repetitions": 2
     }
   }
   ```

## 📚 참고 자료

- [Logic Apps 테스트 가이드](https://learn.microsoft.com/azure/logic-apps/test-logic-apps-mock-data-static-results)
- [Logic Apps 모니터링](https://learn.microsoft.com/azure/logic-apps/monitor-logic-apps)
- [Application Insights 쿼리](https://learn.microsoft.com/azure/azure-monitor/logs/get-started-queries)
- [Azure OpenAI Rate Limits](https://learn.microsoft.com/azure/ai-services/openai/quotas-limits)

---

**작성자**: Kim Young Dae (zer0big)  
**최종 업데이트**: 2025-12-20
