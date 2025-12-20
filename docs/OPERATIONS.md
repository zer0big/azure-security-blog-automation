# Operations Guide

Azure Logic Apps 보안 블로그 자동 요약 시스템의 운영 가이드입니다.

## 📋 목차

- [일일 운영 절차](#일일-운영-절차)
- [주간 점검](#주간-점검)
- [월간 리뷰](#월간-리뷰)
- [모니터링 설정](#모니터링-설정)
- [Alert 규칙](#alert-규칙)
- [긴급 대응 절차](#긴급-대응-절차)
- [비용 관리](#비용-관리)
- [보안 운영](#보안-운영)

## 📅 일일 운영 절차

### 1. 아침 점검 (09:00 KST)

**자동 실행 확인** (매일 09:00 KST 트리거)

```bash
# 오늘 실행 내역 확인
az monitor activity-log list \
  --resource-group rg-security-blog-automation-prod \
  --start-time $(date -u -d '1 day ago' '+%Y-%m-%dT%H:%M:%SZ') \
  --query "[?contains(resourceId, 'logic-prod-security-blog-automation')].{Time:eventTimestamp, Status:status.value, Operation:operationName.localizedValue}" \
  --output table
```

**예상 결과**:
```
Time                  Status     Operation
2025-12-20 00:00:00  Succeeded  Run workflow
```

**점검 항목**:
- ✅ 워크플로 실행 여부 (Run History)
- ✅ 실행 상태: Succeeded
- ✅ 이메일 수신 확인 (새 게시물 있을 경우)
- ✅ 에러 알림 없음

### 2. Application Insights 대시보드 확인

**Azure Portal → Application Insights → Overview**

**주요 메트릭**:
- **Requests**: 최근 24시간 API 호출 수
- **Failed Requests**: 실패 요청 (목표: 0%)
- **Server Response Time**: 평균 응답 시간 (목표: <5초)
- **Availability**: 가용성 (목표: 99.9%)

**쿼리 예시**:
```kusto
requests
| where timestamp > ago(24h)
| summarize 
    TotalRequests = count(),
    FailedRequests = countif(success == false),
    AvgDuration = avg(duration)
| project TotalRequests, FailedRequests, AvgDuration, FailureRate = (FailedRequests * 100.0 / TotalRequests)
```

### 3. 이메일 품질 확인

**수신된 이메일 검토**:
- ✅ 제목 형식: "[Azure Security] YYYY-MM-DD: X개의 새 보안 업데이트"
- ✅ 요약 품질: 3-5문장, 한국어, 주요 내용 포함
- ✅ 링크 유효성: 원본 블로그 링크 클릭 가능
- ✅ HTML 렌더링: 깔끔한 포맷

**품질 문제 발견 시**:
- GPT-4 프롬프트 튜닝 필요
- `workflows/security-blog-summarizer.json` 수정
- GitHub PR 생성 → Review → Deploy

## 📊 주간 점검 (매주 월요일)

### 1. 실행 통계 분석

**지난 7일 실행 내역**:
```bash
# PowerShell
$startDate = (Get-Date).AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ssZ")
az monitor activity-log list \
  --resource-group rg-security-blog-automation-prod \
  --start-time $startDate \
  --query "[?contains(resourceId, 'logic-prod-security-blog-automation')].{Time:eventTimestamp, Status:status.value}" \
  --output table
```

**분석 항목**:
- 총 실행 횟수: 7회 (매일 1회)
- 성공률: 100% 목표
- 실패 원인 분석 (있을 경우)
- 평균 실행 시간

### 2. 비용 리뷰

**Azure Portal → Cost Management → Cost Analysis**

**예상 월간 비용** (Production):
| 서비스 | 사용량 | 단가 | 월 비용 |
|--------|--------|------|---------|
| Logic Apps | 30회 실행 × 6 actions | $0.000025/action | $0.005 |
| Azure OpenAI (GPT-4) | 30회 × 400 tokens | $0.03/1K tokens | $0.36 |
| Application Insights | 100MB 데이터 | $2.88/GB | $0.29 |
| Log Analytics | 500MB 데이터 | $3.11/GB | $1.56 |
| **Total** | | | **~$2.22/월** |

**비용 최적화**:
- Log Analytics 보관 기간: 30일 → 7일 (개발 환경)
- GPT-4 토큰 제한: max_tokens 500 유지
- Application Insights Sampling: 100% → 50% (트래픽 증가 시)

### 3. 보안 점검

**Managed Identity 역할 검증**:
```bash
PRINCIPAL_ID=$(az logic workflow show \
  --resource-group rg-security-blog-automation-prod \
  --name logic-prod-security-blog-automation \
  --query identity.principalId -o tsv)

az role assignment list \
  --assignee $PRINCIPAL_ID \
  --all \
  --query "[].{Role:roleDefinitionName, Scope:scope}" \
  --output table
```

**예상 역할**:
- Cognitive Services OpenAI User (Azure OpenAI)
- (선택) Reader (Resource Group) - 진단용

**점검 항목**:
- ✅ 불필요한 권한 없음
- ✅ 최소 권한 원칙 준수
- ✅ API Connection 인증 유효

### 4. 워크플로 버전 관리

**GitHub Repository 확인**:
```bash
# 최근 커밋 확인
git log --oneline -10

# 최근 배포 태그 확인
git tag --sort=-creatordate | head -5
```

**배포 이력 검토**:
- GitHub Actions → Workflow runs
- 성공/실패 확인
- 배포 시간 검토

## 📆 월간 리뷰 (매월 1일)

### 1. 성능 벤치마크

**Application Insights → Performance**

**측정 항목**:
- 평균 실행 시간: 목표 30초 이내
- OpenAI API 응답 시간: 목표 5초 이내
- 이메일 발송 시간: 목표 3초 이내
- End-to-End 지연: 목표 60초 이내

**쿼리 예시**:
```kusto
requests
| where timestamp > ago(30d)
| where name == "HTTP_Call_Azure_OpenAI"
| summarize 
    AvgDuration = avg(duration),
    P50Duration = percentile(duration, 50),
    P95Duration = percentile(duration, 95),
    P99Duration = percentile(duration, 99)
| project AvgDuration, P50Duration, P95Duration, P99Duration
```

### 2. 게시물 통계 분석

**이메일 로그 분석**:
```kusto
traces
| where message contains "RSS"
| extend itemCount = toint(customDimensions.itemCount)
| summarize 
    TotalPosts = sum(itemCount),
    AvgPostsPerDay = avg(itemCount),
    MaxPostsPerDay = max(itemCount)
| project TotalPosts, AvgPostsPerDay, MaxPostsPerDay
```

**분석 결과**:
- 월간 총 게시물: 20-30개 예상
- 일일 평균: 1-2개
- 최대 일일 게시물: 5개

### 3. 품질 개선 검토

**요약 품질 평가**:
- 사용자 피드백 수집
- GPT-4 프롬프트 효과성 평가
- 한국어 번역 정확도 검토

**개선 방안**:
1. 프롬프트 튜닝 (예: 기술 용어 번역 강화)
2. 요약 길이 조정 (3-5문장 → 5-7문장)
3. 추가 필드 포함 (CVSS 점수, 영향 제품)

### 4. 아키텍처 리뷰

**Well-Architected Framework 점검**:

**Reliability (신뢰성)**:
- ✅ 재시도 정책: Exponential Backoff 3회
- ✅ Catch 블록: 에러 알림 발송
- ✅ SLA 목표: 99% 달성 여부 확인

**Security (보안)**:
- ✅ Managed Identity 사용
- ✅ API 키 하드코딩 없음
- ✅ HTTPS 통신만 사용

**Cost Optimization (비용 최적화)**:
- ✅ Consumption Plan 사용
- ✅ GPT-4 토큰 제한
- ✅ Log 보관 기간 최소화

**Performance Efficiency (성능 효율성)**:
- ✅ For each 동시성 설정
- ✅ OpenAI API 타임아웃 설정

**Operational Excellence (운영 우수성)**:
- ✅ CI/CD 파이프라인
- ✅ 진단 로깅 활성화
- ✅ 모니터링 대시보드

## 🔔 모니터링 설정

### 1. Application Insights Dashboard

**Azure Portal → Dashboards → New dashboard**

**위젯 구성**:
1. **워크플로 실행 상태**
   ```kusto
   requests
   | where timestamp > ago(7d)
   | summarize Count=count() by bin(timestamp, 1d), resultCode
   | render timechart
   ```

2. **OpenAI API 성능**
   ```kusto
   dependencies
   | where timestamp > ago(7d)
   | where name contains "OpenAI"
   | summarize AvgDuration=avg(duration) by bin(timestamp, 1h)
   | render timechart
   ```

3. **에러 발생 빈도**
   ```kusto
   exceptions
   | where timestamp > ago(24h)
   | summarize Count=count() by problemId
   | top 10 by Count desc
   ```

### 2. Azure Monitor Workbooks

**사전 구성된 Workbook 사용**:
- Logic Apps 성능 분석
- 비용 분석
- 보안 점검

**커스텀 Workbook 생성**:
```json
{
  "version": "Notebook/1.0",
  "items": [
    {
      "type": 3,
      "content": {
        "query": "requests | where timestamp > ago(30d) | summarize count() by bin(timestamp, 1d)"
      }
    }
  ]
}
```

## 🚨 Alert 규칙

### Alert 1: 워크플로 실행 실패

**조건**:
- Logic App Run Status = Failed
- 시간 범위: 5분
- 빈도: 1회 이상

**쿼리**:
```kusto
AzureDiagnostics
| where ResourceProvider == "MICROSOFT.LOGIC"
| where status_s == "Failed"
| where TimeGenerated > ago(5m)
```

**Action Group**:
- 이메일: admin@example.com
- SMS: +82-10-1234-5678 (선택)

**심각도**: Sev 2 (Warning)

### Alert 2: OpenAI API Rate Limit

**조건**:
- HTTP Status Code = 429
- 시간 범위: 15분
- 빈도: 3회 이상

**쿼리**:
```kusto
dependencies
| where name contains "OpenAI"
| where resultCode == "429"
| where timestamp > ago(15m)
| summarize Count=count()
| where Count >= 3
```

**Action Group**:
- 이메일: admin@example.com

**심각도**: Sev 3 (Informational)

### Alert 3: 비용 초과

**조건**:
- 일일 비용 > $0.20
- 월간 비용 > $5.00

**설정**:
1. Azure Portal → Cost Management → Budgets
2. New Budget:
   - Name: security-blog-automation-budget
   - Amount: $5.00/month
   - Alert Threshold: 80%, 100%

**Action**:
- 이메일 알림
- Logic App 일시 중지 (100% 초과 시)

## ⚡ 긴급 대응 절차

### 시나리오 1: 워크플로 연속 실패

**증상**:
- 3회 이상 연속 실패
- Alert 수신

**대응 절차**:
1. **즉시 확인** (5분 이내)
   ```bash
   # Run History 확인
   az logic workflow show \
     --resource-group rg-security-blog-automation-prod \
     --name logic-prod-security-blog-automation \
     --query "accessEndpoint" -o tsv
   # Azure Portal에서 Run History 열기
   ```

2. **원인 분석** (15분 이내)
   - Run History → Failed step 확인
   - Error 메시지 읽기
   - Application Insights → Failures 확인

3. **즉시 조치** (30분 이내)
   - **RSS 피드 오류**: URL 유효성 검사 → 수동 수정
   - **OpenAI API 오류**: Quota 확인 → 일시 중지 또는 Deployment 변경
   - **Office 365 오류**: API Connection 재인증

4. **임시 해결책** (1시간 이내)
   - Logic App 일시 중지: 
     ```bash
     az logic workflow update \
       --resource-group rg-security-blog-automation-prod \
       --name logic-prod-security-blog-automation \
       --state Disabled
     ```
   - 수동 이메일 발송 (필요 시)

5. **근본 원인 해결** (24시간 이내)
   - 코드 수정 → GitHub PR
   - CI/CD 파이프라인 실행
   - 테스트 환경 검증
   - Production 배포

### 시나리오 2: OpenAI API Quota 소진

**증상**:
- HTTP 429 에러 연속 발생
- Alert 수신

**대응 절차**:
1. **Quota 확인**:
   ```bash
   # Azure Portal → Azure OpenAI → Quotas
   # Tokens per minute (TPM) 사용량 확인
   ```

2. **즉시 조치**:
   - Logic App 일시 중지 (추가 비용 방지)
   - Quota 증가 요청 (Azure Support)
   - 대체 Deployment 사용 (있을 경우)

3. **임시 해결책**:
   - 트리거 빈도 조정: 매일 → 2일마다
   - max_tokens 감소: 500 → 300

### 시나리오 3: 보안 사고 (API 키 유출)

**증상**:
- 비정상적인 API 호출 증가
- 예상치 못한 비용 증가

**대응 절차**:
1. **즉시 차단** (5분 이내)
   - Logic App 중지
   - Azure OpenAI API Key Rotation
   - Office 365 API Connection 재인증

2. **영향 범위 분석** (30분 이내)
   - Application Insights → 비정상 요청 추적
   - Cost Management → 비용 급증 확인
   - Git History → 민감 정보 커밋 검색

3. **복구** (2시간 이내)
   - 새 API Key 생성 및 GitHub Secrets 업데이트
   - CI/CD 재실행
   - Managed Identity 재설정 (필요 시)

4. **사후 조치**:
   - 보안 감사 수행
   - Git History Rewrite (민감 정보 제거)
   - 모니터링 강화

### 시나리오 4: Azure 리전 장애

**증상**:
- Logic App 응답 없음
- Azure Portal 접근 불가

**대응 절차**:
1. **Azure Status 확인**:
   - https://status.azure.com/
   - 영향 받는 서비스 및 리전 확인

2. **대기**:
   - Azure 복구 대기 (일반적으로 2-4시간)
   - 사용자에게 상황 공지

3. **장기 장애 시 대응** (4시간 이상):
   - 다른 리전에 임시 배포 (DR 환경)
   - GitHub Actions → Manual deploy (다른 리전)

## 💰 비용 관리

### 1. 비용 모니터링

**일일 비용 확인**:
```bash
# PowerShell
az consumption usage list \
  --start-date $(Get-Date).AddDays(-1).ToString("yyyy-MM-dd") \
  --end-date $(Get-Date).ToString("yyyy-MM-dd") \
  --query "[?contains(instanceName, 'security-blog-automation')].{Service:meterName, Cost:pretaxCost}" \
  --output table
```

**월간 예산 알림 설정**:
- Azure Portal → Cost Management → Budgets
- Budget: $10.00/month
- Alert: 50%, 80%, 100%

### 2. 비용 최적화 팁

**Logic Apps**:
- ✅ Consumption Plan 유지 (Standard Plan 대비 90% 절감)
- ✅ For each 동시성 최소화 (순차 처리 권장)
- ✅ 불필요한 Actions 제거

**Azure OpenAI**:
- ✅ max_tokens 제한: 500 이하
- ✅ temperature 최적화: 0.3 (deterministic)
- ✅ GPT-4o 대신 GPT-4o-mini 사용 고려 (80% 비용 절감)

**Application Insights**:
- ✅ Sampling 활성화: 100% → 50%
- ✅ 데이터 보관: 90일 → 30일
- ✅ Daily Cap 설정: 1GB/day

**Log Analytics**:
- ✅ 보관 기간: 30일 → 7일 (개발 환경)
- ✅ Archive Tier 활용 (장기 보관 데이터)

## 🔐 보안 운영

### 1. 정기 보안 점검 (매월)

**Managed Identity 권한 검토**:
```bash
# 모든 Role Assignment 확인
PRINCIPAL_ID=$(az logic workflow show \
  --resource-group rg-security-blog-automation-prod \
  --name logic-prod-security-blog-automation \
  --query identity.principalId -o tsv)

az role assignment list \
  --assignee $PRINCIPAL_ID \
  --all \
  --output table
```

**불필요한 권한 제거**:
- Contributor, Owner 역할 확인
- 최소 권한 원칙 준수

### 2. API Connection 인증 갱신

**Office 365 Connection**:
- 유효 기간: 일반적으로 90일
- 갱신 방법:
  1. Azure Portal → API Connections → office365
  2. Edit API connection
  3. Authorize → Microsoft 로그인

**주기적 테스트**:
```bash
# Logic App 수동 트리거로 연결 테스트
az rest --method post \
  --uri "https://management.azure.com/subscriptions/{subscription-id}/resourceGroups/rg-security-blog-automation-prod/providers/Microsoft.Logic/workflows/logic-prod-security-blog-automation/triggers/Recurrence/run?api-version=2016-06-01"
```

### 3. 진단 로그 보안

**민감 정보 마스킹**:
- Application Insights에서 개인정보 제거
- 이메일 주소, API 키 로깅 금지

**로그 보관 기간**:
- Production: 30일
- Development: 7일
- 규정 준수 요구 사항 확인

## 📚 운영 체크리스트

### 일일 (매일 09:30)
- [ ] Logic App Run History 확인
- [ ] 이메일 수신 확인
- [ ] Application Insights 대시보드 확인
- [ ] Alert 수신 여부 확인

### 주간 (매주 월요일)
- [ ] 지난 7일 실행 통계 분석
- [ ] 비용 리뷰 (Cost Management)
- [ ] Managed Identity 권한 검토
- [ ] GitHub Repository 업데이트 확인

### 월간 (매월 1일)
- [ ] 성능 벤치마크 측정
- [ ] 게시물 통계 분석
- [ ] Well-Architected Framework 점검
- [ ] 보안 감사 수행
- [ ] API Connection 인증 갱신
- [ ] 비용 최적화 검토

### 분기별 (3개월마다)
- [ ] 아키텍처 리뷰
- [ ] 사용자 피드백 수집
- [ ] GPT-4 프롬프트 개선
- [ ] DR (Disaster Recovery) 테스트
- [ ] 보안 취약점 점검

## 📞 연락처 및 지원

**긴급 연락처**:
- 담당자: Kim Young Dae (zer0big)
- Email: admin@example.com
- GitHub: [@zer0big](https://github.com/zer0big)

**Azure Support**:
- Azure Portal → Help + support
- Support Plan: Basic (포함)

**참고 문서**:
- [Logic Apps 운영 가이드](https://learn.microsoft.com/azure/logic-apps/logic-apps-overview)
- [Application Insights 모니터링](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview)
- [Azure OpenAI 모범 사례](https://learn.microsoft.com/azure/ai-services/openai/how-to/best-practices)

---

**작성자**: Kim Young Dae (zer0big)  
**최종 업데이트**: 2025-12-20
