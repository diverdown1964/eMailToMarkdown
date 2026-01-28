namespace EmailToMarkdown.Models;

public enum StorageProviderType
{
    OneDrive,
    GoogleDrive
}

public class StorageResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public string? FileId { get; set; }
    public string? WebUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RequiresReauth { get; set; }

    public static StorageResult Succeeded(string filePath, string? fileId = null, string? webUrl = null)
    {
        return new StorageResult
        {
            Success = true,
            FilePath = filePath,
            FileId = fileId,
            WebUrl = webUrl
        };
    }

    public static StorageResult Failed(string errorMessage, bool requiresReauth = false)
    {
        return new StorageResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            RequiresReauth = requiresReauth
        };
    }
}
