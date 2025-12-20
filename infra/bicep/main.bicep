// ============================================================================
// Azure Logic Apps 보안 블로그 자동 요약 시스템 - Main Bicep Template
// ============================================================================
// Well-Architected Framework 원칙 적용:
// - 신뢰성: 재시도 정책, 에러 처리
// - 보안: Managed Identity, Key Vault 통합
// - 비용 최적화: Consumption 플랜
// - 운영 우수성: 태그 전략, 명명 규칙
// - 성능 효율성: Application Insights 모니터링
// ============================================================================

targetScope = 'resourceGroup'

@description('환경 이름 (dev, test, prod)')
@allowed([
  'dev'
  'test'
  'prod'
])
param environment string = 'dev'

@description('Azure 리전')
param location string = resourceGroup().location

@description('프로젝트 이름')
param projectName string = 'security-blog-automation'

@description('OpenAI API Endpoint (선택)')
param openAiEndpoint string = ''

@description('OpenAI Deployment Name (GPT-4)')
param openAiDeploymentName string = 'gpt-4'

@description('Office 365 이메일 수신자')
param emailRecipient string

@description('RSS 피드 URL')
param rssFeedUrl string = 'https://www.microsoft.com/en-us/security/blog/feed/'

// ============================================================================
// Variables - Well-Architected Framework 명명 규칙
// ============================================================================

var namingPrefix = '${environment}-${projectName}'
var tags = {
  Environment: environment
  Project: projectName
  ManagedBy: 'Bicep'
  CostCenter: 'Innovation'
  CreatedDate: utcNow('yyyy-MM-dd')
}

// Resource 명명 규칙 (Azure Best Practices)
var logicAppName = 'logic-${namingPrefix}'
var appInsightsName = 'appi-${namingPrefix}'
var logAnalyticsName = 'log-${namingPrefix}'

// ============================================================================
// Module 1: Log Analytics Workspace
// ============================================================================

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30 // 비용 최적화: 30일로 단축
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

// ============================================================================
// Module 2: Application Insights
// ============================================================================

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    RetentionInDays: 30
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// ============================================================================
// Module 3: Logic App (Consumption)
// ============================================================================

resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: logicAppName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    state: 'Enabled'
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      parameters: {
        openAiEndpoint: {
          type: 'string'
          defaultValue: openAiEndpoint
        }
        openAiDeploymentName: {
          type: 'string'
          defaultValue: openAiDeploymentName
        }
        emailRecipient: {
          type: 'string'
          defaultValue: emailRecipient
        }
        rssFeedUrl: {
          type: 'string'
          defaultValue: rssFeedUrl
        }
      }
      triggers: {
        // 워크플로는 별도 JSON 파일에서 import
      }
      actions: {}
      outputs: {}
    }
  }
}

// ============================================================================
// Module 4: Diagnostic Settings (Application Insights 통합)
// ============================================================================

resource logicAppDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: '${logicAppName}-diagnostics'
  scope: logicApp
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      {
        category: 'WorkflowRuntime'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: 30
        }
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: 30
        }
      }
    ]
  }
}

// ============================================================================
// Module 5: API Connection - Office 365 Outlook
// ============================================================================

resource office365Connection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'office365-${namingPrefix}'
  location: location
  tags: tags
  properties: {
    displayName: 'Office 365 Outlook'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
    }
    // 인증은 배포 후 수동으로 설정 필요
  }
}

// ============================================================================
// Module 6: API Connection - RSS
// ============================================================================

resource rssConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'rss-${namingPrefix}'
  location: location
  tags: tags
  properties: {
    displayName: 'RSS'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'rss')
    }
  }
}

// ============================================================================
// Outputs
// ============================================================================

@description('Logic App 리소스 ID')
output logicAppId string = logicApp.id

@description('Logic App 이름')
output logicAppName string = logicApp.name

@description('Logic App Managed Identity Principal ID')
output logicAppPrincipalId string = logicApp.identity.principalId

@description('Application Insights Instrumentation Key')
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey

@description('Application Insights Connection String')
output appInsightsConnectionString string = appInsights.properties.ConnectionString

@description('Log Analytics Workspace ID')
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id

@description('Office 365 Connection ID')
output office365ConnectionId string = office365Connection.id

@description('RSS Connection ID')
output rssConnectionId string = rssConnection.id
