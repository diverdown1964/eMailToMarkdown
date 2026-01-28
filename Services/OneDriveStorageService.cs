using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmailToMarkdown.Models;
using Microsoft.Extensions.Logging;

namespace EmailToMarkdown.Services;

public class OneDriveStorageService : IStorageProvider
{
    private readonly TokenService _tokenService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OneDriveStorageService> _logger;

    public StorageProviderType ProviderType => StorageProviderType.OneDrive;

    public OneDriveStorageService(
        TokenService tokenService,
        IHttpClientFactory httpClientFactory,
        ILogger<OneDriveStorageService> logger)
    {
        _tokenService = tokenService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<StorageResult> SaveFileAsync(
        string userEmail,
        string rootFolder,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting SaveFileAsync for {User}, folder: {Folder}, file: {File}", 
                userEmail, rootFolder, fileName);
            
            // Get valid access token (will refresh if needed)
            _logger.LogInformation("Requesting access token for {User}", userEmail);
            var accessToken = await _tokenService.GetValidAccessTokenAsync("microsoft", userEmail);
            
            if (accessToken == null)
            {
                _logger.LogWarning("No valid token available for {User}", userEmail);
                return StorageResult.Failed("No valid token available", requiresReauth: true);
            }
            
            _logger.LogInformation("Access token retrieved successfully for {User} (length: {Length})", 
                userEmail, accessToken.Length);

            // Build date-based path: /RootFolder/YYYY/MM/DD/filename.md
            var now = DateTime.UtcNow;
            var datePath = $"{now:yyyy}/{now:MM}/{now:dd}";
            var fullPath = $"{rootFolder.TrimEnd('/')}/{datePath}/{fileName}";

            _logger.LogInformation("Saving file to OneDrive: {User} -> {Path}", userEmail, fullPath);

            // Use Graph API directly with the delegated access token
            var httpClient = _httpClientFactory.CreateClient("Graph");
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            // Upload to /me/drive (user's own drive with delegated permission)
            var uploadUrl = $"https://graph.microsoft.com/v1.0/me/drive/root:{fullPath}:/content";
            _logger.LogInformation("Upload URL: {Url}", uploadUrl);

            using var requestContent = new ByteArrayContent(content);
            requestContent.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");

            _logger.LogInformation("Sending PUT request to Graph API for {User}", userEmail);
            var response = await httpClient.PutAsync(uploadUrl, requestContent, cancellationToken);

            _logger.LogInformation("Graph API response status: {StatusCode}", response.StatusCode);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to upload to OneDrive for {User}: {Status} {Error}",
                    userEmail, response.StatusCode, errorJson);

                // Check if it's an auth error
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return StorageResult.Failed("Authentication failed - please re-authenticate", requiresReauth: true);
                }

                // Try to extract error message from JSON
                var errorMessage = ExtractErrorMessage(errorJson) ?? $"Upload failed: {response.StatusCode}";
                return StorageResult.Failed(errorMessage);
            }

            var driveItem = await response.Content.ReadFromJsonAsync<DriveItemResponse>(
                cancellationToken: cancellationToken);

            _logger.LogInformation("File saved successfully: {Path}", fullPath);

            return StorageResult.Succeeded(fullPath, driveItem?.Id, driveItem?.WebUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save file to OneDrive for {User}", userEmail);
            return StorageResult.Failed(ex.Message);
        }
    }

    public async Task<bool> ValidateConnectionAsync(
        string userEmail,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await _tokenService.GetValidAccessTokenAsync("microsoft", userEmail);
            if (accessToken == null) return false;

            var httpClient = _httpClientFactory.CreateClient("Graph");
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.GetAsync(
                "https://graph.microsoft.com/v1.0/me/drive",
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate OneDrive connection for {User}", userEmail);
            return false;
        }
    }

    public async Task<IEnumerable<FolderInfo>> ListFoldersAsync(
        string userEmail,
        string parentPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await _tokenService.GetValidAccessTokenAsync("microsoft", userEmail);
            if (accessToken == null) return Enumerable.Empty<FolderInfo>();

            var httpClient = _httpClientFactory.CreateClient("Graph");
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            var pathSegment = string.IsNullOrEmpty(parentPath) || parentPath == "/"
                ? "root"
                : $"root:{parentPath}:";

            var url = $"https://graph.microsoft.com/v1.0/me/drive/{pathSegment}/children?$filter=folder ne null&$select=id,name,folder,parentReference";

            var response = await httpClient.GetFromJsonAsync<DriveItemListResponse>(url, cancellationToken);

            return response?.Value?.Select(item => new FolderInfo
            {
                Id = item.Id,
                Name = item.Name,
                Path = (item.ParentReference?.Path ?? "") + "/" + item.Name,
                HasChildren = (item.Folder?.ChildCount ?? 0) > 0
            }) ?? Enumerable.Empty<FolderInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list folders for {User}", userEmail);
            return Enumerable.Empty<FolderInfo>();
        }
    }

    private string? ExtractErrorMessage(string errorJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(errorJson);
            if (doc.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.TryGetProperty("message", out var messageElement))
                {
                    return messageElement.GetString();
                }
            }
        }
        catch
        {
            // If parsing fails, return null
        }
        return null;
    }
}

// Response models for Graph API
internal class DriveItemResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("webUrl")]
    public string WebUrl { get; set; } = string.Empty;
}

internal class DriveItemListResponse
{
    [JsonPropertyName("value")]
    public List<DriveItem>? Value { get; set; }
}

internal class DriveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("folder")]
    public FolderFacet? Folder { get; set; }

    [JsonPropertyName("parentReference")]
    public ParentReference? ParentReference { get; set; }
}

internal class FolderFacet
{
    [JsonPropertyName("childCount")]
    public int ChildCount { get; set; }
}

internal class ParentReference
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}
