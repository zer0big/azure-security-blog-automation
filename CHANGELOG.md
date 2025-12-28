# Changelog

모든 주요 변경사항은 이 파일에 문서화됩니다.

## [2.0.0] - 2025-12-28

### 🎉 주요 변경사항
- **이중 워크플로우 아키텍처**: Security Blog와 Azure/Cloud Blog 별도 운영
- **총 11개 RSS 피드로 확장** (기존 5개 → Security 5개 + Azure 6개)
- **이모지 기반 소스 표시**: 각 피드별 고유 이모지 아이콘

### ✨ 추가 기능

#### 새로운 Logic App
- **logic-dev-azure-cloud-blog-automation**: Azure/클라우드 전문 워크플로우 추가
  - 6개 Azure 관련 RSS 피드 모니터링
  - 스케줄: 08:00, 16:00, 23:00 (KST)

#### 새로운 RSS 피드 (Azure/Cloud)
- 🔧 Azure DevOps Blog
- 📊 Azure Architecture Blog
- 🏗️ Azure Infrastructure Blog
- 🏢 Azure Governance and Management Blog
- 🔨 Azure DevOps Community
- ⚡ Azure Integration Services Blog

#### 새로운 RSS 피드 (Security)
- 🎯 Threat Intelligence
- 💡 Cybersecurity Insights

#### 기능 개선
- **Emoji 필드 추가**: BlogPost 모델에 emoji 속성 추가
- **동적 이모지 매핑**: SourceEmojiHelper 클래스로 피드별 이모지 자동 할당
- **런타임 파라미터 관리**: Logic App 파라미터 직접 업데이트 기능

### 🔄 변경사항

#### 제거된 RSS 피드
- ❌ Microsoft Defender Blog (Tech Community 안정성 문제)
- ❌ Identity & Access Blog (Tech Community 안정성 문제)
- ❌ Azure Updates (RSS 미제공 - HTML만 반환)

#### 코드 개선
- `GenerateEmailHtml.cs`: 
  - Emoji 속성 파싱 로직 추가 (대소문자 무관)
  - SourceEmojiHelper를 통한 fallback 메커니즘
- `infra/bicep/modules/logic-app-azure-cloud.bicep`: 신규 Logic App 모듈
- `infra/logic-app/workflow-azure-cloud.json`: 신규 워크플로우 정의

### 🛠️ 운영 도구

#### 새로운 Python 스크립트
- `fix-params-complete.py`: Logic App 런타임 파라미터 완전 업데이트
- `fix-azure-feeds.py`: Azure Logic App 피드 구성 수정
- `validate-azure-feeds.py`: RSS 피드 URL 검증 도구
- `update-feeds-correct.py`: 워크플로우 정의 자동 업데이트

### 🐛 버그 수정

#### Logic App 파라미터 동기화 문제
- **문제**: `definition.parameters`와 `parameters` 분리로 인한 런타임 값 불일치
- **해결**: `az logic workflow update --set parameters=@file` 명령으로 런타임 값 직접 업데이트

#### 이모지 표시 누락
- **문제**: BlogPost 클래스에 Emoji 속성 없음
- **해결**: C# 모델 확장 + JSON 파싱 로직 개선 + Function App 재배포

#### UTF-8 인코딩 문제
- **문제**: 한글 scheduleText PowerShell 전달 시 깨짐
- **해결**: scheduleText 파라미터화 + 영문 사용

### 📊 테스트 결과
- ✅ Security Logic App: 모든 액션 Succeeded
- ✅ Azure/Cloud Logic App: 모든 액션 Succeeded
- ✅ 이메일 생성 및 발송 정상 동작
- ✅ 이모지 정상 표시 확인
- ✅ RSS 피드 모든 URL 검증 완료

### 📚 문서 업데이트
- README.md: 이중 워크플로우 아키텍처 반영
- .gitignore: 임시 파일 패턴 추가

---

## [1.0.0] - 2025-12-27

### 초기 릴리스
- Azure Logic App 기반 자동화 시스템
- 5개 Microsoft 보안 RSS 피드 모니터링
- Azure OpenAI GPT-4o 기반 AI 요약
- Azure Table Storage 중복 제거
- Office 365 이메일 자동 발송
- 일 3회 스케줄 실행 (07:00, 15:00, 22:00 KST)
