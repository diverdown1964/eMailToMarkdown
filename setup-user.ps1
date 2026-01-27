# Setup script for configuring a user in the UserPreferences table
# This allows the user to send emails to storage1@bifocal.show and have them saved to their OneDrive
#
# Examples:
#   Basic setup (saves to /EmailToMarkdown in user's OneDrive):
#     .\setup-user.ps1 -UserEmail "user@example.com"
#
#   Save to a different OneDrive account:
#     .\setup-user.ps1 -UserEmail "user@example.com" -OneDriveUserEmail "storage@example.com"
#
#   Obsidian vault integration (vault synced to OneDrive):
#     .\setup-user.ps1 -UserEmail "user@example.com" -RootFolder "/MyVault/Inbox" -DeliveryMethod "onedrive"
#     .\setup-user.ps1 -UserEmail "user@example.com" -RootFolder "/Documents/ObsidianVault/EmailCapture" -DeliveryMethod "onedrive"
#
#   Delivery method options:
#     -DeliveryMethod "email"    - Send markdown as email attachment (default)
#     -DeliveryMethod "onedrive" - Save to OneDrive only
#     -DeliveryMethod "both"     - Send email AND save to OneDrive

param(
    [Parameter(Mandatory=$true)]
    [string]$UserEmail,
    
    [Parameter(Mandatory=$false)]
    [string]$OneDriveUserEmail = "",
    
    [Parameter(Mandatory=$false)]
    [string]$RootFolder = "/EmailToMarkdown",
    
    [Parameter(Mandatory=$false)]
    [string]$StorageProvider = "onedrive",

    [Parameter(Mandatory=$false)]
    [ValidateSet("email", "onedrive", "both")]
    [string]$DeliveryMethod = "email",

    [Parameter(Mandatory=$false)]
    [int]$PollingIntervalSeconds = 60,
    
    [Parameter(Mandatory=$false)]
    [string]$ResourceGroup = "emailtomarkdown",
    
    [Parameter(Mandatory=$false)]
    [string]$FunctionAppName = "emailtomarkdown-func",
    
    [Parameter(Mandatory=$false)]
    [string]$StorageAccountName = "emailtomd6647"
)

Write-Host "Setting up user: $UserEmail" -ForegroundColor Cyan

# If OneDriveUserEmail is not specified, use the UserEmail
if ([string]::IsNullOrEmpty($OneDriveUserEmail)) {
    $OneDriveUserEmail = $UserEmail
}

# 1. Add user to UserPreferences table in Azure Table Storage
Write-Host "`nAdding user preferences to Table Storage..." -ForegroundColor Yellow

# Get storage account key
$storageKey = az storage account keys list `
    --account-name $StorageAccountName `
    --resource-group $ResourceGroup `
    --query "[0].value" `
    -o tsv

# Create UserPreferences table if it doesn't exist
$tableName = "UserPreferences"
az storage table create `
    --name $tableName `
    --account-name $StorageAccountName `
    --account-key $storageKey `
    --output none 2>$null

# Add user preferences using az storage entity
az storage entity insert `
    --table-name $tableName `
    --account-name $StorageAccountName `
    --account-key $storageKey `
    --entity PartitionKey=$UserEmail RowKey=preferences EmailAddress=$UserEmail OneDriveUserEmail=$OneDriveUserEmail RootFolder=$RootFolder StorageProvider=$StorageProvider DeliveryMethod=$DeliveryMethod `
    --if-exists replace `
    --output none

Write-Host "✓ User preferences saved" -ForegroundColor Green

# 2. Update Function App settings with polling interval
Write-Host "`nUpdating Function App settings..." -ForegroundColor Yellow

# Calculate cron expression from seconds
# For minutes: "0 */N * * * *" where N is minutes
# For seconds: Need to use specific seconds pattern
$cronExpression = if ($PollingIntervalSeconds -lt 60) {
    "0/$PollingIntervalSeconds * * * * *"
} elseif ($PollingIntervalSeconds -eq 60) {
    "0 * * * * *"
} else {
    $minutes = [Math]::Floor($PollingIntervalSeconds / 60)
    "0 */$minutes * * * *"
}

$settings = @{
    "POLLING_INTERVAL_SECONDS" = $PollingIntervalSeconds.ToString()
    "POLLING_CRON" = $cronExpression
}

foreach ($key in $settings.Keys) {
    az functionapp config appsettings set `
        --name $FunctionAppName `
        --resource-group $ResourceGroup `
        --settings "$key=$($settings[$key])" `
        --output none
}

Write-Host "✓ Function App settings updated" -ForegroundColor Green
Write-Host "  - POLLING_INTERVAL_SECONDS: $PollingIntervalSeconds" -ForegroundColor Gray
Write-Host "  - POLLING_CRON: $cronExpression" -ForegroundColor Gray

# 3. Display summary
Write-Host "`n=== Setup Complete ===" -ForegroundColor Green
Write-Host "User Email: $UserEmail" -ForegroundColor Cyan
Write-Host "OneDrive Destination: $OneDriveUserEmail" -ForegroundColor Cyan
Write-Host "Root Folder: $RootFolder" -ForegroundColor Cyan
Write-Host "Storage Provider: $StorageProvider" -ForegroundColor Cyan
Write-Host "Delivery Method: $DeliveryMethod" -ForegroundColor Cyan
Write-Host "Polling Interval: $PollingIntervalSeconds seconds" -ForegroundColor Cyan
Write-Host "`nInstructions:" -ForegroundColor Yellow
Write-Host "1. Send an email to storage1@bifocal.show from $UserEmail"
Write-Host "2. Wait up to $PollingIntervalSeconds seconds for processing"

if ($DeliveryMethod -eq "email") {
    Write-Host "3. Check your inbox for a reply with the markdown file attached"
} elseif ($DeliveryMethod -eq "onedrive") {
    Write-Host "3. Check $OneDriveUserEmail's OneDrive at $RootFolder/YYYY/MM/DD/"
} else {
    Write-Host "3. Check your inbox for a reply AND $OneDriveUserEmail's OneDrive at $RootFolder/YYYY/MM/DD/"
}

Write-Host "`nTip: For Obsidian users, use -DeliveryMethod 'onedrive' and set RootFolder to your vault's OneDrive path" -ForegroundColor Gray
