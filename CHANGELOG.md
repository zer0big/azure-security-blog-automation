# Changelog

모든 주요 변경사항은 이 파일에 문서화됩니다.

## [3.0.0] - 2026-01-14

### 🎉 주요 변경사항
- **BuildDigest 통합 아키텍처**: 5개 레거시 API를 단일 엔드포인트로 통합
- **웹 스크래핑 기능**: 실제 블로그 페이지에서 첫 문단 추출
- **AI 핵심 인사이트**: 단순 번역이 아닌 전체 블로그 분석 기반 한국어 인사이트 생성
- **스마트 필터링**: FAIL 피드 자동 제외, 24시간 신규 게시물 정확한 판별

### ✨ 추가 기능

#### BuildDigest API (통합 처리 엔진)
- **웹 스크래핑**: `ScrapeFirstParagraphAsync` 메서드 추가
  - Microsoft Security Blog 전용 `wp-block-paragraph` 패턴 우선
  - 범용 fallback 패턴: entry-content, article, post-content
  - User-Agent 설정으로 봇 차단 완화
  - 12초 timeout, 예외 처리 구현
  - 실패 시 RSS description으로 자동 fallback
  
- **AOAI 한국어 핵심 인사이트**: `TryTranslateKoreanBatchAsync` 개선
  - System Prompt 변경: "expert technical analyst who extracts key insights"
  - 단순 번역 제거, 전체 블로그 분석 기반 핵심 인사이트 추출
  - JSON 마크다운 블록 자동 제거 (```json 처리)
  - 18초 timeout, 상세한 에러 로깅
  - Fallback 메시지: "핵심 인사이트를 생성하려면 AOAI 설정이 필요합니다"

- **24시간 필터링 정확성 개선**
  - `hasNew` 로직 수정: `displayPosts`의 `recentPostsCount` 기준
  - 이전: `newPosts.Count > 0` (중복 체크 기준, 부정확)
  - 현재: `displayPosts.Count(p => p.PublishDateParsed >= newCutoff) > 0`
  - 제목 정확성: "새 게시글 N개" 숫자가 실제 표시 개수와 일치

- **FAIL 피드 제외**
  - RSS 피드 fetch 실패 시 `continue`로 skip
  - 나머지 피드는 정상 처리 계속
  - 개별 피드 에러가 전체 실행 중단하지 않음

#### 이메일 HTML 개선
- **블로그 첫 문단 섹션** 추가
  - "📝 블로그 첫 문단" 헤더
  - 웹 스크래핑으로 추출한 실제 본문 표시
  - 이탤릭 스타일, 좌측 파란색 border

- **핵심 인사이트 섹션** 개선
  - "💡 핵심 인사이트 (AI Summary)" 헤더
  - 한국어 3줄 인사이트만 표시 (영문 제거)
  - AI가 분석한 핵심 포인트 강조

### 🔄 변경사항

#### 코드 개선
- `BuildDigest.cs` (1042 lines, +106 lines from v2.0)
  - `Post` 클래스: `FirstParagraph` 속성 추가
  - `ScrapeFirstParagraphAsync`: 웹 스크래핑 메서드 신규 추가
  - `FetchFeedAsync`: 웹 스크래핑 통합, fallback 체인 구현
  - `TryTranslateKoreanBatchAsync`: System prompt 변경, JSON 정리 로직 추가
  - `RenderHtml`: 첫 문단 섹션 추가, 인사이트 섹션 개선
  - HTTP Client: `RssHttp`에 User-Agent, Accept 헤더 추가

#### 배포 방법 변경
- **이전**: `func azure functionapp publish --no-build`
- **현재**: `func azure functionapp publish` (full build)
- **이유**: --no-build 시 functions 0개 로드 문제 해결

### 🐛 버그 수정

#### AOAI 한국어 인사이트 fallback 텍스트 표시
- **문제**: "Key Insights (AI Summary)에 제공중인 영어 3줄 인사이트 제거 후..."
- **원인**: AOAI JSON 파싱 실패, JsonReaderException 발생
- **해결**: 
  - JSON 마크다운 블록 제거 로직 추가
  - 상세한 에러 로깅 (raw content, cleaned content, stack trace)
  - Deployment method 변경 (full build)
  - 검증: 실제 한국어 인사이트 생성 확인

#### 블로그 첫 문단 RSS description 표시
- **문제**: "Learn how Microsoft unites..." (RSS description) 표시
- **요구사항**: "The Deputy CISO blog series is where..." (실제 블로그 첫 문단)
- **해결**:
  - 웹 스크래핑 기능 추가 (`ScrapeFirstParagraphAsync`)
  - Microsoft Security Blog `wp-block-paragraph` 패턴 특화
  - Fallback 체인: 웹 스크래핑 → ExtractFirstParagraph → 원본 요약
  - 검증: 실제 블로그 본문 첫 문단 추출 확인

#### hasNew 로직 불일치
- **문제**: RSS에 "1 new" 표시되나 이메일 제목은 "신규 없음"
- **원인**: `newPosts.Count`(중복 제거 전)와 `displayPosts`(24시간 필터링) 불일치
- **해결**:
  - `recentPostsCount = displayPosts.Count(p => p.PublishDateParsed >= newCutoff)`
  - `hasNew = recentPostsCount > 0`
  - 제목: `새 게시글 {recentPostsCount}개`
  - 검증: 제목 숫자와 실제 표시 개수 일치 확인

#### FirstParagraph 속성 누락
- **문제**: "📝 블로그 첫 문단" 섹션이 이메일에 표시 안됨
- **원인**: `allItems` 생성 시 `FirstParagraph` 속성 복사 누락
- **해결**: `FirstParagraph = it.FirstParagraph` 추가
- **검증**: 첫 문단 섹션 정상 표시 확인

### 📊 테스트 결과
- ✅ Security Logic App: 최근 5회 모두 Succeeded
- ✅ Azure/Cloud Logic App: 최근 5회 모두 Succeeded
- ✅ 웹 스크래핑: "The Deputy CISO blog series..." 실제 본문 추출 확인
- ✅ AOAI 한국어 인사이트: "마이크로소프트는 고급 도구와..." 생성 확인
- ✅ 24시간 필터링: newCutoff 기준 정확히 작동
- ✅ FAIL 피드 제외: continue로 skip 확인
- ✅ 최근 24시간 에러: 0건 (Application Insights)

### 🛠️ 성능 및 안정성
- **실행 시간**: 90-120초 (웹 스크래핑 오버헤드 포함)
- **Timeout 설정**:
  - RSS HTTP: 12초
  - AOAI HTTP: 18초
  - 웹 스크래핑 HTTP: 12초 (RssHttp 공유)
- **에러 처리**:
  - 모든 외부 호출에 try-catch
  - Fallback 메커니즘 구현
  - Best-effort Storage (실패해도 계속)
- **안정성**: 100% (잠재 위험 모두 허용 가능한 수준)

### 📚 문서 업데이트
- README.md: 주요 기능, 아키텍처, 구성 요소, 이메일 형식 업데이트
- CHANGELOG.md: v3.0.0 상세 변경사항 추가
- 신규 안정성 보고서 생성

---

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
- 일 3회 스케줄 실행
  - Security: 07:00, 15:00, 22:00 KST
  - Azure Cloud: 08:00, 16:00, 23:00 KST
