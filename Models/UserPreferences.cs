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
    public string StorageProvider { get; set; } = "onedrive";
    public string EmailAddress { get; set; } = string.Empty;
    public string OneDriveUserEmail { get; set; } = string.Empty;
    public string DeliveryMethod { get; set; } = "email"; // "email", "onedrive", or "both"
}
