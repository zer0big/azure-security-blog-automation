# Changelog

이 프로젝트의 주요 변경 사항을 기록합니다.

## [Unreleased]

### Changed - 2025-12-27

#### ✅ 안정성 및 동작 수정
- **SummarizePost: null/빈 본문 안전 처리**
  - 빈 또는 null 콘텐츠에 대해 500 에러를 발생시키지 않고 **placeholder 요약**("요약할 내용이 없습니다")를 반환하도록 수정하여 함수의 실패를 방지했습니다.
  - 영향 파일: `functions/Functions/SummarizePost.cs`

- **GenerateEmailHtml: 실제 신규 개수 집계 개선**
  - "No new posts in last 24 hours" 플레이스홀더는 헤더 카운트에서 제외되고, 각 피드에 대해 신규 없음은 단일 라인(이모지 포함)으로 렌더링됩니다.
  - 제목 생성 로직: 실제 신규 개수 N>0 → "[Microsoft Azure 업데이트] 새 게시글 {N}개", 그렇지 않으면 "최근 게시글 요약 (신규 없음)"으로 변경.
  - 영향 파일: `functions/Functions/GenerateEmailHtml.cs`

#### 🔄 워크플로/배포 변경
- **기본 RSS 피드 목록을 5개로 확대** (Tech Community - Defender, Sentinel 추가)
  - 배포 스냅샷: `.backups/backup_2025-12-27_final_5_feeds_with_emoji/deploy_complete_5_feeds.json`
- **스케줄 업데이트**: 트리거 시간 `07:00, 14:00, 21:00 (KST)`로 변경
- **동시성/재시도 설정**: `For_Each_RSS_Feed` 반복 동시성(repetitions)=3, `For_Each_RSS_Item` 반복 동시성=5; 주요 HTTP 액션에 retry 정책 및 timeout 적용
  - 영향 파일: 워크플로 정의 및 배포 JSON

#### 🧾 백업 및 문서화
- 백업 스냅샷 생성: `.backups/backup_2025-12-27_final_5_feeds_with_emoji` (복원 가이드 포함)
- 문서 업데이트: `README.md`, `docs/LOGIC-APP-ARCHITECTURE.md`, `docs/AZURE-INFRASTRUCTURE-ARCHITECTURE.md`, `workflows/README.md` (피드 목록/스케줄/동시성/Retry/Timeout/KeyVault 상태 반영)

#### 🧪 검증 및 로그
- 원격 `GenerateEmailHtml` 호출 테스트 성공(응답: subject + HTML) — 아티팩트: `.artifacts/remote_generate_email_response.json`
- Logic App 실행/액션 로그 수집: `.artifacts/latest_run_actions_full.json` (일부 run에서 downstream action skipped, 일부 run에서 Office365 send 200 응답 확인)

#### ⚠️ 운영 권고
- 반복 이메일이 지속될 경우 **Send 액션 비활성화(또는 조건 추가)**로 즉시 전송 중지 권장; 관련 가이드와 복원 절차는 백업 RESTORE_GUIDE.md에 정리됨

### Added - 2025-12-22

#### 🎨 이메일 UI 개선
- **헤더 색상 가시성 개선**
  - 기존: 어두운 파란색 그라데이션 배경에 흰색 텍스트 (#0078d4 → #005a9e 배경, #fff 텍스트)
  - 변경: 밝은 파란색 그라데이션 배경에 Azure 파란색 텍스트 (#e3f2fd → #bbdefb 배경, #0078d4 텍스트)
  - 목적: 일부 이메일 클라이언트에서 흰색 텍스트가 보이지 않는 문제 해결
  - 영향 파일: `functions/Functions/GenerateEmailHtml.cs`
  - 추가 변경:
    - 헤더 하단에 3px 파란색 테두리 추가 (`border-bottom: 3px solid #0078d4`)
    - 폰트 굵기 증가 (font-weight: bold/600)
    - 카운트 텍스트 색상 강조 (#005a9e)

#### 🔄 Multi-RSS 피드 지원 아키텍처
- **다중 RSS 피드 처리 구조 구현**
  - 새 워크플로 파일: `workflows/security-blog-multi-rss.json`
  - 파라미터 변경: `rssFeedUrl` (String) → `rssFeedUrls` (Array)
  - 기본 피드 설정:
    - Microsoft Security Blog: `https://www.microsoft.com/en-us/security/blog/feed/`
    - Azure Security Blog: `https://azure.microsoft.com/en-us/blog/topics/security/feed/`
  - 중첩 ForEach 구조: RSS 피드 루프 → 개별 아이템 루프
  - SourceName 추적: 각 게시물의 출처 식별
  - 순차 처리: Concurrency control (repetitions: 1) - API throttling 방지

- **BlogPost 데이터 모델 확장**
  - 새 속성: `SourceName` (string, nullable)
  - 목적: 다중 RSS 피드 출처 추적
  - 영향 파일: `functions/Functions/GenerateEmailHtml.cs`

- **소스 배지 UI 구현**
  - 각 게시물 제목 옆에 소스 이름 표시
  - 디자인: 파란색 알약형 배지 (`background: #0078d4; color: #fff; border-radius: 12px`)
  - 조건부 표시: SourceName이 있을 때만 렌더링
  - 영향 파일: `functions/Functions/GenerateEmailHtml.cs`

#### 🧪 테스트 자동화
- **자동 테스트 스크립트 생성**
  - 파일: `test-blue-header.ps1` (75 lines)
  - 기능:
    - ProcessedPosts 테이블의 모든 엔티티 삭제 (10개)
    - 워크플로 REST API 트리거
    - 30초 대기
    - 최신 실행 상태 확인
    - 컬러 코딩된 출력 (✅ 성공, ❌ 실패)
  - 목적: 수동 테스트 12단계 → 1단계로 자동화

#### 📚 문서화
- **Multi-RSS 배포 가이드**
  - 파일: `docs/MULTI-RSS-GUIDE.md` (300+ lines)
  - 내용:
    - 배포 PowerShell 명령어
    - 7개 권장 RSS 피드 (Microsoft/Azure 보안 관련)
    - 이메일 미리보기 목업
    - 트러블슈팅 가이드
    - 단일 RSS vs 다중 RSS 비교표
    - 변경 이력

- **변경 이력 문서**
  - 파일: `docs/CHANGELOG.md` (본 파일)
  - 목적: 프로젝트 변경 사항 체계적 추적

### Changed - 2025-12-22

#### 🎨 CSS 스타일 수정
- **헤더 스타일링 완전 재설계**
  ```css
  /* 기존 (Before) */
  .header {
    background: linear-gradient(135deg, #0078d4 0%, #005a9e 100%);
    color: #fff !important;
  }
  .header h1 {
    color: #fff !important;
  }
  
  /* 변경 (After) */
  .header {
    background: linear-gradient(135deg, #e3f2fd 0%, #bbdefb 100%);
    color: #0078d4 !important;
    border-bottom: 3px solid #0078d4;
  }
  .header h1 {
    color: #0078d4 !important;
    font-weight: bold;
  }
  .header .count {
    color: #005a9e !important;
    font-weight: 600;
  }
  ```
- **이메일 클라이언트 호환성 개선**
  - `!important` 플래그 추가로 인라인 스타일 강제
  - 명도 대비 증가 (WCAG AA 준수)

#### 🔧 Function 배포
- **빌드 시간 개선**
  - 이전: 2.7초
  - 현재: 2.1초
  - 최적화: 불필요한 종속성 제거

- **배포 성공**
  - 배포 ID: `11e6a528c6724505a5f703c49a480738`
  - 시간: 2025-12-21 15:44:22 UTC
  - 상태: `provisioningState: Succeeded`
  - Function App: `func-dev-security-blog-automation`

#### 🧪 테스트 실행
- **색상 변경 검증 테스트**
  - Run ID: `08584352749674094743258665769CU01`
  - 상태: Succeeded
  - 시작: 2025-12-21 15:45:18 UTC
  - 종료: 2025-12-21 15:45:36 UTC
  - 실행 시간: 18초
  - 처리: 10개 엔티티 삭제 → 워크플로 트리거 → 이메일 발송

### Technical Details

#### 파일 변경 내역

**추가된 파일** (4개):
- `workflows/security-blog-multi-rss.json` - Multi-RSS 워크플로 정의
- `test-blue-header.ps1` - 자동화 테스트 스크립트
- `docs/MULTI-RSS-GUIDE.md` - Multi-RSS 배포 가이드
- `docs/CHANGELOG.md` - 변경 이력 문서

**수정된 파일** (1개):
- `functions/Functions/GenerateEmailHtml.cs`
  - CSS 색상 스킴 변경 (라인 40-55)
  - BlogPost 클래스에 SourceName 속성 추가 (라인 120)
  - 소스 배지 HTML 생성 로직 추가 (라인 85-90)

**삭제된 파일**: 없음

#### 배포 정보

**Environment**: Development (`dev`)

**Deployed Resources**:
- Function App: `func-dev-security-blog-automation`
- Logic App: `logic-dev-security-blog-automation`
- Storage Account: `stdevsecblogauto`
- Table: `ProcessedPosts`

**Deployment Status**:
- ✅ Single RSS workflow: Running (deployed)
- ⏳ Multi RSS workflow: Ready (not deployed, awaiting user decision)

#### 권장 RSS 피드 (7개)

1. **Microsoft Security Blog** (기본)
   - URL: `https://www.microsoft.com/en-us/security/blog/feed/`
   - 카테고리: 전체 보안 뉴스

2. **Azure Security Blog** (기본)
   - URL: `https://azure.microsoft.com/en-us/blog/topics/security/feed/`
   - 카테고리: Azure 보안

3. **Microsoft Defender Blog**
   - URL: `https://techcommunity.microsoft.com/plugins/custom/microsoft/o365/custom-blog-rss?board=MicrosoftDefenderBlog`
   - 카테고리: Defender 제품군

4. **Microsoft Sentinel Blog**
   - URL: `https://techcommunity.microsoft.com/plugins/custom/microsoft/o365/custom-blog-rss?board=MicrosoftSentinelBlog`
   - 카테고리: Sentinel SIEM/SOAR

5. **Security, Compliance, and Identity**
   - URL: `https://techcommunity.microsoft.com/plugins/custom/microsoft/o365/custom-blog-rss?board=Identity`
   - 카테고리: ID 및 규정 준수

6. **Azure Updates (Security 필터)**
   - URL: `https://azure.microsoft.com/en-us/updates/feed/?category=security`
   - 카테고리: Azure 보안 업데이트

7. **Azure Architecture Blog**
   - URL: `https://techcommunity.microsoft.com/plugins/custom/microsoft/o365/custom-blog-rss?board=AzureArchitectureBlog`
   - 카테고리: 아키텍처 모범 사례

### Breaking Changes

**없음** - Multi-RSS 구조는 기존 Functions와 호환됩니다.

### Migration Guide

#### Single RSS → Multi-RSS 마이그레이션

**옵션 1: 수동 배포** (권장)
```powershell
# 1. 현재 Logic App 속성 가져오기
$rg = "rg-dev-security-blog-automation"
$logicAppName = "logic-dev-security-blog-automation"
$subscriptionId = "<your-subscription-id>"

az rest --method get \
  --uri "https://management.azure.com/subscriptions/$subscriptionId/resourceGroups/$rg/providers/Microsoft.Logic/workflows/$logicAppName?api-version=2019-05-01" \
  > current-logic-app.json

# 2. Multi-RSS 워크플로 적용
$props = Get-Content current-logic-app.json | ConvertFrom-Json
$workflow = Get-Content workflows\security-blog-multi-rss.json | ConvertFrom-Json
$props.properties.definition = $workflow

$props | ConvertTo-Json -Depth 100 | Set-Content deploy-multi-rss.json

# 3. 배포
az rest --method put \
  --uri "https://management.azure.com/subscriptions/$subscriptionId/resourceGroups/$rg/providers/Microsoft.Logic/workflows/$logicAppName?api-version=2019-05-01" \
  --body '@deploy-multi-rss.json'
```

**옵션 2: 유지** (현재 상태)
- 변경 없음
- 단일 RSS 피드 계속 사용
- 필요시 나중에 Multi-RSS로 업그레이드 가능

### 롤백 절차

색상 변경 또는 Multi-RSS 적용 후 문제 발생 시:

```powershell
# 1. 이전 Function 버전으로 롤백
# (현재는 git으로 이전 커밋 체크아웃 후 재배포)

# 2. 이전 Logic App 정의로 복원
az rest --method put \
  --uri "https://management.azure.com/.../workflows/$logicAppName?api-version=2019-05-01" \
  --body '@backup-logic-app.json'
```

**권장사항**: 배포 전 현재 상태 백업
```powershell
az rest --method get --uri "..." > backup-$(Get-Date -Format 'yyyyMMdd-HHmmss').json
```

### Known Issues

**없음** - 현재 알려진 문제 없음

### 다음 계획

#### 단기 (1-2주)
- [ ] Multi-RSS 프로덕션 배포 검토
- [ ] 추가 RSS 피드 선정 및 테스트
- [ ] 이메일 템플릿 모바일 최적화

#### 중기 (1-3개월)
- [ ] Azure Key Vault 통합
- [ ] Application Insights 대시보드 구성
- [ ] 에러 알림 자동화

#### 장기 (3-6개월)
- [ ] AI 요약 품질 개선 (Few-shot learning)
- [ ] 사용자 피드백 수집 메커니즘
- [ ] 다국어 지원 (영어/한국어 선택)

---

## [1.0.0] - 2025-12-20

### Added
- 초기 프로젝트 구조
- Azure Functions (CheckDuplicate, InsertProcessed, GenerateEmailHtml)
- Azure Logic Apps 워크플로 (security-blog-consolidated.json)
- Azure Table Storage 중복 감지
- OpenAI GPT-4o 3줄 한글 요약
- Office 365 HTML 이메일 발송
- Infrastructure as Code (Bicep 템플릿)
- CI/CD 파이프라인 (GitHub Actions)

### Initial Features
- ✅ RSS 피드 자동 수집
- ✅ 24시간 내 신규 게시물 필터링
- ✅ 중복 게시물 제거
- ✅ AI 기반 한글 요약
- ✅ HTML 이메일 발송
- ✅ 매일 07:00 / 15:00 / 22:00 KST 자동 실행

---

**범례**:
- 🎨 UI/UX 개선
- 🔄 아키텍처 변경
- 🧪 테스트 관련
- 📚 문서화
- 🔧 기술적 변경
- 🔒 보안 관련
- 💰 비용 최적화
