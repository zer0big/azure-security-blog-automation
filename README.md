# Azure Security Blog Automation

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Azure](https://img.shields.io/badge/Azure-Logic%20Apps-0078D4?logo=microsoftazure)](https://azure.microsoft.com/services/logic-apps/)
[![OpenAI](https://img.shields.io/badge/Azure-OpenAI-412991?logo=openai)](https://azure.microsoft.com/products/ai-services/openai-service)

> **Microsoft Security Tech Community 블로그를 매일 자동으로 요약하여 이메일로 발송하는 서버리스 자동화 시스템**

## 📋 프로젝트 개요

이 프로젝트는 Azure Logic Apps를 활용하여 Microsoft Security Tech Community의 보안 관련 블로그 게시물을 자동으로 수집, Azure OpenAI(GPT-4)로 요약하고, Office 365를 통해 이메일로 발송하는 완전 서버리스 솔루션입니다.

### 🎯 주요 기능

- 📰 **자동 RSS 수집**: Microsoft Security Tech Community 블로그 매일 자동 확인
- 🤖 **AI 요약**: Azure OpenAI GPT-4로 핵심 내용 자동 요약
- 📧 **이메일 발송**: Office 365 Outlook을 통한 자동 이메일 발송
- ⏰ **스케줄링**: 매일 오전 9시(KST) 자동 실행
- 💰 **비용 최적화**: Consumption 요금제로 월 $0.72~$7 수준

## 🏗️ 아키텍처

```
┌─────────────────────────────────────────────────────────────────┐
│                     Azure Logic Apps (Consumption)              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────┐    ┌──────────────┐    ┌─────────────────────┐  │
│  │   RSS    │───▶│ Azure OpenAI │───▶│ Office 365 Outlook  │  │
│  │ Trigger  │    │   (GPT-4)    │    │   (Email Sender)    │  │
│  └──────────┘    └──────────────┘    └─────────────────────┘  │
│       │                  │                        │             │
│  매일 09:00         AI 요약                   이메일 발송      │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                            │
                    ┌───────┴────────┐
                    │                │
              ┌─────▼─────┐   ┌─────▼──────┐
              │ Key Vault │   │   Managed  │
              │ (Secrets) │   │  Identity  │
              └───────────┘   └────────────┘
```

### 🔧 기술 스택

| 구성 요소 | 서비스 | 역할 |
|----------|--------|------|
| **오케스트레이션** | Azure Logic Apps (Consumption) | 워크플로 관리 및 스케줄링 |
| **AI 요약** | Azure OpenAI (GPT-4) | 블로그 콘텐츠 요약 |
| **이메일 발송** | Office 365 Outlook | 요약 메일 전송 |
| **보안** | Azure Key Vault | API 키 관리 |
| **인증** | Managed Identity | 안전한 리소스 접근 |
| **IaC** | Azure Bicep | 인프라 코드화 |
| **배포** | GitHub Actions + azd | CI/CD 자동화 |

## 🚀 빠른 시작

### 사전 요구사항

- Azure 구독 (무료 평가판 가능)
- Azure CLI ([설치 가이드](https://learn.microsoft.com/cli/azure/install-azure-cli))
- Azure Developer CLI ([설치 가이드](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd))
- Office 365 계정 (Exchange Online 라이선스)
- Git

### 배포 단계

#### 1. 리포지토리 클론

```bash
git clone https://github.com/zer0big/azure-security-blog-automation.git
cd azure-security-blog-automation
```

#### 2. Azure 로그인

```bash
az login
azd auth login
```

#### 3. 환경 초기화

```bash
azd init
```

#### 4. 배포 실행

```bash
azd up
```

배포 과정에서 다음 정보를 입력합니다:
- Azure 구독 선택
- 배포 리전 (권장: Korea Central)
- Office 365 발신 이메일 주소
- 수신자 이메일 주소

#### 5. Logic Apps 승인

배포 후 Azure Portal에서 Office 365 Outlook 커넥터 승인 필요:

1. Azure Portal → Logic Apps → 리소스 선택
2. 왼쪽 메뉴 → API 연결
3. Office 365 연결 승인

## 📂 프로젝트 구조

```
azure-security-blog-automation/
├── .github/
│   └── workflows/          # GitHub Actions CI/CD 파이프라인
│       └── deploy.yml
├── infra/                  # Infrastructure as Code
│   └── bicep/             # Azure Bicep 템플릿
│       ├── main.bicep     # 메인 인프라 정의
│       ├── parameters.json
│       └── modules/       # 재사용 가능한 모듈
├── workflows/             # Logic Apps 워크플로 정의
│   └── security-blog-summarizer.json
├── docs/                  # 문서
│   ├── architecture.md
│   └── deployment.md
├── .gitignore
├── README.md
├── LICENSE
└── azure.yaml            # azd 구성 파일
```

## 💰 비용 분석

### 예상 월간 비용 (한국 리전 기준)

| 서비스 | 사용량 | 월 예상 비용 |
|--------|--------|-------------|
| Logic Apps (Consumption) | ~30 실행/월 | $0.12 |
| Azure OpenAI (GPT-4) | ~300 토큰/일 | $0.60 |
| Office 365 Outlook | API 호출 무료 | $0.00 |
| Key Vault | 30 read ops/월 | $0.00 |
| **합계** | | **$0.72/월** |

> 💡 실제 비용은 블로그 게시물 수 및 요약 길이에 따라 변동 가능 ($0.72~$7/월)

## 🔒 보안 (Well-Architected Framework 준수)

### 적용된 보안 원칙

- ✅ **Managed Identity**: API 키 대신 관리 ID 사용
- ✅ **Key Vault 통합**: 민감 정보 안전한 저장
- ✅ **최소 권한 원칙**: 필요한 권한만 부여
- ✅ **진단 로깅**: 모든 작업 감사 추적
- ✅ **HTTPS 전용**: 모든 통신 암호화

### 권장 사항 (프로덕션 배포 시)

- [ ] Private Endpoint 구성 (네트워크 격리)
- [ ] Azure Policy 적용 (거버넌스)
- [ ] Multi-region 배포 (고가용성)
- [ ] Application Insights 통합 (모니터링)

## 📊 모니터링

### Application Insights 대시보드

배포 후 다음 메트릭 확인 가능:

- 워크플로 실행 성공률
- Azure OpenAI 응답 시간
- 이메일 발송 성공 여부
- 비용 추적

### 알림 설정

다음 이벤트 발생 시 알림:

- 3회 연속 실행 실패
- OpenAI API 429 에러 (Rate Limit)
- 월간 비용 $10 초과

## 🛠️ 트러블슈팅

<details>
<summary><strong>Logic Apps 승인 오류</strong></summary>

**문제**: Office 365 커넥터 승인되지 않음

**해결**:
```bash
# Azure Portal에서 수동 승인
1. Logic Apps 리소스 → API 연결
2. Office365 연결 클릭 → 승인
```
</details>

<details>
<summary><strong>OpenAI Rate Limit 에러</strong></summary>

**문제**: 429 Too Many Requests

**해결**:
- Logic Apps에 재시도 정책 추가 (자동 구성됨)
- OpenAI TPM(Tokens Per Minute) 한도 증가 요청
</details>

<details>
<summary><strong>이메일 발송 실패</strong></summary>

**문제**: 이메일이 수신되지 않음

**해결**:
1. Office 365 라이선스 확인 (Exchange Online 필요)
2. 스팸 폴더 확인
3. 발신자 이메일 주소 유효성 검증
</details>

## 📚 참고 자료

### 공식 문서

- [Azure Logic Apps 문서](https://learn.microsoft.com/azure/logic-apps/)
- [Azure OpenAI 서비스](https://learn.microsoft.com/azure/ai-services/openai/)
- [Azure Well-Architected Framework](https://learn.microsoft.com/azure/well-architected/)

### 관련 프로젝트

- [Azure Logic Apps 샘플](https://github.com/Azure/logicapps)
- [Azure Bicep 템플릿](https://github.com/Azure/bicep)

## 🤝 기여

기여는 언제나 환영합니다! 다음 절차를 따라주세요:

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 라이선스

이 프로젝트는 MIT 라이선스 하에 배포됩니다. 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.

## 👤 작성자

**Kim Young Dae (zer0big)**
- GitHub: [@zer0big](https://github.com/zer0big)
- Company: Bespin Global
- Role: Microsoft Azure MVP, MigAzure Team Lead

## 🙏 감사의 말

- Microsoft Security Tech Community
- Azure Logic Apps Team
- Azure OpenAI Team

---

⭐ 이 프로젝트가 도움이 되었다면 Star를 눌러주세요!
