using System.Net;
using System.Text.Json.Serialization;
using EmailToMarkdown.Models;
using EmailToMarkdown.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EmailToMarkdown.Functions;

public class AuthFunctions
{
    private readonly TokenService _tokenService;
    private readonly ConfigurationService _configService;
    private readonly StorageProviderFactory _storageFactory;
    private readonly ILogger<AuthFunctions> _logger;

    public AuthFunctions(
        TokenService tokenService,
        ConfigurationService configService,
        StorageProviderFactory storageFactory,
        ILogger<AuthFunctions> logger)
    {
        _tokenService = tokenService;
        _configService = configService;
        _storageFactory = storageFactory;
        _logger = logger;
    }

    /// <summary>
    /// Store tokens and save user preferences after OAuth flow completes in the SPA.
    /// The SPA handles the OAuth flow with MSAL.js and sends the tokens here.
    /// </summary>
    [Function("AuthRegister")]
    public async Task<HttpResponseData> Register(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "auth/register")]
        HttpRequestData req)
    {
        // Handle CORS preflight
        if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return CreateCorsResponse(req);
        }

        try
        {
            var request = await req.ReadFromJsonAsync<RegistrationRequest>();
            if (request == null || string.IsNullOrEmpty(request.Email))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request" });
                return badRequest;
            }

            _logger.LogInformation("Processing registration for {Email}", request.Email);

            // Store tokens (already obtained by MSAL.js in the SPA)
            var tokenStored = await _tokenService.StoreTokensDirectlyAsync(
                request.Provider,
                request.Email,
                request.AccessToken,
                request.RefreshToken,
                request.ExpiresIn);

            if (!tokenStored)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = "Failed to store tokens" });
                return errorResponse;
            }

            // Save user preferences
            var preferences = new UserPreferences
            {
                PartitionKey = "preferences",
                RowKey = request.Email.ToLowerInvariant(),
                EmailAddress = request.Email,
                StorageProvider = request.Provider,
                RootFolder = request.RootFolder ?? "/EmailToMarkdown",
                FolderId = request.FolderId ?? string.Empty,
                DriveId = request.DriveId ?? string.Empty,
                DeliveryMethod = "onedrive",
                ConsentGrantedAt = DateTimeOffset.UtcNow
            };

            await _configService.SaveUserPreferencesAsync(preferences);

            _logger.LogInformation("User registered successfully: {Email}", request.Email);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                email = request.Email,
                rootFolder = preferences.RootFolder
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Registration failed" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Get current user's registration status
    /// </summary>
    [Function("AuthStatus")]
    public async Task<HttpResponseData> GetStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "auth/status/{email}")]
        HttpRequestData req,
        string email)
    {
        try
        {
            var prefs = await _configService.GetUserPreferencesAsync(email);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                isRegistered = prefs != null,
                storageProvider = prefs?.StorageProvider,
                rootFolder = prefs?.RootFolder,
                consentGrantedAt = prefs?.ConsentGrantedAt,
                lastSuccessfulSync = prefs?.LastSuccessfulSync
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get status for {Email}", email);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to get status" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Revoke user's tokens and optionally delete preferences
    /// </summary>
    [Function("AuthRevoke")]
    public async Task<HttpResponseData> Revoke(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "auth/revoke")]
        HttpRequestData req)
    {
        try
        {
            var request = await req.ReadFromJsonAsync<RevokeRequest>();
            if (request == null || string.IsNullOrEmpty(request.Email))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request" });
                return badRequest;
            }

            _logger.LogInformation("Revoking tokens for {Email}", request.Email);

            // Revoke tokens
            await _tokenService.RevokeTokensAsync(
                request.Provider ?? "microsoft",
                request.Email);

            // Optionally delete preferences
            if (request.DeletePreferences)
            {
                await _configService.DeleteUserPreferencesAsync(request.Email);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Revoke failed");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Revoke failed" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Validate that a user's connection is still working
    /// </summary>
    [Function("AuthValidate")]
    public async Task<HttpResponseData> Validate(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "auth/validate/{email}")]
        HttpRequestData req,
        string email)
    {
        try
        {
            var prefs = await _configService.GetUserPreferencesAsync(email);
            if (prefs == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "User not registered" });
                return notFound;
            }

            var provider = _storageFactory.GetProvider(prefs.StorageProvider);
            var isValid = await provider.ValidateConnectionAsync(email);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                email = email,
                isValid = isValid,
                storageProvider = prefs.StorageProvider
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for {Email}", email);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Validation failed" });
            return errorResponse;
        }
    }

    private HttpResponseData CreateCorsResponse(HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        return response;
    }
}

public class RegistrationRequest
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "microsoft";

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("rootFolder")]
    public string? RootFolder { get; set; }

    [JsonPropertyName("folderId")]
    public string? FolderId { get; set; }

    [JsonPropertyName("driveId")]
    public string? DriveId { get; set; }
}

public class RevokeRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("deletePreferences")]
    public bool DeletePreferences { get; set; } = false;
}
