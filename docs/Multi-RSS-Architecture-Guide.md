# 다중 RSS 처리 및 중복 방지 아키텍처 가이드

**문서 버전**: 1.0  
**작성일**: 2025-12-21  
**작성자**: Azure Security Blog Automation Team

---

## 📑 목차

1. [개요](#개요)
2. [현재 구현 상태 분석](#현재-구현-상태-분석)
3. [핵심 해결 전략: Normalize & Accumulate 패턴](#핵심-해결-전략-normalize--accumulate-패턴)
4. [보완 계획 (우선순위별)](#보완-계획-우선순위별)
5. [핵심 기술적 리스크 분석](#핵심-기술적-리스크-분석)
6. [최종 권장 구현 플랜](#최종-권장-구현-플랜)
7. [구현 시 주의사항](#구현-시-주의사항)
8. [참고 자료](#참고-자료)

---

## 개요

### 배경

현재 Azure Logic Apps 기반 보안 블로그 자동 요약 시스템은 **단일 RSS 소스**(Microsoft Security Blog)만 처리하고 있습니다. 향후 확장성을 위해 **다중 RSS 처리** 및 **중복 방지** 기능이 필요합니다.

### 목표

- ✅ **다중 RSS URL 처리**: 여러 보안 블로그를 하나의 워크플로에서 수집
- ✅ **중복 방지**: 이미 처리된 게시물 재발송 방지 (Table Storage 활용)
- ✅ **데이터 정규화**: 서로 다른 XML 구조를 공통 JSON 포맷으로 변환
- ✅ **안정성 확보**: 일부 RSS 실패 시에도 전체 워크플로 정상 동작

### 주요 도전 과제

1. **XML 구조 차이**: RSS 피드마다 네임스페이스, 태그 구조 상이
2. **Table Storage 연동**: Logic Apps에 네이티브 Table Storage 액션 없음
3. **Tech Community RSS**: XML이 아닌 HTML 응답 (파싱 불가)
4. **상태 관리**: 병렬 처리 시 변수 경쟁 조건 발생 가능

---

## 현재 구현 상태 분석

### ✅ 현재 운영 중인 시스템

| 구성 요소 | 상태 | 비고 |
|----------|------|------|
| **RSS 소스** | ✅ Microsoft Security Blog 단일 | XML 파싱 정상 |
| **AI 요약** | ✅ Azure OpenAI GPT-4o | 3줄 한글 요약 |
| **스케줄링** | ✅ 매일 07:00 KST | Recurrence Trigger |
| **이메일 발송** | ✅ Office 365 Outlook | HTML 카드 레이아웃 |
| **필터링** | ✅ 24시간 내 or 최근 5개 | Query/Compose Actions |
| **중복 방지** | ❌ **미구현** | 매일 동일 게시물 재발송 가능 |
| **다중 RSS** | ❌ **미구현** | 단일 URL만 처리 |

### ❌ Tech Community RSS 이슈

```
URL: https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=microsoft-security-blog

문제점:
- Content-Type: text/html (XML이 아님)
- Logic Apps RSS 커넥터로 파싱 불가
- HTTP 커넥터 + 수동 HTML 파싱 필요

결론: Phase 3 선택적 구현 (ROI 낮음)
```

---

## 핵심 해결 전략: Normalize & Accumulate 패턴

### 패턴 개요

여러 RSS를 처리할 때 실패하는 주된 이유는 **XML 구조의 미세한 차이**와 **반복문(Loop) 안에서의 변수 처리 실수**입니다. 이를 해결하기 위해 **데이터 정규화(Normalize)** 패턴을 사용합니다.

### 워크플로 단계

```
1. 입력(Input): RSS URL을 배열(Array) 파라미터로 받는다.

2. 병렬 처리(Parallel Loop): For each 루프를 돌면서 각 RSS를 호출한다.
   (Concurrency 설정 On - 순차 처리 권장)

3. 정규화(Normalize): XML을 받자마자 즉시 공통 JSON 포맷으로 변환한다.
   {
     "title": "...",
     "link": "...",
     "summary": "...",
     "pubDate": "...",
     "source": "Microsoft Security Blog"
   }

4. 축적(Accumulate): 변환된 JSON 객체를 하나의 AllPosts 배열 변수에 담는다.

5. 필터링(Filter): 루프가 끝난 후, AllPosts 배열을 가지고:
   - 중복 검사 (Table Storage)
   - 24시간 필터링
   - AI 요약
```

### 수정된 Logic Apps 흐름도

```
┌─ Initialize Variables
│   ├─ rssUrls (Array)
│   ├─ allPosts (Array)
│   └─ newPosts (Array)
│
├─ For Each (rssUrls) - Concurrency: 1
│   ├─ HTTP GET (Current Item)
│   ├─ Parse XML & Select (제목, 링크, 날짜 추출)
│   └─ Append to array variable (allPosts)
│
├─ For Each (allPosts)
│   ├─ Check Table Storage: RowKey eq Base64(Link) 확인
│   └─ Condition: 결과가 없으면(404)
│       └─ Append to array variable (newPosts)
│
└─ Condition (newPosts is not empty)
    ├─ Azure OpenAI 호출 (Input: newPosts)
    ├─ Parse JSON (AI 응답)
    ├─ Send Email (HTML Table)
    └─ For Each (newPosts)
        └─ Insert Entity to Table Storage
```

---

## 보완 계획 (우선순위별)

### 📌 Phase 1: 중복 방지 인프라 구축 (우선 - 단일 RSS 유지)

**목표**: Table Storage 기반 중복 방지 기능 먼저 안정화

#### 1.1 Storage Account 추가

```bicep
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'st${replace(projectName, '-', '')}${environment}'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
  
  resource tableService 'tableServices@2023-01-01' = {
    name: 'default'
    
    resource processedPostsTable 'tables@2023-01-01' = {
      name: 'ProcessedPosts'
    }
  }
}
```

#### 1.2 Managed Identity RBAC

```bicep
resource storageTableDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, logicApp.id, 'Storage Table Data Contributor')
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions', 
      '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor
    )
    principalId: logicApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}
```

#### 1.3 Logic App 워크플로 수정

```
┌─ Recurrence (07:00 KST)
├─ Initialize Variable: newPosts (Array)
├─ List RSS (Microsoft Security Blog)
├─ For Each (RSS Items)
│   ├─ Check Entity (Table Storage via HTTP Action)
│   └─ Condition (404 = New Post)
│       └─ Append to newPosts Array
├─ Condition (newPosts.length > 0)
│   ├─ OpenAI Summarize
│   ├─ Send Email
│   └─ For Each (newPosts)
│       └─ Insert Entity (Table Storage via HTTP Action)
```

#### ⚠️ 주요 기술적 도전 과제

**1. Table Storage Action 부재**

- Logic Apps Standard는 Table Storage 네이티브 액션 없음
- **해결**: HTTP Action + REST API 직접 호출 (Managed Identity 인증)

**2. Managed Identity 인증 구현**

```json
{
  "method": "GET",
  "uri": "https://<storage>.table.core.windows.net/ProcessedPosts(PartitionKey='MSSecurity',RowKey='<base64>')",
  "authentication": {
    "type": "ManagedServiceIdentity",
    "audience": "https://storage.azure.com/"
  }
}
```

**3. Base64 인코딩 함수**

```
@base64(items('For_each')?['primaryLink'])
```

---

### 📌 Phase 2: 다중 RSS 파라미터 확장 (Tech Community 제외)

**목표**: RSS URL 배열화하되, **검증된 XML 소스만** 추가

#### 2.1 파라미터 변경

```bicep
@description('RSS 피드 URL 배열 (XML 형식만)')
param rssFeedUrls array = [
  'https://www.microsoft.com/en-us/security/blog/feed/'
  // Tech Community는 Phase 3에서 별도 처리
  // 추가 가능한 XML RSS:
  // 'https://msrc.microsoft.com/blog/feed/'
  // 'https://www.microsoft.com/security/blog/category/threat-intelligence/feed/'
]
```

#### 2.2 워크플로 패턴: Normalize & Accumulate

```json
{
  "actions": {
    "Initialize_allPosts": {
      "type": "InitializeVariable",
      "inputs": {
        "variables": [{
          "name": "allPosts",
          "type": "array",
          "value": []
        }]
      }
    },
    "For_each_RSS_URL": {
      "type": "Foreach",
      "foreach": "@parameters('rssFeedUrls')",
      "runtimeConfiguration": {
        "concurrency": {
          "repetitions": 1
        }
      },
      "actions": {
        "List_RSS": {
          "type": "ApiConnection",
          "inputs": {
            "host": {
              "connection": {
                "referenceName": "rss"
              }
            },
            "method": "get",
            "path": "/ListFeedItems",
            "queries": {
              "feedUrl": "@items('For_each_RSS_URL')"
            }
          }
        },
        "For_each_item": {
          "type": "Foreach",
          "foreach": "@body('List_RSS')",
          "actions": {
            "Compose_normalized": {
              "type": "Compose",
              "inputs": {
                "title": "@{items('For_each_item')?['title']}",
                "link": "@{items('For_each_item')?['primaryLink']}",
                "pubDate": "@{items('For_each_item')?['publishDate']}",
                "summary": "@{items('For_each_item')?['summary']}",
                "source": "@items('For_each_RSS_URL')"
              }
            },
            "Append_to_allPosts": {
              "type": "AppendToArrayVariable",
              "inputs": {
                "name": "allPosts",
                "value": "@outputs('Compose_normalized')"
              }
            }
          }
        }
      }
    }
  }
}
```

#### ⚠️ xpath() 함수 이슈

- Logic Apps RSS 커넥터는 이미 파싱된 JSON 반환
- **xpath()는 불필요** (HTTP + XML 직접 파싱 시에만 사용)
- `items('For_each')?['title']` 직접 사용 가능

---

### 📌 Phase 3: Tech Community RSS 통합 (선택적)

**목표**: HTML 응답 RSS를 별도 파싱 로직으로 처리

#### 3.1 문제점 분석

```
Tech Community RSS URL:
https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=microsoft-security-blog

응답: Content-Type: text/html (XML 아님!)
→ Logic Apps RSS 커넥터 사용 불가
```

#### 3.2 대안 솔루션

**옵션 A: HTTP + 정규식 파싱 (복잡도 ⬆️)**

```
HTTP GET → Parse HTML → Extract <item> tags → JSON 변환
```

**옵션 B: Azure Functions 중간 레이어 (권장)**

```
Logic Apps → Azure Functions (HTTP Trigger)
            ↓
          HTML Parsing (BeautifulSoup/HtmlAgilityPack)
            ↓
          JSON 반환 → Logic Apps
```

**옵션 C: 제외 (현실적)**

- Tech Community RSS는 불안정
- Microsoft Security Blog만으로도 충분한 커버리지
- **ROI 낮음** → Phase 3는 선택적 구현

---

## 핵심 기술적 리스크 분석

### 1. Table Storage REST API 복잡도

**문제**: Logic Apps에는 Table Storage 네이티브 액션 없음

**영향**:
- HTTP 액션으로 직접 REST API 호출 필요
- SAS Token 생성 로직 복잡
- Managed Identity 인증 구현 난이도 높음

**권장 해결책**: Azure Functions Proxy 패턴

```bicep
resource checkDuplicateFunction 'Microsoft.Web/sites/functions@2022-03-01' = {
  name: 'CheckDuplicate'
  properties: {
    config: {
      bindings: [
        {
          type: 'httpTrigger'
          direction: 'in'
          methods: ['POST']
        }
        {
          type: 'table'
          direction: 'in'
          tableName: 'ProcessedPosts'
          connection: 'AzureWebJobsStorage'
        }
      ]
    }
  }
}
```

**장점**:
- ✅ Managed Identity 자동 처리
- ✅ Table SDK 사용 가능
- ✅ 에러 처리 용이
- ✅ 로직 재사용 가능

**단점**:
- ❌ 리소스 추가 (Functions App 필요)
- ❌ 비용 증가 (월 $0.20 추가)

---

### 2. RSS 파싱 표준화 불가

**문제**: 제안된 xpath() 함수는 Logic Apps RSS 커넥터에서 불필요

**실제 상황**:
- RSS 커넥터는 이미 JSON으로 파싱된 데이터 반환
- `items('For_each')?['title']` 직접 사용 가능
- xpath()는 HTTP 액션 + XML 직접 처리 시에만 사용

**결론**:
- ✅ Microsoft Security Blog: RSS 커넥터 사용 (현재 방식 유지)
- ❌ Tech Community: HTML 파싱 필요 → 별도 처리 또는 제외

---

### 3. 동시성 제어 (Concurrency)

**문제**: For Each 루프에서 여러 RSS 동시 호출 시 순서 보장 안 됨

**제안된 설정**:

```json
"foreach": "@parameters('rssFeedUrls')",
"runtimeConfiguration": {
  "concurrency": {
    "repetitions": 1  // 순차 처리
  }
}
```

**트레이드오프**:
- **Concurrency 1 (순차)**: 안정적, 느림
- **Concurrency N (병렬)**: 빠름, 변수 경쟁 조건 가능

**권장**: **Concurrency 1**로 시작 → 안정화 후 증가

---

## 최종 권장 구현 플랜

### ✅ 즉시 구현 (Phase 1 - 고우선순위)

#### Step 1: Storage Account 추가

```bicep
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'st${replace(projectName, '-', '')}${environment}'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
  
  resource tableService 'tableServices@2023-01-01' = {
    name: 'default'
    
    resource processedPostsTable 'tables@2023-01-01' = {
      name: 'ProcessedPosts'
    }
  }
}
```

#### Step 2: RBAC 할당

```bicep
resource storageTableDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, logicApp.id, 'Storage Table Data Contributor')
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions', 
      '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
    )
    principalId: logicApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}
```

#### Step 3: Azure Functions Proxy (중복 체크용)

```bicep
resource functionApp 'Microsoft.Web/sites@2022-03-01' = {
  name: 'func-${projectName}-${environment}'
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: consumptionPlan.id
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
      ]
    }
  }
}
```

#### Step 4: Functions 코드 (C# Isolated)

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Azure.Data.Tables;
using System.Text;
using System.Text.Json;

[Function("CheckDuplicate")]
public async Task<HttpResponseData> CheckDuplicate(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
    [TableInput("ProcessedPosts", Connection = "AzureWebJobsStorage")] TableClient tableClient)
{
    var requestBody = await req.ReadAsStringAsync();
    var data = JsonSerializer.Deserialize<CheckRequest>(requestBody);
    
    var rowKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(data.Link));
    var entity = await tableClient.GetEntityIfExistsAsync<TableEntity>(
        "MSSecurity", 
        rowKey
    );
    
    var response = req.CreateResponse(
        entity.HasValue ? HttpStatusCode.Conflict : HttpStatusCode.NotFound
    );
    return response;
}

[Function("InsertProcessed")]
public async Task<HttpResponseData> InsertProcessed(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
    [TableInput("ProcessedPosts", Connection = "AzureWebJobsStorage")] TableClient tableClient)
{
    var requestBody = await req.ReadAsStringAsync();
    var data = JsonSerializer.Deserialize<PostData>(requestBody);
    
    var entity = new TableEntity("MSSecurity", 
        Convert.ToBase64String(Encoding.UTF8.GetBytes(data.Link)))
    {
        { "Title", data.Title },
        { "ProcessedDate", DateTime.UtcNow },
        { "EmailSent", true }
    };
    
    await tableClient.AddEntityAsync(entity);
    
    var response = req.CreateResponse(HttpStatusCode.Created);
    return response;
}

public class CheckRequest
{
    public string Link { get; set; }
}

public class PostData
{
    public string Link { get; set; }
    public string Title { get; set; }
}
```

#### Step 5: Logic App 워크플로 수정

```json
{
  "definition": {
    "actions": {
      "Initialize_newPosts": {
        "type": "InitializeVariable",
        "inputs": {
          "variables": [{
            "name": "newPosts",
            "type": "array",
            "value": []
          }]
        }
      },
      "List_RSS": {
        "type": "ApiConnection",
        "inputs": {
          "host": {
            "connection": {
              "referenceName": "rss"
            }
          },
          "method": "get",
          "path": "/ListFeedItems",
          "queries": {
            "feedUrl": "@parameters('rssFeedUrl')"
          }
        },
        "runAfter": {
          "Initialize_newPosts": ["Succeeded"]
        }
      },
      "For_each_RSS_item": {
        "type": "Foreach",
        "foreach": "@body('List_RSS')",
        "actions": {
          "Check_duplicate": {
            "type": "Http",
            "inputs": {
              "method": "POST",
              "uri": "@{parameters('functionAppUrl')}/api/CheckDuplicate",
              "body": {
                "link": "@{items('For_each_RSS_item')?['primaryLink']}"
              },
              "authentication": {
                "type": "ManagedServiceIdentity",
                "audience": "@{parameters('functionAppUrl')}"
              }
            }
          },
          "Condition_is_new": {
            "type": "If",
            "expression": {
              "equals": [
                "@outputs('Check_duplicate')['statusCode']",
                404
              ]
            },
            "actions": {
              "Append_to_newPosts": {
                "type": "AppendToArrayVariable",
                "inputs": {
                  "name": "newPosts",
                  "value": "@items('For_each_RSS_item')"
                }
              }
            },
            "runAfter": {
              "Check_duplicate": ["Succeeded", "Failed"]
            }
          }
        },
        "runAfter": {
          "List_RSS": ["Succeeded"]
        }
      },
      "Condition_has_new_posts": {
        "type": "If",
        "expression": {
          "greater": [
            "@length(variables('newPosts'))",
            0
          ]
        },
        "actions": {
          "Summarize_with_OpenAI": {
            "type": "Http",
            "inputs": {
              "method": "POST",
              "uri": "@{parameters('openAiEndpoint')}/openai/deployments/@{parameters('openAiDeploymentName')}/chat/completions?api-version=2024-02-15-preview",
              "body": {
                "messages": [
                  {
                    "role": "system",
                    "content": "보안 전문가로서 다음 글들을 분석해. 각 글 별로 한글 3줄 요약을 제공해."
                  },
                  {
                    "role": "user",
                    "content": "@{variables('newPosts')}"
                  }
                ]
              }
            }
          },
          "Send_email": {
            "type": "ApiConnection",
            "inputs": {
              "host": {
                "connection": {
                  "referenceName": "office365"
                }
              },
              "method": "post",
              "path": "/v2/Mail",
              "body": {
                "To": "@parameters('emailRecipient')",
                "Subject": "보안 블로그 요약 - @{utcNow('yyyy-MM-dd')}",
                "Body": "@body('Summarize_with_OpenAI')"
              }
            },
            "runAfter": {
              "Summarize_with_OpenAI": ["Succeeded"]
            }
          },
          "For_each_new_post": {
            "type": "Foreach",
            "foreach": "@variables('newPosts')",
            "actions": {
              "Insert_to_storage": {
                "type": "Http",
                "inputs": {
                  "method": "POST",
                  "uri": "@{parameters('functionAppUrl')}/api/InsertProcessed",
                  "body": {
                    "link": "@{items('For_each_new_post')?['primaryLink']}",
                    "title": "@{items('For_each_new_post')?['title']}"
                  },
                  "authentication": {
                    "type": "ManagedServiceIdentity",
                    "audience": "@{parameters('functionAppUrl')}"
                  }
                }
              }
            },
            "runAfter": {
              "Send_email": ["Succeeded"]
            }
          }
        },
        "runAfter": {
          "For_each_RSS_item": ["Succeeded"]
        }
      }
    }
  }
}
```

---

### 🔄 점진적 구현 (Phase 2 - 중우선순위)

- `rssFeedUrl` → `rssFeedUrls` 배열 변경
- For Each 루프 추가 (Concurrency: 1)
- Normalize & Accumulate 패턴 적용
- **검증된 XML RSS만 추가**

---

### 🚀 선택적 구현 (Phase 3 - 저우선순위)

- Tech Community RSS (HTML 파싱 필요)
- Azure Functions HTML Parser 추가
- 또는 제외 (ROI 고려)

---

## 구현 시 주의사항

### 1. Table Storage Entity 키 설계

```
PartitionKey: "MSSecurity" (고정값, 단일 파티션으로 충분)
RowKey: Base64(Link URL)
Properties: {
  ProcessedDate: DateTime,
  Title: String,
  EmailSent: Boolean
}
```

**설계 근거**:
- **PartitionKey**: 고정값 사용 (월 1,000건 미만 예상, 단일 파티션 충분)
- **RowKey**: URL을 Base64 인코딩 (특수문자 처리)
- **Properties**: 최소 메타데이터만 저장 (비용 최적화)

### 2. Logic Apps 변수 초기화 순서

```json
{
  "actions": {
    "1_Init_newPosts": {
      "type": "InitializeVariable",
      "inputs": {
        "variables": [
          {
            "name": "newPosts",
            "type": "array",
            "value": []
          }
        ]
      },
      "runAfter": {}  // ⚠️ 가장 먼저 실행!
    }
  }
}
```

**주의사항**:
- 변수는 루프 **이전**에 초기화!
- `runAfter: {}` 로 의존성 없음 명시
- 초기화 순서가 잘못되면 런타임 오류 발생

### 3. Managed Identity 권한 전파 시간

```bicep
resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, logicApp.id, 'contributor')
  properties: {
    roleDefinitionId: '...'
    principalId: logicApp.identity.principalId
  }
  dependsOn: [
    logicApp  // ⚠️ 반드시 명시!
  ]
}
```

**주의사항**:
- RBAC 할당 후 **최대 5분** 대기
- Bicep 배포 시 `dependsOn` 명시
- 초기 실행 실패 시 재시도 필요

### 4. 비용 관리

| 리소스 | SKU | 예상 비용 (월) |
|--------|-----|---------------|
| Logic Apps | Consumption | $0.50 - $1.00 |
| Storage Account | Standard_LRS | $0.10 |
| Azure Functions | Consumption | $0.20 |
| **합계** | - | **$0.80 - $1.30** |

**비용 절감 팁**:
- Logic Apps: 불필요한 액션 제거, 조건문 최적화
- Storage: Table만 사용, Blob/Queue 비활성화
- Functions: Consumption 플랜 유지, Cold Start 허용

### 5. 에러 처리 전략

```json
{
  "actions": {
    "Check_duplicate": {
      "type": "Http",
      "inputs": { ... },
      "runAfter": {
        "Previous_action": ["Succeeded", "Failed"]
      }
    },
    "Condition": {
      "expression": {
        "or": [
          {
            "equals": [
              "@outputs('Check_duplicate')['statusCode']",
              404
            ]
          },
          {
            "equals": [
              "@outputs('Check_duplicate')['statusCode']",
              null
            ]
          }
        ]
      }
    }
  }
}
```

**에러 처리 원칙**:
- ✅ 404 Not Found = 정상 (신규 게시물)
- ✅ 409 Conflict = 정상 (중복 게시물)
- ❌ 500 Server Error = 재시도 필요
- ❌ null/undefined = 네트워크 오류

---

## 참고 자료

### Azure 공식 문서

- [Azure Logic Apps - Consumption vs Standard](https://learn.microsoft.com/azure/logic-apps/logic-apps-overview)
- [Azure Table Storage REST API](https://learn.microsoft.com/rest/api/storageservices/table-service-rest-api)
- [Managed Identity 인증](https://learn.microsoft.com/azure/logic-apps/create-managed-service-identity)
- [Azure Functions Isolated Worker](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide)

### 내부 문서

- [Azure Logic Apps 기반 보안 블로그 자동 요약 시스템 구현 가이드](Azure-Logic-Apps-기반-보안-블로그-자동-요약-시스템-구현-가이드.md)
- [GitHub Repository](https://github.com/zer0big/azure-security-blog-automation)
- [Azure DevOps Epic #118](https://dev.azure.com/azure-mvp/azure-secu-updates-notification/_workitems/edit/118)

### 기술 블로그

- [Logic Apps Best Practices](https://learn.microsoft.com/azure/logic-apps/logic-apps-best-practices)
- [Table Storage Performance Guide](https://learn.microsoft.com/azure/storage/tables/table-storage-design-for-query)
- [Azure Well-Architected Framework](https://learn.microsoft.com/azure/well-architected/)

---

## 변경 이력

| 날짜 | 버전 | 변경 내용 | 작성자 |
|-----|------|----------|--------|
| 2025-12-21 | 1.0 | 초안 작성 - Normalize & Accumulate 패턴, Phase 1-3 구현 계획 | Azure Automation Team |

---

**문서 끝** 📄
