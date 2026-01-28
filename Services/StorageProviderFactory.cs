using EmailToMarkdown.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EmailToMarkdown.Services;

public class StorageProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public StorageProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IStorageProvider GetProvider(StorageProviderType providerType)
    {
        return providerType switch
        {
            StorageProviderType.OneDrive => _serviceProvider.GetRequiredService<OneDriveStorageService>(),
            StorageProviderType.GoogleDrive => _serviceProvider.GetRequiredService<GoogleDriveStorageService>(),
            _ => throw new ArgumentException($"Unknown provider type: {providerType}")
        };
    }

    public IStorageProvider GetProvider(string providerName)
    {
        return providerName.ToLowerInvariant() switch
        {
            "onedrive" => GetProvider(StorageProviderType.OneDrive),
            "microsoft" => GetProvider(StorageProviderType.OneDrive), // Alias for OneDrive
            "googledrive" => GetProvider(StorageProviderType.GoogleDrive),
            "google" => GetProvider(StorageProviderType.GoogleDrive), // Alias for Google Drive
            _ => throw new ArgumentException($"Unknown provider: {providerName}")
        };
    }
}
