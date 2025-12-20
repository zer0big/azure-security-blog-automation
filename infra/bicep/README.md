# Bicep Infrastructure as Code (IaC)

Azure Logic Apps 보안 블로그 자동 요약 시스템의 인프라 코드입니다.

## 📁 파일 구조

```
infra/bicep/
├── main.bicep              # 메인 Bicep 템플릿
├── parameters.dev.json     # 개발 환경 파라미터
├── parameters.prod.json    # 프로덕션 환경 파라미터
└── README.md              # 이 파일
```

## 🚀 배포 방법

### 1. 사전 준비

```bash
# Azure CLI 로그인
az login

# 구독 선택
az account set --subscription "your-subscription-id"

# Resource Group 생성
az group create \
  --name rg-security-blog-automation-dev \
  --location koreacentral
```

### 2. 파라미터 파일 수정

`parameters.dev.json` 또는 `parameters.prod.json` 파일을 편집하여 다음 값을 업데이트하세요:

- `emailRecipient`: 이메일 수신자 주소
- `openAiEndpoint`: Azure OpenAI 엔드포인트 (예: `https://your-openai.openai.azure.com/`)
- `openAiDeploymentName`: GPT-4 배포 이름

### 3. 배포 실행

#### 개발 환경 배포

```bash
az deployment group create \
  --resource-group rg-security-blog-automation-dev \
  --template-file main.bicep \
  --parameters @parameters.dev.json
```

#### 프로덕션 환경 배포 (What-If 검증 포함)

```bash
# What-If 검증
az deployment group what-if \
  --resource-group rg-security-blog-automation-prod \
  --template-file main.bicep \
  --parameters @parameters.prod.json

# 실제 배포
az deployment group create \
  --resource-group rg-security-blog-automation-prod \
  --template-file main.bicep \
  --parameters @parameters.prod.json \
  --confirm-with-what-if
```

### 4. 배포 검증

```bash
# 배포 결과 확인
az deployment group show \
  --resource-group rg-security-blog-automation-dev \
  --name main \
  --output table

# Logic App 상태 확인
az logic workflow show \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation
```

## 📦 배포되는 리소스

| 리소스 유형 | 이름 패턴 | 용도 |
|-----------|---------|------|
| Logic App | `logic-{env}-{project}` | 워크플로 실행 |
| Application Insights | `appi-{env}-{project}` | 모니터링 및 진단 |
| Log Analytics | `log-{env}-{project}` | 로그 저장소 |
| API Connection (Office 365) | `office365-{env}-{project}` | 이메일 발송 |
| API Connection (RSS) | `rss-{env}-{project}` | RSS 피드 읽기 |

## 🔧 배포 후 작업

### 1. Office 365 연결 인증

```bash
# Azure Portal에서 수동 인증 필요
# 1. API Connections > office365-{env}-{project} 열기
# 2. "Edit API connection" 클릭
# 3. "Authorize" 버튼 클릭하여 Office 365 계정 인증
```

### 2. Logic App 워크플로 업로드

```bash
# 워크플로 정의 파일 업로드
az logic workflow update \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --definition @../../workflows/security-blog-summarizer.json
```

### 3. Managed Identity 권한 부여

Azure OpenAI 리소스에 Logic App Managed Identity에 `Cognitive Services OpenAI User` 역할을 부여하세요.

```bash
# Logic App Managed Identity Principal ID 확인
PRINCIPAL_ID=$(az logic workflow show \
  --resource-group rg-security-blog-automation-dev \
  --name logic-dev-security-blog-automation \
  --query identity.principalId -o tsv)

# Azure OpenAI 리소스에 역할 할당
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Cognitive Services OpenAI User" \
  --scope /subscriptions/{subscription-id}/resourceGroups/{openai-rg}/providers/Microsoft.CognitiveServices/accounts/{openai-name}
```

## 💰 비용 예상

| 리소스 | 플랜 | 월 예상 비용 |
|--------|------|-------------|
| Logic App | Consumption | $0.50 |
| Azure OpenAI (GPT-4) | Pay-as-you-go | $5-6 |
| Application Insights | First 5GB free | $0 |
| Log Analytics | First 5GB free | $0 |
| **총계** | | **$5.50-6.50** |

## 🔒 보안 모범사례

- ✅ Managed Identity 사용 (API 키 하드코딩 방지)
- ✅ Diagnostic Settings 활성화 (감사 로그)
- ✅ 최소 권한 원칙 적용
- ✅ 환경별 Resource Group 분리
- ✅ 태그 전략 적용 (비용 추적)

## 📚 참고 자료

- [Azure Logic Apps Bicep 참조](https://learn.microsoft.com/azure/templates/microsoft.logic/workflows)
- [Bicep 모범사례](https://learn.microsoft.com/azure/azure-resource-manager/bicep/best-practices)
- [Logic Apps Managed Identity](https://learn.microsoft.com/azure/logic-apps/create-managed-service-identity)
- [Azure 명명 규칙](https://learn.microsoft.com/azure/cloud-adoption-framework/ready/azure-best-practices/resource-naming)
