using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Identity;
using Microsoft.Graph;
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
    TenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID") ?? Environment.GetEnvironmentVariable("TENANT_ID") ?? string.Empty,
    ClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID") ?? Environment.GetEnvironmentVariable("CLIENT_ID") ?? string.Empty,
    ClientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET") ?? Environment.GetEnvironmentVariable("CLIENT_SECRET") ?? string.Empty
};

// Register services
builder.Services.AddSingleton(config);

// Register Microsoft Graph client for OneDrive access
if (!string.IsNullOrEmpty(config.TenantId) && !string.IsNullOrEmpty(config.ClientId) && !string.IsNullOrEmpty(config.ClientSecret))
{
    var credential = new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret);
    var graphClient = new GraphServiceClient(credential);
    builder.Services.AddSingleton(graphClient);
}

// Register ConfigurationService for user preferences
builder.Services.AddSingleton(sp => new ConfigurationService(config.StorageConnectionString));

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
