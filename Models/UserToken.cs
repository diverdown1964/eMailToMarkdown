using Azure;
using Azure.Data.Tables;

namespace EmailToMarkdown.Models;

public class UserToken : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;  // Provider: "microsoft" or "google"
    public string RowKey { get; set; } = string.Empty;        // User email (normalized lowercase)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Encrypted token data
    public string EncryptedAccessToken { get; set; } = string.Empty;
    public string EncryptedRefreshToken { get; set; } = string.Empty;

    // Token metadata (not sensitive)
    public DateTimeOffset AccessTokenExpiry { get; set; }
    public string Scopes { get; set; } = string.Empty;

    // Provider-specific identifiers
    public string ProviderUserId { get; set; } = string.Empty;   // Microsoft OID or Google sub
    public string ProviderTenantId { get; set; } = string.Empty; // Microsoft tenant ID

    // Status tracking
    public bool IsValid { get; set; } = true;
    public string LastError { get; set; } = string.Empty;
    public int RefreshFailureCount { get; set; } = 0;
}
