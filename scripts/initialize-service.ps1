# Initialize Email-to-Markdown Service
# This script sets up the user preferences and creates the initial subscription

Write-Host "Initializing Email-to-Markdown Service..." -ForegroundColor Cyan

# Load environment variables
$envFile = "c:\Repos\eMailToMarkdown\.env"
if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]+)\s*=\s*(.+)\s*$') {
            [Environment]::SetEnvironmentVariable($matches[1].Trim(), $matches[2].Trim(), "Process")
        }
    }
} else {
    Write-Host "Error: .env file not found!" -ForegroundColor Red
    exit 1
}

# Check required variables
$required = @(
    "AZURE_TENANT_ID",
    "AZURE_CLIENT_ID",
    "AZURE_CLIENT_SECRET",
    "EMAIL_ADDRESS",
    "STORAGE_ACCOUNT_NAME",
    "STORAGE_ACCOUNT_KEY",
    "ONEDRIVE_ROOT_FOLDER"
)

$missing = @()
foreach ($var in $required) {
    if (-not [Environment]::GetEnvironmentVariable($var, "Process")) {
        $missing += $var
    }
}

if ($missing.Count -gt 0) {
    Write-Host "Error: Missing environment variables: $($missing -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Environment variables loaded" -ForegroundColor Green

# Build the project
Write-Host "`nBuilding TypeScript project..." -ForegroundColor Yellow
npm run build

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Build complete" -ForegroundColor Green

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "1. Create Azure Storage Account and update .env with credentials" -ForegroundColor White
Write-Host "2. Run initialization function to create tables and subscription" -ForegroundColor White
Write-Host "3. Start the Function App: npm start" -ForegroundColor White
Write-Host "4. Expose webhook to internet (ngrok or Azure deployment)" -ForegroundColor White
Write-Host "`nSetup complete! 🎉" -ForegroundColor Green
