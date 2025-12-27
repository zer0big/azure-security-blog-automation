// Logic App (Standard) Module
@description('Logic App name')
param logicAppName string

@description('Storage Account name for Logic App')
param storageAccountName string

@description('Location for resources')
param location string = resourceGroup().location

@description('Application Insights connection string')
param appInsightsConnectionString string = ''

@description('Storage connection string')
@secure()
param storageConnectionString string

@description('Tags for resources')
param tags object = {}

// App Service Plan for Logic App (Standard)
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: '${logicAppName}-plan'
  location: location
  tags: tags
  sku: {
    name: 'WS1'
    tier: 'WorkflowStandard'
  }
  properties: {
    reserved: true
  }
}

// Logic App (Standard)
resource logicApp 'Microsoft.Web/sites@2023-01-01' = {
  name: logicAppName
  location: location
  tags: tags
  kind: 'functionapp,workflowapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    reserved: true
    siteConfig: {
      netFrameworkVersion: 'v6.0'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: storageConnectionString
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower(logicAppName)
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'node'
        }
        {
          name: 'WEBSITE_NODE_DEFAULT_VERSION'
          value: '~18'
        }
        {
          name: 'AzureFunctionsJobHost__extensionBundle__id'
          value: 'Microsoft.Azure.Functions.ExtensionBundle.Workflows'
        }
        {
          name: 'AzureFunctionsJobHost__extensionBundle__version'
          value: '[1.*, 2.0.0)'
        }
        {
          name: 'APP_KIND'
          value: 'workflowApp'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
      ]
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      use32BitWorkerProcess: false
    }
    httpsOnly: true
  }
}

output logicAppId string = logicApp.id
output logicAppName string = logicApp.name
output logicAppHostName string = logicApp.properties.defaultHostName
output logicAppPrincipalId string = logicApp.identity.principalId
