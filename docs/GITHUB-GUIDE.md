# GitHub 활용 가이드

## 📋 문서 정보

- **프로젝트명**: Azure Security Blog Automation
- **Repository**: https://github.com/zer0big/azure-security-blog-automation
- **가시성**: Public Repository
- **기본 브랜치**: main
- **생성일**: 2025-12-20
- **마지막 업데이트**: 2025-12-22
- **저장소 크기**: 111 KB

---

## 🎯 GitHub 활용 목적

### 1. 소스 코드 버전 관리 (Primary)

**Git 기반 분산 버전 관리**:
- **이력 추적**: 모든 코드 변경 사항의 완전한 이력 보존
- **협업**: 여러 개발자가 동시에 작업 가능
- **브랜치 전략**: 기능 개발, 버그 수정, 릴리스 분리
- **롤백**: 문제 발생 시 이전 버전으로 즉시 복구
- **코드 리뷰**: Pull Request를 통한 동료 검토

### 2. CI/CD 파이프라인 (GitHub Actions)

**완전 자동화된 배포 파이프라인**:
- **Infrastructure as Code**: Bicep 템플릿 자동 배포
- **Continuous Integration**: 코드 푸시 시 자동 빌드 및 검증
- **Continuous Deployment**: 검증 통과 후 Azure 자동 배포
- **Multi-Environment**: dev/prod 환경별 분리 배포
- **Integration Testing**: 배포 후 자동 통합 테스트 실행

### 3. 프로젝트 문서화

**통합 문서 관리**:
- **README.md**: 프로젝트 개요 및 Quick Start 가이드
- **기술 문서**: 아키텍처, API 명세, 운영 가이드
- **코드 주석**: 인라인 문서화 (Markdown, JSDoc)
- **릴리스 노트**: 버전별 변경사항 및 breaking changes
- **이슈 추적**: GitHub Issues를 통한 버그/개선사항 관리

### 4. 협업 플랫폼

**팀 협업 도구**:
- **Pull Requests**: 코드 리뷰 및 토론
- **Issues**: 작업 추적, 버그 보고, 기능 요청
- **Projects**: 칸반 보드 스타일 프로젝트 관리
- **Discussions**: 팀 토론 및 Q&A
- **Wiki**: 상세 문서화 (선택 사항)

### 5. 보안 및 품질 관리

**자동화된 보안 검사**:
- **Dependabot**: 의존성 취약점 자동 스캔 및 PR 생성
- **Code Scanning**: 정적 분석을 통한 보안 취약점 탐지
- **Secret Scanning**: 코드에 포함된 비밀키 자동 감지
- **Branch Protection**: main 브랜치 직접 푸시 방지
- **Required Reviews**: PR 승인 필수화

---

## 📁 Repository 구조

```
azure-security-blog-automation/
├── .github/
│   └── workflows/
│       ├── deploy.yml              # Azure 배포 워크플로
│       ├── deploy.yml.backup       # 백업 워크플로
│       ├── deploy.yml.original     # 원본 워크플로
│       └── README.md               # 워크플로 가이드
├── docs/
│   ├── AZURE-INFRASTRUCTURE-ARCHITECTURE.md  # 인프라 아키텍처 문서
│   ├── LOGIC-APP-ARCHITECTURE.md             # Logic App 상세 문서
│   ├── GITHUB-GUIDE.md                        # 이 문서
│   ├── ADO-UPDATE-GUIDE.md                    # ADO Work Item 업데이트 가이드
│   ├── OPERATIONS.md                          # 운영 가이드
│   ├── TESTING.md                             # 테스트 가이드
│   └── README.md                              # 문서 인덱스
├── functions/
│   ├── CheckDuplicate/            # 중복 체크 Function
│   ├── SummarizePost/             # AI 요약 Function
│   ├── GenerateEmailHtml/         # HTML 생성 Function
│   ├── InsertProcessed/           # Table Storage 저장 Function
│   ├── host.json                  # Functions 호스트 설정
│   ├── local.settings.json        # 로컬 설정 (미포함)
│   └── README.md                  # Functions 가이드
├── infra/
│   └── bicep/
│       ├── main.bicep             # 메인 Bicep 템플릿
│       ├── parameters.dev.json    # 개발 환경 파라미터
│       ├── parameters.prod.json   # 프로덕션 환경 파라미터
│       ├── modules/               # 재사용 가능한 Bicep 모듈
│       └── README.md              # IaC 가이드
├── workflows/
│   ├── security-blog-definition.json          # Logic App 워크플로 정의
│   ├── security-blog-summarizer.json          # 전체 Logic App 구성
│   └── README.md                              # 워크플로 설명
├── .gitignore                     # Git 제외 파일 목록
├── azure.yaml                     # Azure Developer CLI 설정
├── LICENSE                        # MIT 라이선스
└── README.md                      # 프로젝트 메인 README
```

---

## 🚀 GitHub Actions CI/CD

### 워크플로 개요

**파일**: `.github/workflows/deploy.yml`  
**목적**: Azure 인프라 자동 배포 및 Logic App 구성

### 트리거 조건

#### 1. Automatic Trigger (Push)
```yaml
on:
  push:
    branches:
      - main
```
- **조건**: main 브랜치에 코드 푸시 시
- **대상 환경**: dev (기본값)
- **실행 시점**: 푸시 직후 (1분 이내)

#### 2. Manual Trigger (Workflow Dispatch)
```yaml
on:
  workflow_dispatch:
    inputs:
      environment:
        description: 'Environment to deploy to'
        required: true
        default: 'dev'
        type: choice
        options:
          - dev
          - prod
```
- **조건**: GitHub Actions UI에서 수동 실행
- **대상 환경**: dev 또는 prod 선택 가능
- **실행 방법**: Repository → Actions → Deploy to Azure → Run workflow

### 워크플로 아키텍처

```
┌─────────────────────────────────────────────────────────────────┐
│                     GitHub Actions Pipeline                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────┐                                               │
│  │   validate   │  Bicep 템플릿 검증                            │
│  └──────┬───────┘                                               │
│         │                                                        │
│         ▼                                                        │
│  ┌──────────────────────┐                                       │
│  │ deploy-infrastructure│  Resource Group + Bicep 배포          │
│  └──────┬───────────────┘                                       │
│         │                                                        │
│         ▼                                                        │
│  ┌──────────────────┐                                           │
│  │ deploy-workflow  │  Logic App 워크플로 JSON 업로드           │
│  └──────┬───────────┘                                           │
│         │                                                        │
│         ▼                                                        │
│  ┌──────────────────────────┐                                   │
│  │ configure-managed-identity│  OpenAI 역할 할당                │
│  └──────┬───────────────────┘                                   │
│         │                                                        │
│         ▼                                                        │
│  ┌──────────────────┐                                           │
│  │ integration-test │  Logic App 수동 트리거 및 검증            │
│  └──────┬───────────┘                                           │
│         │                                                        │
│         ▼                                                        │
│  ┌──────────────┐                                               │
│  │    notify    │  배포 성공/실패 알림                          │
│  └──────────────┘                                               │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Job 상세 설명

#### Job 1: validate
**목적**: Bicep 템플릿 문법 및 구성 검증

**Steps**:
1. **Checkout code**: Repository 코드 가져오기
2. **Azure Login**: OIDC 기반 인증 (Federated Credential)
3. **Validate Bicep**: `az deployment group validate` 실행

**성공 조건**:
- Bicep 문법 오류 없음
- 모든 필수 파라미터 존재
- 리소스 타입 및 API 버전 유효

**실행 시간**: ~30초

---

#### Job 2: deploy-infrastructure
**목적**: Azure 인프라 리소스 배포

**Steps**:
1. **Checkout code**: Repository 코드 가져오기
2. **Azure Login**: OIDC 인증
3. **Resource Group 생성**: 없을 경우에만 생성
4. **Bicep 배포**: `az deployment group create` 실행
5. **Outputs 추출**: Logic App 이름, ID 저장

**배포 리소스**:
- Logic App (Consumption)
- Storage Account (Standard LRS)
- API Connections (RSS, Office 365)
- Application Insights
- Log Analytics Workspace

**Outputs**:
- `logicAppName`: Logic App 리소스 이름
- `logicAppId`: Logic App 전체 리소스 ID

**실행 시간**: ~3-5분

---

#### Job 3: deploy-workflow
**목적**: Logic App 워크플로 정의 업로드

**Steps**:
1. **Checkout code**: Repository 코드 가져오기
2. **Azure Login**: OIDC 인증
3. **Connection IDs 조회**: RSS, Office 365 커넥션 ID 추출
4. **Workflow Payload 생성**: `jq`로 JSON 구성
5. **REST API 호출**: Logic App 워크플로 업데이트

**Workflow Payload 구조**:
```json
{
  "location": "koreacentral",
  "properties": {
    "definition": { /* 워크플로 정의 */ },
    "parameters": {
      "$connections": { /* API 커넥션 */ },
      "openAiEndpoint": { "value": "..." },
      "openAiDeploymentName": { "value": "..." },
      "emailRecipient": { "value": "..." },
      "rssFeedUrl": { "value": "..." }
    }
  }
}
```

**실행 시간**: ~1-2분

---

#### Job 4: configure-managed-identity
**목적**: Logic App Managed Identity에 Azure OpenAI 접근 권한 부여

**Steps**:
1. **Azure Login**: OIDC 인증
2. **Principal ID 추출**: Logic App의 Managed Identity 식별자 조회
3. **Role Assignment**: "Cognitive Services OpenAI User" 역할 할당

**역할 할당 명령**:
```bash
az role assignment create \
  --assignee {principal-id} \
  --role "Cognitive Services OpenAI User" \
  --scope {openai-resource-id}
```

**실행 시간**: ~30초

---

#### Job 5: integration-test
**목적**: 배포된 Logic App 자동 테스트

**Steps**:
1. **Azure Login**: OIDC 인증
2. **Logic Apps CLI Extension 설치**: `az extension add --name logic`
3. **Logic App 수동 트리거**: REST API로 Recurrence 트리거 실행
4. **30초 대기**: 워크플로 실행 시간 확보
5. **Run Status 확인**: 최근 실행 결과 조회

**성공 조건**:
- Run Status = "Succeeded"

**실패 시 동작**:
- 워크플로 전체 실패 처리
- notify Job에서 실패 알림

**실행 시간**: ~1분

---

#### Job 6: notify
**목적**: 배포 성공/실패 알림

**조건**:
- `if: always()` - 이전 Job 성공/실패 관계없이 항상 실행

**Steps**:
1. **Success Notification**: integration-test 성공 시
   - 환경(dev/prod) 표시
   - 성공 메시지 출력
   
2. **Failure Notification**: integration-test 실패 시
   - 환경(dev/prod) 표시
   - 실패 메시지 출력
   - Exit Code 1 반환

**향후 확장**:
- [ ] Slack 알림 연동
- [ ] Email 알림 연동
- [ ] Azure DevOps Work Item 자동 생성

**실행 시간**: ~10초

---

## 🔐 GitHub Secrets 관리

### 필수 Secrets 목록

GitHub Repository → **Settings** → **Secrets and variables** → **Actions**에서 설정:

| Secret 이름 | 설명 | 값 형식 | 예시 값 |
|-------------|------|---------|---------|
| `AZURE_CLIENT_ID` | Service Principal Client ID | GUID | `12345678-1234-1234-1234-123456789abc` |
| `AZURE_TENANT_ID` | Azure AD Tenant ID | GUID | `87654321-4321-4321-4321-cba987654321` |
| `AZURE_SUBSCRIPTION_ID` | Azure 구독 ID | GUID | `3864b016-4594-40ad-a96b-4a08ac96b537` |
| `EMAIL_RECIPIENT` | 이메일 수신자 주소 | Email | `azure-mvp@zerobig.kr` |
| `OPENAI_ENDPOINT` | Azure OpenAI 엔드포인트 | URL | `https://aoai-knowledge-base-demo.openai.azure.com/` |
| `OPENAI_DEPLOYMENT_NAME` | GPT 모델 배포 이름 | String | `gpt-4o` |
| `OPENAI_RESOURCE_ID` | Azure OpenAI 리소스 전체 ID | Resource ID | `/subscriptions/{sub-id}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{name}` |

### Service Principal 생성 방법

#### 1. Azure CLI로 App Registration 생성

```bash
# 1. Service Principal 생성
az ad sp create-for-rbac \
  --name "github-actions-security-blog-automation" \
  --role "Contributor" \
  --scopes /subscriptions/3864b016-4594-40ad-a96b-4a08ac96b537

# 2. 출력 예시
{
  "appId": "12345678-1234-1234-1234-123456789abc",          # → AZURE_CLIENT_ID
  "displayName": "github-actions-security-blog-automation",
  "password": "...",                                         # (사용 안 함)
  "tenant": "87654321-4321-4321-4321-cba987654321"          # → AZURE_TENANT_ID
}
```

#### 2. Federated Credential 설정 (OIDC)

**장점**: 비밀키 없이 GitHub Actions에서 Azure 인증 가능 (보안 강화)

```bash
# 1. App ID 저장
APP_ID="12345678-1234-1234-1234-123456789abc"

# 2. Federated Credential 생성
az ad app federated-credential create \
  --id $APP_ID \
  --parameters '{
    "name": "github-actions-oidc-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:zer0big/azure-security-blog-automation:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"],
    "description": "GitHub Actions OIDC for main branch"
  }'
```

**Subject 형식**:
- **Main 브랜치**: `repo:zer0big/azure-security-blog-automation:ref:refs/heads/main`
- **특정 브랜치**: `repo:{owner}/{repo}:ref:refs/heads/{branch}`
- **Pull Request**: `repo:{owner}/{repo}:pull_request`
- **모든 브랜치**: `repo:{owner}/{repo}:ref:refs/heads/*`

#### 3. Azure OpenAI 리소스 ID 확인

```bash
# OpenAI 리소스 ID 조회
az cognitiveservices account show \
  --resource-group RG-AOAI-AgenticMVP \
  --name aoai-knowledge-base-demo \
  --query id -o tsv

# 출력 예시
# /subscriptions/3864b016-4594-40ad-a96b-4a08ac96b537/resourceGroups/RG-AOAI-AgenticMVP/providers/Microsoft.CognitiveServices/accounts/aoai-knowledge-base-demo
```

### Secrets 보안 Best Practices

✅ **권장 사항**:
- Service Principal은 **최소 권한** 부여 (Contributor, 특정 Resource Group만)
- Federated Credential 사용 (비밀키 노출 위험 제거)
- Secrets 값은 **절대 커밋 금지** (`.gitignore` 활용)
- 정기적으로 Service Principal 자격 증명 갱신 (6개월마다)

❌ **피해야 할 사항**:
- Owner 역할 부여 (과도한 권한)
- 비밀키 기반 인증 (OIDC 대신)
- Secrets를 코드나 로그에 출력
- 여러 프로젝트에서 동일한 Service Principal 재사용

---

## 🔄 배포 시나리오

### 시나리오 1: 개발 환경 자동 배포

**상황**: 코드 수정 후 dev 환경에 자동 배포

**절차**:
1. 로컬에서 코드 수정 (예: Logic App 워크플로 변경)
2. Git 커밋 및 푸시
   ```bash
   git add .
   git commit -m "feat: Add new RSS feed source"
   git push origin main
   ```
3. GitHub Actions 자동 실행 (1분 이내)
4. 5-10분 후 배포 완료
5. Azure Portal 또는 이메일로 결과 확인

**장점**:
- 수동 배포 작업 제거
- 일관된 배포 프로세스
- 배포 이력 자동 추적

---

### 시나리오 2: 프로덕션 환경 수동 배포

**상황**: dev 환경 테스트 완료 후 prod 환경 배포

**절차**:
1. GitHub Repository → **Actions** 탭 클릭
2. 좌측 "Deploy to Azure" 워크플로 선택
3. 우측 상단 **"Run workflow"** 버튼 클릭
4. **Environment** 드롭다운에서 `prod` 선택
5. **"Run workflow"** 클릭하여 실행
6. 각 Job 진행 상황 실시간 모니터링
7. Integration Test 성공 확인
8. Azure Portal에서 Logic App 동작 확인

**주의사항**:
- prod 배포 전 dev 환경 충분히 테스트
- 배포 시간대 고려 (업무 시간 외 권장)
- 롤백 계획 사전 수립

---

### 시나리오 3: 긴급 롤백

**상황**: 배포 후 문제 발생, 이전 버전으로 복구 필요

**방법 1: Git Revert (권장)**
```bash
# 1. 문제가 있는 커밋 확인
git log --oneline -10

# 2. 해당 커밋 revert
git revert {commit-hash}

# 3. 푸시 (자동 배포 트리거)
git push origin main
```

**방법 2: 수동 재배포 (긴급)**
```bash
# 1. 이전 버전 Bicep으로 재배포
az deployment group create \
  --resource-group rg-security-blog-automation-prod \
  --template-file infra/bicep/main.bicep \
  --parameters @infra/bicep/parameters.prod.json \
  --mode Complete
```

**방법 3: GitHub Actions 이전 워크플로 재실행**
1. GitHub → Actions → 성공했던 이전 Run 선택
2. 우측 상단 "Re-run all jobs" 클릭

---

### 시나리오 4: 환경별 파라미터 변경

**상황**: dev/prod 환경별로 다른 설정 적용 필요

**파일 위치**:
- Dev: `infra/bicep/parameters.dev.json`
- Prod: `infra/bicep/parameters.prod.json`

**변경 예시 - RSS Feed URL 추가**:
```json
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "environment": {
      "value": "dev"
    },
    "rssFeedUrls": {
      "value": [
        "https://www.microsoft.com/en-us/security/blog/feed/",
        "https://azure.microsoft.com/blog/feed/"  // 추가
      ]
    }
  }
}
```

**배포**:
```bash
git add infra/bicep/parameters.dev.json
git commit -m "feat: Add Azure Blog RSS feed"
git push origin main
```

---

## 📊 모니터링 및 디버깅

### GitHub Actions 로그 확인

**경로**: Repository → **Actions** → 실행된 워크플로 클릭 → Job 클릭

**주요 확인 사항**:
1. **validate Job**: Bicep 검증 오류 메시지
2. **deploy-infrastructure Job**: 
   - Resource Group 생성 여부
   - Bicep 배포 성공/실패
   - Outputs 값 확인
3. **deploy-workflow Job**: 
   - Connection IDs 조회 성공 여부
   - Workflow Payload JSON 구조
   - REST API 응답 코드
4. **integration-test Job**: 
   - Logic App 트리거 성공 여부
   - Run Status 값 (Succeeded/Failed/Running)

**로그 다운로드**:
- 우측 상단 톱니바퀴 아이콘 → "Download log archive" 클릭

---

### Azure Portal에서 확인

**Logic App Run History**:
1. Azure Portal → Resource Groups → `rg-security-blog-automation-dev`
2. Logic App 리소스 클릭
3. 좌측 "Overview" → "Runs history" 확인
4. 실패한 Run 클릭 → 각 Action별 입력/출력 확인

**Application Insights 쿼리**:
```kql
// 최근 1시간 Logic App 실행 기록
requests
| where cloud_RoleName == "logic-dev-security-blog-automation"
| where timestamp > ago(1h)
| project timestamp, name, resultCode, duration
| order by timestamp desc

// 에러 발생 현황
exceptions
| where timestamp > ago(24h)
| summarize count() by type, outerMessage
| order by count_ desc
```

---

### 일반적인 오류 및 해결책

#### 오류 1: Azure Login 실패
```
Error: Login failed with Error: AADSTS700016: 
Application with identifier '...' was not found
```

**원인**: Federated Credential 설정 오류

**해결**:
1. Service Principal App ID 확인
2. Federated Credential의 Subject 값 정확성 검증
   ```bash
   az ad app federated-credential list --id {app-id}
   ```
3. Subject가 `repo:zer0big/azure-security-blog-automation:ref:refs/heads/main` 형식인지 확인

---

#### 오류 2: Bicep 배포 실패
```
Error: InvalidTemplate - The template deployment failed because the 
template parameter 'emailRecipient' is not provided.
```

**원인**: GitHub Secrets 누락 또는 파라미터 이름 불일치

**해결**:
1. GitHub Secrets에 `EMAIL_RECIPIENT` 존재 확인
2. `parameters.dev.json`에 해당 파라미터 정의 확인
3. Bicep 템플릿의 파라미터 이름 일치 확인

---

#### 오류 3: Integration Test 실패
```
Run Status: Failed
```

**원인**: Logic App 워크플로 실행 중 오류 발생

**해결**:
1. Azure Portal → Logic App → Run History에서 실패 원인 확인
2. 주요 확인 사항:
   - Office 365 Connection 인증 상태
   - Azure OpenAI Managed Identity 역할 할당
   - RSS Feed URL 접근 가능 여부
   - Table Storage 연결 문자열 유효성

**디버깅 명령**:
```bash
# Logic App 최근 실행 조회
az rest --method GET \
  --uri "/subscriptions/{sub-id}/resourceGroups/rg-security-blog-automation-dev/providers/Microsoft.Logic/workflows/logic-dev-security-blog-automation/runs?api-version=2016-06-01&\$top=5" \
  --query "value[].{name:name, status:properties.status, startTime:properties.startTime}" -o table
```

---

#### 오류 4: API Connection 인증 실패
```
Error: The API connection 'office365' is not connected
```

**원인**: API Connection 미인증 또는 토큰 만료

**해결**:
1. Azure Portal → API Connections → office365 리소스 클릭
2. 좌측 "Edit API connection" 클릭
3. "Authorize" 버튼 클릭하여 재인증
4. azure-mvp@zerobig.kr 계정으로 로그인
5. 저장 후 Logic App 재실행

---

## 🛠️ 로컬 개발 및 테스트

### 로컬 환경 설정

#### 1. 필수 도구 설치

```bash
# Azure CLI
winget install Microsoft.AzureCLI

# GitHub CLI
winget install GitHub.cli

# Visual Studio Code
winget install Microsoft.VisualStudioCode

# Bicep CLI
az bicep install
```

#### 2. Azure CLI 로그인

```bash
# 대화형 로그인
az login

# 구독 설정
az account set --subscription 3864b016-4594-40ad-a96b-4a08ac96b537

# 로그인 확인
az account show
```

#### 3. GitHub CLI 인증

```bash
# GitHub 로그인
gh auth login

# Repository 클론
gh repo clone zer0big/azure-security-blog-automation
cd azure-security-blog-automation
```

---

### Bicep 템플릿 로컬 검증

```bash
# 1. Bicep 문법 검증
az bicep build --file infra/bicep/main.bicep

# 2. What-If 분석 (실제 배포 없이 시뮬레이션)
az deployment group what-if \
  --resource-group rg-security-blog-automation-dev \
  --template-file infra/bicep/main.bicep \
  --parameters @infra/bicep/parameters.dev.json \
  --parameters emailRecipient=test@example.com

# 3. 파라미터 파일 검증
az deployment group validate \
  --resource-group rg-security-blog-automation-dev \
  --template-file infra/bicep/main.bicep \
  --parameters @infra/bicep/parameters.dev.json \
  --verbose
```

---

### 수동 배포 (로컬에서)

```bash
# 1. Resource Group 생성 (없을 경우)
az group create \
  --name rg-security-blog-automation-dev \
  --location koreacentral \
  --tags Environment=dev Project=security-blog-automation ManagedBy=Manual

# 2. Bicep 배포
az deployment group create \
  --resource-group rg-security-blog-automation-dev \
  --template-file infra/bicep/main.bicep \
  --parameters @infra/bicep/parameters.dev.json \
  --parameters emailRecipient=azure-mvp@zerobig.kr \
  --parameters openAiEndpoint=https://aoai-knowledge-base-demo.openai.azure.com/ \
  --parameters openAiDeploymentName=gpt-4o \
  --output json > deployment-output.json

# 3. Outputs 확인
cat deployment-output.json | jq '.properties.outputs'
```

---

### Logic App 워크플로 로컬 테스트

**Logic App 워크플로는 클라우드에서만 실행 가능**하지만, 워크플로 정의 JSON의 유효성은 검증 가능:

```bash
# 1. Logic App 워크플로 JSON 검증
az logic workflow validate \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --definition @workflows/security-blog-definition.json

# 2. 워크플로 수동 트리거
az rest --method POST \
  --uri "/subscriptions/3864b016-4594-40ad-a96b-4a08ac96b537/resourceGroups/rg-security-blog-automation-dev/providers/Microsoft.Logic/workflows/logic-dev-security-blog-automation/triggers/Recurrence/run?api-version=2016-06-01"

# 3. 최근 실행 결과 확인
az logic workflow run show \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --run-name {run-id}
```

---

## 📚 Git 워크플로 및 브랜치 전략

### 현재 브랜치 구조

```
main (기본 브랜치)
└── 모든 배포는 main 브랜치에서 수행
```

**특징**:
- **Simple Workflow**: 단일 브랜치 전략 (Trunk-Based Development)
- **Automatic Deployment**: main 브랜치 푸시 시 dev 환경 자동 배포
- **Manual Production**: prod 배포는 수동 트리거 필요

---

### 권장 Git 워크플로

#### 1. Feature 개발

```bash
# 1. 최신 main 브랜치로 업데이트
git checkout main
git pull origin main

# 2. Feature 브랜치 생성
git checkout -b feature/add-azure-blog-rss

# 3. 코드 수정 및 커밋
git add .
git commit -m "feat: Add Azure Blog RSS feed support"

# 4. 원격 브랜치에 푸시
git push origin feature/add-azure-blog-rss
```

#### 2. Pull Request 생성

**GitHub 웹에서**:
1. Repository → **Pull requests** → **New pull request**
2. Base: `main` ← Compare: `feature/add-azure-blog-rss`
3. 제목 및 설명 작성 (변경 내용, 테스트 결과)
4. **Create pull request** 클릭
5. 코드 리뷰 요청 (팀원 지정)

**GitHub CLI 사용**:
```bash
gh pr create \
  --title "feat: Add Azure Blog RSS feed support" \
  --body "- RSS feed URL 추가\n- Multi-RSS 구조로 변경\n- Unit Test 통과" \
  --base main \
  --head feature/add-azure-blog-rss
```

#### 3. 코드 리뷰 및 병합

**리뷰어**:
- 코드 품질 검토
- 보안 취약점 확인
- 테스트 커버리지 검증
- 승인 또는 변경 요청

**병합**:
```bash
# 1. PR 승인 후 병합
gh pr merge {pr-number} --squash --delete-branch

# 2. 로컬 main 업데이트
git checkout main
git pull origin main
```

**자동 배포**:
- 병합 즉시 GitHub Actions 자동 실행 (dev 환경)

---

### Commit Message 규칙

**형식**: `<type>(<scope>): <subject>`

**Types**:
- `feat`: 새 기능 추가
- `fix`: 버그 수정
- `docs`: 문서 변경
- `style`: 코드 포맷팅 (기능 변경 없음)
- `refactor`: 코드 리팩토링
- `test`: 테스트 추가/수정
- `chore`: 빌드, 설정 파일 수정

**예시**:
```bash
git commit -m "feat(logic-app): Add Microsoft 365 Defender blog RSS feed"
git commit -m "fix(bicep): Correct storage account SKU to Standard_LRS"
git commit -m "docs(readme): Update deployment instructions"
git commit -m "refactor(functions): Extract email HTML generation logic"
```

---

## 🔒 보안 Best Practices

### 1. 코드 보안

**민감 정보 제외**:
```gitignore
# .gitignore 파일
*.env
local.settings.json
parameters.*.json
*.pfx
*.key
*.pem
appsettings.Development.json
```

**검증**:
```bash
# 커밋 전 민감 정보 스캔
git secrets --scan
```

---

### 2. Branch Protection Rules

**GitHub Repository → Settings → Branches → Branch protection rules**:

**main 브랜치 보호 규칙**:
- ✅ **Require a pull request before merging**
- ✅ **Require approvals**: 최소 1명
- ✅ **Dismiss stale pull request approvals when new commits are pushed**
- ✅ **Require status checks to pass before merging**
  - ✅ `validate` Job 성공 필수
- ✅ **Require branches to be up to date before merging**
- ✅ **Include administrators** (관리자도 규칙 적용)
- ✅ **Restrict who can push to matching branches** (선택 사항)

---

### 3. Dependabot 설정

**파일 생성**: `.github/dependabot.yml`

```yaml
version: 2
updates:
  # GitHub Actions 의존성
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    labels:
      - "dependencies"
      - "github-actions"

  # npm 의존성 (Functions)
  - package-ecosystem: "npm"
    directory: "/functions"
    schedule:
      interval: "weekly"
    labels:
      - "dependencies"
      - "npm"
```

**효과**:
- 매주 자동으로 의존성 버전 체크
- 취약점 발견 시 자동 PR 생성
- 승인 후 병합으로 보안 업데이트 적용

---

### 4. Code Scanning (GitHub Advanced Security)

**활성화 방법**:
1. Repository → **Settings** → **Code security and analysis**
2. **Code scanning** → **Set up** → **Default** 선택
3. CodeQL 자동 실행

**검출 내용**:
- SQL Injection
- XSS (Cross-Site Scripting)
- Path Traversal
- Hardcoded Credentials
- 기타 OWASP Top 10 취약점

---

## 📈 성능 최적화

### GitHub Actions 최적화

#### 1. Cache 활용

```yaml
# Bicep 모듈 캐싱
- name: Cache Bicep modules
  uses: actions/cache@v4
  with:
    path: ~/.azure/bicep
    key: ${{ runner.os }}-bicep-${{ hashFiles('**/main.bicep') }}
    restore-keys: |
      ${{ runner.os }}-bicep-
```

#### 2. 병렬 실행

```yaml
jobs:
  test-bicep:
    name: Test Bicep Templates
    runs-on: ubuntu-latest
    strategy:
      matrix:
        environment: [dev, prod]
    steps:
      - name: Validate ${{ matrix.environment }}
        run: |
          az deployment group validate \
            --resource-group rg-security-blog-automation-${{ matrix.environment }} \
            --template-file infra/bicep/main.bicep \
            --parameters @infra/bicep/parameters.${{ matrix.environment }}.json
```

#### 3. Conditional Jobs

```yaml
jobs:
  deploy-prod:
    if: github.event_name == 'workflow_dispatch' && github.event.inputs.environment == 'prod'
    runs-on: ubuntu-latest
    steps:
      # 프로덕션 배포 로직
```

---

## 🔗 통합 및 확장

### Azure DevOps 통합

**Scenarios**:
- GitHub에서 소스 관리
- Azure DevOps에서 Work Item 관리
- GitHub Actions에서 배포 후 ADO Work Item 자동 업데이트

**구현**:
```yaml
# .github/workflows/deploy.yml
- name: Update ADO Work Item
  run: |
    # Azure DevOps PAT 사용
    az devops configure --defaults organization=https://dev.azure.com/azure-mvp project=azure-secu-updates-notification
    
    # Work Item 상태 업데이트
    az boards work-item update \
      --id 145 \
      --state "Done" \
      --discussion "Deployed to ${{ github.event.inputs.environment }} via GitHub Actions Run #${{ github.run_number }}"
  env:
    AZURE_DEVOPS_EXT_PAT: ${{ secrets.AZURE_DEVOPS_PAT }}
```

---

### Slack 알림 연동

```yaml
# .github/workflows/deploy.yml
- name: Send Slack notification
  if: always()
  uses: slackapi/slack-github-action@v1
  with:
    payload: |
      {
        "text": "Deployment to ${{ github.event.inputs.environment }} ${{ job.status }}",
        "blocks": [
          {
            "type": "section",
            "text": {
              "type": "mrkdwn",
              "text": "*Deployment Status*: ${{ job.status }}\n*Environment*: ${{ github.event.inputs.environment }}\n*Commit*: ${{ github.sha }}"
            }
          }
        ]
      }
  env:
    SLACK_WEBHOOK_URL: ${{ secrets.SLACK_WEBHOOK_URL }}
```

---

## 📖 참고 자료

### GitHub Actions 공식 문서

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Workflow Syntax](https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions)
- [Azure Login Action](https://github.com/Azure/login)
- [GitHub Actions for Azure](https://github.com/Azure/actions)

### Azure 관련 문서

- [Azure CLI in GitHub Actions](https://learn.microsoft.com/azure/developer/github/connect-from-azure)
- [Logic Apps CI/CD](https://learn.microsoft.com/azure/logic-apps/devops-deployment)
- [Bicep Best Practices](https://learn.microsoft.com/azure/azure-resource-manager/bicep/best-practices)
- [Federated Identity Credentials](https://learn.microsoft.com/azure/active-directory/develop/workload-identity-federation)

### Git 관련 문서

- [Git Documentation](https://git-scm.com/doc)
- [GitHub Flow](https://docs.github.com/en/get-started/quickstart/github-flow)
- [Conventional Commits](https://www.conventionalcommits.org/)

---

## 🆘 트러블슈팅 체크리스트

### 배포 실패 시 확인 사항

- [ ] GitHub Secrets 모두 설정되어 있는가?
- [ ] Service Principal Federated Credential 정확히 설정되었는가?
- [ ] Bicep 템플릿 문법 오류 없는가?
- [ ] Azure 구독에 충분한 리소스 할당량이 있는가?
- [ ] API Connections (RSS, Office 365) 인증 완료되었는가?
- [ ] Azure OpenAI 리소스 접근 가능한가?
- [ ] Logic App Managed Identity 역할 할당 완료되었는가?

### Integration Test 실패 시 확인 사항

- [ ] Logic App 워크플로 정의가 유효한가?
- [ ] RSS Feed URL 접근 가능한가?
- [ ] Office 365 Connection 토큰 만료되지 않았는가?
- [ ] Table Storage 연결 문자열 유효한가?
- [ ] Azure OpenAI API 호출 성공하는가?
- [ ] 이메일 수신자 주소 유효한가?

### 일반적인 디버깅 명령

```bash
# 1. GitHub Actions 로그 다운로드
gh run download {run-id}

# 2. Azure 배포 상태 확인
az deployment group show \
  --resource-group rg-security-blog-automation-dev \
  --name main \
  --query properties.provisioningState

# 3. Logic App 상태 확인
az logic workflow show \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --query state

# 4. Application Insights 쿼리
az monitor app-insights query \
  --app appi-dev-security-blog-automation \
  --analytics-query "requests | where timestamp > ago(1h) | summarize count() by resultCode"
```

---

## 📝 변경 이력

### v1.0.0 (2025-12-22)

**초기 GitHub 환경 구축**:
- Repository 생성: https://github.com/zer0big/azure-security-blog-automation
- GitHub Actions CI/CD 파이프라인 구성
- Service Principal OIDC 인증 설정
- 7개 Secrets 등록 및 검증
- Multi-environment 배포 구조 (dev/prod)

**주요 기능**:
- Bicep 템플릿 자동 검증
- Azure 인프라 자동 배포
- Logic App 워크플로 업로드
- Managed Identity 역할 할당
- Integration Test 자동화
- 배포 성공/실패 알림

**문서화**:
- GitHub Actions 워크플로 가이드
- Service Principal 설정 가이드
- 배포 시나리오별 절차
- 트러블슈팅 가이드

---

## 🤝 기여 가이드

### 버그 리포트

**GitHub Issues 생성**:
1. Repository → **Issues** → **New issue**
2. 제목: `[BUG] 간단한 설명`
3. 내용:
   - **환경**: OS, Azure CLI 버전, Node.js 버전
   - **재현 절차**: 1, 2, 3...
   - **예상 결과**: ...
   - **실제 결과**: ...
   - **로그**: GitHub Actions 로그, Azure Portal 스크린샷

### 기능 요청

**GitHub Issues 생성**:
1. Repository → **Issues** → **New issue**
2. 제목: `[FEATURE] 간단한 설명`
3. 내용:
   - **기능 설명**: 무엇을 하고 싶은가?
   - **사용 사례**: 왜 필요한가?
   - **제안된 솔루션**: 어떻게 구현할 것인가?
   - **대안**: 다른 방법은?

### Pull Request

1. Fork Repository
2. Feature 브랜치 생성
3. 코드 수정 및 테스트
4. Commit (Conventional Commits 규칙 준수)
5. Pull Request 생성
6. 코드 리뷰 대응
7. 병합 승인 대기

---

## 📧 문의

**프로젝트**: Azure Security Blog Automation  
**Repository**: https://github.com/zer0big/azure-security-blog-automation  
**담당자**: Azure MVP Team  
**이메일**: azure-mvp@zerobig.kr  
**ADO 프로젝트**: https://dev.azure.com/azure-mvp/azure-secu-updates-notification

---

*본 문서는 GitHub를 활용한 소스 관리, CI/CD, 협업의 전체 프로세스를 설명하며, 실제 운영 환경의 Best Practices를 반영하고 있습니다.*
