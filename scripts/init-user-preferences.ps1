# Initialize User Preferences in Azure Table Storage
# Run this after deploying to Azure to set up initial user configuration

param(
    [string]$StorageAccountName,
    [string]$StorageAccountKey,
    [string]$UserId = "storage1@bifocal.show",
    [string]$RootFolder = "/EmailToMarkdown",
    [string]$StorageProvider = "onedrive"
)

Write-Host "Initializing User Preferences..." -ForegroundColor Cyan

# Add user preferences
Write-Host "`nAdding user preferences for $UserId..." -ForegroundColor Yellow

$userPrefs = @{
    PartitionKey = $UserId
    RowKey = "preferences"
    rootFolder = $RootFolder
    storageProvider = $StorageProvider
    emailAddress = $UserId
}

# Convert to JSON for Azure CLI
$propsJson = ($userPrefs.GetEnumerator() | ForEach-Object {
    if ($_.Key -notin @('PartitionKey', 'RowKey')) {
        "`"$($_.Key)`":`"$($_.Value)`""
    }
}) -join ","

az storage entity insert `
    --account-name $StorageAccountName `
    --account-key $StorageAccountKey `
    --table-name "UserPreferences" `
    --entity PartitionKey=$UserId RowKey=preferences $propsJson

Write-Host "✓ User preferences added" -ForegroundColor Green
Write-Host "`nConfiguration:" -ForegroundColor Cyan
Write-Host "  User: $UserId" -ForegroundColor White
Write-Host "  Root Folder: $RootFolder" -ForegroundColor White
Write-Host "  Storage Provider: $StorageProvider" -ForegroundColor White
Write-Host "`nInitialization complete! ✓" -ForegroundColor Green
