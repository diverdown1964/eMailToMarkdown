using Azure;
using Azure.Data.Tables;

namespace EmailToMarkdown.Models;

public class UserPreferences : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = "preferences";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    public string RootFolder { get; set; } = "/EmailToMarkdown";
    public string StorageProvider { get; set; } = "onedrive";  // "onedrive" or "googledrive"
    public string EmailAddress { get; set; } = string.Empty;
    public string DeliveryMethod { get; set; } = "onedrive";   // "email", "onedrive", or "both"

    // Delegated auth fields
    public string ProviderUserId { get; set; } = string.Empty;   // Microsoft OID or Google sub
    public string ProviderTenantId { get; set; } = string.Empty; // User's home tenant
    public string DriveId { get; set; } = string.Empty;          // Specific drive ID
    public string FolderId { get; set; } = string.Empty;         // Selected folder ID
    public DateTimeOffset? ConsentGrantedAt { get; set; }
    public DateTimeOffset? LastSuccessfulSync { get; set; }
}
