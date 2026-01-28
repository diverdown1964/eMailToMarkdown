using Azure.Data.Tables;
using EmailToMarkdown.Models;

namespace EmailToMarkdown.Services;

public class ConfigurationService
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly string _tableName = "UserPreferences";

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
}
