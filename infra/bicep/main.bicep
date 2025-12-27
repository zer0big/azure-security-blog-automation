// Main Bicep Template - Azure Security Blog Automation
targetScope = 'resourceGroup'

@description('Environment name (e.g., dev, prod)')
param environmentName string = 'dev'

@description('Location for all resources')
param location string = resourceGroup().location

@description('Storage Account name')
param storageAccountName string

@description('Function App name')
param functionAppName string

@description('Logic App name')
param logicAppName string

@description('Application Insights name')
param appInsightsName string

@description('Log Analytics Workspace name')
param workspaceName string

@description('Azure OpenAI endpoint')
@secure()
param azureOpenAIEndpoint string

@description('Azure OpenAI key')
@secure()
param azureOpenAIKey string

@description('Azure OpenAI deployment name')
param azureOpenAIDeployment string = 'gpt-4o'

@description('Tags for all resources')
param tags object = {
  Environment: environmentName
  Project: 'Azure Security Blog Automation'
  ManagedBy: 'Bicep'
}

// Application Insights Module
module appInsights './modules/app-insights.bicep' = {
  name: 'appInsights-deployment'
  params: {
    appInsightsName: appInsightsName
    workspaceName: workspaceName
    location: location
    tags: tags
  }
}

// Storage Account Module
module storage './modules/storage.bicep' = {
  name: 'storage-deployment'
  params: {
    storageAccountName: storageAccountName
    location: location
    tags: tags
  }
}

// Function App Module
module functionApp './modules/function-app.bicep' = {
  name: 'functionApp-deployment'
  params: {
    functionAppName: functionAppName
    storageAccountName: storageAccountName
    location: location
    appInsightsConnectionString: appInsights.outputs.appInsightsConnectionString
    azureOpenAIEndpoint: azureOpenAIEndpoint
    azureOpenAIKey: azureOpenAIKey
    azureOpenAIDeployment: azureOpenAIDeployment
    storageConnectionString: storage.outputs.primaryConnectionString
    tags: tags
  }
  dependsOn: [
    storage
    appInsights
  ]
}

// Logic App Module
module logicApp './modules/logic-app.bicep' = {
  name: 'logicApp-deployment'
  params: {
    logicAppName: logicAppName
    storageAccountName: storageAccountName
    location: location
    appInsightsConnectionString: appInsights.outputs.appInsightsConnectionString
    storageConnectionString: storage.outputs.primaryConnectionString
    tags: tags
  }
  dependsOn: [
    storage
    appInsights
  ]
}

// Outputs
output resourceGroupName string = resourceGroup().name
output storageAccountName string = storage.outputs.storageAccountName
output functionAppName string = functionApp.outputs.functionAppName
output functionAppHostName string = functionApp.outputs.functionAppHostName
output logicAppName string = logicApp.outputs.logicAppName
output logicAppHostName string = logicApp.outputs.logicAppHostName
output appInsightsName string = appInsights.outputs.appInsightsName
output appInsightsConnectionString string = appInsights.outputs.appInsightsConnectionString
