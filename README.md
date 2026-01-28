# Email to Markdown Service

An Azure Function App that converts emails to markdown files and saves them to cloud storage. Built with .NET 8 and Azure Functions v4.

## Overview

This service provides automated email-to-markdown conversion with cloud storage integration. Users forward emails to a designated address, and the service:

1.  Converts the email content from HTML to clean markdown format
2.  Saves the markdown file to the user's cloud storage (OneDrive and/or Google Drive) in an organized date-based structure
3.  Sends a confirmation email with the markdown file as an attachment (optional)
4.  Notifies users via email if any storage saves fail

## Key Features

*   **Multi-Provider Storage**: Save to OneDrive, Google Drive, or both simultaneously
*   **Web-Based Registration**: Easy setup via web UI with OAuth authentication
*   **Automatic Failover**: If one storage provider fails, files are sent via email
*   **Date-Based Organization**: Files automatically organized in `/EmailToMarkdown/YYYY/MM/DD/` folders
*   **Forwarded Email Support**: Extracts original sender metadata from forwarded emails
*   **Multiple Delivery Options**: Email attachment, cloud storage, or both
*   **Error Notifications**: Automatic email with attachment when storage saves fail

## Supported Storage Providers

| Provider | Status | Features |
|----------|--------|----------|
| **Microsoft OneDrive** | ✅ Fully Supported | Personal & Business accounts |
| **Google Drive** | ✅ Fully Supported | Personal Google accounts |

## How It Works

1.  **Register**: Visit the web app and connect your storage providers via OAuth
2.  **Forward Email**: Forward any email to your configured SendGrid address
3.  **Automatic Processing**: Email is converted to markdown immediately via webhook
4.  **Multi-Provider Delivery**: File is saved to ALL connected storage providers
5.  **Confirmation**: Receive email notification for any failures (with markdown attached)

## Technology Stack

*   **.NET 8.0**: Modern C# with minimal APIs
*   **Azure Functions v4**: Serverless compute (isolated worker)
*   **Azure Static Web Apps**: React-based registration frontend
*   **Microsoft Graph API**: OneDrive integration
*   **Google Drive API**: Google Drive integration
*   **Azure Table Storage**: User preferences and token storage
*   **Azure Communication Services**: Email notifications
*   **SendGrid Inbound Parse**: Email webhook processing

## Prerequisites

*   Azure subscription
*   Microsoft 365 or Google account for storage
*   SendGrid account (for inbound email parsing)

## Quick Start

### 1. Register via Web UI

Visit the registration page and connect your storage providers:

1. Go to: `https://icy-ocean-04b9b9b0f.4.azurestaticapps.net`
2. Enter your email address
3. Click "Connect" for OneDrive and/or Google Drive
4. Authorize the application via OAuth
5. Your storage is now configured!

### 2. Forward an Email

Forward any email to your configured SendGrid inbound address. The service will:
- Convert the email to markdown
- Save to all your connected storage providers
- Organize files by date: `/EmailToMarkdown/YYYY/MM/DD/`

### PowerShell Setup (Alternative)

For advanced users or automation, use the setup script:

```powershell
# Basic setup
.\setup-user.ps1 -UserEmail "user@example.com"

# With custom folder
.\setup-user.ps1 -UserEmail "user@example.com" -RootFolder "/MyVault/Inbox"
```

## Configuration Options

### Delivery Methods

| Method | Description |
|--------|-------------|
| `storage` | Save to connected cloud storage only |
| `email` | Send markdown as email attachment only |
| `both` | Save to storage AND send email attachment |

### Storage Organization

Files are automatically organized by date:

## Usage

1):  **Register** via the web UI or PowerShell script
2. **Connect storage providers** (OneDrive and/or Google Drive)
3. **Forward emails** to your configured SendGrid address
4. **Files appear** in your cloud storage organized by date

### File Location

```
/EmailToMarkdown/YYYY/MM/DD/yyyy-MM-dd-SenderName-Subject.md
```

### Forwarded Emails

When you forward an email, the service extracts the original sender's information and uses it for the filename and metadata, not your forwarding email.

## Obsidian Integration

Sync your Obsidian vault to OneDrive and have emails automatically saved directly to your vault.

### Setup for Obsidian

1. **Ensure your Obsidian vault is synced to OneDrive**
   - Your vault folder should be inside your OneDrive folder (e.g., `OneDrive/MyVault`)

2. **Find your vault's OneDrive path**
   - If your vault is at `C:\Users\You\OneDrive\MyVault`, the OneDrive path is `/MyVault`
   - If at `C:\Users\You\OneDrive\Documents\Notes\MyVault`, the path is `/Documents/Notes/MyVault`

3. **Register with your vault path and OneDrive delivery**

```powershell
# Save emails to the root of your Obsidian vault
.\setup-user.ps1 -UserEmail "user@example.com" -RootFolder "/MyVault" -DeliveryMethod "onedrive"

# Save emails to a specific folder within your vault
.\setup-user.ps1 -UserEmail "user@example.com" -RootFolder "/MyVault/Inbox" -DeliveryMethod "onedrive"
```

### Folder Structure in Your Vault

Emails are organized by date within your specified folder:

```
MyVault/Inbox/
  2026/
    01/
      25/
        2026-01-25-Sender-Subject.md
```

### Tips for Obsidian Users

- **Use a dedicated subfolder**: Keep email notes separate (e.g., `/MyVault/Inbox`)
- **Works with plugins**: Date-organized structure integrates with daily notes and calendar plugins
- **Includes metadata**: Converted markdown includes frontmatter that Obsidian can parse

## File Organization

Files are organized in OneDrive by date:

```
/EmailToMarkdown/
  2026/
    01/
      25/
        2026-01-25-JohnDoe-ProjectUpdate.md
        2026-01-25-JaneSmith-QuarterlyReport.md
    02/
      01/
        2026-02-01-BobWilson-MeetingNotes.md
```

## Architecture

### Core Components

- **SendGridInbound**: HTTP function that receives webhooks from SendGrid
- **MarkdownConversionService**: Converts HTML email to markdown
- **OneDriveStorageService**: Saves files to Microsoft OneDrive
- **GoogleDriveStorageService**: Saves files to Google Drive
- **StorageProviderFactory**: Routes to appropriate storage provider
- **TokenService**: Manages OAuth tokens with automatic refresh
- **AzureCommunicationEmailService**: Sends notification emails
- **ConfigurationService**: Manages user preferences and connections

### API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/inbound` | POST | SendGrid webhook for incoming emails |
| `/api/auth/register` | POST | Register user with storage provider |
| `/api/auth/status/{email}` | GET | Get user registration status |
| `/api/auth/providers/{email}` | GET | List connected storage providers |
| `/api/auth/revoke` | POST | Revoke storage provider connection |
| `/api/auth/validate/{email}` | GET | Validate storage connections |

### Data Storage

- **UserPreferences** (Table): User-specific settings
- **UserTokens** (Table): Encrypted OAuth tokens
- **UserStorageConnections** (Table): Per-provider storage configuration
- **UserIdentityLinks** (Table): Links between email identities

## Development

### Local Development

1. Install dependencies:
```powershell
dotnet restore
```

2. Configure local settings in `local.settings.json`

3. Start Azurite for local storage:
```powershell
azurite
```

4. Run the function app:
```powershell
func start
```

5. Register a test user:
```powershell
.\setup-user.ps1 -UserEmail "myemail@example.com"
```

### Deployment

Deploy to Azure using Azure Functions Core Tools:

```powershell
func azure functionapp publish emailtomarkdown-func
```

Or use the deployment script:

```powershell
.\scripts\deploy-to-azure.ps1
```

### Required App Settings

Configure these in Azure Function App settings:

| Setting | Description |
|---------|-------------|
| `TENANT_ID` | Azure AD tenant ID |
| `CLIENT_ID` | Microsoft app registration client ID |
| `CLIENT_SECRET` | Microsoft app registration client secret |
| `GOOGLE_CLIENT_ID` | Google OAuth client ID |
| `GOOGLE_CLIENT_SECRET` | Google OAuth client secret |
| `STORAGE_ACCOUNT_NAME` | Azure storage account name |
| `STORAGE_ACCOUNT_KEY` | Azure storage account key |
| `ACS_CONNECTION_STRING` | Azure Communication Services connection |

## Deployment

### Deploy Azure Functions

**Important**: Rename the solution file before deploying to avoid metadata issues:

```powershell
cd c:\Repos\eMailToMarkdown
Rename-Item eMailToMarkdown.sln eMailToMarkdown.sln.bak
func azure functionapp publish emailtomarkdown-func --dotnet-isolated
Rename-Item eMailToMarkdown.sln.bak eMailToMarkdown.sln
```

### Deploy Web Frontend

```powershell
cd web
npm run build
swa deploy ./dist --deployment-token $env:SWA_CLI_DEPLOYMENT_TOKEN --env production
```

## Troubleshooting

### "User not subscribed" Error

Register via the web UI or PowerShell script:

```powershell
.\setup-user.ps1 -UserEmail "user@example.com"
```

### Files Not Appearing in Cloud Storage

1. Check the web UI to verify storage providers are connected
2. Look for error notification emails
3. Review Azure Function logs for errors
4. Verify OAuth tokens haven't expired (re-authenticate if needed)

### Storage Save Failures

When storage saves fail, you'll receive an email with:
- The markdown file as an attachment
- Details about which providers failed and why
- Instructions to re-authenticate if needed

## Project Structure

```
eMailToMarkdown/
├── Functions/                    # Azure Functions
│   ├── SendGridInbound.cs        # Email webhook processor
│   └── AuthFunctions.cs          # OAuth & registration endpoints
├── Models/                       # Data models
│   ├── AppConfiguration.cs
│   ├── UserPreferences.cs
│   ├── UserStorageConnection.cs
│   ├── UserToken.cs
│   └── StorageResult.cs
├── Services/                     # Business logic
│   ├── ConfigurationService.cs   # User preferences management
│   ├── TokenService.cs           # OAuth token management
│   ├── TokenEncryptionService.cs # Token encryption
│   ├── MarkdownConversionService.cs
│   ├── OneDriveStorageService.cs
│   ├── GoogleDriveStorageService.cs
│   ├── StorageProviderFactory.cs
│   └── AzureCommunicationEmailService.cs
├── web/                          # React frontend
│   ├── src/
│   │   ├── components/
│   │   │   └── StorageDashboard.tsx
│   │   └── App.tsx
│   └── package.json
├── scripts/                      # PowerShell scripts
│   ├── deploy-functions.ps1
│   └── deploy-to-azure.ps1
├── docs/                         # Documentation
│   ├── AZURE_FUNCTIONS_DEPLOYMENT.md
│   └── IMPLEMENTATION_STATUS.md
├── setup-user.ps1                # User registration script
├── Program.cs                    # Application entry point
├── host.json                     # Function app configuration
└── local.settings.json           # Local development settings
```

## License

MIT