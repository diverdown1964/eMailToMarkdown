# Quick Start Guide

## Setup Your Account

1. Run the setup script with your email:
   ```powershell
   .\setup-user.ps1 -UserEmail "your-email@example.com"
   ```

2. The script will:
   - Register your email in the system
   - Configure it to save to your OneDrive
   - Set the default polling interval (60 seconds)

## Send Your First Email

1. From your registered email account, compose a new email
2. Send it to: **storage1@bifocal.show**
3. Wait up to 60 seconds for processing
4. Check your OneDrive at `/EmailToMarkdown/YYYY/MM/DD/`

## Advanced Configuration

### Save to a Different OneDrive

If you want files saved to a different OneDrive account (e.g., a shared account):

```powershell
.\setup-user.ps1 `
    -UserEmail "your-email@example.com" `
    -OneDriveUserEmail "shared-storage@example.com"
```

### Change the Root Folder

To organize files in a custom folder:

```powershell
.\setup-user.ps1 `
    -UserEmail "your-email@example.com" `
    -RootFolder "/MyEmailArchive"
```

### Adjust Polling Speed

For faster processing (checks every 30 seconds):

```powershell
.\setup-user.ps1 `
    -UserEmail "your-email@example.com" `
    -PollingIntervalSeconds 30
```

For slower processing to save costs (checks every 5 minutes):

```powershell
.\setup-user.ps1 `
    -UserEmail "your-email@example.com" `
    -PollingIntervalSeconds 300
```

## File Naming and Organization

Files are automatically organized by date:

```
/EmailToMarkdown/
  2026/
    01/
      25/
        2026-01-25-JohnSmith-MeetingNotes.md
        2026-01-25-JaneDoce-ProjectUpdate.md
```

Filename format: `YYYY-MM-DD-SenderName-Subject.md`

## What Gets Converted

The markdown file includes:
- Email subject as title
- Sender name and email
- Date received
- Full email body (HTML converted to markdown)
- Preserves formatting, links, and basic styling

## Troubleshooting

### "User not subscribed" error
Run the setup script to register:
```powershell
.\setup-user.ps1 -UserEmail "your-email@example.com"
```

### Files not appearing in OneDrive
- Wait the full polling interval (default 60 seconds)
- Check you sent from the registered email address
- Verify the OneDrive account has proper permissions

### Processing taking too long
Update the polling interval to check more frequently:
```powershell
.\setup-user.ps1 -UserEmail "your-email@example.com" -PollingIntervalSeconds 30
```

## Examples

### Basic setup for personal use
```powershell
.\setup-user.ps1 -UserEmail "john@example.com"
```
- Saves to john@example.com's OneDrive
- Uses default /EmailToMarkdown folder
- Checks every 60 seconds

### Team setup with shared OneDrive
```powershell
.\setup-user.ps1 `
    -UserEmail "team-member@example.com" `
    -OneDriveUserEmail "team-archive@example.com" `
    -RootFolder "/TeamEmailArchive"
```
- Team member sends email to storage1@bifocal.show
- Files save to team-archive@example.com's OneDrive
- Organized under /TeamEmailArchive

### Fast processing setup
```powershell
.\setup-user.ps1 `
    -UserEmail "urgent@example.com" `
    -PollingIntervalSeconds 15
```
- Checks for new emails every 15 seconds
- Faster processing, higher costs

## Next Steps

1. Register your email with the setup script
2. Send a test email to storage1@bifocal.show
3. Check your OneDrive after the polling interval
4. Adjust settings as needed
