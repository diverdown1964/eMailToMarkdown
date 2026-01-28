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
                AddCorsHeaders(badRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request" });
                return badRequest;
            }

            _logger.LogInformation("Processing registration for {Email} with provider {Provider}", request.Email, request.Provider);

            bool tokensStored = false;

            // Option 1: Authorization code exchange (preferred - gets refresh tokens)
            if (!string.IsNullOrEmpty(request.Code) && !string.IsNullOrEmpty(request.CodeVerifier))
            {
                tokensStored = await _tokenService.ExchangeCodeForTokensAsync(
                    request.Provider,
                    request.Email,
                    request.Code,
                    request.CodeVerifier,
                    request.RedirectUri ?? "");

                if (!tokensStored)
                {
                    _logger.LogWarning("Failed to exchange authorization code for {Email}", request.Email);
                }
            }
            // Option 2: Direct tokens provided (legacy - may not have refresh token)
            else if (!string.IsNullOrEmpty(request.RefreshToken))
            {
                tokensStored = await _tokenService.StoreTokensDirectlyAsync(
                    request.Provider,
                    request.Email,
                    request.AccessToken,
                    request.RefreshToken,
                    request.ExpiresIn);
            }
            else if (!string.IsNullOrEmpty(request.AccessToken))
            {
                // Store access token even without refresh token - useful for immediate validation
                tokensStored = await _tokenService.StoreTokensDirectlyAsync(
                    request.Provider,
                    request.Email,
                    request.AccessToken,
                    "", // No refresh token
                    request.ExpiresIn > 0 ? request.ExpiresIn : 3600);
            }
            else
            {
                _logger.LogWarning("No authorization code or tokens provided for {Email}", request.Email);
            }

            // Save storage connection for this provider
            var storageConnection = new UserStorageConnection
            {
                RootFolder = request.RootFolder ?? "/EmailToMarkdown",
                FolderId = request.FolderId ?? string.Empty,
                DriveId = request.DriveId ?? string.Empty,
                ConsentGrantedAt = DateTimeOffset.UtcNow,
                IsActive = true
            };

            await _configService.SaveStorageConnectionAsync(request.Email, request.Provider, storageConnection);

            // Link identities if a linked email is provided
            if (!string.IsNullOrEmpty(request.LinkedEmail) &&
                !request.LinkedEmail.Equals(request.Email, StringComparison.OrdinalIgnoreCase))
            {
                await _configService.LinkIdentitiesAsync(request.LinkedEmail, request.Email, request.Provider);
            }

            // Also save to legacy UserPreferences for backward compatibility
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

            _logger.LogInformation("User registered successfully: {Email} with provider {Provider}", request.Email, request.Provider);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                email = request.Email,
                rootFolder = preferences.RootFolder,
                tokensStored = tokensStored,
                hasRefreshToken = tokensStored && !string.IsNullOrEmpty(request.Code) // Code exchange provides refresh tokens
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for {Email}", req.Query["email"] ?? "unknown");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse);
            await errorResponse.WriteAsJsonAsync(new {
                success = false,
                error = $"Registration failed: {ex.Message}"
            });
            return errorResponse;
        }
    }

    /// <summary>
    /// Get current user's registration status
    /// </summary>
    [Function("AuthStatus")]
    public async Task<HttpResponseData> GetStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "auth/status/{email}")]
        HttpRequestData req,
        string email)
    {
        // Handle CORS preflight
        if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return CreateCorsResponse(req);
        }

        try
        {
            var prefs = await _configService.GetUserPreferencesAsync(email);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response);
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
            AddCorsHeaders(errorResponse);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to get status" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Get all connected storage providers for a user, including linked identities
    /// </summary>
    [Function("AuthProviders")]
    public async Task<HttpResponseData> GetProviders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "auth/providers/{email}")]
        HttpRequestData req,
        string email)
    {
        // Handle CORS preflight
        if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return CreateCorsResponse(req);
        }

        try
        {
            _logger.LogInformation("Getting providers for {Email}", email);
            
            // Get all storage connections across linked identities
            var connections = await _configService.GetAllStorageConnectionsForUserAsync(email);
            
            _logger.LogInformation("Found {Count} storage connections for {Email}", connections.Count, email);
            var linkedIdentities = await _configService.GetLinkedIdentitiesAsync(email);

            var providers = connections.Select(kvp => new
            {
                provider = kvp.Key,
                rootFolder = kvp.Value.RootFolder,
                isActive = kvp.Value.IsActive,
                consentGrantedAt = kvp.Value.ConsentGrantedAt,
                lastSuccessfulSync = kvp.Value.LastSuccessfulSync
            }).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response);
            await response.WriteAsJsonAsync(new
            {
                email = email,
                providers = providers,
                linkedIdentities = linkedIdentities.Select(l => new { email = l.RowKey, provider = l.Provider })
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get providers for {Email}", email);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to get providers" });
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
        AddCorsHeaders(response);
        return response;
    }

    private void AddCorsHeaders(HttpResponseData response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
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

    /// <summary>
    /// If the user is already logged in with another identity, pass that email here
    /// to link the identities together.
    /// </summary>
    [JsonPropertyName("linkedEmail")]
    public string? LinkedEmail { get; set; }

    /// <summary>
    /// Authorization code for server-side token exchange (preferred for getting refresh tokens)
    /// </summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>
    /// PKCE code verifier for the authorization code
    /// </summary>
    [JsonPropertyName("codeVerifier")]
    public string? CodeVerifier { get; set; }

    /// <summary>
    /// Redirect URI used in the authorization request
    /// </summary>
    [JsonPropertyName("redirectUri")]
    public string? RedirectUri { get; set; }
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
