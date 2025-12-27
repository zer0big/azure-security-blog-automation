#!/usr/bin/env pwsh
#Requires -Version 7.0

<#
.SYNOPSIS
    Deploy Azure Security Blog Automation infrastructure to Azure.

.DESCRIPTION
    This script deploys all required Azure resources including Storage Account,
    Function App, Logic App, and Application Insights using Bicep templates.

.PARAMETER SubscriptionId
    Azure Subscription ID. If not provided, uses current subscription.

.PARAMETER ResourceGroupName
    Name of the resource group to deploy to. Defaults to 'rg-security-blog-automation-dev'.

.PARAMETER Location
    Azure region for deployment. Defaults to 'koreacentral'.

.PARAMETER ParameterFile
    Path to Bicep parameter file. Defaults to './parameters/dev.bicepparam'.

.PARAMETER AzureOpenAIEndpoint
    Azure OpenAI endpoint URL (required).

.PARAMETER AzureOpenAIKey
    Azure OpenAI API key (required).

.EXAMPLE
    .\deploy.ps1 -AzureOpenAIEndpoint "https://your-openai.openai.azure.com/" -AzureOpenAIKey "your-key"

.EXAMPLE
    .\deploy.ps1 -ResourceGroupName "rg-prod" -Location "eastus" -ParameterFile "./parameters/prod.bicepparam"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId = "",

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName = "rg-security-blog-automation-dev",

    [Parameter(Mandatory = $false)]
    [string]$Location = "koreacentral",

    [Parameter(Mandatory = $false)]
    [string]$ParameterFile = "./parameters/dev.bicepparam",

    [Parameter(Mandatory = $true)]
    [string]$AzureOpenAIEndpoint,

    [Parameter(Mandatory = $true)]
    [string]$AzureOpenAIKey
)

$ErrorActionPreference = "Stop"

# Function to write colored output
function Write-Step {
    param([string]$Message)
    Write-Host "🔹 $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Error-Message {
    param([string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

# Main deployment logic
try {
    Write-Host "`n╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
    Write-Host "║  Azure Security Blog Automation - Infrastructure Deployment   ║" -ForegroundColor Magenta
    Write-Host "╚════════════════════════════════════════════════════════════════╝`n" -ForegroundColor Magenta

    # Set environment variables for Bicep parameter file
    $env:AZURE_OPENAI_ENDPOINT = $AzureOpenAIEndpoint
    $env:AZURE_OPENAI_KEY = $AzureOpenAIKey

    # Step 1: Check Azure CLI
    Write-Step "Checking Azure CLI installation..."
    $azVersion = az version --output json 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI is not installed. Please install from: https://aka.ms/azure-cli"
    }
    Write-Success "Azure CLI version: $($azVersion.'azure-cli')"

    # Step 2: Login check
    Write-Step "Checking Azure login status..."
    $account = az account show 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        Write-Step "Not logged in. Starting Azure login..."
        az login
        if ($LASTEXITCODE -ne 0) {
            throw "Azure login failed"
        }
        $account = az account show | ConvertFrom-Json
    }
    Write-Success "Logged in as: $($account.user.name)"

    # Step 3: Set subscription
    if ($SubscriptionId) {
        Write-Step "Setting subscription to: $SubscriptionId..."
        az account set --subscription $SubscriptionId
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to set subscription"
        }
    }
    $currentSub = az account show | ConvertFrom-Json
    Write-Success "Using subscription: $($currentSub.name) ($($currentSub.id))"

    # Step 4: Check/Create Resource Group
    Write-Step "Checking resource group: $ResourceGroupName..."
    $rg = az group show --name $ResourceGroupName 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        Write-Step "Creating resource group: $ResourceGroupName in $Location..."
        az group create --name $ResourceGroupName --location $Location
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create resource group"
        }
        Write-Success "Resource group created"
    } else {
        Write-Success "Resource group exists"
    }

    # Step 5: Validate Bicep template
    Write-Step "Validating Bicep template..."
    $bicepPath = Join-Path $PSScriptRoot "main.bicep"
    $paramPath = Join-Path $PSScriptRoot $ParameterFile

    if (-not (Test-Path $bicepPath)) {
        throw "Bicep template not found at: $bicepPath"
    }
    if (-not (Test-Path $paramPath)) {
        throw "Parameter file not found at: $paramPath"
    }

    az deployment group validate `
        --resource-group $ResourceGroupName `
        --template-file $bicepPath `
        --parameters $paramPath

    if ($LASTEXITCODE -ne 0) {
        throw "Bicep template validation failed"
    }
    Write-Success "Bicep template validation passed"

    # Step 6: Deploy infrastructure
    Write-Host "`n╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
    Write-Host "║  Starting Infrastructure Deployment                           ║" -ForegroundColor Yellow
    Write-Host "╚════════════════════════════════════════════════════════════════╝`n" -ForegroundColor Yellow

    $deploymentName = "security-blog-automation-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

    az deployment group create `
        --name $deploymentName `
        --resource-group $ResourceGroupName `
        --template-file $bicepPath `
        --parameters $paramPath `
        --verbose

    if ($LASTEXITCODE -ne 0) {
        throw "Deployment failed"
    }

    # Step 7: Get deployment outputs
    Write-Step "Retrieving deployment outputs..."
    $outputs = az deployment group show `
        --name $deploymentName `
        --resource-group $ResourceGroupName `
        --query properties.outputs `
        --output json | ConvertFrom-Json

    # Display deployment summary
    Write-Host "`n╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "║  Deployment Completed Successfully                            ║" -ForegroundColor Green
    Write-Host "╚════════════════════════════════════════════════════════════════╝`n" -ForegroundColor Green

    Write-Host "📋 Deployment Summary:" -ForegroundColor Cyan
    Write-Host "  Resource Group     : $($outputs.resourceGroupName.value)" -ForegroundColor White
    Write-Host "  Storage Account    : $($outputs.storageAccountName.value)" -ForegroundColor White
    Write-Host "  Function App       : $($outputs.functionAppName.value)" -ForegroundColor White
    Write-Host "  Function App URL   : https://$($outputs.functionAppHostName.value)" -ForegroundColor White
    Write-Host "  Logic App          : $($outputs.logicAppName.value)" -ForegroundColor White
    Write-Host "  Logic App URL      : https://$($outputs.logicAppHostName.value)" -ForegroundColor White
    Write-Host "  Application Insights: $($outputs.appInsightsName.value)" -ForegroundColor White

    Write-Host "`n📌 Next Steps:" -ForegroundColor Yellow
    Write-Host "  1. Deploy Function App code:" -ForegroundColor White
    Write-Host "     cd functions" -ForegroundColor Gray
    Write-Host "     func azure functionapp publish $($outputs.functionAppName.value)" -ForegroundColor Gray
    Write-Host "`n  2. Configure Logic App workflow:" -ForegroundColor White
    Write-Host "     - Import workflow from: infra/logic-app/workflow-full.json" -ForegroundColor Gray
    Write-Host "     - Configure API connections (Office 365, RSS, HTTP)" -ForegroundColor Gray
    Write-Host "     - Set recurrence schedule: 07:00, 15:00, 22:00 KST" -ForegroundColor Gray
    Write-Host "`n  3. Verify deployment:" -ForegroundColor White
    Write-Host "     - Check Application Insights for telemetry" -ForegroundColor Gray
    Write-Host "     - Test Function endpoints" -ForegroundColor Gray
    Write-Host "     - Manually trigger Logic App workflow" -ForegroundColor Gray

    Write-Success "`nDeployment completed successfully! 🎉"

} catch {
    Write-Error-Message "`nDeployment failed: $_"
    exit 1
} finally {
    # Clean up environment variables
    Remove-Item Env:AZURE_OPENAI_ENDPOINT -ErrorAction SilentlyContinue
    Remove-Item Env:AZURE_OPENAI_KEY -ErrorAction SilentlyContinue
}
