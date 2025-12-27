# Deployment Guide

## Prerequisites

### Required Tools

1. **Azure CLI** (version 2.50.0 or later)
   - Windows: `winget install Microsoft.AzureCLI`
   - Mac: `brew install azure-cli`
   - Linux: [Installation Guide](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli-linux)

2. **Azure Functions Core Tools** (version 4.x)
   - Windows: `npm install -g azure-functions-core-tools@4`
   - Mac: `brew install azure-functions-core-tools@4`
   - Linux: [Installation Guide](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)

3. **.NET 8 SDK**
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0
   - Verify: `dotnet --version` (should be 8.0.x)

4. **Git**
   - Download: https://git-scm.com/downloads
   - Verify: `git --version`

5. **(Optional) Visual Studio Code**
   - Download: https://code.visualstudio.com/
   - Recommended Extensions:
     - Azure Functions
     - Azure Logic Apps (Standard)
     - Bicep

### Azure Resources Prerequisites

1. **Active Azure Subscription**
   - Verify access: `az account show`
   - Set subscription: `az account set --subscription <subscription-id>`

2. **Azure OpenAI Service**
   - **Model**: GPT-4o deployment
   - **Region**: Any supported region (preferably same as Logic App for lower latency)
   - **Note**: You need to request access to Azure OpenAI if not already granted
   - **Documentation**: [Azure OpenAI Quickstart](https://learn.microsoft.com/en-us/azure/ai-services/openai/quickstart)

3. **Required Azure Permissions**
   - `Contributor` role on subscription or resource group
   - Permissions to create:
     - Storage Accounts
     - App Service Plans
     - Function Apps
     - Logic Apps (Standard)
     - Application Insights

4. **Office 365 Account**
   - Required for sending email notifications
   - Must have permissions to use Office 365 Connector in Logic Apps

## Step-by-Step Deployment

### Step 1: Clone Repository

```bash
git clone https://github.com/zer0big/azure-security-blog-automation.git
cd azure-security-blog-automation
```

### Step 2: Configure Azure OpenAI

#### Option A: Use Existing Azure OpenAI

1. Get your Azure OpenAI endpoint and key:
   ```bash
   # List your Azure OpenAI resources
   az cognitiveservices account list --query "[?kind=='OpenAI'].{Name:name, Endpoint:properties.endpoint, ResourceGroup:resourceGroup}" --output table
   
   # Get the API key
   az cognitiveservices account keys list --name <your-openai-resource-name> --resource-group <resource-group> --query key1 --output tsv
   ```

2. Set environment variables:
   ```powershell
   # PowerShell
   $env:AZURE_OPENAI_ENDPOINT = "https://<your-openai-resource>.openai.azure.com/"
   $env:AZURE_OPENAI_KEY = "<your-api-key>"
   ```
   
   ```bash
   # Bash
   export AZURE_OPENAI_ENDPOINT="https://<your-openai-resource>.openai.azure.com/"
   export AZURE_OPENAI_KEY="<your-api-key>"
   ```

#### Option B: Create New Azure OpenAI Resource

```bash
# Create Azure OpenAI resource
az cognitiveservices account create \
  --name "aoai-security-blog-automation" \
  --resource-group "rg-security-blog-automation-dev" \
  --location "koreacentral" \
  --kind "OpenAI" \
  --sku "S0"

# Deploy GPT-4o model
az cognitiveservices account deployment create \
  --name "aoai-security-blog-automation" \
  --resource-group "rg-security-blog-automation-dev" \
  --deployment-name "gpt-4o" \
  --model-name "gpt-4o" \
  --model-version "2024-08-06" \
  --model-format "OpenAI" \
  --sku-capacity 10 \
  --sku-name "Standard"
```

### Step 3: Deploy Infrastructure with Bicep

Navigate to the infrastructure directory:

```bash
cd infra
```

#### Option A: Deploy with PowerShell (Windows)

```powershell
.\deploy.ps1 `
  -AzureOpenAIEndpoint $env:AZURE_OPENAI_ENDPOINT `
  -AzureOpenAIKey $env:AZURE_OPENAI_KEY `
  -ResourceGroupName "rg-security-blog-automation-dev" `
  -Location "koreacentral"
```

#### Option B: Deploy with Bash (Linux/Mac)

```bash
chmod +x deploy.sh

./deploy.sh \
  --openai-endpoint "$AZURE_OPENAI_ENDPOINT" \
  --openai-key "$AZURE_OPENAI_KEY" \
  --resource-group "rg-security-blog-automation-dev" \
  --location "koreacentral"
```

#### Expected Output

```
✅ Deployment completed successfully! 🎉

📋 Deployment Summary:
  Resource Group     : rg-security-blog-automation-dev
  Storage Account    : stdevsecurityblog
  Function App       : func-dev-security-blog-automation
  Function App URL   : https://func-dev-security-blog-automation.azurewebsites.net
  Logic App          : logic-dev-security-blog-automation
  Logic App URL      : https://logic-dev-security-blog-automation.azurewebsites.net
  Application Insights: appi-dev-security-blog-automation
```

### Step 4: Deploy Function App Code

1. Navigate to functions directory:
   ```bash
   cd ../functions
   ```

2. Build the Function App:
   ```bash
   dotnet build --configuration Release
   ```

3. Deploy to Azure:
   ```bash
   func azure functionapp publish func-dev-security-blog-automation
   ```

4. Verify deployment:
   ```bash
   # Get Function App URL
   az functionapp show --name func-dev-security-blog-automation --resource-group rg-security-blog-automation-dev --query defaultHostName --output tsv
   
   # Test SummarizePost endpoint (requires function key)
   curl https://func-dev-security-blog-automation.azurewebsites.net/api/SummarizePost?code=<function-key>
   ```

### Step 5: Configure Logic App Workflow

#### Import Workflow Definition

1. Open Azure Portal: https://portal.azure.com

2. Navigate to Logic App:
   - Resource Groups → `rg-security-blog-automation-dev`
   - Select `logic-dev-security-blog-automation`

3. **Method 1: Import via Portal**
   - Click "Logic app code view" in left menu
   - Copy contents from `infra/logic-app/workflow-full.json`
   - Paste into editor
   - Save (Note: API connections will need reconfiguration)

4. **Method 2: Import via VS Code**
   - Install "Azure Logic Apps (Standard)" extension
   - Open workspace in VS Code
   - Connect to Azure subscription
   - Right-click Logic App → "Download Remote Workflows"
   - Replace downloaded workflow with `infra/logic-app/workflow-full.json`
   - Upload to Azure

#### Configure API Connections

The workflow uses three types of connections that need authorization:

##### 1. Office 365 Connection (for sending emails)

1. In Logic App Designer, open the "Send_an_email_(V2)" action
2. Click "Change connection"
3. Click "+ Add new"
4. Sign in with your Office 365 account
5. Grant permissions for sending emails
6. Save the workflow

##### 2. RSS Connections (for fetching feeds)

The workflow has 5 RSS actions. These are built-in connectors and typically don't require additional configuration.

If you encounter connection errors:
1. Open each RSS action in Designer
2. Verify the feed URL is correct
3. Test the connection

##### 3. HTTP Actions (calling Azure Functions)

Update the HTTP action URLs to point to your deployed Function App:

1. Open "SummarizePost_HTTP" action
2. Update URI to: `https://func-dev-security-blog-automation.azurewebsites.net/api/SummarizePost?code=<function-key>`
3. Open "GenerateEmailHtml_HTTP" action
4. Update URI to: `https://func-dev-security-blog-automation.azurewebsites.net/api/GenerateEmailHtml?code=<function-key>`

**Get Function Key:**
```bash
az functionapp function keys list \
  --name func-dev-security-blog-automation \
  --resource-group rg-security-blog-automation-dev \
  --function-name SummarizePost \
  --query default --output tsv
```

#### Set Recurrence Schedule

1. Open "Recurrence" trigger in Designer
2. Configure schedule:
   - **Interval**: 1
   - **Frequency**: Hour
   - **At these hours**: 7, 15, 22
   - **Time zone**: (UTC+09:00) Seoul
3. Save the workflow

### Step 6: Verify Deployment

#### Test Function App

```bash
# Get Function App key
FUNCTION_KEY=$(az functionapp function keys list \
  --name func-dev-security-blog-automation \
  --resource-group rg-security-blog-automation-dev \
  --function-name SummarizePost \
  --query default --output tsv)

# Test SummarizePost
curl -X POST "https://func-dev-security-blog-automation.azurewebsites.net/api/SummarizePost?code=$FUNCTION_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://www.microsoft.com/en-us/security/blog/2024/01/01/test-post/",
    "title": "Test Security Post",
    "description": "This is a test description"
  }'
```

Expected response:
```json
{
  "postId": "abc123def456",
  "summaryEnglish": "Test summary in English...",
  "summaryKorean": "한국어 테스트 요약...",
  "isProcessed": true
}
```

#### Test Logic App Manually

1. Go to Azure Portal → Logic App
2. Click "Overview" → "Run Trigger" → "Recurrence"
3. Monitor run history:
   - Click on the run
   - Expand each action to see inputs/outputs
   - Verify email was sent

4. Check Application Insights:
   ```bash
   az monitor app-insights query \
     --app appi-dev-security-blog-automation \
     --resource-group rg-security-blog-automation-dev \
     --analytics-query "traces | where timestamp > ago(1h) | order by timestamp desc | take 50"
   ```

#### Verify Table Storage

```bash
# List ProcessedPosts table entries
az storage entity query \
  --account-name stdevsecurityblog \
  --table-name ProcessedPosts \
  --auth-mode login \
  --query "items[].{PartitionKey:PartitionKey, RowKey:RowKey, Title:Title, ProcessedDate:ProcessedDate}" \
  --output table
```

### Step 7: Email Verification

1. Check the email recipient inbox (configured in Office 365 action)
2. Verify email format:
   - **Subject**: `[Microsoft Azure 업데이트] 최근 게시글 요약 (신규 X개)` or `[Microsoft Azure 업데이트] 최근 게시글 요약 (신규 없음)`
   - **Body**: HTML formatted with:
     - Header with date
     - List of posts grouped by feed
     - English and Korean summaries
     - Footer with links

3. Expected email samples:
   - **With new posts**: Shows count, summaries, and links
   - **No new posts**: Shows "새로운 게시글 0개" message

## Troubleshooting

### Common Issues

#### 1. Bicep Deployment Fails

**Error**: `Resource 'Microsoft.Storage/storageAccounts' already exists`

**Solution**:
```bash
# Delete existing resource group
az group delete --name rg-security-blog-automation-dev --yes

# Redeploy
cd infra
.\deploy.ps1 -AzureOpenAIEndpoint $env:AZURE_OPENAI_ENDPOINT -AzureOpenAIKey $env:AZURE_OPENAI_KEY
```

#### 2. Function App Not Responding

**Error**: `503 Service Unavailable`

**Solution**:
```bash
# Restart Function App
az functionapp restart --name func-dev-security-blog-automation --resource-group rg-security-blog-automation-dev

# Check logs
az functionapp log tail --name func-dev-security-blog-automation --resource-group rg-security-blog-automation-dev
```

#### 3. Logic App Connection Errors

**Error**: `Unauthorized` or `Connection not authenticated`

**Solution**:
1. Navigate to Logic App → API Connections
2. Click on each connection (Office 365, RSS)
3. Re-authorize the connection
4. Test the workflow again

#### 4. Azure OpenAI Rate Limiting

**Error**: `429 Too Many Requests`

**Solution**:
```bash
# Increase Azure OpenAI quota
az cognitiveservices account deployment update \
  --name <your-openai-resource> \
  --resource-group <resource-group> \
  --deployment-name gpt-4o \
  --sku-capacity 20
```

#### 5. Email Not Received

**Checklist**:
- [ ] Office 365 connection authenticated
- [ ] Email recipient address correct in "Send_an_email_(V2)" action
- [ ] Logic App run history shows success
- [ ] Check spam/junk folder
- [ ] Verify Office 365 account has permissions to send emails

#### 6. Table Storage Access Denied

**Error**: `AuthorizationFailure` or `403 Forbidden`

**Solution**:
```bash
# Assign Storage Table Data Contributor role to Function App Managed Identity
FUNCTION_PRINCIPAL_ID=$(az functionapp identity show --name func-dev-security-blog-automation --resource-group rg-security-blog-automation-dev --query principalId --output tsv)

az role assignment create \
  --assignee $FUNCTION_PRINCIPAL_ID \
  --role "Storage Table Data Contributor" \
  --scope "/subscriptions/<subscription-id>/resourceGroups/rg-security-blog-automation-dev/providers/Microsoft.Storage/storageAccounts/stdevsecurityblog"
```

### Debugging Tips

#### Enable Verbose Logging

**Function App**:
```bash
# Set logging level to Information
az functionapp config appsettings set \
  --name func-dev-security-blog-automation \
  --resource-group rg-security-blog-automation-dev \
  --settings "AzureFunctionsJobHost__logging__logLevel__default=Information"
```

**Logic App**:
1. Go to Logic App → Settings → Workflow settings
2. Enable "Run history with input/output details"
3. Save

#### View Application Insights Live Metrics

```bash
# Open Live Metrics in browser
az monitor app-insights component show \
  --app appi-dev-security-blog-automation \
  --resource-group rg-security-blog-automation-dev \
  --query "appId" --output tsv
```

Navigate to: `https://portal.azure.com/#@<tenant-id>/resource/subscriptions/<subscription-id>/resourceGroups/rg-security-blog-automation-dev/providers/microsoft.insights/components/appi-dev-security-blog-automation/quickPulse`

#### Export Logs for Analysis

```bash
# Export last 24 hours of Function logs
az monitor app-insights query \
  --app appi-dev-security-blog-automation \
  --resource-group rg-security-blog-automation-dev \
  --analytics-query "traces | where timestamp > ago(24h) | project timestamp, message, severityLevel | order by timestamp desc" \
  --output json > function-logs.json
```

## Post-Deployment Configuration

### Customize Email Recipients

1. Open Logic App Designer
2. Find "Send_an_email_(V2)" action
3. Modify:
   - **To**: Add or change email addresses
   - **CC**: Add carbon copy recipients
   - **Subject**: Customize subject line template
   - **Body**: Modify HTML template (advanced)

### Adjust Schedule

To change the email frequency (e.g., only once a day at 09:00):

1. Open Logic App Designer
2. Click "Recurrence" trigger
3. Update:
   - **At these hours**: 9
   - **Time zone**: (UTC+09:00) Seoul
4. Save

### Add More RSS Feeds

1. Open Logic App Designer
2. Duplicate one of the existing RSS feed processing blocks
3. Update:
   - **Feed URL**: New RSS feed URL
   - **Feed Name**: Emoji + descriptive name
4. Connect to the same processing flow
5. Save

### Change Azure OpenAI Model

```bash
# Update Function App application setting
az functionapp config appsettings set \
  --name func-dev-security-blog-automation \
  --resource-group rg-security-blog-automation-dev \
  --settings "AZURE_OPENAI_DEPLOYMENT=gpt-4o-mini"
```

## Cleanup

### Delete All Resources

```bash
# Delete resource group (removes all resources)
az group delete --name rg-security-blog-automation-dev --yes --no-wait
```

### Partial Cleanup (Keep Storage)

```bash
# Delete Function App
az functionapp delete --name func-dev-security-blog-automation --resource-group rg-security-blog-automation-dev

# Delete Logic App
az logicapp delete --name logic-dev-security-blog-automation --resource-group rg-security-blog-automation-dev

# Keep Storage Account for historical data
```

## Production Deployment

For production environments, consider:

1. **Use Production Parameter File**:
   ```bash
   .\deploy.ps1 -ParameterFile "./parameters/prod.bicepparam" -ResourceGroupName "rg-security-blog-automation-prod"
   ```

2. **Enable Enhanced Security**:
   - Use Azure Key Vault for secrets
   - Enable Private Endpoints for Storage
   - Configure Managed Identity for all connections
   - Enable Azure Defender for Cloud

3. **Configure Monitoring**:
   - Set up Azure Monitor alerts
   - Configure email notifications for failures
   - Enable availability tests

4. **Implement CI/CD**:
   - Use GitHub Actions or Azure DevOps
   - Automate Bicep deployments
   - Automate Function App code deployments

## Support & Feedback

- **GitHub Issues**: https://github.com/zer0big/azure-security-blog-automation/issues
- **Azure Support**: [Azure Portal Support](https://portal.azure.com/#blade/Microsoft_Azure_Support/HelpAndSupportBlade)
- **Microsoft Docs**: [Azure Documentation](https://learn.microsoft.com/azure/)

## Next Steps

- Review [ARCHITECTURE.md](ARCHITECTURE.md) for detailed system design
- Explore [LOGIC_APP_WORKFLOW.md](LOGIC_APP_WORKFLOW.md) for workflow deep-dive
- Customize email templates in `functions/GenerateEmailHtml.cs`
- Add more RSS feeds or change summarization prompts
