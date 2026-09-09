using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using StorageStandby.Backend.Core;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml.Linq;

public class TokenManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtectionProvider _dataProtector;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    // Memory cache for the short-lived access token
    private readonly ConcurrentDictionary<Providers, ConcurrentDictionary<string, string>?> _cachedAccessTokens = new() {
        [Providers.Google] = new ConcurrentDictionary<string, string> { }
    };
    private readonly ConcurrentDictionary<Providers, ConcurrentDictionary<string, DateTime>?> _accessTokenExpirations = new() {
        [Providers.Google] = new ConcurrentDictionary<string, DateTime> { },
    };

    public TokenManager(
        IServiceScopeFactory scopeFactory,
        IDataProtectionProvider dataProtector,
        BackupEngineState state,
        IConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _dataProtector = dataProtector;
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    // Thread-safe-ish helpers (EF Core DbContext is not thread-safe, so we create a new scope for each operation), add locking if heavy concurrency expected
    public void SetCachedAccessToken(Providers provider, string accountId, string accessToken, int expiresInSeconds)
    {
        if (_cachedAccessTokens[provider] == null)
        {
            _cachedAccessTokens[provider] = new ConcurrentDictionary<string, string>();
        }
        if (_accessTokenExpirations[provider] == null)
        {
            _accessTokenExpirations[provider] = new ConcurrentDictionary<string, DateTime>();
        }
        _cachedAccessTokens[provider][accountId] = accessToken;
        _accessTokenExpirations[provider][accountId] = DateTime.UtcNow.AddSeconds(expiresInSeconds);
    }

    public void ClearCachedAccessToken(Providers provider, string accountId)
    {
        if (_cachedAccessTokens.TryGetValue(provider, out var providerDict))
        {
            providerDict.TryRemove(accountId, out _);
        }
        if (_accessTokenExpirations.TryGetValue(provider, out var expirationDict))
        {
            expirationDict.TryRemove(accountId, out _);
        }
    }

    // writing data to SQLite with EF Core example (INSERT)
    // Persist encrypted refresh token using a scoped DbContext (TokenManager is singleton, so create scope)
    public async Task SaveRefreshTokenAsync(
        Providers provider,
        string encryptedToken,
        string accountId,
        string? email=null,
        string? name=null)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.CloudTokens.FirstOrDefaultAsync(t => t.ProviderName == provider && t.AccountId == accountId);

        if (existing == null) { 
            var newToken = new CloudToken
            {
                ProviderName = provider,
                EncryptedRefreshToken = encryptedToken,
                Status = ConnectionStatus.Connected,
                AccountId = accountId,
                Email = email,
                AccountName = name,
                LastUpdated = DateTime.UtcNow
            };

            // 1. Tell EFCore to track this new object
            db.CloudTokens.Add(newToken);

            // 2. Actually execute the SQL INSERT command to save it to the hard drive
            await db.SaveChangesAsync();
        }
        // Update existing token
        else
        {
            existing.EncryptedRefreshToken = encryptedToken;
            existing.LastUpdated = DateTime.UtcNow; 
            existing.Status = ConnectionStatus.Connected;
            existing.AccountId = accountId;
            existing.Email = email;
            existing.AccountName = name;
            await db.SaveChangesAsync();
        }
    }

    public async Task<CloudToken?> GetRefreshTokenAsync(Providers provider, string accountId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Generates a SQL query: SELECT * FROM CloudTokens WHERE ProviderName = 'Google' LIMIT 1
        CloudToken? token = await db.CloudTokens
            .FirstOrDefaultAsync(token => token.ProviderName == provider && token.AccountId == accountId);

        return token; // returns null if not found
    }

    // DEV: SQL Update equivalent
    // DEV: EF Core tracks objects you read, and if you change a property and call SaveChangesAsync, EF Core will automatically generate an UPDATE SQL query for the fields you just updated
    public async Task FlagRefreshTokenAsExpiredAsync(Providers providerName, string accountId)
    {
        // 1. Fetch the token from the database
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var token = await db.CloudTokens.FirstOrDefaultAsync(t => t.ProviderName == providerName && t.AccountId == accountId);

        if (token != null)
        {
            // 2. Modify the C# object
            token.Status = ConnectionStatus.Reauthenticate;
            token.LastUpdated = DateTime.UtcNow;

            // 3. Save changes. EF Core knows 'token' was modified and updates the DB automatically!
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteRefreshTokenAsync(Providers provider, string accountId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var token = await db.CloudTokens.FirstOrDefaultAsync(t => t.ProviderName == provider && t.AccountId == accountId);
        if (token != null)
        {
            db.CloudTokens.Remove(token);
            await db.SaveChangesAsync();
        }
        ClearCachedAccessToken(provider, accountId);
    }

    // DEV: Access tokens: short-lived (~1 hour), used for API calls. Refresh tokens: long-lived, used to get new access tokens.
    /// Returns a valid, active Access Token. Automatically requests a new one from Google if the current token is expired or missing.
    public async Task<string> GetValidAccessTokenAsync(Providers provider, string accountId)
    {
        // 1. Check if our in-memory access token is still valid (with a 5-minute safety buffer)
        if (_cachedAccessTokens.TryGetValue(provider, out var providerDict)
            && providerDict.TryGetValue(accountId, out var cachedToken)
            && providerDict != null
            && !string.IsNullOrEmpty(cachedToken) 
            && _accessTokenExpirations.TryGetValue(provider, out var expDict)
            && expDict.TryGetValue(accountId, out var expiry)
            && DateTime.UtcNow.AddMinutes(5) < expiry)
        {
            return cachedToken;
        }

        // 2. Otherwise, fetch and decrypt the Refresh Token from SQLite
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tokenRecord = await db.CloudTokens.FirstOrDefaultAsync(t => t.ProviderName == provider && t.AccountId == accountId);
        // Refresh Token is null
        if (tokenRecord == null || string.IsNullOrEmpty(tokenRecord.EncryptedRefreshToken))
        {
            // therefore, account not connected
            throw new NullReferenceException($"{ProviderMetadata.ProviderNames[provider]} account is not connected. User must authenticate first.");
        }

        var protector = _dataProtector.CreateProtector($"{ProviderMetadata.ProviderNames[provider]}TokenProtector");
        string refreshToken = protector.Unprotect(tokenRecord.EncryptedRefreshToken);

        // 3. POST to Google's Token Endpoint with grant_type = "refresh_token"
        string clientId = _config[$"{ProviderMetadata.ProviderNames[provider]}OAuth:ClientId"]!;
        string clientSecret = _config[$"{ProviderMetadata.ProviderNames[provider]}OAuth:ClientSecret"]!;

        var httpClient = _httpClientFactory.CreateClient(ProviderMetadata.ProviderNames[provider]);

        var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "refresh_token", refreshToken },
            { "grant_type", "refresh_token" } // Notice grant_type here!
        });

        var response = await httpClient.PostAsync(ProviderMetadata.TokenEndpoints[provider], requestBody);

        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadFromJsonAsync<TokenResponse>() ?? throw new InvalidOperationException("Failed to read JSON response from " + ProviderMetadata.ProviderNames[provider]);

            // cache access token in RAM
            SetCachedAccessToken(provider, accountId, json.AccessToken, json.ExpiresIn);

            // ensure DB status is set to Connected
            if (tokenRecord.Status != ConnectionStatus.Connected)
            {
                tokenRecord.Status = ConnectionStatus.Connected;
                await db.SaveChangesAsync();
            }

            return json.AccessToken;
        }
        // error in retrieving access code, likely due to revoked refresh token or user deauthorization
        else
        {
            ClearCachedAccessToken(provider, accountId);
            await FlagRefreshTokenAsExpiredAsync(provider, accountId);
            throw new UnauthorizedAccessException($"Token refresh failed: { await response.Content.ReadAsStringAsync() }");
        }
    }

    // Helper class to parse Google's JSON response
    public class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}