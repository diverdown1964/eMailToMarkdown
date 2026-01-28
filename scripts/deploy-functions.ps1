# Deploy Azure Functions to the Function App
# Run this after the Functions code is ready

param(
    [string]$FunctionAppName = "emailtomarkdown-func",
    [string]$ResourceGroup = "emailtomarkdown"
)

Write-Host "Deploying Azure Functions to $FunctionAppName..." -ForegroundColor Cyan

# Build the project
Write-Host "`nBuilding Functions project..." -ForegroundColor Yellow
Push-Location "c:\Repos\eMailToMarkdown"

dotnet build --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    Pop-Location
    exit 1
}
Write-Host "✓ Build completed" -ForegroundColor Green

# Publish the project
Write-Host "`nPublishing Functions project..." -ForegroundColor Yellow
dotnet publish --configuration Release --output ./publish
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    Pop-Location
    exit 1
}
Write-Host "✓ Publish completed" -ForegroundColor Green

# Deploy to Azure using func tools
Write-Host "`nDeploying to Azure Function App..." -ForegroundColor Yellow
func azure functionapp publish $FunctionAppName --dotnet-isolated

if ($LASTEXITCODE -ne 0) {
    Write-Host "Deployment failed!" -ForegroundColor Red
    Write-Host "Make sure Azure Functions Core Tools is installed: npm install -g azure-functions-core-tools@4" -ForegroundColor Yellow
    Pop-Location
    exit 1
}

Pop-Location

Write-Host "`n✓ Deployment completed successfully!" -ForegroundColor Green
Write-Host "Function App URL: https://$FunctionAppName.azurewebsites.net" -ForegroundColor Cyan
Write-Host "`nTest the deployment:" -ForegroundColor Yellow
Write-Host "  curl https://$FunctionAppName.azurewebsites.net/api/auth/status/test@example.com" -ForegroundColor White
