# Setup Script for EmailToMarkdown Azure AD App Registration
# This script creates an app registration in Microsoft Entra ID with required permissions

Write-Host "Creating Azure AD App Registration for EmailToMarkdown..." -ForegroundColor Cyan

# Step 1: Create the app registration
Write-Host "`nStep 1: Creating app registration..." -ForegroundColor Yellow
$appResult = az ad app create `
    --display-name "EmailToMarkdown" `
    --sign-in-audience AzureADMyOrg `
    --web-redirect-uris "http://localhost:3000/auth/callback" `
    | ConvertFrom-Json

$appId = $appResult.appId
$objectId = $appResult.id

Write-Host "[OK] App created successfully!" -ForegroundColor Green
Write-Host "  App ID (Client ID): $appId" -ForegroundColor White
Write-Host "  Object ID: $objectId" -ForegroundColor White

# Step 2: Add Microsoft Graph API permissions (Application permissions for app-only access)
Write-Host "`nStep 2: Adding Microsoft Graph permissions..." -ForegroundColor Yellow
Write-Host "  - Mail.Read (Application): 810c84a8-4a9e-49e6-bf7d-12d183f40d01" -ForegroundColor Gray
Write-Host "  - Mail.Send (Application): b633e1c5-b582-4048-a93e-9f11b44c7e96" -ForegroundColor Gray
Write-Host "  - Files.ReadWrite.All (Application): 01d4889c-1287-42c6-ac1f-5d1e02578ef6" -ForegroundColor Gray

az ad app permission add `
    --id $appId `
    --api 00000003-0000-0000-c000-000000000000 `
    --api-permissions 810c84a8-4a9e-49e6-bf7d-12d183f40d01=Role b633e1c5-b582-4048-a93e-9f11b44c7e96=Role 01d4889c-1287-42c6-ac1f-5d1e02578ef6=Role

Write-Host "[OK] Permissions added successfully!" -ForegroundColor Green

# Step 3: Grant admin consent
Write-Host "`nStep 3: Granting admin consent..." -ForegroundColor Yellow
az ad app permission admin-consent --id $appId

Write-Host "[OK] Admin consent granted!" -ForegroundColor Green

# Step 4: Create client secret
Write-Host "`nStep 4: Creating client secret..." -ForegroundColor Yellow
$secretResult = az ad app credential reset --id $appId --append | ConvertFrom-Json

$clientSecret = $secretResult.password
$tenantId = $secretResult.tenant

Write-Host "[OK] Client secret created successfully!" -ForegroundColor Green

# Output summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "IMPORTANT: Save these values securely!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "`nTenant ID:       $tenantId" -ForegroundColor White
Write-Host "Client ID:       $appId" -ForegroundColor White
Write-Host "Client Secret:   $clientSecret" -ForegroundColor White
Write-Host "`nRedirect URI:    http://localhost:3000/auth/callback" -ForegroundColor White

# Save to .env.new file
Write-Host "`nCreating .env.new file with new credentials..." -ForegroundColor Yellow
$envContent = @"
# Azure AD App Registration Configuration
AZURE_TENANT_ID=$tenantId
AZURE_CLIENT_ID=$appId
AZURE_CLIENT_SECRET=$clientSecret

# Mailbox to monitor
EMAIL_ADDRESS=your-email@example.com

# Storage Configuration
STORAGE_ACCOUNT_NAME=your-storage-account-name
STORAGE_ACCOUNT_KEY=your-storage-account-key-here

# OneDrive Configuration
ONEDRIVE_ROOT_FOLDER=/EmailToMarkdown
"@

$envContent | Out-File -FilePath "c:\Repos\eMailToMarkdown\.env.new" -Encoding UTF8

Write-Host "[OK] .env.new created!" -ForegroundColor Green
Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "1. Review .env.new and copy to .env" -ForegroundColor White
Write-Host "2. Update local.settings.json with new TENANT_ID, CLIENT_ID, CLIENT_SECRET" -ForegroundColor White
Write-Host "3. Restart the function app" -ForegroundColor White
Write-Host "`nApp registration complete!" -ForegroundColor Green
