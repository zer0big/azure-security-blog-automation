#!/bin/bash

# Azure Security Blog Automation - Infrastructure Deployment Script
# Description: Deploy all required Azure resources using Bicep templates
# Requirements: Azure CLI, Bicep CLI

set -euo pipefail

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

# Default parameters
SUBSCRIPTION_ID="${SUBSCRIPTION_ID:-}"
RESOURCE_GROUP_NAME="${RESOURCE_GROUP_NAME:-rg-security-blog-automation-dev}"
LOCATION="${LOCATION:-koreacentral}"
PARAMETER_FILE="${PARAMETER_FILE:-./parameters/dev.bicepparam}"
AZURE_OPENAI_ENDPOINT="${AZURE_OPENAI_ENDPOINT:-}"
AZURE_OPENAI_KEY="${AZURE_OPENAI_KEY:-}"

# Function to print colored messages
print_step() {
    echo -e "${CYAN}🔹 $1${NC}"
}

print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

# Function to check command existence
check_command() {
    if ! command -v "$1" &> /dev/null; then
        print_error "$1 is not installed. Please install it first."
        exit 1
    fi
}

# Function to show usage
show_usage() {
    cat << EOF
Usage: $0 [OPTIONS]

Deploy Azure Security Blog Automation infrastructure to Azure.

Options:
    -s, --subscription ID          Azure Subscription ID
    -g, --resource-group NAME      Resource Group name (default: rg-security-blog-automation-dev)
    -l, --location LOCATION        Azure region (default: koreacentral)
    -p, --parameter-file FILE      Bicep parameter file (default: ./parameters/dev.bicepparam)
    -e, --openai-endpoint URL      Azure OpenAI endpoint (required)
    -k, --openai-key KEY           Azure OpenAI key (required)
    -h, --help                     Show this help message

Environment Variables:
    SUBSCRIPTION_ID                Azure Subscription ID
    RESOURCE_GROUP_NAME            Resource Group name
    LOCATION                       Azure region
    PARAMETER_FILE                 Bicep parameter file path
    AZURE_OPENAI_ENDPOINT          Azure OpenAI endpoint URL
    AZURE_OPENAI_KEY               Azure OpenAI API key

Example:
    $0 -e "https://your-openai.openai.azure.com/" -k "your-api-key"
    
    export AZURE_OPENAI_ENDPOINT="https://your-openai.openai.azure.com/"
    export AZURE_OPENAI_KEY="your-api-key"
    $0 -g rg-prod -l eastus

EOF
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -s|--subscription)
            SUBSCRIPTION_ID="$2"
            shift 2
            ;;
        -g|--resource-group)
            RESOURCE_GROUP_NAME="$2"
            shift 2
            ;;
        -l|--location)
            LOCATION="$2"
            shift 2
            ;;
        -p|--parameter-file)
            PARAMETER_FILE="$2"
            shift 2
            ;;
        -e|--openai-endpoint)
            AZURE_OPENAI_ENDPOINT="$2"
            shift 2
            ;;
        -k|--openai-key)
            AZURE_OPENAI_KEY="$2"
            shift 2
            ;;
        -h|--help)
            show_usage
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            show_usage
            exit 1
            ;;
    esac
done

# Validate required parameters
if [[ -z "$AZURE_OPENAI_ENDPOINT" ]]; then
    print_error "Azure OpenAI endpoint is required. Use -e or set AZURE_OPENAI_ENDPOINT environment variable."
    show_usage
    exit 1
fi

if [[ -z "$AZURE_OPENAI_KEY" ]]; then
    print_error "Azure OpenAI key is required. Use -k or set AZURE_OPENAI_KEY environment variable."
    show_usage
    exit 1
fi

# Main deployment logic
main() {
    echo ""
    echo -e "${MAGENTA}╔════════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${MAGENTA}║  Azure Security Blog Automation - Infrastructure Deployment   ║${NC}"
    echo -e "${MAGENTA}╚════════════════════════════════════════════════════════════════╝${NC}"
    echo ""

    # Export environment variables for Bicep
    export AZURE_OPENAI_ENDPOINT
    export AZURE_OPENAI_KEY

    # Step 1: Check prerequisites
    print_step "Checking prerequisites..."
    check_command "az"
    check_command "jq"
    
    AZ_VERSION=$(az version --query '\"azure-cli\"' -o tsv 2>/dev/null || echo "unknown")
    print_success "Azure CLI version: $AZ_VERSION"

    # Step 2: Check Azure login status
    print_step "Checking Azure login status..."
    if ! az account show &> /dev/null; then
        print_step "Not logged in. Starting Azure login..."
        az login
    fi
    
    ACCOUNT_NAME=$(az account show --query user.name -o tsv)
    print_success "Logged in as: $ACCOUNT_NAME"

    # Step 3: Set subscription
    if [[ -n "$SUBSCRIPTION_ID" ]]; then
        print_step "Setting subscription to: $SUBSCRIPTION_ID..."
        az account set --subscription "$SUBSCRIPTION_ID"
    fi
    
    CURRENT_SUB=$(az account show --query name -o tsv)
    CURRENT_SUB_ID=$(az account show --query id -o tsv)
    print_success "Using subscription: $CURRENT_SUB ($CURRENT_SUB_ID)"

    # Step 4: Check/Create Resource Group
    print_step "Checking resource group: $RESOURCE_GROUP_NAME..."
    if ! az group show --name "$RESOURCE_GROUP_NAME" &> /dev/null; then
        print_step "Creating resource group: $RESOURCE_GROUP_NAME in $LOCATION..."
        az group create --name "$RESOURCE_GROUP_NAME" --location "$LOCATION" --output none
        print_success "Resource group created"
    else
        print_success "Resource group exists"
    fi

    # Step 5: Validate Bicep template
    print_step "Validating Bicep template..."
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    BICEP_PATH="$SCRIPT_DIR/main.bicep"
    PARAM_PATH="$SCRIPT_DIR/$PARAMETER_FILE"

    if [[ ! -f "$BICEP_PATH" ]]; then
        print_error "Bicep template not found at: $BICEP_PATH"
        exit 1
    fi

    if [[ ! -f "$PARAM_PATH" ]]; then
        print_error "Parameter file not found at: $PARAM_PATH"
        exit 1
    fi

    az deployment group validate \
        --resource-group "$RESOURCE_GROUP_NAME" \
        --template-file "$BICEP_PATH" \
        --parameters "$PARAM_PATH" \
        --output none

    print_success "Bicep template validation passed"

    # Step 6: Deploy infrastructure
    echo ""
    echo -e "${YELLOW}╔════════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${YELLOW}║  Starting Infrastructure Deployment                           ║${NC}"
    echo -e "${YELLOW}╚════════════════════════════════════════════════════════════════╝${NC}"
    echo ""

    DEPLOYMENT_NAME="security-blog-automation-$(date +%Y%m%d-%H%M%S)"

    az deployment group create \
        --name "$DEPLOYMENT_NAME" \
        --resource-group "$RESOURCE_GROUP_NAME" \
        --template-file "$BICEP_PATH" \
        --parameters "$PARAM_PATH" \
        --verbose

    # Step 7: Get deployment outputs
    print_step "Retrieving deployment outputs..."
    OUTPUTS=$(az deployment group show \
        --name "$DEPLOYMENT_NAME" \
        --resource-group "$RESOURCE_GROUP_NAME" \
        --query properties.outputs \
        --output json)

    RESOURCE_GROUP_OUT=$(echo "$OUTPUTS" | jq -r '.resourceGroupName.value')
    STORAGE_ACCOUNT=$(echo "$OUTPUTS" | jq -r '.storageAccountName.value')
    FUNCTION_APP=$(echo "$OUTPUTS" | jq -r '.functionAppName.value')
    FUNCTION_HOST=$(echo "$OUTPUTS" | jq -r '.functionAppHostName.value')
    LOGIC_APP=$(echo "$OUTPUTS" | jq -r '.logicAppName.value')
    LOGIC_HOST=$(echo "$OUTPUTS" | jq -r '.logicAppHostName.value')
    APP_INSIGHTS=$(echo "$OUTPUTS" | jq -r '.appInsightsName.value')

    # Display deployment summary
    echo ""
    echo -e "${GREEN}╔════════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║  Deployment Completed Successfully                            ║${NC}"
    echo -e "${GREEN}╚════════════════════════════════════════════════════════════════╝${NC}"
    echo ""

    echo -e "${CYAN}📋 Deployment Summary:${NC}"
    echo -e "  Resource Group     : $RESOURCE_GROUP_OUT"
    echo -e "  Storage Account    : $STORAGE_ACCOUNT"
    echo -e "  Function App       : $FUNCTION_APP"
    echo -e "  Function App URL   : https://$FUNCTION_HOST"
    echo -e "  Logic App          : $LOGIC_APP"
    echo -e "  Logic App URL      : https://$LOGIC_HOST"
    echo -e "  Application Insights: $APP_INSIGHTS"

    echo ""
    echo -e "${YELLOW}📌 Next Steps:${NC}"
    echo -e "  1. Deploy Function App code:"
    echo -e "     ${CYAN}cd functions${NC}"
    echo -e "     ${CYAN}func azure functionapp publish $FUNCTION_APP${NC}"
    echo ""
    echo -e "  2. Configure Logic App workflow:"
    echo -e "     - Import workflow from: ${CYAN}infra/logic-app/workflow-full.json${NC}"
    echo -e "     - Configure API connections (Office 365, RSS, HTTP)"
    echo -e "     - Set recurrence schedule: ${CYAN}07:00, 15:00, 22:00 KST${NC}"
    echo ""
    echo -e "  3. Verify deployment:"
    echo -e "     - Check Application Insights for telemetry"
    echo -e "     - Test Function endpoints"
    echo -e "     - Manually trigger Logic App workflow"
    echo ""

    print_success "Deployment completed successfully! 🎉"
}

# Run main function
main

# Clean up
unset AZURE_OPENAI_ENDPOINT
unset AZURE_OPENAI_KEY
