using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Data.Tables;
using EmailToMarkdown.Models;
using Microsoft.Extensions.Logging;

namespace EmailToMarkdown.Services;

public class TokenService
{
    private readonly TableClient _tokenTable;
    private readonly TokenEncryptionService _encryption;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppConfiguration _config;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        TableServiceClient tableServiceClient,
        TokenEncryptionService encryption,
        IHttpClientFactory httpClientFactory,
        AppConfiguration config,
        ILogger<TokenService> logger)
    {
        _tokenTable = tableServiceClient.GetTableClient("UserTokens");
        _tokenTable.CreateIfNotExists();
        _encryption = encryption;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<string?> GetValidAccessTokenAsync(string provider, string userEmail)
    {
        var token = await GetStoredTokenAsync(provider, userEmail);
        if (token == null)
        {
            _logger.LogWarning("No token found for {Provider}/{User}", provider, userEmail);
            return null;
        }

        if (!token.IsValid)
        {
            _logger.LogWarning("Token marked invalid for {Provider}/{User}", provider, userEmail);
            return null;
        }

        // Check if access token is still valid (with 5 minute buffer)
        if (token.AccessTokenExpiry > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _encryption.Decrypt(token.EncryptedAccessToken);
        }

        // Need to refresh
        return await RefreshAccessTokenAsync(token);
    }

    public async Task<bool> ExchangeCodeForTokensAsync(
        string provider,
        string userEmail,
        string authorizationCode,
        string codeVerifier,
        string redirectUri)
    {
        var tokenEndpoint = GetTokenEndpoint(provider);
        var scopes = GetScopes(provider);
        var (clientId, clientSecret) = GetClientCredentials(provider);

        var requestBody = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = authorizationCode,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };

        // Google doesn't use code_verifier the same way, but we include it for Microsoft
        if (provider.ToLowerInvariant() == "microsoft")
        {
            requestBody["scope"] = scopes;
            requestBody["code_verifier"] = codeVerifier;
        }
        else if (provider.ToLowerInvariant() == "google")
        {
            // Google requires code_verifier for PKCE
            if (!string.IsNullOrEmpty(codeVerifier))
            {
                requestBody["code_verifier"] = codeVerifier;
            }
        }

        // For confidential clients (Web apps), add client_secret
        // Both Microsoft (now configured as Web app) and Google require client_secret
        if (!string.IsNullOrEmpty(clientSecret))
        {
            requestBody["client_secret"] = clientSecret;
        }

        var httpClient = _httpClientFactory.CreateClient("OAuth");

        var response = await httpClient.PostAsync(
            tokenEndpoint,
            new FormUrlEncodedContent(requestBody));

        if (!response.IsSuccessStatusCode)
        {
            var errorResponse = await response.Content.ReadAsStringAsync();
            _logger.LogError("Token exchange failed for {Provider}/{User}: {Error}", provider, userEmail, errorResponse);
            return false;
        }

        var rawResponse = await response.Content.ReadAsStringAsync();
        var tokenResponse = System.Text.Json.JsonSerializer.Deserialize<OAuthTokenResponse>(rawResponse);
        if (tokenResponse == null)
        {
            _logger.LogError("Failed to parse token response for {Provider}/{User}", provider, userEmail);
            return false;
        }

        // Parse JWT to get user identifiers
        var claims = ParseJwtClaims(tokenResponse.AccessToken);

        var userToken = new UserToken
        {
            PartitionKey = provider,
            RowKey = userEmail.ToLowerInvariant(),
            EncryptedAccessToken = _encryption.Encrypt(tokenResponse.AccessToken),
            EncryptedRefreshToken = _encryption.Encrypt(tokenResponse.RefreshToken ?? ""),
            AccessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
            Scopes = tokenResponse.Scope,
            ProviderUserId = claims.GetValueOrDefault("oid") ?? claims.GetValueOrDefault("sub") ?? "",
            ProviderTenantId = claims.GetValueOrDefault("tid") ?? "",
            IsValid = true,
            RefreshFailureCount = 0,
            LastError = string.Empty
        };

        await _tokenTable.UpsertEntityAsync(userToken);

        _logger.LogInformation("Tokens stored for {Provider}/{User}", provider, userEmail);
        return true;
    }

    public async Task<bool> StoreTokensDirectlyAsync(
        string provider,
        string userEmail,
        string accessToken,
        string refreshToken,
        int expiresIn)
    {
        var claims = ParseJwtClaims(accessToken);

        var userToken = new UserToken
        {
            PartitionKey = provider,
            RowKey = userEmail.ToLowerInvariant(),
            EncryptedAccessToken = _encryption.Encrypt(accessToken),
            EncryptedRefreshToken = _encryption.Encrypt(refreshToken),
            AccessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            Scopes = GetScopes(provider),
            ProviderUserId = claims.GetValueOrDefault("oid") ?? claims.GetValueOrDefault("sub") ?? "",
            ProviderTenantId = claims.GetValueOrDefault("tid") ?? "",
            IsValid = true,
            RefreshFailureCount = 0,
            LastError = string.Empty
        };

        await _tokenTable.UpsertEntityAsync(userToken);
        return true;
    }

    public async Task RevokeTokensAsync(string provider, string userEmail)
    {
        try
        {
            await _tokenTable.DeleteEntityAsync(provider, userEmail.ToLowerInvariant());
            _logger.LogInformation("Tokens revoked for {Provider}/{User}", provider, userEmail);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already deleted, that's fine
        }
    }

    private async Task<string?> RefreshAccessTokenAsync(UserToken token)
    {
        var refreshToken = _encryption.Decrypt(token.EncryptedRefreshToken);
        if (string.IsNullOrEmpty(refreshToken))
        {
            _logger.LogWarning("No refresh token available for {RowKey}", token.RowKey);
            return null;
        }

        var tokenEndpoint = GetTokenEndpoint(token.PartitionKey);
        var (clientId, clientSecret) = GetClientCredentials(token.PartitionKey);

        var requestBody = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };

        // Microsoft requires scope, Google doesn't
        if (token.PartitionKey.ToLowerInvariant() == "microsoft")
        {
            requestBody["scope"] = token.Scopes;
        }

        if (!string.IsNullOrEmpty(clientSecret))
        {
            requestBody["client_secret"] = clientSecret;
        }

        var httpClient = _httpClientFactory.CreateClient("OAuth");
        var response = await httpClient.PostAsync(
            tokenEndpoint,
            new FormUrlEncodedContent(requestBody));

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Token refresh failed for {RowKey}: {Error}", token.RowKey, error);

            token.RefreshFailureCount++;
            token.LastError = error;

            if (token.RefreshFailureCount >= 3)
            {
                token.IsValid = false;
                _logger.LogWarning("Token marked invalid after 3 refresh failures: {RowKey}", token.RowKey);
            }

            await _tokenTable.UpdateEntityAsync(token, token.ETag, TableUpdateMode.Replace);
            return null;
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>();
        if (tokenResponse == null)
        {
            return null;
        }

        // Update stored tokens
        token.EncryptedAccessToken = _encryption.Encrypt(tokenResponse.AccessToken);
        if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
        {
            token.EncryptedRefreshToken = _encryption.Encrypt(tokenResponse.RefreshToken);
        }
        token.AccessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        token.RefreshFailureCount = 0;
        token.LastError = string.Empty;
        token.IsValid = true;

        await _tokenTable.UpdateEntityAsync(token, token.ETag, TableUpdateMode.Replace);
        return tokenResponse.AccessToken;
    }

    private async Task<UserToken?> GetStoredTokenAsync(string provider, string userEmail)
    {
        try
        {
            var response = await _tokenTable.GetEntityAsync<UserToken>(
                provider,
                userEmail.ToLowerInvariant());
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static string GetTokenEndpoint(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "microsoft" => "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            "google" => "https://oauth2.googleapis.com/token",
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };
    }

    private static string GetScopes(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "microsoft" => "openid profile User.Read Files.ReadWrite offline_access",
            "google" => "openid profile email https://www.googleapis.com/auth/drive.file",
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };
    }

    private (string clientId, string clientSecret) GetClientCredentials(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "microsoft" => (_config.ClientId, _config.ClientSecret),
            "google" => (_config.GoogleClientId, _config.GoogleClientSecret),
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };
    }

    private static Dictionary<string, string> ParseJwtClaims(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return new Dictionary<string, string>();

            var payload = parts[1];
            // Add padding if needed
            var paddingNeeded = (4 - payload.Length % 4) % 4;
            payload = payload.PadRight(payload.Length + paddingNeeded, '=');

            // Replace URL-safe characters
            payload = payload.Replace('-', '+').Replace('_', '/');

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            return claims?.ToDictionary(k => k.Key, v => v.Value.ToString()) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}

public class OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }
}
