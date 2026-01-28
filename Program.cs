using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using EmailToMarkdown.Models;
using EmailToMarkdown.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Configure application settings
var config = new AppConfiguration
{
    SendGridApiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY") ?? string.Empty,
    AcsConnectionString = Environment.GetEnvironmentVariable("ACS_CONNECTION_STRING") ?? string.Empty,
    StorageAccountName = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_NAME") ?? string.Empty,
    StorageAccountKey = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_KEY") ?? string.Empty,
    // Multi-tenant app uses /common endpoint, so TenantId is no longer needed for auth
    // but we keep it for backwards compatibility in config
    TenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID") ?? Environment.GetEnvironmentVariable("TENANT_ID") ?? string.Empty,
    ClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID") ?? Environment.GetEnvironmentVariable("CLIENT_ID") ?? string.Empty,
    ClientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET") ?? Environment.GetEnvironmentVariable("CLIENT_SECRET") ?? string.Empty,
    // Google OAuth credentials
    GoogleClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty,
    GoogleClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? string.Empty
};

// Register configuration
builder.Services.AddSingleton(config);

// Configure Data Protection for token encryption
// Keys are persisted to Azure Blob Storage for consistency across function instances
if (!string.IsNullOrEmpty(config.StorageConnectionString))
{
    var blobServiceClient = new BlobServiceClient(config.StorageConnectionString);
    var containerClient = blobServiceClient.GetBlobContainerClient("dataprotection");
    containerClient.CreateIfNotExists();

    builder.Services.AddDataProtection()
        .SetApplicationName("EmailToMarkdown")
        .PersistKeysToAzureBlobStorage(containerClient.GetBlobClient("keys.xml"));
}
else
{
    // Fallback for local development without Azure Storage
    builder.Services.AddDataProtection()
        .SetApplicationName("EmailToMarkdown");
}

// Configure HTTP clients for OAuth and Graph API
builder.Services.AddHttpClient("Graph", client =>
{
    client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddHttpClient("OAuth");

// Register Table Service Client
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<AppConfiguration>();
    return new TableServiceClient(cfg.StorageConnectionString);
});

// Register services
builder.Services.AddSingleton<TokenEncryptionService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<ConfigurationService>(sp =>
{
    var cfg = sp.GetRequiredService<AppConfiguration>();
    return new ConfigurationService(cfg.StorageConnectionString);
});

// Register storage providers
builder.Services.AddSingleton<OneDriveStorageService>();
builder.Services.AddSingleton<GoogleDriveStorageService>();
builder.Services.AddSingleton<StorageProviderFactory>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
