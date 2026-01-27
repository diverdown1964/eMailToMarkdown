# Deploy Email-to-Markdown Service to Azure
# This script creates all necessary Azure resources in the 'emailtomarkdown' resource group

param(
    [string]$ResourceGroup = "emailtomarkdown",
    [string]$Location = "eastus",
    [string]$StorageAccountName = "emailtomd$(Get-Random -Minimum 1000 -Maximum 9999)",
    [string]$FunctionAppName = "emailtomarkdown-func"
)

Write-Host "Deploying Email-to-Markdown Service to Azure" -ForegroundColor Cyan
Write-Host "Resource Group: $ResourceGroup" -ForegroundColor White
Write-Host "Location: $Location" -ForegroundColor White
Write-Host "Storage Account: $StorageAccountName" -ForegroundColor White
Write-Host "Function App: $FunctionAppName" -ForegroundColor White
Write-Host ""

# Check if logged in to Azure
Write-Host "Checking Azure CLI login status..." -ForegroundColor Yellow
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Not logged in to Azure. Please run 'az login' first." -ForegroundColor Red
    exit 1
}
Write-Host "✓ Logged in as: $($account.user.name)" -ForegroundColor Green
Write-Host "✓ Subscription: $($account.name) ($($account.id))" -ForegroundColor Green

# Create resource group
Write-Host "`nCreating resource group..." -ForegroundColor Yellow
az group create --name $ResourceGroup --location $Location | Out-Null
Write-Host "✓ Resource group '$ResourceGroup' created" -ForegroundColor Green

# Create storage account
Write-Host "`nCreating storage account..." -ForegroundColor Yellow
az storage account create `
    --name $StorageAccountName `
    --resource-group $ResourceGroup `
    --location $Location `
    --sku Standard_LRS `
    --kind StorageV2 | Out-Null

Write-Host "✓ Storage account '$StorageAccountName' created" -ForegroundColor Green

# Get storage account key
Write-Host "`nRetrieving storage account key..." -ForegroundColor Yellow
$storageKey = az storage account keys list `
    --resource-group $ResourceGroup `
    --account-name $StorageAccountName `
    --query "[0].value" `
    --output tsv

Write-Host "✓ Storage account key retrieved" -ForegroundColor Green

# Create storage tables
Write-Host "`nCreating storage tables..." -ForegroundColor Yellow
az storage table create --name "UserPreferences" --account-name $StorageAccountName --account-key $storageKey | Out-Null
az storage table create --name "SubscriptionState" --account-name $StorageAccountName --account-key $storageKey | Out-Null
Write-Host "✓ Tables created: UserPreferences, SubscriptionState" -ForegroundColor Green

# Create blob container for attachments
Write-Host "`nCreating blob container..." -ForegroundColor Yellow
az storage container create `
    --name "email-attachments" `
    --account-name $StorageAccountName `
    --account-key $storageKey `
    --public-access blob | Out-Null
Write-Host "✓ Blob container 'email-attachments' created" -ForegroundColor Green

# Create Function App
Write-Host "`nCreating Function App..." -ForegroundColor Yellow
az functionapp create `
    --resource-group $ResourceGroup `
    --consumption-plan-location $Location `
    --runtime dotnet-isolated `
    --runtime-version 8 `
    --functions-version 4 `
    --name $FunctionAppName `
    --storage-account $StorageAccountName `
    --os-type Windows

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error creating Function App" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Function App '$FunctionAppName' created" -ForegroundColor Green

# Get Function App URL
$functionAppUrl = "https://$FunctionAppName.azurewebsites.net"
Write-Host "✓ Function App URL: $functionAppUrl" -ForegroundColor Green

# Load environment variables from .env
Write-Host "`nLoading configuration from .env..." -ForegroundColor Yellow
$envVars = @{}
Get-Content "c:\Repos\eMailToMarkdown\.env" | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]+)\s*=\s*(.+)\s*$') {
        $envVars[$matches[1].Trim()] = $matches[2].Trim()
    }
}

# Configure Function App settings
Write-Host "`nConfiguring Function App settings..." -ForegroundColor Yellow
az functionapp config appsettings set `
    --name $FunctionAppName `
    --resource-group $ResourceGroup `
    --settings `
        "AZURE_TENANT_ID=$($envVars['AZURE_TENANT_ID'])" `
        "AZURE_CLIENT_ID=$($envVars['AZURE_CLIENT_ID'])" `
        "AZURE_CLIENT_SECRET=$($envVars['AZURE_CLIENT_SECRET'])" `
        "EMAIL_ADDRESS=$($envVars['EMAIL_ADDRESS'])" `
        "STORAGE_ACCOUNT_NAME=$StorageAccountName" `
        "STORAGE_ACCOUNT_KEY=$storageKey" `
        "ONEDRIVE_ROOT_FOLDER=$($envVars['ONEDRIVE_ROOT_FOLDER'])" | Out-Null

Write-Host "✓ Function App settings configured" -ForegroundColor Green

# Build the project
Write-Host "`nBuilding project..." -ForegroundColor Yellow
Set-Location "c:\Repos\eMailToMarkdown"
npm run build | Out-Null
Write-Host "✓ Project built" -ForegroundColor Green

# Deploy to Function App
Write-Host "`nDeploying to Azure..." -ForegroundColor Yellow
func azure functionapp publish $FunctionAppName | Out-Null
Write-Host "✓ Deployment complete" -ForegroundColor Green

# Output summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Deployment Complete!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "`nResource Group: $ResourceGroup" -ForegroundColor White
Write-Host "Storage Account: $StorageAccountName" -ForegroundColor White
Write-Host "Function App: $FunctionAppName" -ForegroundColor White
Write-Host "Function App URL: $functionAppUrl" -ForegroundColor White
Write-Host "`nWebhook URL: $functionAppUrl/api/graphWebhook" -ForegroundColor Yellow
Write-Host "`nNext Steps:" -ForegroundColor Cyan
Write-Host "1. Add user preferences to Table Storage" -ForegroundColor White
Write-Host "2. Create Microsoft Graph subscription with webhook URL" -ForegroundColor White
Write-Host "3. Test by sending an email to your configured SendGrid inbound address" -ForegroundColor White
Write-Host "`nDeployment complete! 🎉" -ForegroundColor Green
