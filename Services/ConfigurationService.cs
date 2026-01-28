using Azure.Data.Tables;
using EmailToMarkdown.Models;

namespace EmailToMarkdown.Services;

public class ConfigurationService
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly string _tableName = "UserPreferences";
    private readonly string _identityLinksTable = "UserIdentityLinks";
    private readonly string _storageConnectionsTable = "UserStorageConnections";

    public ConfigurationService(string connectionString)
    {
        _tableServiceClient = new TableServiceClient(connectionString);
    }

    public async Task<UserPreferences?> GetUserPreferencesAsync(string emailAddress)
    {
        var tableClient = _tableServiceClient.GetTableClient(_tableName);
        await tableClient.CreateIfNotExistsAsync();

        try
        {
            var response = await tableClient.GetEntityAsync<UserPreferences>(
                "preferences",  // PartitionKey
                emailAddress    // RowKey
            );
            return response.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SaveUserPreferencesAsync(UserPreferences preferences)
    {
        var tableClient = _tableServiceClient.GetTableClient(_tableName);
        await tableClient.CreateIfNotExistsAsync();
        await tableClient.UpsertEntityAsync(preferences);
    }

    public async Task DeleteUserPreferencesAsync(string emailAddress)
    {
        var tableClient = _tableServiceClient.GetTableClient(_tableName);
        try
        {
            await tableClient.DeleteEntityAsync("preferences", emailAddress.ToLowerInvariant());
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Already deleted, that's fine
        }
    }

    #region Identity Links

    /// <summary>
    /// Link two identities together. Creates bidirectional links so either can find the other.
    /// </summary>
    public async Task LinkIdentitiesAsync(string primaryEmail, string linkedEmail, string linkedProvider)
    {
        var tableClient = _tableServiceClient.GetTableClient(_identityLinksTable);
        await tableClient.CreateIfNotExistsAsync();

        primaryEmail = primaryEmail.ToLowerInvariant();
        linkedEmail = linkedEmail.ToLowerInvariant();

        // Create bidirectional links
        var link1 = new UserIdentityLink
        {
            PartitionKey = primaryEmail,
            RowKey = linkedEmail,
            Provider = linkedProvider,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var link2 = new UserIdentityLink
        {
            PartitionKey = linkedEmail,
            RowKey = primaryEmail,
            Provider = linkedProvider,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await tableClient.UpsertEntityAsync(link1);
        await tableClient.UpsertEntityAsync(link2);
    }

    /// <summary>
    /// Get all linked identities for an email address
    /// </summary>
    public async Task<List<UserIdentityLink>> GetLinkedIdentitiesAsync(string emailAddress)
    {
        var tableClient = _tableServiceClient.GetTableClient(_identityLinksTable);
        await tableClient.CreateIfNotExistsAsync();

        emailAddress = emailAddress.ToLowerInvariant();
        var links = new List<UserIdentityLink>();

        await foreach (var link in tableClient.QueryAsync<UserIdentityLink>(l => l.PartitionKey == emailAddress))
        {
            links.Add(link);
        }

        return links;
    }

    /// <summary>
    /// Get all emails in an identity group (the email itself plus all linked emails)
    /// </summary>
    public async Task<List<string>> GetIdentityGroupAsync(string emailAddress)
    {
        emailAddress = emailAddress.ToLowerInvariant();
        var emails = new List<string> { emailAddress };

        var links = await GetLinkedIdentitiesAsync(emailAddress);
        Console.WriteLine($"[GetIdentityGroupAsync] Found {links.Count} links for {emailAddress}");
        
        foreach (var link in links)
        {
            Console.WriteLine($"[GetIdentityGroupAsync] Link: {link.PartitionKey} -> {link.RowKey} ({link.Provider})");
            if (!emails.Contains(link.RowKey))
            {
                emails.Add(link.RowKey);
            }
        }
        
        Console.WriteLine($"[GetIdentityGroupAsync] Identity group for {emailAddress}: {string.Join(", ", emails)}");
        return emails;
    }

    #endregion

    #region Storage Connections

    /// <summary>
    /// Save a storage connection for a specific provider
    /// </summary>
    public async Task SaveStorageConnectionAsync(string emailAddress, string provider, UserStorageConnection connection)
    {
        var tableClient = _tableServiceClient.GetTableClient(_storageConnectionsTable);
        await tableClient.CreateIfNotExistsAsync();

        connection.PartitionKey = emailAddress.ToLowerInvariant();
        connection.RowKey = provider.ToLowerInvariant();

        await tableClient.UpsertEntityAsync(connection);
    }

    /// <summary>
    /// Get a specific storage connection
    /// </summary>
    public async Task<UserStorageConnection?> GetStorageConnectionAsync(string emailAddress, string provider)
    {
        var tableClient = _tableServiceClient.GetTableClient(_storageConnectionsTable);
        await tableClient.CreateIfNotExistsAsync();

        try
        {
            var response = await tableClient.GetEntityAsync<UserStorageConnection>(
                emailAddress.ToLowerInvariant(),
                provider.ToLowerInvariant()
            );
            return response.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Get all storage connections for an email
    /// </summary>
    public async Task<List<UserStorageConnection>> GetStorageConnectionsAsync(string emailAddress)
    {
        var tableClient = _tableServiceClient.GetTableClient(_storageConnectionsTable);
        await tableClient.CreateIfNotExistsAsync();

        emailAddress = emailAddress.ToLowerInvariant();
        var connections = new List<UserStorageConnection>();

        await foreach (var conn in tableClient.QueryAsync<UserStorageConnection>(c => c.PartitionKey == emailAddress))
        {
            if (conn.IsActive)
            {
                connections.Add(conn);
            }
        }

        return connections;
    }

    /// <summary>
    /// Get all storage connections across all linked identities
    /// </summary>
    public async Task<Dictionary<string, UserStorageConnection>> GetAllStorageConnectionsForUserAsync(string emailAddress)
    {
        Console.WriteLine($"[GetAllStorageConnectionsForUserAsync] Starting for {emailAddress}");
        var connections = new Dictionary<string, UserStorageConnection>();
        var identityGroup = await GetIdentityGroupAsync(emailAddress);

        Console.WriteLine($"[GetAllStorageConnectionsForUserAsync] Identity group has {identityGroup.Count} emails");
        
        foreach (var email in identityGroup)
        {
            var emailConnections = await GetStorageConnectionsAsync(email);
            Console.WriteLine($"[GetAllStorageConnectionsForUserAsync] {email} has {emailConnections.Count} connections");
            
            foreach (var conn in emailConnections)
            {
                Console.WriteLine($"[GetAllStorageConnectionsForUserAsync] Connection: {conn.RowKey} for {email}");
                // Use provider as key, so we get one connection per provider
                // Prefer connections from the primary email if duplicates exist
                if (!connections.ContainsKey(conn.RowKey))
                {
                    connections[conn.RowKey] = conn;
                }
            }
        }

        Console.WriteLine($"[GetAllStorageConnectionsForUserAsync] Returning {connections.Count} total connections");
        return connections;
    }

    /// <summary>
    /// Delete a storage connection
    /// </summary>
    public async Task DeleteStorageConnectionAsync(string emailAddress, string provider)
    {
        var tableClient = _tableServiceClient.GetTableClient(_storageConnectionsTable);
        try
        {
            await tableClient.DeleteEntityAsync(
                emailAddress.ToLowerInvariant(),
                provider.ToLowerInvariant()
            );
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Already deleted
        }
    }

    #endregion
}
