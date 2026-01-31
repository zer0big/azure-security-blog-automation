using './main.bicep'

// Environment
param environmentName = 'dev'
param location = 'koreacentral'

// Resource Names
param storageAccountName = 'stdevsecurityblog'
param functionAppName = 'func-dev-security-blog-automation'
param logicAppName = 'logic-dev-security-blog-automation'
param appInsightsName = 'appi-dev-security-blog-automation'
param workspaceName = 'log-dev-security-blog-automation'

// Azure OpenAI Configuration
param azureOpenAIEndpoint = readEnvironmentVariable('AZURE_OPENAI_ENDPOINT', '')
param azureOpenAIKey = readEnvironmentVariable('AZURE_OPENAI_KEY', '')
param azureOpenAIDeployment = 'gpt-4o'

// Tags
param tags = {
  Environment: 'Development'
  Project: 'Azure Security Blog Automation'
  ManagedBy: 'Bicep'
}
