# Azure 인프라 아키텍처 문서

## 📋 문서 정보

- **시스템명**: Azure Security Blog Automation
- **환경**: Development (Korea Central)
- **리소스 그룹**: rg-security-blog-automation-dev
- **버전**: 1.0.0
- **최종 업데이트**: 2025-12-22
- **작성자**: Azure MVP Team
- **구독 ID**: 3864b016-4594-40ad-a96b-4a08ac96b537

---

## 🎯 인프라 개요

### 목적
Microsoft 보안 블로그 자동 요약 시스템을 위한 서버리스 기반 클라우드 인프라

### 주요 특징
- **서버리스 아키텍처**: Logic Apps (Consumption), Azure Functions (Consumption Plan)
- **관리형 서비스**: 인프라 관리 부담 최소화
- **종량제 과금**: 실제 사용량 기반 비용 최적화
- **완전 관리형**: 패치, 업데이트, 스케일링 자동 처리
- **고가용성**: Azure 기본 제공 SLA 99.95%

### 아키텍처 원칙
- **신뢰성**: 재시도 정책, 에러 처리, 모니터링
- **보안**: HTTPS 강제, Managed Identity, 최소 권한
- **비용 최적화**: Consumption 기반, 자동 스케일링
- **운영 우수성**: 태그 전략, 명명 규칙, 로깅
- **성능 효율성**: Application Insights, 병렬 처리

---

## 🏗️ 전체 시스템 아키텍처

### 인프라 구성도

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    Azure Subscription (3864b016-4594-40ad-a96b-4a08ac96b537) │
│                                                                               │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │  Resource Group: rg-security-blog-automation-dev (Korea Central)       │ │
│  │                                                                          │ │
│  │  ┌──────────────────────────────────────────────────────────────────┐  │ │
│  │  │  Monitoring & Observability Layer                               │  │ │
│  │  │                                                                    │  │ │
│  │  │  ┌────────────────────────┐    ┌───────────────────────────┐    │  │ │
│  │  │  │ Log Analytics Workspace│◄───┤ Application Insights      │    │  │ │
│  │  │  │ log-dev-*              │    │ appi-dev-*                │    │  │ │
│  │  │  │                        │    │                           │    │  │ │
│  │  │  │ SKU: PerGB2018         │    │ Type: Web                 │    │  │ │
│  │  │  │ Retention: 30 days     │    │ Retention: 30 days        │    │  │ │
│  │  │  └────────────────────────┘    └───────────────────────────┘    │  │ │
│  │  └──────────────────────────────────────────────────────────────────┘  │ │
│  │                                                                          │ │
│  │  ┌──────────────────────────────────────────────────────────────────┐  │ │
│  │  │  Compute Layer (Serverless)                                      │  │ │
│  │  │                                                                    │  │ │
│  │  │  ┌──────────────────────────────────────────────────────────┐    │  │ │
│  │  │  │  Logic App (Consumption)                                 │    │  │ │
│  │  │  │  logic-dev-security-blog-automation                      │    │  │ │
│  │  │  │                                                            │    │  │ │
│  │  │  │  • State: Enabled                                         │    │  │ │
│  │  │  │  • Trigger: Recurrence (Daily 7:00 AM KST)               │    │  │ │
│  │  │  │  • Workflow: 18 actions                                   │    │  │ │
│  │  │  │  • Connections: RSS, Office 365                           │    │  │ │
│  │  │  └────────────────────┬─────────────────────────────────────┘    │  │ │
│  │  │                       │                                            │  │ │
│  │  │                       ▼                                            │  │ │
│  │  │  ┌──────────────────────────────────────────────────────────┐    │  │ │
│  │  │  │  Azure Functions (.NET 8)                                │    │  │ │
│  │  │  │  func-dev-security-blog-automation                       │    │  │ │
│  │  │  │                                                            │    │  │ │
│  │  │  │  Functions:                                               │    │  │ │
│  │  │  │  ├─ CheckDuplicate (POST /api/CheckDuplicate)            │    │  │ │
│  │  │  │  ├─ SummarizePost (POST /api/SummarizePost)              │    │  │ │
│  │  │  │  ├─ GenerateEmailHtml (POST /api/GenerateEmailHtml)      │    │  │ │
│  │  │  │  └─ InsertProcessed (POST /api/InsertProcessed)          │    │  │ │
│  │  │  │                                                            │    │  │ │
│  │  │  │  Runtime: .NET 8 (Isolated)                               │    │  │ │
│  │  │  │  HTTPS Only: true                                         │    │  │ │
│  │  │  │  Min TLS: 1.2                                             │    │  │ │
│  │  │  └────────────────────┬─────────────────────────────────────┘    │  │ │
│  │  │                       │                                            │  │ │
│  │  │  ┌────────────────────┴─────────────────────────────────────┐    │  │ │
│  │  │  │  App Service Plan (Consumption)                          │    │  │ │
│  │  │  │  plan-dev-security-blog-automation-func                  │    │  │ │
│  │  │  │                                                            │    │  │ │
│  │  │  │  SKU: Y1 (Dynamic/Consumption)                           │    │  │ │
│  │  │  │  Tier: Dynamic                                            │    │  │ │
│  │  │  │  Scaling: Automatic (0-200 instances)                    │    │  │ │
│  │  │  └──────────────────────────────────────────────────────────┘    │  │ │
│  │  └──────────────────────────────────────────────────────────────────┘  │ │
│  │                                                                          │ │
│  │  ┌──────────────────────────────────────────────────────────────────┐  │ │
│  │  │  Storage Layer                                                   │  │ │
│  │  │                                                                    │  │ │
│  │  │  ┌──────────────────────────────────────────────────────────┐    │  │ │
│  │  │  │  Storage Account (General Purpose v2)                    │    │  │ │
│  │  │  │  stdevsecblogauto                                         │    │  │ │
│  │  │  │                                                            │    │  │ │
│  │  │  │  SKU: Standard_LRS                                        │    │  │ │
│  │  │  │  Kind: StorageV2                                          │    │  │ │
│  │  │  │  Encryption: AES-256 (Blob, File)                        │    │  │ │
│  │  │  │                                                            │    │  │ │
│  │  │  │  Services:                                                │    │  │ │
│  │  │  │  ├─ Table Storage                                         │    │  │ │
│  │  │  │  │  └─ ProcessedPosts (중복 체크용)                       │    │  │ │
│  │  │  │  │  └─ AzureFunctionsDiagnosticEvents202512               │    │  │ │
│  │  │  │  ├─ Blob Storage (Functions 코드 저장)                    │    │  │ │
│  │  │  │  ├─ File Storage (Functions 공유)                         │    │  │ │
│  │  │  │  └─ Queue Storage (Functions 트리거)                      │    │  │ │
│  │  │  └──────────────────────────────────────────────────────────┘    │  │ │
│  │  └──────────────────────────────────────────────────────────────────┘  │ │
│  │                                                                          │ │
│  │  ┌──────────────────────────────────────────────────────────────────┐  │ │
│  │  │  Integration Layer (API Connections)                             │  │ │
│  │  │                                                                    │  │ │
│  │  │  ┌────────────────────────┐    ┌───────────────────────────┐    │  │ │
│  │  │  │ RSS Connection         │    │ Office 365 Connection     │    │  │ │
│  │  │  │ rss-dev-*              │    │ office365-dev-*           │    │  │ │
│  │  │  │                        │    │                           │    │  │ │
│  │  │  │ Status: Connected      │    │ Status: Connected         │    │  │ │
│  │  │  │ API: RSS Connector     │    │ User: azure-mvp@...       │    │  │ │
│  │  │  └────────────────────────┘    └───────────────────────────┘    │  │ │
│  │  └──────────────────────────────────────────────────────────────────┘  │ │
│  │                                                                          │ │
│  │  ┌──────────────────────────────────────────────────────────────────┐  │ │
│  │  │  Alerting & Diagnostics                                          │  │ │
│  │  │                                                                    │  │ │
│  │  │  ┌──────────────────────────────────────────────────────────┐    │  │ │
│  │  │  │  Failure Anomalies Smart Detector                        │    │  │ │
│  │  │  │  (Application Insights 자동 생성)                         │    │  │ │
│  │  │  │                                                            │    │  │ │
│  │  │  │  Type: Smart Detector Alert Rule                         │    │  │ │
│  │  │  │  Status: Active                                           │    │  │ │
│  │  │  └──────────────────────────────────────────────────────────┘    │  │ │
│  │  └──────────────────────────────────────────────────────────────────┘  │ │
│  │                                                                          │ │
│  │  ┌──────────────────────────────────────────────────────────────────┐  │ │
│  │  │  Event-Driven Infrastructure                                     │  │ │
│  │  │                                                                    │  │ │
│  │  │  ┌──────────────────────────────────────────────────────────┐    │  │ │
│  │  │  │  Event Grid System Topic                                 │    │  │ │
│  │  │  │  stdevsecblogauto-[guid]                                 │    │  │ │
│  │  │  │                                                            │    │  │ │
│  │  │  │  Source: Storage Account                                 │    │  │ │
│  │  │  │  Topic Type: Microsoft.Storage.StorageAccounts           │    │  │ │
│  │  │  │  Events: Blob/Queue/Table 변경 이벤트                     │    │  │ │
│  │  │  └──────────────────────────────────────────────────────────┘    │  │ │
│  │  └──────────────────────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                                                               │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │  External Dependency: RG-AOAI-AgenticMVP (Korea Central)               │ │
│  │                                                                          │ │
│  │  ┌──────────────────────────────────────────────────────────────────┐  │ │
│  │  │  Azure OpenAI Service                                            │  │ │
│  │  │  aoai-knowledge-base-demo                                        │  │ │
│  │  │                                                                    │  │ │
│  │  │  Kind: AIServices                                                │  │ │
│  │  │  SKU: S0 (Standard)                                              │  │ │
│  │  │                                                                    │  │ │
│  │  │  Deployed Models:                                                │  │ │
│  │  │  ├─ gpt-4o (2024-11-20)                                          │  │ │
│  │  │  │  └─ Capacity: 250K TPM (Global Standard)                     │  │ │
│  │  │  └─ gpt-5.1-chat (2025-11-13)                                    │  │ │
│  │  │     └─ Capacity: 250K TPM (Global Standard)                     │  │ │
│  │  │                                                                    │  │ │
│  │  │  Endpoint: aoai-knowledge-base-demo.cognitiveservices.azure.com │  │ │
│  │  └──────────────────────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘

                          External Services (Microsoft)
                          ┌────────────────────────────┐
                          │ RSS Feeds (3 sources)      │
                          │ • Microsoft Security Blog  │
                          │ • Azure Security Blog      │
                          │ • Threat Intelligence      │
                          └────────────────────────────┘
```

### 네트워크 플로우

```
                     ┌─────────────────┐
                     │  Internet/RSS   │
                     │   Feed Sources  │
                     └────────┬────────┘
                              │
                              ▼
                    ┌───────────────────┐
                    │   Logic App       │
                    │  (Consumption)    │
                    └─────┬──────┬──────┘
                          │      │
          ┌───────────────┘      └───────────────┐
          │                                       │
          ▼                                       ▼
┌──────────────────┐                  ┌────────────────────┐
│ Azure Functions  │◄─────────────────┤  Office 365 API    │
│   (.NET 8)       │                  │   (Email Send)     │
└────┬──────┬──────┘                  └────────────────────┘
     │      │
     │      └──────────────┐
     │                     │
     ▼                     ▼
┌──────────────┐    ┌──────────────────┐
│ Table Storage│    │  Azure OpenAI    │
│ (Duplicate)  │    │    (GPT-4o)      │
└──────────────┘    └──────────────────┘
     │
     ▼
┌──────────────────────┐
│ Application Insights │
│ (Telemetry/Logs)     │
└──────────────────────┘
```

---

## 📦 리소스 상세 정보

### 1. Logic App (Workflow Orchestration)

**리소스 정보**:
- **이름**: `logic-dev-security-blog-automation`
- **타입**: `Microsoft.Logic/workflows`
- **위치**: Korea Central
- **상태**: Enabled
- **SKU**: Consumption (종량제)

**주요 설정**:
```json
{
  "state": "Enabled",
  "tier": "Consumption",
  "accessEndpoint": "https://prod-31.koreacentral.logic.azure.com:443/workflows/5fba9db657bf4a63b2f4c79ef1175b00"
}
```

**네트워크 구성**:
- **Access Endpoint IPs** (수신):
  - 20.249.169.92, 20.249.169.87, 20.249.169.155
  - 20.249.171.205, 4.230.149.190, 20.249.170.248
  - 20.249.171.120, 20.249.171.7

- **Outgoing IPs** (발신):
  - 20.200.202.75, 20.200.231.139, 20.249.169.25
  - 20.249.169.18, 20.249.169.147, 20.249.171.130
  - 4.230.149.189, 20.249.169.207, 20.249.171.17, 20.249.169.238

- **Connector Outgoing IPs** (API Connections):
  - 52.141.1.104, 52.141.36.214
  - 20.44.29.64/27, 52.231.18.208/28
  - 20.200.194.160/27, 20.200.194.192/28
  - 20.196.250.135, 20.196.249.145

**역할**:
- RSS 피드 수집 오케스트레이션
- Azure Functions 호출 조정
- 이메일 발송 트리거
- 워크플로우 상태 관리

**비용**:
- **실행 횟수**: $0.000025/액션 실행
- **예상 월 비용**: ~$3-5 (일 1회 실행 기준)

**참고 문서**: [Logic Apps Pricing](https://azure.microsoft.com/pricing/details/logic-apps/)

---

### 2. Azure Functions (Compute Engine)

**리소스 정보**:
- **이름**: `func-dev-security-blog-automation`
- **타입**: `Microsoft.Web/sites` (Function App)
- **위치**: Korea Central
- **상태**: Running
- **런타임**: .NET 8 (Isolated Worker)
- **호스트**: https://func-dev-security-blog-automation.azurewebsites.net

**주요 설정**:
```json
{
  "kind": "functionapp",
  "state": "Running",
  "httpsOnly": true,
  "netFrameworkVersion": "v8.0",
  "minTlsVersion": "1.2",
  "ftpsState": "Disabled"
}
```

**Functions 목록**:

#### CheckDuplicate
- **경로**: `POST /api/CheckDuplicate`
- **기능**: Table Storage에서 중복 게시물 확인
- **입력**: `{ link, sourceName }`
- **출력**: `{ isDuplicate: boolean }`
- **평균 실행 시간**: ~200ms

#### SummarizePost
- **경로**: `POST /api/SummarizePost`
- **기능**: Azure OpenAI GPT-4o를 통한 3줄 요약 생성
- **입력**: `{ title, content }`
- **출력**: `{ englishSummary: [], koreanSummary: [] }`
- **평균 실행 시간**: ~3-5초 (OpenAI 응답 시간 포함)

#### GenerateEmailHtml
- **경로**: `POST /api/GenerateEmailHtml`
- **기능**: 게시물 배열을 HTML 이메일 템플릿으로 변환
- **입력**: `{ posts: [] }`
- **출력**: `{ subject, html }`
- **평균 실행 시간**: ~500ms

#### InsertProcessed
- **경로**: `POST /api/InsertProcessed`
- **기능**: 처리된 게시물을 Table Storage에 저장
- **입력**: `{ link, title, publishDate, sourceName }`
- **출력**: `{ success: boolean }`
- **평균 실행 시간**: ~150ms

**보안 설정**:
- **HTTPS Only**: Enabled
- **Minimum TLS**: 1.2
- **FTPS**: Disabled
- **IP Restrictions**: Allow All (향후 제한 권장)
- **Authentication**: Function Key 기반

**스케일링**:
- **Scale Limit**: 0-200 instances
- **Auto-scale**: 요청 부하 기반 자동
- **Cold Start**: ~2-3초 (첫 요청 시)

**역할**:
- 비즈니스 로직 처리
- Azure OpenAI API 통합
- Table Storage CRUD 작업
- HTML 템플릿 렌더링

**비용**:
- **실행 시간**: $0.000016/GB-초
- **실행 횟수**: 처음 1백만 건 무료
- **예상 월 비용**: ~$1-2 (일 1회 워크플로우 기준)

**참고 문서**: [Azure Functions Pricing](https://azure.microsoft.com/pricing/details/functions/)

---

### 3. App Service Plan (Hosting Infrastructure)

**리소스 정보**:
- **이름**: `plan-dev-security-blog-automation-func`
- **타입**: `Microsoft.Web/serverFarms`
- **위치**: Korea Central
- **SKU**: Y1 (Dynamic)

**주요 특징**:
```json
{
  "sku": {
    "name": "Y1",
    "tier": "Dynamic",
    "family": "Y",
    "capacity": 0
  },
  "kind": "functionapp"
}
```

**Consumption Plan 특징**:
- **동적 스케일링**: 0-200 인스턴스
- **메모리**: 1.5GB per instance
- **타임아웃**: 5분 (HTTP 트리거)
- **Always On**: 지원 안 함 (Cold Start 발생)
- **과금**: 실행 시간 + 실행 횟수 기반

**역할**:
- Azure Functions 호스팅
- 자동 스케일링 관리
- 리소스 할당 및 격리

**비용**:
- 플랜 자체는 무료 (Consumption)
- Functions 실행 비용만 청구

---

### 4. Storage Account (Data Persistence)

**리소스 정보**:
- **이름**: `stdevsecblogauto`
- **타입**: `Microsoft.Storage/storageAccounts`
- **위치**: Korea Central
- **종류**: StorageV2 (General Purpose v2)
- **SKU**: Standard_LRS (Locally Redundant Storage)

**주요 설정**:
```json
{
  "kind": "StorageV2",
  "sku": "Standard_LRS",
  "encryption": {
    "blob": "AES-256",
    "file": "AES-256"
  }
}
```

**엔드포인트**:
- **Blob**: https://stdevsecblogauto.blob.core.windows.net/
- **Table**: https://stdevsecblogauto.table.core.windows.net/
- **Queue**: https://stdevsecblogauto.queue.core.windows.net/
- **File**: https://stdevsecblogauto.file.core.windows.net/

**Table Storage 스키마**:

#### ProcessedPosts 테이블
```
PartitionKey: sourceName (예: "Microsoft Security Blog")
RowKey: link (URL 해시)
Properties:
  - Title: string
  - PublishDate: datetime
  - ProcessedDate: datetime
  - Link: string
```

**용도**:
- **Table Storage**: 중복 게시물 추적 (ProcessedPosts)
- **Blob Storage**: Functions 배포 패키지 저장
- **Queue Storage**: Functions 트리거 메시지 (향후)
- **File Storage**: Functions 공유 파일 시스템

**데이터 보존**:
- **중복 데이터**: 무제한 (수동 정리 필요)
- **Functions 로그**: 30일 (Application Insights 연동)

**역할**:
- 게시물 중복 체크 데이터 저장
- Functions 코드 및 설정 저장
- 진단 로그 저장

**비용**:
- **Storage**: $0.0184/GB/월 (LRS)
- **Transactions**: $0.00036/10K (Table)
- **예상 월 비용**: ~$0.50-1 (소량 데이터)

**참고 문서**: [Storage Pricing](https://azure.microsoft.com/pricing/details/storage/tables/)

---

### 5. Application Insights (Monitoring & Diagnostics)

**리소스 정보**:
- **이름**: `appi-dev-security-blog-automation`
- **타입**: `Microsoft.Insights/components`
- **위치**: Korea Central
- **Application Type**: Web
- **Retention**: 30 days

**주요 설정**:
```json
{
  "applicationType": "web",
  "kind": "web",
  "retentionInDays": 30,
  "workspaceResourceId": "/subscriptions/.../log-dev-security-blog-automation"
}
```

**수집 데이터**:
- **Traces**: Function 실행 로그
- **Exceptions**: 에러 및 예외 정보
- **Requests**: HTTP 요청/응답 시간
- **Dependencies**: 외부 API 호출 (OpenAI, Table Storage)
- **Custom Events**: 사용자 정의 텔레메트리
- **Performance Counters**: CPU, 메모리 사용량

**주요 메트릭**:
- **Function Execution Count**: 일별 실행 횟수
- **Function Execution Duration**: 평균/P95 실행 시간
- **Failure Rate**: 실패율 (%)
- **Dependency Call Duration**: OpenAI API 응답 시간

**알림 규칙**:
- **Failure Anomalies**: 실패율 급증 시 자동 알림
- **Smart Detection**: AI 기반 이상 패턴 감지

**역할**:
- Function 성능 모니터링
- 에러 추적 및 진단
- 사용자 지정 메트릭 수집
- KPI 대시보드 제공

**비용**:
- **Ingestion**: 처음 5GB/월 무료
- **추가 데이터**: $2.30/GB
- **예상 월 비용**: ~$0 (무료 한도 내)

**참고 문서**: [Application Insights Pricing](https://azure.microsoft.com/pricing/details/monitor/)

---

### 6. Log Analytics Workspace (Centralized Logging)

**리소스 정보**:
- **이름**: `log-dev-security-blog-automation`
- **타입**: `Microsoft.OperationalInsights/workspaces`
- **위치**: Korea Central
- **SKU**: PerGB2018 (Pay-as-you-go)
- **Retention**: 30 days

**주요 설정**:
```json
{
  "sku": "PerGB2018",
  "retentionInDays": 30,
  "publicNetworkAccessForIngestion": "Enabled"
}
```

**데이터 소스**:
- Application Insights 원격 분석
- Azure Functions 진단 로그
- Logic Apps 실행 기록
- Storage Account 메트릭

**쿼리 언어**: Kusto Query Language (KQL)

**샘플 쿼리**:
```kql
// Function 실행 시간 분석
requests
| where cloud_RoleName == "func-dev-security-blog-automation"
| summarize avg(duration), percentile(duration, 95) by name
| order by avg_duration desc

// 에러 발생 빈도
exceptions
| where timestamp > ago(24h)
| summarize count() by type, outerMessage
| order by count_ desc
```

**역할**:
- 통합 로그 저장소
- Application Insights 데이터 백엔드
- KQL 기반 로그 분석
- 장기 보존 옵션

**비용**:
- **Ingestion**: $2.76/GB
- **Retention**: 처음 31일 무료
- **예상 월 비용**: ~$1-2 (소량 로그)

**참고 문서**: [Log Analytics Pricing](https://azure.microsoft.com/pricing/details/monitor/)

---

### 7. API Connections (Managed Connectors)

#### RSS Connection

**리소스 정보**:
- **이름**: `rss-dev-security-blog-automation`
- **타입**: `Microsoft.Web/connections`
- **API**: RSS Connector (Managed)
- **상태**: Connected

**주요 설정**:
```json
{
  "api": {
    "name": "rss",
    "displayName": "RSS",
    "category": "Standard"
  },
  "connectionState": "Enabled",
  "overallStatus": "Connected"
}
```

**기능**:
- RSS 피드 파싱
- 날짜 기반 필터링 (since 파라미터)
- 표준 RSS 2.0 지원

**사용 위치**:
- Logic App: List_RSS_Feed_Items
- Logic App: List_Recent_Items

**비용**: 무료 (Logic App 실행 비용에 포함)

#### Office 365 Outlook Connection

**리소스 정보**:
- **이름**: `office365-dev-security-blog-automation`
- **타입**: `Microsoft.Web/connections`
- **API**: Office 365 Outlook (Managed)
- **인증 사용자**: azure-mvp@zerobig.kr
- **상태**: Connected

**주요 설정**:
```json
{
  "api": {
    "name": "office365",
    "displayName": "Office 365 Outlook"
  },
  "authenticatedUser": {
    "name": "azure-mvp@zerobig.kr"
  },
  "connectionState": "Enabled"
}
```

**기능**:
- 이메일 발송 (HTML 지원)
- 첨부 파일 지원 (미사용)
- 중요도 설정 (Normal/High)

**사용 위치**:
- Logic App: Send_Consolidated_Email

**권한**:
- Mail.Send (위임된 권한)
- Office 365 라이선스 필요

**비용**: 무료 (Logic App 실행 비용에 포함)

---

### 8. Event Grid System Topic (Event-Driven Infrastructure)

**리소스 정보**:
- **이름**: `stdevsecblogauto-bff685b9-6aac-481f-9210-80cde3c5ec19`
- **타입**: `Microsoft.EventGrid/systemTopics`
- **소스**: Storage Account (stdevsecblogauto)
- **Topic Type**: Microsoft.Storage.StorageAccounts

**이벤트 유형**:
- `Microsoft.Storage.BlobCreated`
- `Microsoft.Storage.BlobDeleted`
- `Microsoft.Storage.BlobTierChanged`

**현재 사용**: 없음 (향후 확장 가능)

**잠재적 사용 사례**:
- Functions 배포 패키지 업로드 감지
- Table Storage 변경 알림
- 자동 백업 트리거

**비용**: 무료 (이벤트 구독 없음)

---

### 9. Smart Detector Alert Rule (Intelligent Alerting)

**리소스 정보**:
- **이름**: `Failure Anomalies - appi-dev-security-blog-automation`
- **타입**: `microsoft.alertsmanagement/smartDetectorAlertRules`
- **위치**: Global
- **상태**: Active

**기능**:
- Application Insights 데이터 기반 AI 분석
- 실패율 급증 자동 감지
- 이메일 알림 (Action Group)

**감지 패턴**:
- 평소 대비 실패율 3배 이상 증가
- 새로운 예외 유형 발생
- 의존성 호출 실패 급증

**알림 대상**: 구독 관리자 (기본)

**비용**: 무료 (Application Insights 포함)

---

### 10. Azure OpenAI Service (External Dependency)

**리소스 정보**:
- **이름**: `aoai-knowledge-base-demo`
- **타입**: `Microsoft.CognitiveServices/accounts`
- **위치**: Korea Central
- **종류**: AIServices
- **SKU**: S0 (Standard)
- **리소스 그룹**: RG-AOAI-AgenticMVP *(별도 RG)*

**배포된 모델**:

#### gpt-4o
- **버전**: 2024-11-20
- **용량**: 250,000 TPM (Tokens Per Minute)
- **스케일 타입**: Global Standard
- **용도**: 보안 게시물 요약 생성

#### gpt-5.1-chat
- **버전**: 2025-11-13
- **용량**: 250,000 TPM
- **스케일 타입**: Global Standard
- **용도**: 향후 고도화 예정

**엔드포인트**:
```
https://aoai-knowledge-base-demo.cognitiveservices.azure.com
```

**인증**: API Key 기반 (Function App에서 관리)

**사용 위치**:
- Azure Function: SummarizePost
- Azure Function: (향후) AdvancedAnalysis

**프롬프트 전략**:
```
System: You are an expert security analyst. Summarize security blog posts in 3 concise bullet points.
Temperature: 0.3 (일관성)
Max Tokens: 1000
Response Format: JSON
```

**역할**:
- 보안 게시물 핵심 인사이트 추출
- 영문 → 한글 번역
- 3줄 요약 생성 (각 150자 이내)

**비용**:
- **Input**: $2.50/1M tokens
- **Output**: $10.00/1M tokens
- **예상 월 비용**: ~$5-10 (일 1회 실행, 게시물 10개 기준)

**참고 문서**: [Azure OpenAI Pricing](https://azure.microsoft.com/pricing/details/cognitive-services/openai-service/)

---

## 🔐 보안 아키텍처

### 인증 및 권한

#### Azure Functions
- **인증 방식**: Function Key (Host Key + Function Key)
- **HTTPS 강제**: Enabled
- **Minimum TLS**: 1.2
- **CORS**: 비활성화

**개선 권장사항**:
- [ ] Managed Identity 활성화
- [ ] Key Vault에 Function Key 저장
- [ ] IP 제한 설정 (Logic App IP만 허용)

#### Logic App
- **인증**: Workflow SAS Token
- **커넥션 인증**: OAuth 2.0 (Office 365)
- **트리거 보안**: Shared Access Signature

#### Storage Account
- **인증**: Access Key (Functions에서 사용)
- **암호화**: AES-256 (Blob, File)
- **Public Access**: Disabled (Blob)

**개선 권장사항**:
- [ ] Managed Identity로 전환
- [ ] Firewall 규칙 설정
- [ ] Soft Delete 활성화

### 네트워크 보안

**현재 구성**:
- **Public Endpoint**: 모든 리소스 Public 접근 가능
- **IP Restrictions**: 없음
- **VNet Integration**: 없음

**개선 권장사항**:
- [ ] Private Endpoint 구성 (Storage, Functions)
- [ ] VNet Integration (Functions)
- [ ] Application Gateway (WAF)
- [ ] IP 화이트리스트 적용

### 데이터 보안

**저장 데이터 (Data at Rest)**:
- Storage Account: AES-256 암호화
- Application Insights: 암호화됨
- Log Analytics: 암호화됨

**전송 데이터 (Data in Transit)**:
- HTTPS/TLS 1.2+ 강제
- RSS: HTTPS
- Office 365 API: TLS 1.2
- Azure OpenAI: TLS 1.3

**민감 데이터 관리**:
- Function Keys: Function App 설정 (SecureString)
- OpenAI API Key: Function App 환경 변수
- Storage Connection String: Function App 설정

**개선 권장사항**:
- [ ] Azure Key Vault 통합
- [ ] Customer-Managed Keys (CMK)
- [ ] 감사 로깅 강화

---

## 💰 비용 분석

### 월간 예상 비용 (USD)

| 리소스                  | SKU/Tier         | 예상 비용     | 근거                          |
|-------------------------|------------------|---------------|-------------------------------|
| Logic App               | Consumption      | $3-5          | ~18 액션 × 30일               |
| Azure Functions         | Consumption (Y1) | $1-2          | ~40 실행/일 × 2초             |
| Storage Account         | Standard LRS     | $0.50-1       | ~100MB Table + Blob           |
| Application Insights    | Per GB           | $0            | 무료 한도 내 (~2GB/월)        |
| Log Analytics Workspace | PerGB2018        | $1-2          | ~1GB/월                       |
| Azure OpenAI (gpt-4o)   | S0               | $5-10         | ~100K tokens/일               |
| API Connections         | Standard         | $0            | Logic App 비용 포함           |
| **총 예상 비용**        |                  | **$10.50-20** | 일 1회 실행 기준              |

### 비용 최적화 전략

**현재 최적화**:
- ✅ Consumption 기반 서버리스 (사용량 기반)
- ✅ LRS (Local Redundancy) - 개발 환경 적정
- ✅ 30일 로그 보존 (과도한 보존 방지)
- ✅ Application Insights 샘플링 (100%)

**추가 최적화 가능**:
- [ ] Application Insights Adaptive Sampling (90% 샘플링)
- [ ] Table Storage 오래된 데이터 정리 (>90일)
- [ ] Log Analytics 쿼리 최적화
- [ ] OpenAI 토큰 사용 최소화 (프롬프트 최적화)

### 프로덕션 환경 비용 예측

**가정**:
- 일 3회 실행 (아침/점심/저녁)
- 게시물 평균 15개/일
- GRS (Geo-Redundant Storage)
- 90일 로그 보존

**예상 증가**:
- Logic App: $15-20 (3배 실행)
- Functions: $3-5 (3배 실행)
- Storage: $2-3 (GRS + 증가된 데이터)
- OpenAI: $15-25 (3배 API 호출)
- Monitoring: $5-10 (90일 보존)

**프로덕션 월 비용**: ~$40-63

---

## 📊 성능 및 스케일링

### 현재 성능 메트릭

**Logic App**:
- **실행 시간**: 평균 8-12분
- **성공률**: 98%+
- **동시 실행**: 1 (순차 처리)

**Azure Functions**:
- **Cold Start**: 2-3초
- **Warm Execution**: <500ms (CheckDuplicate, InsertProcessed)
- **AI Summary**: 3-5초 (OpenAI 응답 시간)

**Storage Account**:
- **Table Operations**: <100ms (평균)
- **Throughput**: ~100 ops/분

### 병목 구간

1. **Azure OpenAI API 호출** (가장 느림)
   - 현재: 순차 처리 (1개씩)
   - 개선: 병렬 처리 (3개 동시)
   - 예상 단축: 60% (10분 → 4분)

2. **RSS Feed 조회** (두 번째)
   - 현재: 순차 처리 (1개씩)
   - 개선: 병렬 처리 (3개 동시)
   - 예상 단축: 50%

3. **Cold Start** (간헐적)
   - 현재: ~2-3초 (5분 미사용 후)
   - 개선: Premium Plan ($100/월)
   - 예상 단축: Cold Start 제거

### 스케일링 전략

**Horizontal Scaling** (수평 확장):
- Functions: 자동 스케일 (0-200 인스턴스)
- Logic App: Consumption은 자동 스케일
- Storage: 무제한 스케일

**Vertical Scaling** (수직 확장):
- Functions Premium Plan: 전용 인스턴스, 더 빠른 실행
- Storage Premium: SSD 기반, 낮은 지연 시간

**향후 확장 시나리오**:
- **일 10회 실행**: 현재 아키텍처로 처리 가능
- **100개 RSS 피드**: Storage 증설, Premium Plan 권장
- **실시간 처리**: Event Grid + Durable Functions 검토

---

## 🏷️ 태깅 전략

### 현재 태그 구성

모든 리소스에 공통 적용:
```json
{
  "Environment": "dev",
  "Project": "security-blog-automation",
  "ManagedBy": "Bicep",
  "CreatedDate": "2025-12-21",
  "CostCenter": "Innovation"
}
```

### 태그 용도

- **Environment**: 환경 분리 (dev/test/prod)
- **Project**: 프로젝트별 비용 추적
- **ManagedBy**: IaC 도구 식별
- **CreatedDate**: 리소스 수명 관리
- **CostCenter**: 부서별 차지백

### 권장 추가 태그

```json
{
  "Owner": "azure-mvp@zerobig.kr",
  "Criticality": "Low",
  "DataClassification": "Public",
  "BackupPolicy": "None",
  "ComplianceRequirement": "None"
}
```

---

## 🔄 재해 복구 (DR) 및 백업

### 현재 구성

**백업 정책**: 없음 (개발 환경)

**최근 백업(참고)**: `.backups/backup_2025-12-27_final_5_feeds_with_emoji` 생성됨 — 5개 RSS 피드, 이모지, 복원 가이드 포함. 복원 가이드는 `.backups/backup_2025-12-27_final_5_feeds_with_emoji/RESTORE_GUIDE.md`를 참조하세요.

**데이터 지속성**:
- **Storage Account**: LRS (3 copy, 단일 리전)
- **Application Insights**: 30일 보존
- **Log Analytics**: 30일 보존

**RPO/RTO**:
- **RPO** (Recovery Point Objective): 24시간 (일 1회 실행)
- **RTO** (Recovery Time Objective): 1시간 (수동 재배포)

### 프로덕션 권장사항

**데이터 백업**:
- [ ] Storage Account → GRS (Geo-Redundant)
- [ ] Table Storage 자동 백업 (AzCopy 스크립트)
- [ ] Logic App 정의 Git 버전 관리
- [ ] Functions 코드 Git 버전 관리 (이미 적용)

**재해 복구 절차**:
1. Bicep 템플릿으로 Secondary Region에 재배포
2. Table Storage 백업 복원 (AzCopy)
3. API Connections 재인증
4. DNS/Endpoint 업데이트

**자동화**:
- [ ] Azure Site Recovery (Logic App은 미지원)
- [ ] Automation Runbook (재배포 자동화)
- [ ] Traffic Manager (Multi-region 장애조치)

---

## 📋 운영 체크리스트

### 일일 운영

- [ ] Application Insights 대시보드 확인 (실패율, 실행 시간)
- [ ] Logic App 실행 기록 검토 (성공/실패)
- [ ] 이메일 수신 확인 (azure-mvp@zerobig.kr)

### 주간 운영

- [ ] Table Storage 크기 모니터링 (ProcessedPosts)
- [ ] OpenAI API 비용 확인 (Azure Cost Management)
- [ ] Function 로그 분석 (예외/경고)

### 월간 운영

- [ ] 전체 비용 리뷰 (예산 대비)
- [ ] 보안 업데이트 확인 (.NET, 커넥터)
- [ ] Storage Account 정리 (오래된 데이터 삭제)
- [ ] Application Insights 쿼리 최적화

### 분기별 운영

- [ ] 아키텍처 리뷰 (개선사항 도출)
- [ ] Bicep 템플릿 업데이트 (새 기능 반영)
- [ ] DR 테스트 (재배포 시뮬레이션)
- [ ] 보안 감사 (Azure Advisor, Security Center)

---

## 🔗 참고 자료

### Azure 공식 문서

#### Logic Apps
- [Logic Apps Documentation](https://learn.microsoft.com/azure/logic-apps/)
- [Consumption Pricing](https://azure.microsoft.com/pricing/details/logic-apps/)
- [Limits and Configuration](https://learn.microsoft.com/azure/logic-apps/logic-apps-limits-and-config)

#### Azure Functions
- [Functions Documentation](https://learn.microsoft.com/azure/azure-functions/)
- [.NET 8 Isolated Worker](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide)
- [Consumption Plan](https://learn.microsoft.com/azure/azure-functions/consumption-plan)

#### Storage Account
- [Table Storage](https://learn.microsoft.com/azure/storage/tables/table-storage-overview)
- [Security Best Practices](https://learn.microsoft.com/azure/storage/common/storage-security-guide)

#### Application Insights
- [Application Insights Overview](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview)
- [KQL Reference](https://learn.microsoft.com/azure/data-explorer/kusto/query/)

#### Azure OpenAI
- [Azure OpenAI Service](https://learn.microsoft.com/azure/ai-services/openai/)
- [GPT-4o Model](https://learn.microsoft.com/azure/ai-services/openai/concepts/models#gpt-4o)

### Well-Architected Framework
- [Reliability](https://learn.microsoft.com/azure/well-architected/reliability/)
- [Security](https://learn.microsoft.com/azure/well-architected/security/)
- [Cost Optimization](https://learn.microsoft.com/azure/well-architected/cost-optimization/)
- [Operational Excellence](https://learn.microsoft.com/azure/well-architected/operational-excellence/)
- [Performance Efficiency](https://learn.microsoft.com/azure/well-architected/performance-efficiency/)

### IaC (Infrastructure as Code)
- [Bicep Documentation](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)
- [Bicep Best Practices](https://learn.microsoft.com/azure/azure-resource-manager/bicep/best-practices)

---

## 🔄 버전 히스토리

### v1.0.0 (2024-12-22)

**초기 인프라 배포**:
- Logic App (Consumption)
- Azure Functions (.NET 8)
- Storage Account (Standard LRS)
- Application Insights
- Log Analytics Workspace
- API Connections (RSS, Office 365)
- Azure OpenAI (외부 의존성)

**주요 설정**:
- Region: Korea Central
- Environment: Development
- Managed By: Bicep IaC
- Tagging Strategy: 5 tags

**알려진 제한사항 (As-Is)**:
- Managed Identity: SystemAssigned 활성화됨 (Logic App Identity 확인됨)
- Public Endpoint만 지원 (네트워크 격리 미구성)
- IP 제한 없음
- Key Vault 통합 없음 (Function Key가 여전히 파라미터로 보관됨)
- DR 정책: 백업/복원 절차는 수동이며 자동화 필요

**개선 예정 / 진행중**:
- [WI 147] Key Vault 통합 (진행 예정) — Function Key를 Key Vault로 이관
- [WI 148] 에러 핸들링 개선 (부분 적용됨: retry/timeout 추가)
- [WI TBD] Private Endpoint 구성

---

## 📧 문의

**프로젝트**: Azure Security Blog Automation  
**환경**: Development (Korea Central)  
**리소스 그룹**: rg-security-blog-automation-dev  
**담당자**: Azure MVP Team  
**이메일**: azure-mvp@zerobig.kr  
**ADO 프로젝트**: https://dev.azure.com/azure-mvp/azure-secu-updates-notification

---

*본 문서는 현재 배포된 Azure 인프라의 As-Is 상태를 기준으로 작성되었으며, 개선사항은 ADO Work Item으로 관리됩니다.*
