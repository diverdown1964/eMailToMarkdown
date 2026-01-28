# Azure Functions Deployment Guide

## Critical Issue: Solution File Interference

### Problem

When deploying Azure Functions using `func azure functionapp publish`, the presence of a `.sln` (solution) file in the project directory causes deployment failures. The symptoms include:

*   Deployment completes with "success" message
*   Functions list appears empty: `az functionapp function list` returns `[]`
*   HTTP endpoints return 404 errors
*   Functions metadata is not generated correctly

### Root Cause

The `func` CLI tool prioritizes building `.sln` files over `.csproj` files. When building a solution file with the `--output` parameter, the .NET SDK warning indicates:

```
warning NETSDK1194: The "--output" option isn't supported when building a solution.
Specifying a solution-level output path results in all projects copying outputs to
the same directory, which can lead to inconsistent builds.
```

This causes the Functions metadata (`functions.metadata` file) to be generated incorrectly or not at all, resulting in the Azure Functions runtime being unable to discover the Functions.

### Solution

**Option 1: Temporary Solution File Rename (Recommended for one-time deployment)**

```
cd c:\Repos\eMailToMarkdown
Rename-Item eMailToMarkdown.sln eMailToMarkdown.sln.bak
func azure functionapp publish emailtomarkdown-func --dotnet-isolated
Rename-Item eMailToMarkdown.sln.bak eMailToMarkdown.sln
```

**Option 2: Specify Project File (Alternative)**

Some versions of `func` CLI may support specifying the project file directly, though this is not consistently documented.

**Option 3: Use Separate Directory for Deployment**

Create a deployment script that publishes to a clean directory without the solution file.

### Verification Steps

After deployment, verify Functions are registered:

```
# List all functions
az functionapp function list --name <function-app-name> --resource-group <resource-group> --output json

# Test a specific endpoint
Invoke-RestMethod -Uri "https://<function-app-name>.azurewebsites.net/api/<function-route>" -Method GET
```

### Correct Deployment Output

A successful deployment should show:

```
Functions in <function-app-name>:
    AuthProviders - [httpTrigger]
        Invoke url: https://<function-app-name>.azurewebsites.net/api/auth/providers/{email}
    
    AuthRegister - [httpTrigger]
        Invoke url: https://<function-app-name>.azurewebsites.net/api/auth/register
    
    [... additional functions listed ...]
```

### Project Structure Context

This issue is specific to:

*   **.NET 8.0 Isolated Functions** (`dotnet-isolated`)
*   **Projects with both** `**.sln**` **and** `**.csproj**` **files** in the same directory
*   **Azure Functions Core Tools** (`func` CLI)

### Related Configuration Files

Ensure these files are present and correctly configured:

1.  **host.json** - Functions runtime configuration
2.  **local.settings.json** - Local development settings (not deployed)
3.  **.funcignore** - Files to exclude from deployment (does not exclude `.sln` by default)
4.  **functions.metadata** - Generated during build (should not be committed)

### Prevention

Consider adding to `.funcignore` if solution file should never be deployed:

```
*.sln
```

However, this does not prevent the build process from using the solution file if present in the directory.

### Additional Notes

*   The Functions deployment uses Run-From-Package (ZIP deployment)
*   Total package size is typically 54-56 MB for this project
*   Deployment time is approximately 2-3 minutes
*   Functions runtime needs 5-10 seconds after deployment to initialize HTTP triggers

## Last Updated

January 28, 2026