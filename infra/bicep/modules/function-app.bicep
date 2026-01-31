// Function App Module
@description('Function App name')
param functionAppName string

@description('Storage Account name for Function App')
param storageAccountName string

@description('Location for resources')
param location string = resourceGroup().location

@description('Application Insights connection string')
param appInsightsConnectionString string = ''

@description('Azure OpenAI endpoint')
@secure()
param azureOpenAIEndpoint string

@description('Azure OpenAI key')
@secure()
param azureOpenAIKey string

@description('Azure OpenAI deployment name')
param azureOpenAIDeployment string = 'gpt-4o'

@description('Storage connection string')
@secure()
param storageConnectionString string

@description('Tags for resources')
param tags object = {}

// App Service Plan (Consumption)
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: '${functionAppName}-plan'
  location: location
  tags: tags
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: true
  }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: appServicePlan.id
    reserved: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
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
          value: toLower(functionAppName)
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'AZURE_OPENAI_ENDPOINT'
          value: azureOpenAIEndpoint
        }
        {
          name: 'AZURE_OPENAI_KEY'
          value: azureOpenAIKey
        }
        {
          name: 'AZURE_OPENAI_DEPLOYMENT'
          value: azureOpenAIDeployment
        }
        {
          name: 'STORAGE_CONNECTION_STRING'
          value: storageConnectionString
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
      ]
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      use32BitWorkerProcess: false
      cors: {
        allowedOrigins: [
          'https://portal.azure.com'
        ]
      }
    }
    httpsOnly: true
  }
}

output functionAppId string = functionApp.id
output functionAppName string = functionApp.name
output functionAppHostName string = functionApp.properties.defaultHostName
output functionAppPrincipalId string = functionApp.identity.principalId
