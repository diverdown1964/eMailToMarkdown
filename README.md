# Email to Markdown Service

An Azure Function App that converts emails to markdown files and saves them to OneDrive. Built with .NET 8 and Azure Functions v4.

## Overview

This service provides automated email-to-markdown conversion with OneDrive integration. Users send emails to a designated address, and the service:

1.  Converts the email content from HTML to clean markdown format
2.  Saves the markdown file to the user's OneDrive in an organized date-based structure
3.  Sends a confirmation email with the markdown file as an attachment

## Key Features

*   **Subscriber-Based Access**: Users must be registered to use the service
*   **OneDrive Integration**: Automatic file organization by date (YYYY/MM/DD structure)
*   **Multiple Delivery Options**: Email attachment, OneDrive storage, or both
*   **Configurable Polling**: Adjustable check intervals for email processing
*   **Smart HTML Conversion**: Preserves formatting while removing unwanted elements (signatures, forwarding headers, etc.)
*   **Date-Based Organization**: Files automatically organized in `/EmailToMarkdown/YYYY/MM/DD/` folders

## How It Works

1.  **Send Email**: Send an email to `storage1@bifocal.show` from your registered email address
2.  **Processing**: The service polls for unread emails at configured intervals (default: 60 seconds)
3.  **Conversion**: HTML content is cleaned and converted to markdown using ReverseMarkdown and Pandoc
4.  **Delivery**: Based on user preferences:
    *   Sent as email attachment (default)
    *   Saved to OneDrive
    *   Or both
5.  **Confirmation**: Original email marked as read; confirmation sent with markdown attachment

## Technology Stack

*   **.NET 8.0**: Modern C# with minimal APIs
*   **Azure Functions v4**: Serverless compute platform
*   **Microsoft Graph API**: Email and OneDrive access
*   **Azure Table Storage**: User preferences storage
*   **Azure Communication Services**: Email sending
*   **ReverseMarkdown & Pandoc**: HTML to markdown conversion

## Prerequisites

*   Azure subscription
*   PowerShell 7+ with Az modules
*   Microsoft 365 account with OneDrive
*   Azure CLI (for deployment)

## Setup for New Users

### Register a New User

Run the setup script to register a user and configure their preferences:

```powershell
# Basic setup (saves to user's own OneDrive)
.\setup-user.ps1 -UserEmail "user@example.com"

# Advanced setup (saves to different OneDrive account)
.\setup-user.ps1 `
    -UserEmail "user@example.com" `
    -OneDriveUserEmail "storage@example.com" `
    -RootFolder "/EmailArchive" `
    -PollingIntervalSeconds 120
```

### Script Parameters

- **UserEmail** (required): Email address of the user sending emails to the service
- **OneDriveUserEmail** (optional): OneDrive account for file storage (defaults to UserEmail)
- **RootFolder** (optional): Root folder in OneDrive (default: `/EmailToMarkdown`)
- **DeliveryMethod** (optional): `email` (default), `onedrive`, or `both`
- **StorageProvider** (optional): Storage provider (default: `onedrive`)

## Configuration Options

### Delivery Method

Choose how you want to receive your converted markdown files:

```powershell
# Email only (default) - receive markdown as email attachment
.\setup-user.ps1 -UserEmail "user@example.com" -DeliveryMethod "email"

# OneDrive only - save directly to OneDrive (great for Obsidian users)
.\setup-user.ps1 -UserEmail "user@example.com" -DeliveryMethod "onedrive" -RootFolder "/MyVault/Inbox"

# Both - get email attachment AND save to OneDrive
.\setup-user.ps1 -UserEmail "user@example.com" -DeliveryMethod "both"
```

### Required Permissions

The Azure App Registration requires these Microsoft Graph API permissions:

- `Mail.Send` - Send confirmation emails
- `Files.ReadWrite.All` - Write to any user's OneDrive

## Usage

1. **Register your email** using the setup script
2. **Configure SendGrid** Inbound Parse to forward emails to your function
3. **Send an email** to your configured email address
4. **Immediate processing** via SendGrid webhook
5. **Check your destination**:
   - OneDrive location: `/EmailToMarkdown/YYYY/MM/DD/`
   - Filename format: `yyyy-MM-dd-SenderName-Subject.md`
6. **Receive confirmation** email with markdown file attached (if delivery method includes email)

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

- **SendGridInbound**: HTTP function that receives webhook from SendGrid
- **MarkdownConversionService**: Converts HTML email to markdown
- **OneDriveStorageService**: Saves files to OneDrive
- **AzureCommunicationEmailService**: Sends confirmation emails
- **ConfigurationService**: Manages user preferences

### Data Storage

- **ProcessedEmails** (Table): Tracks processed emails to avoid duplicates
- **UserPreferences** (Table): Stores user-specific settings
  - EmailAddress (PartitionKey)
  - OneDriveUserEmail
  - RootFolder
  - DeliveryMethod
  - StorageProvider

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

- `TENANT_ID`: Azure AD tenant ID
- `CLIENT_ID`: App registration client ID
- `CLIENT_SECRET`: App registration client secret
- `STORAGE_ACCOUNT_NAME`: Azure storage account name
- `STORAGE_ACCOUNT_KEY`: Azure storage account key
- `ACS_CONNECTION_STRING`: Azure Communication Services connection string
- `SENDGRID_API_KEY`: SendGrid API key (if using SendGrid for sending)

## Troubleshooting

### "User not subscribed" Error

Register the user first:

```powershell
.\setup-user.ps1 -UserEmail "user@example.com"
```

### Files Not Appearing in OneDrive

1. Verify OneDriveUserEmail in UserPreferences table
2. Check app has `Files.ReadWrite.All` permission
3. Review Azure Function logs for errors
4. Ensure proper OneDrive folder path format

### Emails Not Being Processed

1. Verify SendGrid Inbound Parse is configured correctly
2. Check function endpoint is accessible: `/api/inbound`
3. Review Azure Function logs for webhook errors
4. Confirm email sent from registered address
5. Check ProcessedEmails table for duplicate prevention

## Project Structure

```
eMailToMarkdown/
├── Functions/              # Azure Functions
│   └── SendGridInbound.cs  # HTTP-triggered webhook function
├── Models/                 # Data models
│   ├── AppConfiguration.cs
│   └── UserPreferences.cs
├── Services/               # Business logic
│   ├── ConfigurationService.cs
│   ├── EmailOrchestrationService.cs
│   ├── GraphEmailService.cs
│   ├── MarkdownConversionService.cs
│   └── OneDriveStorageService.cs
├── scripts/                # PowerShell scripts
│   ├── deploy-to-azure.ps1
│   ├── initialize-service.ps1
│   └── setup-entra-app.ps1
├── setup-user.ps1          # User registration script
├── Program.cs              # Application entry point
├── host.json               # Function app configuration
└── local.settings.json     # Local development settings
```

## License

MIT