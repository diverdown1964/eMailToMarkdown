namespace EmailToMarkdown.Models;

public class AppConfiguration
{
    public string SendGridApiKey { get; set; } = string.Empty;
    public string AcsConnectionString { get; set; } = string.Empty;
    public string StorageAccountName { get; set; } = string.Empty;
    public string StorageAccountKey { get; set; } = string.Empty;

    // Azure AD credentials for Microsoft Graph
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public string StorageConnectionString =>
        $"DefaultEndpointsProtocol=https;AccountName={StorageAccountName};AccountKey={StorageAccountKey};EndpointSuffix=core.windows.net";
}
