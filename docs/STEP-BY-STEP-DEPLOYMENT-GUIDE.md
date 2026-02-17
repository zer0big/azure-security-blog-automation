# 🚀 Azure Security Blog Automation - 완전 초보자용 구축 가이드

> **대상**: Azure 클라우드 엔지니어 입문자  
> **소요 시간**: 약 2-3시간  
> **난이도**: ⭐⭐ (중하)  
> **최종 업데이트**: 2026년 1월

이 가이드는 Azure를 처음 접하는 엔지니어가 **처음부터 끝까지** 단계별로 따라할 수 있도록 작성되었습니다.

---

## 📋 목차

1. [시스템 개요](#1-시스템-개요)
2. [사전 요구사항](#2-사전-요구사항)
3. [개발 환경 구성](#3-개발-환경-구성)
4. [Azure 구독 및 인증 설정](#4-azure-구독-및-인증-설정)
5. [Azure OpenAI 리소스 생성](#5-azure-openai-리소스-생성)
6. [인프라 배포 (Bicep IaC)](#6-인프라-배포-bicep-iac)
7. [Azure Functions 배포](#7-azure-functions-배포)
8. [Logic App 워크플로우 구성](#8-logic-app-워크플로우-구성)
9. [Office 365 이메일 연결 설정](#9-office-365-이메일-연결-설정)
10. [테스트 및 검증](#10-테스트-및-검증)
11. [모니터링 설정](#11-모니터링-설정)
12. [문제 해결 가이드](#12-문제-해결-가이드)
13. [운영 및 유지보수](#13-운영-및-유지보수)

---

## 1. 시스템 개요

### 1.1 이 시스템은 무엇인가요?

**Azure Security Blog Automation**은 Microsoft의 보안 및 Azure 관련 블로그에서 최신 게시글을 자동으로 수집하고, AI(GPT-4o)를 활용해 한국어로 요약한 후 이메일로 발송하는 자동화 시스템입니다.

### 1.2 주요 기능

| 기능 | 설명 |
|------|------|
| 📰 RSS 피드 수집 | 12개의 Microsoft 공식 블로그에서 최신 게시글 자동 수집 |
| 🤖 AI 요약 | Azure OpenAI GPT-4o를 활용한 한국어 핵심 인사이트 생성 |
| 📧 이메일 발송 | 하루 4회 (07:00, 08:00, 19:00, 20:00 KST) 자동 발송 |
| 🔄 중복 제거 | Azure Table Storage를 활용한 게시글 중복 처리 방지 |

### 1.3 시스템 아키텍처

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Azure Cloud                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌──────────────────┐    ┌──────────────────┐    ┌──────────────────┐  │
│  │   Logic App #1   │    │   Logic App #2   │    │  Azure OpenAI    │  │
│  │  (Security 5개)  │    │ (Azure/Cloud 7개)│    │    GPT-4o        │  │
│  │ 07:00, 19:00   │    │ 08:00, 20:00   │    │                  │  │
│  └────────┬─────────┘    └────────┬─────────┘    └────────▲─────────┘  │
│           │                       │                       │             │
│           ▼                       ▼                       │             │
│  ┌────────────────────────────────────────────────────────┴──────────┐  │
│  │                      Azure Functions (.NET 8)                     │  │
│  │  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────────┐  │  │
│  │  │ BuildDigest  │ │CheckDuplicate│ │ GenerateEmailHtml 등     │  │  │
│  │  │ (통합 엔진)  │ │              │ │                          │  │  │
│  │  └──────────────┘ └──────────────┘ └──────────────────────────┘  │  │
│  └────────────────────────────────────┬──────────────────────────────┘  │
│                                       │                                 │
│           ┌───────────────────────────┼───────────────────────────┐     │
│           ▼                           ▼                           ▼     │
│  ┌──────────────────┐    ┌──────────────────┐    ┌──────────────────┐  │
│  │  Table Storage   │    │   Application    │    │    Office 365    │  │
│  │  ProcessedPosts  │    │     Insights     │    │    Email 발송    │  │
│  └──────────────────┘    └──────────────────┘    └──────────────────┘  │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 1.4 배포될 Azure 리소스 목록

| 리소스 유형 | 리소스 이름 (예시) | 용도 |
|-------------|-------------------|------|
| Resource Group | rg-security-blog-automation-dev | 모든 리소스를 담는 컨테이너 |
| Storage Account | stdevsecurityblog | Table Storage, Function 저장소 |
| App Service Plan | func-dev-security-blog-automation-plan | Function App 호스팅 |
| Function App | func-dev-security-blog-automation | API 엔드포인트 호스팅 |
| Logic App (Consumption) | logic-dev-security-blog-automation | Security 워크플로우 |
| Logic App (Consumption) | logic-dev-azure-cloud-blog-automation | Azure/Cloud 워크플로우 |
| Application Insights | appi-dev-security-blog-automation | 모니터링 및 로깅 |
| Log Analytics Workspace | log-dev-security-blog-automation | 로그 저장 |
| Azure OpenAI | aoai-security-blog-automation | AI 요약 서비스 |

---

## 2. 사전 요구사항

### 2.1 필수 항목 체크리스트

시작하기 전에 다음 항목을 확인하세요:

- [ ] **Azure 구독** - 유효한 Azure 구독이 있어야 합니다
- [ ] **Azure 구독 권한** - 구독에서 `기여자(Contributor)` 역할 이상
- [ ] **Azure OpenAI 액세스** - Azure OpenAI 서비스 사용 승인 필요 ([신청 링크](https://aka.ms/oai/access))
- [ ] **Office 365 계정** - 이메일 발송용 (회사/개인 계정 모두 가능)
- [ ] **Windows 10/11 PC** - 개발 및 배포 환경
- [ ] **인터넷 연결** - 안정적인 네트워크 연결

### 2.2 Azure OpenAI 액세스 신청 (필수)

> ⚠️ **중요**: Azure OpenAI는 사전 승인이 필요합니다. 승인에 1-2일 소요될 수 있습니다.

1. https://aka.ms/oai/access 접속
2. Microsoft 계정으로 로그인
3. 신청 양식 작성:
   - **Subscription ID**: Azure 구독 ID 입력
   - **Use Case**: "Blog content summarization and translation"
   - **Company**: 회사명 또는 개인
4. 제출 후 승인 이메일 대기

---

## 3. 개발 환경 구성

### 3.1 필수 도구 설치

#### 3.1.1 Git 설치

```powershell
# Windows Package Manager (winget) 사용
winget install --id Git.Git -e --source winget

# 설치 확인
git --version
# 예상 출력: git version 2.43.0.windows.1
```

**수동 설치**: https://git-scm.com/download/win

#### 3.1.2 .NET 8 SDK 설치

```powershell
# winget으로 설치
winget install Microsoft.DotNet.SDK.8

# 설치 확인
dotnet --version
# 예상 출력: 8.0.xxx
```

**수동 설치**: https://dotnet.microsoft.com/download/dotnet/8.0

#### 3.1.3 Azure CLI 설치

```powershell
# winget으로 설치
winget install -e --id Microsoft.AzureCLI

# 설치 확인 (새 터미널 창 열기)
az --version
# 예상 출력: azure-cli 2.55.0 이상
```

**수동 설치**: https://aka.ms/installazurecliwindows

#### 3.1.4 Azure Functions Core Tools 설치

```powershell
# npm으로 설치 (Node.js 필요)
npm install -g azure-functions-core-tools@4 --unsafe-perm true

# 또는 winget으로 설치
winget install Microsoft.Azure.FunctionsCoreTools

# 설치 확인
func --version
# 예상 출력: 4.x.x
```

#### 3.1.5 Visual Studio Code 설치 (권장)

```powershell
# winget으로 설치
winget install -e --id Microsoft.VisualStudioCode

# VS Code 확장 설치 (VS Code 설치 후)
code --install-extension ms-azuretools.vscode-azurefunctions
code --install-extension ms-azuretools.vscode-azurelogicapps
code --install-extension ms-azuretools.vscode-bicep
code --install-extension ms-dotnettools.csharp
```

### 3.2 설치 확인

모든 도구가 올바르게 설치되었는지 확인합니다:

```powershell
# PowerShell에서 실행
Write-Host "=== 개발 환경 확인 ===" -ForegroundColor Cyan

# Git
$gitVersion = git --version 2>$null
if ($gitVersion) { Write-Host "✅ Git: $gitVersion" -ForegroundColor Green }
else { Write-Host "❌ Git이 설치되지 않았습니다" -ForegroundColor Red }

# .NET
$dotnetVersion = dotnet --version 2>$null
if ($dotnetVersion) { Write-Host "✅ .NET SDK: $dotnetVersion" -ForegroundColor Green }
else { Write-Host "❌ .NET SDK가 설치되지 않았습니다" -ForegroundColor Red }

# Azure CLI
$azVersion = az --version 2>$null | Select-Object -First 1
if ($azVersion) { Write-Host "✅ Azure CLI: $azVersion" -ForegroundColor Green }
else { Write-Host "❌ Azure CLI가 설치되지 않았습니다" -ForegroundColor Red }

# Azure Functions Core Tools
$funcVersion = func --version 2>$null
if ($funcVersion) { Write-Host "✅ Azure Functions Core Tools: $funcVersion" -ForegroundColor Green }
else { Write-Host "❌ Azure Functions Core Tools가 설치되지 않았습니다" -ForegroundColor Red }

Write-Host "========================" -ForegroundColor Cyan
```

### 3.3 프로젝트 클론

```powershell
# 작업 디렉토리로 이동 (원하는 경로로 변경)
cd C:\Users\$env:USERNAME\source\repos

# 리포지토리 클론
git clone https://github.com/zer0big/azure-security-blog-automation.git

# 프로젝트 디렉토리로 이동
cd azure-security-blog-automation

# 프로젝트 구조 확인
Get-ChildItem -Directory | Select-Object Name

# 예상 출력:
# Name
# ----
# .github
# .vscode
# config
# docs
# functions
# infra
```

---

## 4. Azure 구독 및 인증 설정

### 4.1 Azure CLI 로그인

```powershell
# Azure에 로그인 (브라우저 창이 열립니다)
az login

# 로그인 후 계정 정보 확인
az account show --output table
```

**예상 출력**:
```
EnvironmentName    IsDefault    Name                    State    TenantId
-----------------  -----------  ----------------------  -------  ------------------------------------
AzureCloud         True         My Azure Subscription   Enabled  xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

### 4.2 구독 선택 (다중 구독인 경우)

```powershell
# 사용 가능한 구독 목록 조회
az account list --output table

# 특정 구독 선택 (구독 이름 또는 ID 사용)
az account set --subscription "My Azure Subscription"

# 선택 확인
az account show --query "{Name:name, SubscriptionId:id}" --output table
```

### 4.3 권한 확인

```powershell
# 현재 사용자의 역할 할당 확인
az role assignment list --assignee $(az ad signed-in-user show --query id -o tsv) --output table

# 최소 '기여자(Contributor)' 역할이 있어야 합니다
```

> **💡 팁**: 권한이 없다면 Azure 관리자에게 구독에 대한 `기여자` 역할을 요청하세요.

---

## 5. Azure OpenAI 리소스 생성

### 5.1 리소스 그룹 생성

먼저 모든 리소스를 담을 리소스 그룹을 생성합니다:

```powershell
# 변수 설정
$RESOURCE_GROUP = "rg-security-blog-automation-dev"
$LOCATION = "koreacentral"

# 리소스 그룹 생성
az group create `
    --name $RESOURCE_GROUP `
    --location $LOCATION `
    --tags Environment=Development Project="Security Blog Automation"

# 생성 확인
az group show --name $RESOURCE_GROUP --output table
```

**예상 출력**:
```
Location      Name                                 ProvisioningState    Tags
------------  -----------------------------------  -------------------  --------------------------------
koreacentral  rg-security-blog-automation-dev      Succeeded           {"Environment":"Development",...}
```

### 5.2 Azure OpenAI 리소스 생성

```powershell
# Azure OpenAI 리소스 생성
$OPENAI_NAME = "aoai-security-blog-$(Get-Random -Maximum 9999)"

az cognitiveservices account create `
    --name $OPENAI_NAME `
    --resource-group $RESOURCE_GROUP `
    --location "eastus" `
    --kind "OpenAI" `
    --sku "S0" `
    --custom-domain $OPENAI_NAME

# 생성 확인 (몇 분 소요될 수 있음)
az cognitiveservices account show `
    --name $OPENAI_NAME `
    --resource-group $RESOURCE_GROUP `
    --query "{Name:name, ProvisioningState:properties.provisioningState, Endpoint:properties.endpoint}" `
    --output table
```

> **⚠️ 참고**: Azure OpenAI는 모든 리전에서 사용 가능하지 않습니다. `eastus`, `westus2`, `swedencentral` 등 지원 리전을 사용하세요.

### 5.3 GPT-4o 모델 배포

```powershell
# GPT-4o 모델 배포
az cognitiveservices account deployment create `
    --name $OPENAI_NAME `
    --resource-group $RESOURCE_GROUP `
    --deployment-name "gpt-4o" `
    --model-name "gpt-4o" `
    --model-version "2024-08-06" `
    --model-format "OpenAI" `
    --sku-capacity 10 `
    --sku-name "Standard"

# 배포 확인
az cognitiveservices account deployment list `
    --name $OPENAI_NAME `
    --resource-group $RESOURCE_GROUP `
    --output table
```

**예상 출력**:
```
Name    Properties.Model.Format    Properties.Model.Name    Properties.Model.Version    Properties.ProvisioningState
------  ------------------------   ----------------------   -------------------------   ----------------------------
gpt-4o  OpenAI                     gpt-4o                   2024-08-06                  Succeeded
```

### 5.4 API 키 및 엔드포인트 저장

```powershell
# 엔드포인트 가져오기
$AZURE_OPENAI_ENDPOINT = $(az cognitiveservices account show `
    --name $OPENAI_NAME `
    --resource-group $RESOURCE_GROUP `
    --query "properties.endpoint" `
    --output tsv)

# API 키 가져오기
$AZURE_OPENAI_KEY = $(az cognitiveservices account keys list `
    --name $OPENAI_NAME `
    --resource-group $RESOURCE_GROUP `
    --query "key1" `
    --output tsv)

# 환경 변수로 설정 (현재 세션)
$env:AZURE_OPENAI_ENDPOINT = $AZURE_OPENAI_ENDPOINT
$env:AZURE_OPENAI_KEY = $AZURE_OPENAI_KEY

# 값 확인 (키는 일부만 표시)
Write-Host "AZURE_OPENAI_ENDPOINT: $AZURE_OPENAI_ENDPOINT"
Write-Host "AZURE_OPENAI_KEY: $($AZURE_OPENAI_KEY.Substring(0,10))..."
```

> **🔐 보안 주의**: API 키는 절대로 Git에 커밋하거나 공개 저장소에 노출하지 마세요!

---

## 6. 인프라 배포 (Bicep IaC)

### 6.1 Bicep이란?

**Bicep**은 Azure 리소스를 선언적으로 배포하기 위한 Azure 네이티브 IaC(Infrastructure as Code) 언어입니다. ARM 템플릿보다 읽기 쉽고 작성하기 편합니다.

### 6.2 Bicep 파일 구조 이해

```
infra/
├── bicep/
│   ├── main.bicep                 # 메인 배포 파일
│   ├── modules/
│   │   ├── app-insights.bicep     # Application Insights 모듈
│   │   ├── function-app.bicep     # Function App 모듈
│   │   ├── logic-app.bicep        # Logic App #1 모듈
│   │   ├── logic-app-azure-cloud.bicep  # Logic App #2 모듈
│   │   └── storage.bicep          # Storage Account 모듈
│   └── parameters/
│       └── dev.bicepparam         # 개발 환경 파라미터
└── logic-app/
    ├── workflow.json              # Security 워크플로우
    └── workflow-azure-cloud.json  # Azure/Cloud 워크플로우
```

### 6.3 파라미터 파일 수정

배포 전 파라미터 파일을 환경에 맞게 수정합니다:

```powershell
# 파라미터 파일 열기 (VS Code 사용)
code infra/bicep/parameters/dev.bicepparam
```

**dev.bicepparam 파일 내용 확인**:
```bicep
using './main.bicep'

// Environment
param environmentName = 'dev'
param location = 'koreacentral'

// Resource Names - 필요시 수정
param storageAccountName = 'stdevsecurityblog'    // 3-24자, 소문자/숫자만
param functionAppName = 'func-dev-security-blog-automation'
param logicAppName = 'logic-dev-security-blog-automation'
param logicAppAzureCloudName = 'logic-dev-azure-cloud-blog-automation'
param appInsightsName = 'appi-dev-security-blog-automation'
param workspaceName = 'log-dev-security-blog-automation'

// Azure OpenAI Configuration - 환경 변수에서 읽어옴
param azureOpenAIEndpoint = readEnvironmentVariable('AZURE_OPENAI_ENDPOINT', '')
param azureOpenAIKey = readEnvironmentVariable('AZURE_OPENAI_KEY', '')
param azureOpenAIDeployment = 'gpt-4o'
```

> **💡 참고**: Storage Account 이름은 Azure 전체에서 고유해야 합니다. 이미 사용 중이면 다른 이름을 사용하세요.

### 6.4 Bicep 템플릿 유효성 검사

```powershell
# infra 디렉토리로 이동
cd infra/bicep

# 템플릿 유효성 검사
az deployment group validate `
    --resource-group $RESOURCE_GROUP `
    --template-file main.bicep `
    --parameters parameters/dev.bicepparam

# 성공 시 출력 없음, 실패 시 오류 메시지 표시
```

### 6.5 What-If 분석 (배포 미리보기)

실제 배포 전에 어떤 리소스가 생성/변경되는지 확인합니다:

```powershell
# What-If 분석 실행
az deployment group what-if `
    --resource-group $RESOURCE_GROUP `
    --template-file main.bicep `
    --parameters parameters/dev.bicepparam
```

**예상 출력**:
```
Resource and property changes are indicated with these symbols:
  + Create
  ~ Modify
  - Delete

The deployment will update the following scope:
Scope: /subscriptions/xxx/resourceGroups/rg-security-blog-automation-dev

  + Microsoft.Storage/storageAccounts/stdevsecurityblog [2023-01-01]
  + Microsoft.Web/serverfarms/func-dev-security-blog-automation-plan [2023-01-01]
  + Microsoft.Web/sites/func-dev-security-blog-automation [2023-01-01]
  ...
```

### 6.6 인프라 배포 실행

```powershell
# 배포 실행 (10-15분 소요)
$DEPLOYMENT_NAME = "security-blog-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

az deployment group create `
    --name $DEPLOYMENT_NAME `
    --resource-group $RESOURCE_GROUP `
    --template-file main.bicep `
    --parameters parameters/dev.bicepparam `
    --verbose

# 배포 상태 확인
az deployment group show `
    --name $DEPLOYMENT_NAME `
    --resource-group $RESOURCE_GROUP `
    --query "properties.provisioningState" `
    --output tsv
```

**성공 시**: `Succeeded`

### 6.7 배포 결과 확인

```powershell
# 배포된 리소스 목록 확인
az resource list `
    --resource-group $RESOURCE_GROUP `
    --output table

# 배포 출력값 확인
az deployment group show `
    --name $DEPLOYMENT_NAME `
    --resource-group $RESOURCE_GROUP `
    --query "properties.outputs" `
    --output json
```

**예상 출력**:
```
Name                                         ResourceGroup                         Location      Type                                     
-------------------------------------------  ------------------------------------  ------------  ----------------------------------
stdevsecurityblog                            rg-security-blog-automation-dev        koreacentral  Microsoft.Storage/storageAccounts
func-dev-security-blog-automation-plan       rg-security-blog-automation-dev        koreacentral  Microsoft.Web/serverfarms
func-dev-security-blog-automation            rg-security-blog-automation-dev        koreacentral  Microsoft.Web/sites
logic-dev-security-blog-automation-plan      rg-security-blog-automation-dev        koreacentral  Microsoft.Web/serverfarms
logic-dev-security-blog-automation           rg-security-blog-automation-dev        koreacentral  Microsoft.Web/sites
...
```

### 6.8 deploy.ps1 스크립트 사용 (대안)

위 단계를 자동화한 스크립트도 제공됩니다:

```powershell
# infra 디렉토리에서 실행
cd infra

.\deploy.ps1 `
    -AzureOpenAIEndpoint $env:AZURE_OPENAI_ENDPOINT `
    -AzureOpenAIKey $env:AZURE_OPENAI_KEY `
    -ResourceGroupName $RESOURCE_GROUP `
    -Location "koreacentral"
```

---

## 7. Azure Functions 배포

### 7.1 프로젝트 구조 이해

```
functions/
├── BuildDigest.cs          # 핵심 통합 API (RSS 수집, AI 요약, 중복 체크)
├── CheckDuplicate.cs       # 중복 게시글 확인
├── GenerateEmailHtml.cs    # 이메일 HTML 생성
├── GetRecentPosts.cs       # 최근 게시글 조회
├── InsertProcessed.cs      # 처리된 게시글 저장
├── ListRssFeedItems.cs     # RSS 피드 항목 목록
├── SummarizePost.cs        # 게시글 요약
├── Program.cs              # 앱 진입점
├── ProcessedPostsApi.csproj # 프로젝트 파일
├── host.json               # Function 호스트 설정
└── local.settings.json     # 로컬 개발 설정 (Git 제외)
```

### 7.2 로컬 설정 파일 구성

```powershell
# functions 디렉토리로 이동
cd ..\functions

# local.settings.json 생성/수정
@"
{
  "IsEncrypted": false,
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "AZURE_OPENAI_ENDPOINT": "$env:AZURE_OPENAI_ENDPOINT",
    "AZURE_OPENAI_KEY": "$env:AZURE_OPENAI_KEY",
    "AZURE_OPENAI_DEPLOYMENT": "gpt-4o"
  }
}
"@ | Out-File -FilePath local.settings.json -Encoding utf8
```

### 7.3 로컬 빌드 및 테스트

```powershell
# NuGet 패키지 복원
dotnet restore

# 프로젝트 빌드
dotnet build

# 빌드 성공 확인
# 오류가 없으면 성공
```

### 7.4 릴리스 빌드 및 퍼블리시

```powershell
# Release 모드로 빌드
dotnet build --configuration Release

# 배포용 퍼블리시
dotnet publish --configuration Release --output ./publish

# 퍼블리시된 파일 확인
Get-ChildItem ./publish -Name | Select-Object -First 10
```

### 7.5 Azure에 Function App 배포

#### 방법 1: Azure Functions Core Tools 사용 (권장)

```powershell
# Function App 이름 설정
$FUNCTION_APP_NAME = "func-dev-security-blog-automation"

# Azure에 배포
func azure functionapp publish $FUNCTION_APP_NAME

# 배포 진행 상황이 표시됩니다
# 성공 시 "Deployment completed successfully" 메시지
```

**예상 출력**:
```
Getting site publishing info...
Uploading package...
Uploading 5.12 MB...
Upload completed successfully.
Deployment completed successfully.
Functions in func-dev-security-blog-automation:
    BuildDigest - [httpTrigger]
    CheckDuplicate - [httpTrigger]
    GenerateEmailHtml - [httpTrigger]
    GetRecentPosts - [httpTrigger]
    InsertProcessed - [httpTrigger]
    ListRssFeedItems - [httpTrigger]
    SummarizePost - [httpTrigger]
```

#### 방법 2: ZIP 배포 사용

```powershell
# 퍼블리시 폴더를 ZIP으로 압축
Compress-Archive -Path ./publish/* -DestinationPath function-app.zip -Force

# Azure CLI로 ZIP 배포
az functionapp deployment source config-zip `
    --resource-group $RESOURCE_GROUP `
    --name $FUNCTION_APP_NAME `
    --src function-app.zip

# 배포 확인
az functionapp function list `
    --name $FUNCTION_APP_NAME `
    --resource-group $RESOURCE_GROUP `
    --query "[].name" `
    --output table
```

### 7.6 Function App 재시작

```powershell
# 배포 후 Function App 재시작 (권장)
az functionapp restart `
    --name $FUNCTION_APP_NAME `
    --resource-group $RESOURCE_GROUP

Write-Host "✅ Function App이 재시작되었습니다" -ForegroundColor Green
```

### 7.7 배포 검증

```powershell
# Function 목록 조회
az functionapp function list `
    --name $FUNCTION_APP_NAME `
    --resource-group $RESOURCE_GROUP `
    --output table

# Function App 상태 확인
az functionapp show `
    --name $FUNCTION_APP_NAME `
    --resource-group $RESOURCE_GROUP `
    --query "{Name:name, State:state, DefaultHostName:defaultHostName}" `
    --output table
```

**예상 출력**:
```
Name                                   State    DefaultHostName
-------------------------------------  -------  -----------------------------------------------
func-dev-security-blog-automation      Running  func-dev-security-blog-automation.azurewebsites.net
```

### 7.8 Function Key 가져오기

Logic App에서 Function을 호출할 때 필요한 키를 가져옵니다:

```powershell
# Function App의 Host Key 가져오기
$FUNCTION_KEY = $(az functionapp keys list `
    --name $FUNCTION_APP_NAME `
    --resource-group $RESOURCE_GROUP `
    --query "functionKeys.default" `
    --output tsv)

# 키 확인 (일부만 표시)
Write-Host "Function Key: $($FUNCTION_KEY.Substring(0,20))..."

# 환경 변수로 저장
$env:FUNCTION_KEY = $FUNCTION_KEY
```

### 7.9 Function 테스트

```powershell
# Function App URL
$FUNCTION_APP_URL = "https://$FUNCTION_APP_NAME.azurewebsites.net"

# CheckDuplicate 테스트
$testBody = @{
    link = "https://example.com/test-post"
    sourceName = "Test"
} | ConvertTo-Json

$response = Invoke-RestMethod `
    -Uri "$FUNCTION_APP_URL/api/CheckDuplicate?code=$FUNCTION_KEY" `
    -Method POST `
    -Body $testBody `
    -ContentType "application/json"

Write-Host "테스트 결과:" -ForegroundColor Cyan
$response | ConvertTo-Json
```

**예상 출력**:
```json
{
    "isDuplicate": false,
    "link": "https://example.com/test-post"
}
```

---

## 8. Logic App 워크플로우 구성

### 8.1 Logic App (Consumption) 이해

이 프로젝트는 **Logic App (Consumption)** 을 사용합니다:

| 특성 | Consumption |
|------|-------------|
| 호스팅 | 공유 인프라 (서버리스) |
| 가격 | 실행 건당 과금 |
| 성능 | 콜드 스타트 있음, HTTP 120초 제한 |
| 워크플로우 | 앱당 하나의 워크플로우 |
| 개발 | Portal + ARM/Bicep |

### 8.2 Azure Portal에서 Logic App 열기

```powershell
# Logic App URL 가져오기
$LOGIC_APP_NAME = "logic-dev-security-blog-automation"

az webapp show `
    --name $LOGIC_APP_NAME `
    --resource-group $RESOURCE_GROUP `
    --query "defaultHostName" `
    --output tsv

# 브라우저에서 Azure Portal 열기
Start-Process "https://portal.azure.com/#@/resource/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$LOGIC_APP_NAME/logicApp"
```

### 8.3 워크플로우 파라미터 설정

Logic App (Consumption)에서 파라미터를 설정합니다:

1. Azure Portal에서 Logic App 열기
2. 왼쪽 메뉴에서 **Settings** → **Configuration** 클릭
3. **Application settings** 탭에서 다음 설정 추가:

| 이름 | 값 |
|------|-----|
| `functionsAppUrl` | `https://func-dev-security-blog-automation.azurewebsites.net` |
| `functionKey` | (위에서 가져온 Function Key) |
| `emailRecipient` | (이메일 수신자 주소) |

**Azure CLI로 설정**:
```powershell
# Logic App 설정 추가
az webapp config appsettings set `
    --name $LOGIC_APP_NAME `
    --resource-group $RESOURCE_GROUP `
    --settings `
        functionsAppUrl="https://$FUNCTION_APP_NAME.azurewebsites.net" `
        functionKey="$FUNCTION_KEY" `
        emailRecipient="your-email@example.com"
```

### 8.4 워크플로우 JSON 배포

#### 8.4.1 Security 워크플로우 (Logic App #1)

```powershell
# 프로젝트 루트로 이동
cd ..

# 워크플로우 JSON 확인
$workflowPath = "infra/logic-app/workflow.json"
if (Test-Path $workflowPath) {
    Write-Host "✅ Security 워크플로우 파일 존재: $workflowPath" -ForegroundColor Green
} else {
    Write-Host "❌ 워크플로우 파일을 찾을 수 없습니다" -ForegroundColor Red
}
```

**Portal에서 워크플로우 임포트**:

1. Azure Portal → Logic App (`logic-dev-security-blog-automation`)
2. 왼쪽 메뉴 **Workflows** → **+ Add**
3. 워크플로우 이름: `security-blog-workflow`
4. 상태: `Stateful`
5. **Create** 클릭
6. 생성된 워크플로우 클릭 → **Designer** 열기
7. **Code view** 클릭
8. `infra/logic-app/workflow.json`의 `definition` 부분 복사/붙여넣기
9. **Save**

#### 8.4.2 Azure/Cloud 워크플로우 (Logic App #2)

같은 방식으로 두 번째 Logic App에 워크플로우를 추가합니다:

```powershell
$LOGIC_APP_AZURE_NAME = "logic-dev-azure-cloud-blog-automation"

# 워크플로우 JSON 확인
$azureWorkflowPath = "infra/logic-app/workflow-azure-cloud.json"
if (Test-Path $azureWorkflowPath) {
    Write-Host "✅ Azure/Cloud 워크플로우 파일 존재" -ForegroundColor Green
}
```

### 8.5 RSS 피드 파라미터 설정

각 Logic App의 워크플로우에서 RSS 피드 URL을 파라미터로 설정합니다.

**Security 워크플로우 RSS 피드 (5개)**:
```json
{
  "rssFeedUrls": [
    {"sourceName": "Microsoft Security Blog", "url": "https://www.microsoft.com/en-us/security/blog/feed/", "emoji": "🛡️"},
    {"sourceName": "Microsoft Sentinel Blog", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=MicrosoftSentinelBlog", "emoji": "🔐"},
    {"sourceName": "Microsoft Defender Blog", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=MicrosoftDefenderBlog", "emoji": "🛡️"},
    {"sourceName": "Zero Trust Blog", "url": "https://www.microsoft.com/en-us/security/blog/topic/zero-trust/feed/", "emoji": "🌐"},
    {"sourceName": "Identity Blog", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=Identity", "emoji": "🔑"}
  ]
}
```

**Azure/Cloud 워크플로우 RSS 피드 (7개)**:
```json
{
  "rssFeedUrls": [
    {"sourceName": "Azure Blog", "url": "https://azure.microsoft.com/en-us/blog/feed/", "emoji": "☁️"},
    {"sourceName": "Azure DevOps Blog", "url": "https://devblogs.microsoft.com/devops/feed/", "emoji": "🔧"},
    {"sourceName": "Fabric Blog", "url": "https://blog.fabric.microsoft.com/en-us/blog/feed/", "emoji": "📊"},
    {"sourceName": "Azure Infrastructure Blog", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=AzureInfrastructureBlog", "emoji": "🏗️"},
    {"sourceName": "Microsoft 365 Dev Blog", "url": "https://devblogs.microsoft.com/microsoft365dev/feed/", "emoji": "🔨"},
    {"sourceName": "Power Platform Blog", "url": "https://cloudblogs.microsoft.com/powerplatform/feed/", "emoji": "⚡"},
    {"sourceName": "Azure AI Foundry Blog", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=azure-ai-foundry-blog", "emoji": "🤖"}
  ]
}
```

### 8.6 반복(Recurrence) 트리거 설정

각 워크플로우의 트리거 스케줄을 설정합니다:

**Security 워크플로우**:
- 빈도: Day
- 간격: 1  
- 시간대: (UTC+09:00) Seoul
- 실행 시간: 7, 19 (07:00, 19:00 KST)

**Azure/Cloud 워크플로우**:
- 빈도: Day
- 간격: 1
- 시간대: (UTC+09:00) Seoul
- 실행 시간: 8, 20 (08:00, 20:00 KST)

---

## 9. Office 365 이메일 연결 설정

### 9.1 API 연결(Connection) 생성

Logic App에서 Office 365를 통해 이메일을 보내려면 API 연결을 생성해야 합니다.

**Portal에서 설정**:

1. Azure Portal → Logic App 열기
2. 왼쪽 메뉴 **Workflows** → 워크플로우 선택 → **Designer**
3. 워크플로우에서 **Send an email (V2)** 액션 찾기
4. **Change connection** 클릭
5. **Add new** → Office 365 계정으로 로그인
6. 권한 동의 → **Save**

### 9.2 API 연결 확인

```powershell
# 생성된 API 연결 확인
az resource list `
    --resource-group $RESOURCE_GROUP `
    --resource-type "Microsoft.Web/connections" `
    --output table
```

**예상 출력**:
```
Name                                ResourceGroup                         Location      Type
----------------------------------  ------------------------------------  ------------  -------------------------
office365-dev-security-blog         rg-security-blog-automation-dev       koreacentral  Microsoft.Web/connections
```

### 9.3 이메일 수신자 설정

워크플로우 파라미터에서 이메일 수신자를 설정합니다:

```powershell
# emailRecipient 파라미터 업데이트
az webapp config appsettings set `
    --name $LOGIC_APP_NAME `
    --resource-group $RESOURCE_GROUP `
    --settings emailRecipient="your-email@company.com"
```

---

## 10. 테스트 및 검증

### 10.1 Function App 개별 테스트

```powershell
# 변수 설정
$FUNCTION_APP_URL = "https://func-dev-security-blog-automation.azurewebsites.net"

# 1. CheckDuplicate 테스트
Write-Host "=== CheckDuplicate 테스트 ===" -ForegroundColor Cyan
$checkBody = @{
    link = "https://www.microsoft.com/test-$(Get-Random)"
    sourceName = "Test"
} | ConvertTo-Json

$checkResult = Invoke-RestMethod `
    -Uri "$FUNCTION_APP_URL/api/CheckDuplicate?code=$env:FUNCTION_KEY" `
    -Method POST `
    -Body $checkBody `
    -ContentType "application/json"

Write-Host "결과: isDuplicate = $($checkResult.isDuplicate)"

# 2. BuildDigest 테스트 (메인 API)
Write-Host "`n=== BuildDigest 테스트 ===" -ForegroundColor Cyan
$digestBody = @{
    rssFeedUrls = @(
        @{
            sourceName = "Azure Blog"
            url = "https://azure.microsoft.com/en-us/blog/feed/"
            emoji = "☁️"
        }
    )
    daysSince = 7
    maxItems = 3
    newWindowHours = 24
    scheduleText = "테스트"
} | ConvertTo-Json -Depth 3

$digestResult = Invoke-RestMethod `
    -Uri "$FUNCTION_APP_URL/api/BuildDigest?code=$env:FUNCTION_KEY" `
    -Method POST `
    -Body $digestBody `
    -ContentType "application/json"

Write-Host "결과: $($digestResult.feedStatuses.Count)개 피드 처리됨"
Write-Host "새 게시글: $($digestResult.newPostsCount)개"
```

### 10.2 Logic App 수동 실행

Azure Portal에서 Logic App을 수동으로 트리거합니다:

1. Azure Portal → Logic App 열기
2. **Workflows** → 워크플로우 선택
3. **Overview** 탭
4. **Run Trigger** → **Run** 클릭

### 10.3 실행 기록 확인

```powershell
# Logic App 실행 기록 확인 (Portal에서 확인 권장)
Write-Host "Azure Portal에서 Logic App 실행 기록을 확인하세요:" -ForegroundColor Yellow
Write-Host "https://portal.azure.com/#@/resource/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$LOGIC_APP_NAME/logicApp"
```

### 10.4 이메일 수신 확인

1. 설정한 이메일 수신자의 받은 편지함 확인
2. 이메일 제목 형식: `[Microsoft Azure 업데이트] 최근 게시글 요약 (신규 X개)`
3. 이메일 내용:
   - 게시글 목록 (피드별 그룹화)
   - 영문 요약
   - 한국어 AI 인사이트
   - 원문 링크

### 10.5 Table Storage 데이터 확인

```powershell
# Storage Account 연결 문자열 가져오기
$STORAGE_ACCOUNT = "stdevsecurityblog"
$STORAGE_KEY = $(az storage account keys list `
    --account-name $STORAGE_ACCOUNT `
    --resource-group $RESOURCE_GROUP `
    --query "[0].value" `
    --output tsv)

# ProcessedPosts 테이블 데이터 조회
az storage entity query `
    --account-name $STORAGE_ACCOUNT `
    --account-key $STORAGE_KEY `
    --table-name ProcessedPosts `
    --num-results 5 `
    --query "items[].{PartitionKey:PartitionKey, Title:Title, ProcessedDate:ProcessedDate}" `
    --output table
```

---

## 11. 모니터링 설정

### 11.1 Application Insights 대시보드

```powershell
# Application Insights 리소스 정보
$APP_INSIGHTS = "appi-dev-security-blog-automation"

az monitor app-insights component show `
    --app $APP_INSIGHTS `
    --resource-group $RESOURCE_GROUP `
    --query "{Name:name, ConnectionString:connectionString}" `
    --output table
```

### 11.2 로그 쿼리 예시

```powershell
# 최근 1시간 Function 호출 로그
az monitor app-insights query `
    --app $APP_INSIGHTS `
    --resource-group $RESOURCE_GROUP `
    --analytics-query "requests | where timestamp > ago(1h) | summarize count() by name | order by count_ desc"

# 오류 로그 확인
az monitor app-insights query `
    --app $APP_INSIGHTS `
    --resource-group $RESOURCE_GROUP `
    --analytics-query "exceptions | where timestamp > ago(24h) | project timestamp, type, outerMessage | order by timestamp desc | take 10"
```

### 11.3 알림 규칙 설정 (선택사항)

```powershell
# Function 실패 시 이메일 알림 설정
az monitor metrics alert create `
    --name "FunctionAppFailureAlert" `
    --resource-group $RESOURCE_GROUP `
    --scopes "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$FUNCTION_APP_NAME" `
    --condition "total Http5xx > 5" `
    --window-size 5m `
    --evaluation-frequency 1m `
    --action-group "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Insights/actionGroups/EmailAlerts" `
    --description "Function App HTTP 5xx errors exceeded threshold"
```

---

## 12. 문제 해결 가이드

### 12.1 일반적인 문제 및 해결책

#### 문제 1: Function App 배포 실패
```
Error: Deployment failed with status code 'Conflict'
```

**해결책**:
```powershell
# Function App 재시작 후 재배포
az functionapp restart --name $FUNCTION_APP_NAME --resource-group $RESOURCE_GROUP
Start-Sleep -Seconds 30
func azure functionapp publish $FUNCTION_APP_NAME
```

#### 문제 2: Azure OpenAI API 호출 실패
```
Error: 401 Unauthorized
```

**해결책**:
```powershell
# API 키 재확인
az cognitiveservices account keys list `
    --name $OPENAI_NAME `
    --resource-group $RESOURCE_GROUP

# Function App 환경 변수 업데이트
az functionapp config appsettings set `
    --name $FUNCTION_APP_NAME `
    --resource-group $RESOURCE_GROUP `
    --settings AZURE_OPENAI_KEY="새로운키"
```

#### 문제 3: Logic App 워크플로우 실행 실패
```
Error: The workflow run action 'HTTP' had a failure...
```

**해결책**:
1. Function App URL 및 Key 확인
2. Logic App Designer에서 HTTP 액션의 URI 확인
3. Function App 로그 확인: Application Insights → Logs

#### 문제 4: RSS 피드 읽기 실패
```
Error: 403 Forbidden
```

**해결책**:
- 일부 RSS 피드는 봇 차단이 있을 수 있음
- User-Agent 헤더가 설정되어 있는지 확인
- 다른 RSS 피드 URL로 교체

#### 문제 5: 이메일 발송 실패
```
Error: The connector 'office365' is not connected
```

**해결책**:
1. Azure Portal → Logic App → Workflows → 워크플로우
2. Designer 열기
3. Send email 액션에서 **Fix connection** 클릭
4. Office 365 계정으로 재로그인

### 12.2 로그 확인 방법

```powershell
# Function App 스트리밍 로그
az webapp log tail `
    --name $FUNCTION_APP_NAME `
    --resource-group $RESOURCE_GROUP

# Application Insights 실시간 메트릭
# Portal에서: Application Insights → Live Metrics
```

### 12.3 리소스 상태 확인

```powershell
# 모든 리소스 상태 확인
az resource list `
    --resource-group $RESOURCE_GROUP `
    --query "[].{Name:name, Type:type, ProvisioningState:provisioningState}" `
    --output table
```

---

## 13. 운영 및 유지보수

### 13.1 일일 점검 사항

- [ ] 이메일이 정상적으로 수신되는지 확인
- [ ] Application Insights에서 오류 로그 확인
- [ ] Logic App 실행 기록에서 실패한 실행 확인

### 13.2 RSS 피드 추가/수정

```powershell
# config/rss-feeds-config.json 수정
code config/rss-feeds-config.json

# 변경 후 Logic App 파라미터 업데이트
python update-all-params.py
python deploy-params-to-azure.py
```

### 13.3 비용 모니터링

```powershell
# 리소스 그룹 비용 확인 (Portal 권장)
az consumption usage list `
    --start-date $(Get-Date).AddDays(-30).ToString("yyyy-MM-dd") `
    --end-date $(Get-Date).ToString("yyyy-MM-dd") `
    --query "[?contains(instanceName, 'security-blog')].{Resource:instanceName, Cost:pretaxCost}" `
    --output table
```

**예상 월간 비용**:
| 리소스 | 예상 비용 (USD) |
|--------|-----------------|
| Function App (Consumption) | $0-5 |
| Logic App (Standard WS1) | $150-200 |
| Storage Account | $1-5 |
| Application Insights | $5-20 |
| Azure OpenAI (GPT-4o) | $10-50 |
| **총계** | **$170-280** |

### 13.4 리소스 정리 (삭제)

프로젝트를 더 이상 사용하지 않을 때:

```powershell
# ⚠️ 주의: 이 명령은 모든 리소스를 삭제합니다!
# 리소스 그룹 삭제 (모든 리소스 포함)
az group delete `
    --name $RESOURCE_GROUP `
    --yes `
    --no-wait

Write-Host "리소스 그룹 삭제가 시작되었습니다. 완료까지 몇 분 소요됩니다." -ForegroundColor Yellow
```

---

## 🎉 축하합니다!

이 가이드를 완료하셨다면 다음을 성공적으로 구축한 것입니다:

- ✅ Azure 인프라 (IaC with Bicep)
- ✅ Azure Functions (.NET 8 Isolated)
- ✅ Logic App (Consumption) 워크플로우 2개
- ✅ Azure OpenAI 통합
- ✅ Office 365 이메일 발송
- ✅ Application Insights 모니터링

**다음 단계로 추천**:
- 🔒 [Azure Key Vault](https://learn.microsoft.com/azure/key-vault/)로 비밀 관리 개선
- 🚀 [GitHub Actions](https://github.com/features/actions)로 CI/CD 파이프라인 구축
- 📊 [Azure Monitor Workbooks](https://learn.microsoft.com/azure/azure-monitor/visualize/workbooks-overview)로 대시보드 생성

---

## 📚 참고 자료

- [Azure Functions 개발자 가이드](https://learn.microsoft.com/azure/azure-functions/)
- [Logic Apps (Standard) 문서](https://learn.microsoft.com/azure/logic-apps/single-tenant-overview-compare)
- [Bicep 언어 참조](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)
- [Azure OpenAI Service 문서](https://learn.microsoft.com/azure/ai-services/openai/)
- [프로젝트 GitHub 리포지토리](https://github.com/zer0big/azure-security-blog-automation)

---

**문서 작성**: GitHub Copilot  
**최종 검토**: 2026년 1월 31일
