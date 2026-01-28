using EmailToMarkdown.Models;

namespace EmailToMarkdown.Services;

public interface IStorageProvider
{
    StorageProviderType ProviderType { get; }

    Task<StorageResult> SaveFileAsync(
        string userEmail,
        string rootFolder,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateConnectionAsync(
        string userEmail,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<FolderInfo>> ListFoldersAsync(
        string userEmail,
        string parentPath,
        CancellationToken cancellationToken = default);
}

public class FolderInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool HasChildren { get; set; }
}
