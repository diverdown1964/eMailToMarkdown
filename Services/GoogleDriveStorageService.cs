using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmailToMarkdown.Models;
using Microsoft.Extensions.Logging;

namespace EmailToMarkdown.Services;

public class GoogleDriveStorageService : IStorageProvider
{
    private readonly TokenService _tokenService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleDriveStorageService> _logger;

    public StorageProviderType ProviderType => StorageProviderType.GoogleDrive;

    public GoogleDriveStorageService(
        TokenService tokenService,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleDriveStorageService> logger)
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
            var accessToken = await _tokenService.GetValidAccessTokenAsync("google", userEmail);

            if (accessToken == null)
            {
                _logger.LogWarning("No valid token available for {User}", userEmail);
                return StorageResult.Failed("No valid token available", requiresReauth: true);
            }

            _logger.LogInformation("Access token retrieved successfully for {User}", userEmail);

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            // Build date-based path: /RootFolder/YYYY/MM/DD/
            var now = DateTime.UtcNow;
            var datePath = $"{now:yyyy}/{now:MM}/{now:dd}";
            var fullPath = $"{rootFolder.TrimEnd('/')}/{datePath}";

            _logger.LogInformation("Creating folder structure: {Path}", fullPath);

            // Create folder structure and get the final folder ID
            var folderId = await CreateFolderPathAsync(httpClient, fullPath, cancellationToken);

            if (string.IsNullOrEmpty(folderId))
            {
                _logger.LogError("Failed to create folder structure for {User}", userEmail);
                return StorageResult.Failed("Failed to create folder structure");
            }

            _logger.LogInformation("Folder created/found with ID: {FolderId}", folderId);

            // Upload the file using multipart upload
            var (fileId, uploadError) = await UploadFileAsync(httpClient, folderId, fileName, content, cancellationToken);

            if (string.IsNullOrEmpty(fileId))
            {
                return StorageResult.Failed(uploadError ?? "Failed to upload file");
            }

            var webUrl = $"https://drive.google.com/file/d/{fileId}/view";
            _logger.LogInformation("File saved successfully: {Path}/{FileName}", fullPath, fileName);

            return StorageResult.Succeeded($"{fullPath}/{fileName}", fileId, webUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save file to Google Drive for {User}", userEmail);
            return StorageResult.Failed(ex.Message);
        }
    }

    private async Task<string?> CreateFolderPathAsync(
        HttpClient httpClient,
        string fullPath,
        CancellationToken cancellationToken)
    {
        // Split path into segments and create each folder if it doesn't exist
        var segments = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parentId = "root"; // Start from root

        foreach (var segment in segments)
        {
            // Search for existing folder
            var existingId = await FindFolderAsync(httpClient, parentId, segment, cancellationToken);

            if (!string.IsNullOrEmpty(existingId))
            {
                parentId = existingId;
                continue;
            }

            // Create the folder
            var newFolderId = await CreateFolderAsync(httpClient, parentId, segment, cancellationToken);
            if (string.IsNullOrEmpty(newFolderId))
            {
                return null;
            }
            parentId = newFolderId;
        }

        return parentId;
    }

    private async Task<string?> FindFolderAsync(
        HttpClient httpClient,
        string parentId,
        string folderName,
        CancellationToken cancellationToken)
    {
        try
        {
            // Escape single quotes in folder name for query
            var escapedName = folderName.Replace("'", "\\'");
            var query = $"name='{escapedName}' and '{parentId}' in parents and mimeType='application/vnd.google-apps.folder' and trashed=false";
            var url = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id,name)";

            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to search for folder {Name}: {Status}",
                    folderName, response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<GoogleDriveFileListResponse>(
                cancellationToken: cancellationToken);

            return result?.Files?.FirstOrDefault()?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for folder {Name}", folderName);
            return null;
        }
    }

    private async Task<string?> CreateFolderAsync(
        HttpClient httpClient,
        string parentId,
        string folderName,
        CancellationToken cancellationToken)
    {
        try
        {
            var metadata = new
            {
                name = folderName,
                mimeType = "application/vnd.google-apps.folder",
                parents = new[] { parentId }
            };

            var json = JsonSerializer.Serialize(metadata);
            var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(
                "https://www.googleapis.com/drive/v3/files",
                requestContent,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to create folder {Name}: {Status} {Error}",
                    folderName, response.StatusCode, error);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<GoogleDriveFileResponse>(
                cancellationToken: cancellationToken);

            _logger.LogInformation("Created folder {Name} with ID {Id}", folderName, result?.Id);
            return result?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating folder {Name}", folderName);
            return null;
        }
    }

    private async Task<(string? FileId, string? Error)> UploadFileAsync(
        HttpClient httpClient,
        string folderId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if file already exists and update it, or create new
            var existingFileId = await FindFileAsync(httpClient, folderId, fileName, cancellationToken);

            if (!string.IsNullOrEmpty(existingFileId))
            {
                // Update existing file
                var updatedId = await UpdateFileAsync(httpClient, existingFileId, content, cancellationToken);
                return (updatedId, updatedId == null ? "Failed to update existing file" : null);
            }

            // Create new file using multipart upload
            var metadata = new
            {
                name = fileName,
                parents = new[] { folderId }
            };

            var metadataJson = JsonSerializer.Serialize(metadata);

            using var multipartContent = new MultipartContent("related");

            // Add metadata part
            var metadataPart = new StringContent(metadataJson, Encoding.UTF8, "application/json");
            multipartContent.Add(metadataPart);

            // Add file content part
            var filePart = new ByteArrayContent(content);
            filePart.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
            multipartContent.Add(filePart);

            var response = await httpClient.PostAsync(
                "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name,webViewLink",
                multipartContent,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to upload file {Name}: {Status} {Error}",
                    fileName, response.StatusCode, errorJson);

                // Try to extract the error message from the JSON response
                var errorMessage = ExtractErrorMessage(errorJson) ?? $"Upload failed with status {response.StatusCode}";

                return (null, errorMessage);
            }

            var result = await response.Content.ReadFromJsonAsync<GoogleDriveFileResponse>(
                cancellationToken: cancellationToken);

            _logger.LogInformation("Uploaded file {Name} with ID {Id}", fileName, result?.Id);
            return (result?.Id, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {Name}", fileName);
            return (null, ex.Message);
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

    private async Task<string?> FindFileAsync(
        HttpClient httpClient,
        string folderId,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            var escapedName = fileName.Replace("'", "\\'");
            var query = $"name='{escapedName}' and '{folderId}' in parents and trashed=false";
            var url = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id)";

            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<GoogleDriveFileListResponse>(
                cancellationToken: cancellationToken);

            return result?.Files?.FirstOrDefault()?.Id;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> UpdateFileAsync(
        HttpClient httpClient,
        string fileId,
        byte[] content,
        CancellationToken cancellationToken)
    {
        try
        {
            using var requestContent = new ByteArrayContent(content);
            requestContent.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");

            var request = new HttpRequestMessage(HttpMethod.Patch,
                $"https://www.googleapis.com/upload/drive/v3/files/{fileId}?uploadType=media")
            {
                Content = requestContent
            };

            var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to update file {Id}: {Status} {Error}",
                    fileId, response.StatusCode, error);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<GoogleDriveFileResponse>(
                cancellationToken: cancellationToken);

            _logger.LogInformation("Updated file with ID {Id}", result?.Id);
            return result?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file {Id}", fileId);
            return null;
        }
    }

    public async Task<bool> ValidateConnectionAsync(
        string userEmail,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await _tokenService.GetValidAccessTokenAsync("google", userEmail);
            if (accessToken == null) return false;

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.GetAsync(
                "https://www.googleapis.com/drive/v3/about?fields=user",
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate Google Drive connection for {User}", userEmail);
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
            var accessToken = await _tokenService.GetValidAccessTokenAsync("google", userEmail);
            if (accessToken == null) return Enumerable.Empty<FolderInfo>();

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            // Find the parent folder ID from path
            var parentId = "root";
            if (!string.IsNullOrEmpty(parentPath) && parentPath != "/")
            {
                parentId = await CreateFolderPathAsync(httpClient, parentPath, cancellationToken) ?? "root";
            }

            var query = $"'{parentId}' in parents and mimeType='application/vnd.google-apps.folder' and trashed=false";
            var url = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id,name)";

            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Enumerable.Empty<FolderInfo>();
            }

            var result = await response.Content.ReadFromJsonAsync<GoogleDriveFileListResponse>(
                cancellationToken: cancellationToken);

            return result?.Files?.Select(f => new FolderInfo
            {
                Id = f.Id,
                Name = f.Name,
                Path = $"{parentPath.TrimEnd('/')}/{f.Name}",
                HasChildren = true // We don't know without another API call
            }) ?? Enumerable.Empty<FolderInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list folders for {User}", userEmail);
            return Enumerable.Empty<FolderInfo>();
        }
    }
}

// Response models for Google Drive API
internal class GoogleDriveFileResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("webViewLink")]
    public string? WebViewLink { get; set; }
}

internal class GoogleDriveFileListResponse
{
    [JsonPropertyName("files")]
    public List<GoogleDriveFileResponse>? Files { get; set; }
}
