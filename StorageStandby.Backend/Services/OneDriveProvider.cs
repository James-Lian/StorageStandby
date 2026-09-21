using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using StorageStandby.Backend.Core;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

namespace StorageStandby.Backend.Services
{
    public class OneDriveProvider : ICloudProvider
    {
        private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
        private readonly AppDbContext _db;
        private readonly IDataProtectionProvider _dataProtector;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly TokenManager _tokenManager;

        public string ProviderName => "Microsoft";

        // ----------------------------------------------------------
        // SYNC AND UPLOAD METHODS
        // ----------------------------------------------------------

        public OneDriveProvider(
            AppDbContext db,
            IDataProtectionProvider dataProtector,
            HttpClient httpClient,
            IConfiguration configuration,
            TokenManager tokenManager)
        {
            _db = db;
            _dataProtector = dataProtector;
            _httpClient = httpClient;
            _configuration = configuration;
            _tokenManager = tokenManager;
        }

        private GraphServiceClient BuildDriveClient(string accountId)
        {
            var tokenProvider = new TokenManagerAccessTokenProvider(
                _tokenManager, 
                accountId);
            
            var authProvider = new BaseBearerTokenAuthenticationProvider(tokenProvider);
            return new GraphServiceClient(authProvider);
        }

        // custom access token expiration management 
        private sealed class TokenManagerAccessTokenProvider : IAccessTokenProvider
        {
            private readonly TokenManager _tokenManager;
            private readonly string _accountId;

            public TokenManagerAccessTokenProvider(
                TokenManager tokenManager,
                string accountId)
            {
                _tokenManager = tokenManager;
                _accountId = accountId;
                AllowedHostsValidator = new AllowedHostsValidator(
                    new[] { "graph.microsoft.com" });
            }

            public AllowedHostsValidator AllowedHostsValidator { get; }

            public Task<string> GetAuthorizationTokenAsync(
                Uri uri,
                Dictionary<string, object>? additionalAuthenticationContext = null,
                CancellationToken cancellationToken = default)
            {
                return _tokenManager.GetValidAccessTokenAsync(
                    Providers.Microsoft,
                    _accountId);
            }
        }


        // TODO: fix accessToken expiration <-- double check
        public async Task<SyncResult> ExecuteSyncQueueAsync(
            SyncEvent syncEvent, // pre-created and passed in
            string accountId,
            IReadOnlyList<PendingSyncItem> queue, // queue to be persisted
            CancellationToken cancellationToken = default)
        {
            string refreshToken = _tokenManager.UnencryptRefreshToken(await _tokenManager.GetRefreshTokenAsync(
                Providers.Microsoft, 
                accountId) 
            ?? throw new NullReferenceException(_tokenManager.NullReferenceExceptionMsg(Providers.Microsoft, accountId)));

            ArgumentNullException.ThrowIfNull(syncEvent);
            ArgumentException.ThrowIfNullOrEmpty(accountId);
            ArgumentNullException.ThrowIfNull(queue);
            
            syncEvent.UnfinishedItems = string.Join(";", queue.Select(i => i.LocalPath));

            // automatic garbage cleanup at the end of scope
            using var graphService = BuildDriveClient(accountId);

            // retrieving matching watchedfolder using id and also load its associated cloud information
            var watchedFolder = await _db.WatchedFolders
                .Include(folder => folder.AssignedClouds)
                .SingleAsync(folder => folder.Id == syncEvent.WatchedFolderId, cancellationToken);
            var cloud = watchedFolder.AssignedClouds.Single(metadata =>
                metadata.Provider == Providers.Google && metadata.AccountId == syncEvent.AccountId);

            // sync event metadata
            syncEvent.CompletionType = SyncEventType.InProgress;
            syncEvent.CompletedTimestamp = null;
            await _db.SaveChangesAsync(cancellationToken);

            // success counters
            int initialCount = syncEvent.UnfinishedItems
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Length;
            int succeeded = 0;
            int failed = 0;

            foreach (var item in queue)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await ExecuteItemAsync(graphService, watchedFolder.LocalPath!, cloud.RemoteFolderId, item, cancellationToken);
                    succeeded++;
                    syncEvent.SyncedItems = SyncEventPathHelper.AppendPath(syncEvent.SyncedItems, item.LocalPath);
                    syncEvent.UnfinishedItems = SyncEventPathHelper.RemovePath(syncEvent.UnfinishedItems, item.LocalPath);
                }
                catch (OperationCanceledException)
                {
                    syncEvent.CompletionType = SyncEventType.Interrupted;
                    syncEvent.CompletedTimestamp = DateTime.UtcNow;
                    _db.SaveChanges();
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    syncEvent.FailedItems.Add(new FailedItemsDetails
                    {
                        FailedItem = item.LocalPath,
                        Details = ex.Message
                    });
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

                        syncEvent.CompletionType = failed == 0 ? SyncEventType.Succeeded : SyncEventType.Unfinished;
            syncEvent.CompletedTimestamp = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return new SyncResult
            {
                Status = syncEvent.CompletionType,
                Succeeded = succeeded,
                Failed = failed,
                Unfinished = initialCount - succeeded - failed
            };
        }

        // // Adds a local path to a semicolon-delimited event path list.
        // private static string AppendPath(string current, string path)
        // {
        //     if (string.IsNullOrEmpty(current)) return string.Empty;

        //     return string.IsNullOrEmpty(current) ? path : $"{current};{path}";
        // }

        // // Removes a local path from a semicolon-delimited event path list.
        // private static string RemovePath(string current, string path)
        // {
        //     if (string.IsNullOrEmpty(current)) return string.Empty;
            
        //     return string.Join(";", current
        //         .Split(';', StringSplitOptions.RemoveEmptyEntries)
        //         .Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)));
        // }

        private async Task ExecuteItemAsync(
            GraphServiceClient graphService,
            string localRootPath,
            string remoteRootId,
            PendingSyncItem item,
            CancellationToken cancellationToken
        )
        {
            
        }

        private async Task UploadAsync(
            GraphServiceClient graphService,
            CancellationToken cancellationToken
        )
        {
            
        }

        public async Task UploadFileAsync(
            string accountId,
            string localFilePath,
            string remoteFolderPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
            ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(remoteFolderPath);

            string accessToken = await _tokenManager.GetValidAccessTokenAsync(Providers.Microsoft, accountId);
            string remotePath = string.IsNullOrWhiteSpace(remoteFolderPath)
                ? Path.GetFileName(localFilePath)
                : $"{remoteFolderPath.TrimEnd('/')}/{Path.GetFileName(localFilePath)}";
            string encodedPath = string.Join(
                "/",
                remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));

            using var request = CreateGraphRequest(
                HttpMethod.Put,
                $"/me/drive/root:/{encodedPath}:/content",
                accessToken);
            await using FileStream fileStream = File.OpenRead(localFilePath);
            request.Content = new StreamContent(fileStream);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var response = await _httpClient.SendAsync(request);
            await EnsureSuccessAsync(response);
        }

        // ----------------------------------------------------------
        // REST API
        // ----------------------------------------------------------
        public async Task<StorageQuotaDto> GetRemainingStorageQuotaAsync(string accountId, CancellationToken cancellationToken=default)
        {
            string accessToken = await _tokenManager.GetValidAccessTokenAsync(Providers.Microsoft, accountId);
            using var request = CreateGraphRequest(HttpMethod.Get, "/me/drive?$select=quota", accessToken);
            using var response = await _httpClient.SendAsync(request);
            await EnsureSuccessAsync(response);

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            JsonElement quota = document.RootElement.GetProperty("quota");

            return new StorageQuotaDto
            {
                TotalBytes = quota.GetProperty("total").GetInt64(),
                UsedBytes = quota.GetProperty("used").GetInt64()
            };
        }

        // ----------------------------------------------------------
        // USERS/AUTH/SIGN-IN
        // ----------------------------------------------------------
        public Task<List<ConnectedAccountDto>> GetConnectedAccounts()
        {
            return _db.CloudTokens
                .Where(account => account.ProviderName == Providers.Microsoft)
                .Select(account => new ConnectedAccountDto
                {
                    Id = account.AccountId,
                    Status = account.Status,
                    Email = account.Email,
                    Name = account.AccountName
                })
                .ToListAsync();
        }

        public async Task<AuthResult> StartOAuthAsync()
        {
            string clientId = Uri.EscapeDataString(_configuration["MicrosoftOAuth:ClientID"])
                ?? throw new InvalidOperationException("Microsoft client ID not configured.");
            string tenant = _configuration["MicrosoftOAuth:TenantID"] ?? "common";
            int port = GetAvailablePort(5432);
            string redirectUri = Uri.EscapeDataString($"http://localhost:{port}/callback/");
            string state = Uri.EscapeDataString(CreateCode(32));
            string codeVerifier = CreateCode(64);
            string codeChallenge = Uri.EscapeDataString(CreateCodeChallenge(codeVerifier));
            string requestedScope = Uri.EscapeDataString("openid profile email offline_access User.Read Files.ReadWrite");

            string authorizationUrl =
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenant)}/oauth2/v2.0/authorize" +
                $"?client_id={clientId}" +
                "&response_type=code" +
                $"&redirect_uri={redirectUri}" +
                "&response_mode=query" +
                $"&scope={requestedScope}" +
                $"&state={state}" +
                $"&code_challenge={codeChallenge}" +
                "&code_challenge_method=S256";

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);

            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = $"Failed to start HTTP listener on {redirectUri}: {ex.Message}"
                };
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = authorizationUrl,
                    UseShellExecute = true
                });

                Task<HttpListenerContext> listenerTask = listener.GetContextAsync();
                Task timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));
                if (await Task.WhenAny(listenerTask, timeoutTask) == timeoutTask)
                {
                    return new AuthResult { Success = false, Message = "Authentication timed out." };
                }

                HttpListenerContext context = await listenerTask;
                string? code = context.Request.QueryString["code"];
                string? returnedState = context.Request.QueryString["state"];
                string? error = context.Request.QueryString["error"];

                if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
                {
                    await WriteBrowserResponseAsync(context, "Authentication was cancelled or failed.");
                    return new AuthResult
                    {
                        Success = false,
                        Message = $"Microsoft OAuth error: {error ?? "No authorization code returned."}"
                    };
                }

                if (string.IsNullOrEmpty(returnedState) ||
                    !CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(state),
                        Encoding.UTF8.GetBytes(returnedState)))
                {
                    await WriteBrowserResponseAsync(context, "Authentication failed.");
                    return new AuthResult { Success = false, Message = "OAuth state validation failed." };
                }

                await WriteBrowserResponseAsync(context, "Authentication successful. You may close this tab.");

                var tokenRequest = new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["scope"] = requestedScope,
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code",
                    ["code_verifier"] = codeVerifier
                };

                using var tokenResponse = await _httpClient.PostAsync(
                    $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenant)}/oauth2/v2.0/token",
                    new FormUrlEncodedContent(tokenRequest));
                string tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    return new AuthResult { Success = false, Message = $"Token exchange failed: {tokenJson}" };
                }

                using JsonDocument tokenDocument = JsonDocument.Parse(tokenJson);
                JsonElement tokenRoot = tokenDocument.RootElement;
                string accessToken = tokenRoot.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("Microsoft did not return an access token.");
                string refreshToken = tokenRoot.GetProperty("refresh_token").GetString()
                    ?? throw new InvalidOperationException("Microsoft did not return a refresh token.");
                int expiresIn = tokenRoot.TryGetProperty("expires_in", out JsonElement expires)
                    ? expires.GetInt32()
                    : 3600;

                string accountId = await GetMicrosoftAccountIdAsync(accessToken);
                string? email = await GetMicrosoftUserPropertyAsync(accessToken, "mail")
                    ?? await GetMicrosoftUserPropertyAsync(accessToken, "userPrincipalName");
                string? name = await GetMicrosoftUserPropertyAsync(accessToken, "displayName");
                string encryptedRefreshToken = _dataProtector
                    .CreateProtector("MicrosoftTokenProtector")
                    .Protect(refreshToken);

                _tokenManager.SetCachedAccessToken(Providers.Microsoft, accountId, accessToken, expiresIn);
                await _tokenManager.SaveRefreshTokenAsync(
                    Providers.Microsoft,
                    encryptedRefreshToken,
                    accountId,
                    email,
                    name);

                return new AuthResult { Success = true, Message = "OneDrive connection succeeded." };
            }
            catch (Exception ex)
            {
                return new AuthResult { Success = false, Message = "Microsoft authentication failed: " + ex.Message };
            }
            finally
            {
                listener.Stop();
            }
        }

        public async Task<AuthResult> RevokeOAuthAsync(string accountId)
        {
            CloudToken? token = await _tokenManager.GetRefreshTokenAsync(Providers.Microsoft, accountId);
            if (token is null)
            {
                return new AuthResult { Success = false, Message = "No Microsoft account found." };
            }

            // Microsoft Graph has no Google-style token revocation endpoint.
            await _tokenManager.DeleteRefreshTokenAsync(Providers.Microsoft, accountId);
            return new AuthResult { Success = true, Message = "OneDrive connection removed." };
        }

        private HttpRequestMessage CreateGraphRequest(HttpMethod method, string path, string accessToken)
        {
            var request = new HttpRequestMessage(method, GraphBaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }

        private async Task<string> GetMicrosoftAccountIdAsync(string accessToken)
        {
            using var request = CreateGraphRequest(HttpMethod.Get, "/me?$select=id", accessToken);
            using var response = await _httpClient.SendAsync(request);
            await EnsureSuccessAsync(response);
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            return document.RootElement.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Microsoft did not return an account ID.");
        }

        private async Task<string?> GetMicrosoftUserPropertyAsync(string accessToken, string propertyName)
        {
            using var request = CreateGraphRequest(HttpMethod.Get, $"/me?$select={propertyName}", accessToken);
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            return document.RootElement.TryGetProperty(propertyName, out JsonElement property)
                ? property.GetString()
                : null;
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Microsoft Graph returned {(int)response.StatusCode}: {body}");
            }
        }

        private static async Task WriteBrowserResponseAsync(HttpListenerContext context, string message)
        {
            byte[] body = Encoding.UTF8.GetBytes(
                $"<html><body><h2>{WebUtility.HtmlEncode(message)}</h2></body></html>");
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }

        private static string CreateCode(int byteCount)
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        private static string CreateCodeChallenge(string codeVerifier)
        {
            return Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)))
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        private static int GetAvailablePort(int preferredPort)
        {
            if (IsPortAvailable(preferredPort))
            {
                return preferredPort;
            }

            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }

        private static bool IsPortAvailable(int port)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}
