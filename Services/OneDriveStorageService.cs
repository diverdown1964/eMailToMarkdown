using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace EmailToMarkdown.Services;

public class OneDriveStorageService
{
    private readonly GraphServiceClient _graphClient;
    private readonly ILogger? _logger;

    public OneDriveStorageService(GraphServiceClient graphClient, ILogger? logger = null)
    {
        _graphClient = graphClient;
        _logger = logger;
    }

    public async Task<bool> SaveFileAsync(
        string userEmail,
        string rootFolder,
        string fileName,
        byte[] content)
    {
        try
        {
            // Build date-based path: /RootFolder/YYYY/MM/DD/filename.md
            var now = DateTime.UtcNow;
            var datePath = $"{now:yyyy}/{now:MM}/{now:dd}";
            var fullPath = $"{rootFolder.TrimEnd('/')}/{datePath}/{fileName}";

            _logger?.LogInformation($"Saving file to OneDrive: {userEmail} -> {fullPath}");

            // Graph SDK v5: First get the user's drive, then upload via Drives[driveId]
            var drive = await _graphClient.Users[userEmail].Drive.GetAsync();
            if (drive?.Id == null)
            {
                _logger?.LogError($"Could not get drive for user {userEmail}");
                return false;
            }

            // Upload file using Graph API
            // The path-based upload will create folders automatically
            using var stream = new MemoryStream(content);

            await _graphClient.Drives[drive.Id]
                .Items["root"]
                .ItemWithPath(fullPath)
                .Content
                .PutAsync(stream);

            _logger?.LogInformation($"File saved successfully: {fullPath}");
            return true;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            _logger?.LogError($"Failed to save file to OneDrive: {odataEx.Message}");
            _logger?.LogError($"OData Error Code: {odataEx.Error?.Code}");
            _logger?.LogError($"OData Error Message: {odataEx.Error?.Message}");
            if (odataEx.Error?.InnerError?.AdditionalData != null)
            {
                foreach (var kvp in odataEx.Error.InnerError.AdditionalData)
                {
                    _logger?.LogError($"Inner Error {kvp.Key}: {kvp.Value}");
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to save file to OneDrive: {ex.Message}");
            _logger?.LogError($"Exception details: {ex}");
            return false;
        }
    }
}
