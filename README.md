# Azure Security Blog Automation

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Azure](https://img.shields.io/badge/Azure-Logic%20Apps-0078D4?logo=microsoftazure)](https://azure.microsoft.com/services/logic-apps/)
[![OpenAI](https://img.shields.io/badge/Azure-OpenAI-412991?logo=openai)](https://azure.microsoft.com/products/ai-services/openai-service)

> **Microsoft Security Blog를 매일 자동으로 3줄 요약하여 이메일로 발송하는 서버리스 자동화 시스템**

## 📋 프로젝트 개요

이 프로젝트는 Azure Logic Apps를 활용하여 Microsoft Security Blog의 보안 관련 게시물을 자동으로 수집, Azure OpenAI(GPT-4o)로 3줄 한글 요약하고, Office 365를 통해 HTML 이메일로 발송하는 완전 서버리스 솔루션입니다.

### 🎯 주요 기능

- 📰 **자동 RSS 수집**: Microsoft Security Blog 매일 자동 확인
- 🔄 **Multi-RSS 지원**: 여러 보안 블로그를 통합 모니터링 (확장 가능한 구조)
- 🤖 **AI 3줄 요약**: Azure OpenAI GPT-4o로 핵심 내용 한글 3줄 요약
- 📧 **HTML 이메일**: 카드 레이아웃 스타일의 반응형 이메일 발송 (가시성 최적화)
- 🏷️ **소스 배지**: 각 게시물의 출처를 시각적으로 표시
- ⏰ **스케줄링**: 매일 07:00, 14:00, 21:00 (KST) 자동 실행
- 🔍 **스마트 중복 제거**: Azure Table Storage 기반 중복 게시물 필터링
- 💰 **비용 최적화**: Consumption 요금제로 월 $1~$5 수준
- 💾 **백업 스냅샷**: `backup_2025-12-27_final_5_feeds_with_emoji` (5개 RSS 피드, 이모지, 복원 가이드 포함) - 자세한 내용은 `.backups/RESTORE_GUIDE.md` 참고

### 💡 기대 효과

- ⏱️ **시간 절약**: 매일 보안 블로그를 수동으로 확인하는 시간 제거 (일 30분 → 0분)
- 🚀 **신속한 대응**: 중요 보안 업데이트를 놓치지 않고 즉시 파악
- 📚 **지식 공유**: 팀 전체에 보안 인사이트 자동 공유로 조직 보안 역량 강화
- 📈 **생산성 향상**: AI 기반 요약으로 핵심 내용만 빠르게 이해

## 🏗️ 아키텍처

```
┌─────────────────────────────────────────────────────────────────┐
│                     Azure Logic Apps (Consumption)              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────┐    ┌──────────────┐    ┌─────────────────────┐  │
│  │Recurrence│───▶│ Azure OpenAI │───▶│ Office 365 Outlook  │  │
│  │ Trigger  │    │   (GPT-4o)   │    │   (HTML Email)      │  │
│  └──────────┘    └──────────────┘    └─────────────────────┘  │
│       │                  │                        │             │
│  매일 07:00        3줄 한글 요약            카드 레이아웃      │
│   (KST)          (번호 + <br>)              반응형 디자인     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                            │
                    ┌───────┴────────┐
                    │                │
              ┌─────▼─────┐   ┌─────▼──────┐
              │ RSS Feed  │   │  Filtering │
              │ Microsoft │   │  24h / 5개 │
              │ Security  │   │   최신순   │
              └───────────┘   └────────────┘
```

### 🔧 기술 스택

| 구성 요소 | 서비스 | 역할 |
|----------|--------|------|
| **오케스트레이션** | Azure Logic Apps (Consumption) | 워크플로 관리 및 스케줄링 |
| **AI 요약** | Azure OpenAI GPT-4o | 블로그 3줄 한글 요약 |
| **이메일 발송** | Office 365 Outlook | 요약 메일 전송 |
| **RSS 소스** | Microsoft Security Blog / Azure Security Blog / MS Security - Threat Intelligence / TC - Microsoft Defender / TC - Microsoft Sentinel | 공식 보안 블로그 RSS (5개 피드) |
| **필터링** | Logic Apps Query/Compose | 24시간 내 or 최근 5개 |

## 🚀 빠른 시작

### 사전 요구사항

- Azure 구독 (무료 평가판 가능)
- Azure CLI ([설치 가이드](https://learn.microsoft.com/cli/azure/install-azure-cli))
- Azure Developer CLI ([설치 가이드](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd))
- Office 365 계정 (Exchange Online 라이선스)
- Git

### Function App 설정 값 (App Settings)

- `AOAI_API_KEY`: Azure OpenAI API Key (필수)
- `SUMMARY_POINTS`: 요약 포인트 개수(기본 3, 범위 1~10). Azure Portal → Function App → Configuration에서 변경하면 재배포 없이 즉시 반영됩니다.

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
├── functions/              # Azure Functions (.NET 8 Isolated)
│   └── Functions/
│       ├── CheckDuplicate.cs         # 중복 검사 API
│       ├── InsertProcessed.cs        # 처리 이력 저장
│       └── GenerateEmailHtml.cs      # HTML 이메일 생성 (색상 최적화)
├── infra/                  # Infrastructure as Code
│   └── bicep/             # Azure Bicep 템플릿
│       ├── main.bicep     # 메인 인프라 정의
│       ├── parameters.dev.json
│       ├── parameters.prod.json
│       └── modules/       # 재사용 가능한 모듈
├── workflows/             # Logic Apps 워크플로 정의
│   ├── security-blog-consolidated.json      # 단일 RSS (현재 배포)
│   └── security-blog-multi-rss.json         # Multi-RSS (확장 구조)
├── docs/                  # 문서
│   ├── OPERATIONS.md      # 운영 가이드
│   ├── TESTING.md         # 테스트 가이드
│   ├── MULTI-RSS-GUIDE.md # Multi-RSS 배포 가이드
│   └── CHANGELOG.md       # 변경 이력
├── test-blue-header.ps1   # 자동화 테스트 스크립트
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

- ✅ **Managed Identity**: SystemAssigned 관리 ID 사용 (활성화됨)
- ⚠️ **Key Vault 통합**: 미적용 — 현재 Function Key가 파라미터로 저장되어 있음; Key Vault로 이관 권장 (WI 147)
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

## � TODO / 향후 개선 계획

### 🔐 보안 강화
- [ ] **Azure Key Vault 통합**: OpenAI API 키를 Key Vault에서 관리
- [ ] **Managed Identity**: Key Vault 접근을 위한 시스템 할당 관리 ID 구성

### 📰 콘텐츠 확장
- [ ] **Tech Community RSS 추가**: Microsoft Tech Community Security Blog RSS 통합
- [ ] **다중 RSS 소스**: 여러 보안 블로그 통합 (Compose + Union)

### 🎨 UI/UX 개선
- [ ] **이메일 템플릿 최적화**: 모바일 반응형 개선
- [ ] **AI 요약 품질 향상**: Few-shot learning 프롬프트 개선

### ⚙️ 운영 개선
- [ ] **Application Insights**: 상세 모니터링 및 알림 구성
- [ ] **에러 핸들링**: 재시도 로직 및 에러 알림 추가

### 🚀 IaC 자동화
- [ ] **Azure Bicep 템플릿**: 전체 인프라 코드화
- [ ] **GitHub Actions CI/CD**: 자동 배포 파이프라인 구성

## 📄 라이선스

이 프로젝트는 MIT 라이선스 하에 배포됩니다. 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.

## 👤 작성자

**Kim Young Dae (zer0big)**
- GitHub: [@zer0big](https://github.com/zer0big)
- Company: TDG
- Role: Microsoft Azure MVP, Microsoft Certified Trainer

## 🙏 감사의 말

- Microsoft Security Blog Team
- Azure Logic Apps Team
- Azure OpenAI Team

---

⭐ 이 프로젝트가 도움이 되었다면 Star를 눌러주세요!

