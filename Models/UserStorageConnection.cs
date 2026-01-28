using Azure;
using Azure.Data.Tables;

namespace EmailToMarkdown.Models;

/// <summary>
/// Stores storage connection settings per provider for a user.
/// Allows a user to have both OneDrive and Google Drive connected simultaneously.
/// </summary>
public class UserStorageConnection : ITableEntity
{
    /// <summary>
    /// Partition key is the user's email (normalized to lowercase)
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key is the provider name (microsoft, google)
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>
    /// Root folder path for this storage provider
    /// </summary>
    public string RootFolder { get; set; } = "/EmailToMarkdown";

    /// <summary>
    /// Specific drive ID (optional)
    /// </summary>
    public string DriveId { get; set; } = string.Empty;

    /// <summary>
    /// Specific folder ID (optional)
    /// </summary>
    public string FolderId { get; set; } = string.Empty;

    /// <summary>
    /// When consent was granted for this provider
    /// </summary>
    public DateTimeOffset? ConsentGrantedAt { get; set; }

    /// <summary>
    /// Last successful sync to this storage provider
    /// </summary>
    public DateTimeOffset? LastSuccessfulSync { get; set; }

    /// <summary>
    /// Whether this connection is active
    /// </summary>
    public bool IsActive { get; set; } = true;
}
