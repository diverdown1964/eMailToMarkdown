# Quick Start Guide

## Setup Your Account (Web UI - Recommended)

1. Visit the registration page:
   ```
   https://icy-ocean-04b9b9b0f.4.azurestaticapps.net
   ```

2. Enter your email address

3. Connect your storage providers:
   - Click "Connect OneDrive" and authorize with Microsoft
   - Click "Connect Google Drive" and authorize with Google
   - You can connect one or both!

4. You're ready to go!

## Alternative: PowerShell Setup

```powershell
.\setup-user.ps1 -UserEmail "your-email@example.com"
```

## Forward Your First Email

1. From your registered email account, forward any email
2. Send it to your configured SendGrid inbound address
3. The email is processed immediately via webhook
4. Check your cloud storage at `/EmailToMarkdown/YYYY/MM/DD/`

## Storage Locations

Files are saved to ALL connected providers:

| Provider | Location |
|----------|----------|
| OneDrive | `/EmailToMarkdown/2026/01/28/...` |
| Google Drive | `EmailToMarkdown/2026/01/28/...` |

## File Naming and Organization

Files are automatically organized by date:

```
EmailToMarkdown/
  2026/
    01/
      28/
        2026-01-28-JohnSmith-MeetingNotes.md
        2026-01-28-JaneDoe-ProjectUpdate.md
```

Filename format: `YYYY-MM-DD-SenderName-Subject.md`

## Forwarded Emails

When you forward an email, the service extracts the original sender's information:
- Original sender name and email used for filename
- Original sent date used for organization
- Forwarding headers are removed from content

## What Gets Converted

The markdown file includes:
- Email subject as title
- Sender name and email
- Date received
- Full email body (HTML converted to markdown)
- Preserves formatting, links, and basic styling

## Multi-Provider Benefits

With both OneDrive and Google Drive connected:
- Files are saved to BOTH locations simultaneously
- If one provider fails, the other still works
- You receive an email notification if any saves fail

## Error Handling

If a storage save fails, you'll receive an email with:
- The markdown file attached
- Details about which provider failed
- The specific error message
- Instructions to fix (e.g., re-authenticate)

## Troubleshooting

### "User not subscribed" error
Register via the web UI or run:
```powershell
.\setup-user.ps1 -UserEmail "your-email@example.com"
```

### Files not appearing in storage
1. Check the web UI - are your providers connected?
2. Did you receive a failure notification email?
3. Try disconnecting and reconnecting the provider
4. Check if storage quota is exceeded

### "Re-authentication required" error
Your OAuth token has expired. Visit the web UI and reconnect the affected provider.

## Obsidian Integration

Sync your Obsidian vault to OneDrive and have emails automatically saved to your vault:

1. Ensure your vault is synced to OneDrive
2. Register with your vault path:
   ```powershell
   .\setup-user.ps1 -UserEmail "user@example.com" -RootFolder "/MyVault/Inbox"
   ```

## Next Steps

1. Register via the web UI
2. Connect OneDrive and/or Google Drive
3. Forward a test email
4. Check your storage locations
5. Enjoy automated email archiving!
