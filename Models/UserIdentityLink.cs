using Azure;
using Azure.Data.Tables;

namespace EmailToMarkdown.Models;

/// <summary>
/// Links multiple identities (email addresses from different providers) to a single user.
/// When a user connects both Microsoft and Google accounts, they are linked together
/// so logging in with either identity shows all storage connections.
/// </summary>
public class UserIdentityLink : ITableEntity
{
    /// <summary>
    /// Partition key is the primary email (first identity registered)
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key is the linked email (the email being linked to the primary)
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>
    /// The provider for the linked identity (microsoft, google)
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// When this link was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
