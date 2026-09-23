using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StorageStandby.Backend.Core;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using StorageStandby.Backend.Services;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dropbox.Api;
using Dropbox.Api.Files;

namespace StorageStandby.Backend.Services
{
    public class DropboxProvider : ICloudProvider
    {
        private readonly AppDbContext _db;
        private readonly BackupEngineState _state;
        private readonly IDataProtectionProvider _dataProtector;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly TokenManager _tokenManager;

        public string ProviderName => "Dropbox";

        public DropboxProvider(
            AppDbContext db,
            BackupEngineState state,
            IDataProtectionProvider dataProtector,
            HttpClient httpClient,
            IConfiguration configuration,
            TokenManager tokenManager)
        {
            _db = db;
            _state = state;
            _dataProtector = dataProtector;
            _httpClient = httpClient;
            _configuration = configuration;
            _tokenManager = tokenManager;
        }
        
        // ----------------------------------------------------------
        // SYNC AND UPLOAD METHODS
        // ----------------------------------------------------------

        public DropboxClient CreateDropboxClient(string refreshToken)
        {
            // load oAuth credentials from configuration
            string clientId = _configuration["DropboxOAuth:AppKey"] 
                ?? throw new InvalidOperationException("Dropbox Client ID not configured.");
            string clientSecret = _configuration["DropboxOAuth:AppSecret"]
                ?? throw new InvalidOperationException("Dropbox Client Secret not configured.");

            // dropbox sdk automatically intercepts 401 errors, fetches a new access token, and retries failed file operation seamlessly
            return new DropboxClient(
                refreshToken, 
                clientId, 
                clientSecret);
        }

        public async Task<SyncResult> ExecuteSyncQueueAsync(
            SyncEvent syncEvent, // pre-created and passed in
            string accountId,
            IReadOnlyList<PendingSyncItem> queue, // queue to be persisted
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(syncEvent);
            ArgumentException.ThrowIfNullOrEmpty(accountId);
            ArgumentNullException.ThrowIfNull(queue);

            string refreshToken = _tokenManager.UnencryptRefreshToken(await _tokenManager.GetRefreshTokenAsync(
                Providers.Dropbox, 
                accountId)
            ?? throw new NullReferenceException(_tokenManager.NullReferenceExceptionMsg(Providers.Dropbox, accountId)));

            syncEvent.UnfinishedItems = string.Join(";", queue.Select(i => i.LocalPath));

            // retrieving matching watchedfolder using id and also load its associated cloud information
            var watchedFolder = await _db.WatchedFolders
                .Include(folder => folder.AssignedClouds)
                .SingleAsync(folder => folder.Id == syncEvent.WatchedFolderId, cancellationToken);
            var cloud = watchedFolder.AssignedClouds.Single(metadata =>
                metadata.Provider == Providers.Dropbox && metadata.AccountId == accountId);

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
                    using var dropboxClient = CreateDropboxClient(refreshToken);
                    await ExecuteItemAsync(
                        dropboxClient,
                        watchedFolder.LocalPath!,
                        cloud.RemoteFolderId,
                        item,
                        cancellationToken);
                    succeeded++;
                    syncEvent.SyncedItems = SyncEventPathHelper.AppendPath(syncEvent.SyncedItems, item.LocalPath);
                    syncEvent.UnfinishedItems = SyncEventPathHelper.RemovePath(syncEvent.UnfinishedItems, item.LocalPath);
                }
                catch (OperationCanceledException)
                {
                    syncEvent.CompletionType = SyncEventType.Interrupted;
                    syncEvent.CompletedTimestamp = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
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

        private async Task ExecuteItemAsync(
            DropboxClient dropboxClient,
            string localRootPath,
            string remoteRootId,
            PendingSyncItem item,
            CancellationToken cancellationToken
        )
        {
            string sourcePath = item.OriginalLocalPath ?? item.LocalPath;

            if (item.Deleted)
            {
                string remotePath = await ResolveRemotePathAsync(
                    dropboxClient, localRootPath, remoteRootId, sourcePath, cancellationToken)
                    ?? throw new FileNotFoundException("Remote item to delete was not found.", sourcePath);
                await dropboxClient.Files.DeleteV2Async(remotePath);
                return;
            }

            string? existingPath = null;
            if (item.Moved || item.Renamed)
            {
                existingPath = await ResolveRemotePathAsync(
                    dropboxClient, localRootPath, remoteRootId, sourcePath, cancellationToken)
                    ?? throw new FileNotFoundException("Remote item to update was not found.", sourcePath);
            }

            if (item.Moved)
            {
                string destinationPath = GetRemotePath(localRootPath, remoteRootId, item.LocalPath);
                string destinationParent = GetParentPath(destinationPath, remoteRootId);
                await EnsureFolderPathAsync(dropboxClient, destinationParent, cancellationToken);
                await dropboxClient.Files.MoveV2Async(existingPath!, destinationPath, autorename: false);
                existingPath = destinationPath;
            }

            if (item.Renamed)
            {
                string renamedPath = GetRemotePath(localRootPath, remoteRootId, item.LocalPath);
                await dropboxClient.Files.MoveV2Async(existingPath!, renamedPath, autorename: false);
                existingPath = renamedPath;
            }

            if (item.Changed || item.Created)
            {
                if (item.IsFolder)
                {
                    await EnsureFolderPathAsync(
                        dropboxClient,
                        GetRemotePath(localRootPath, remoteRootId, item.LocalPath),
                        cancellationToken);
                    return;
                }

                string remotePath = GetRemotePath(localRootPath, remoteRootId, item.LocalPath);
                string parentPath = GetParentPath(remotePath, remoteRootId);
                await EnsureFolderPathAsync(dropboxClient, parentPath, cancellationToken);
                await using var stream = File.OpenRead(item.LocalPath);
                await dropboxClient.Files.UploadAsync(
                    new UploadArg(remotePath, WriteMode.Overwrite.Instance), stream);
            }
        }

        private static string GetRemotePath(string localRootPath, string remoteRootPath, string localPath)
        {
            string relativePath = Path.GetRelativePath(localRootPath, localPath);
            if (relativePath == ".") return remoteRootPath;

            string normalizedRelativePath = relativePath
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            return $"/{remoteRootPath.Trim('/')}/{normalizedRelativePath.Trim('/')}";
        }

        private static string GetParentPath(string path, string fallbackPath)
        {
            int separatorIndex = path.LastIndexOf('/');
            return separatorIndex <= 0 ? $"/{fallbackPath.Trim('/')}" : path[..separatorIndex];
        }

        private async Task EnsureFolderPathAsync(
            DropboxClient dropboxClient,
            string folderPath,
            CancellationToken cancellationToken
        )
        {
            string[] segments = folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string currentPath = string.Empty;
            foreach (string segment in segments)
            {
                currentPath += "/" + segment;
                var metadata = await FindChildByNameAsync(
                    dropboxClient,
                    GetParentPath(currentPath, "/"),
                    segment,
                    allowFiles: false,
                    cancellationToken);
                if (metadata is not null)
                {
                    continue;
                }

                await dropboxClient.Files.CreateFolderV2Async(
                    new CreateFolderArg(currentPath, autorename: false));
            }
        }

        private async Task<string?> ResolveRemotePathAsync(
            DropboxClient dropboxClient,
            string localRootPath,
            string remoteRootPath,
            string localPath,
            CancellationToken cancellationToken)
        {
            string relativePath = Path.GetRelativePath(localRootPath, localPath);
            if (relativePath == ".") return remoteRootPath;

            string currentPath = $"/{remoteRootPath.Trim('/')}";
            foreach (string segment in relativePath
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(segment => !string.IsNullOrWhiteSpace(segment) && segment != "."))
            {
                var child = await FindChildByNameAsync(
                    dropboxClient, currentPath, segment, allowFiles: true, cancellationToken);
                if (child is null) return null;
                currentPath = $"{currentPath.TrimEnd('/')}/{child.Name}";
            }

            return currentPath;
        }

        private static async Task<Metadata?> FindChildByNameAsync(
            DropboxClient dropboxClient,
            string parentPath,
            string name,
            bool allowFiles,
            CancellationToken cancellationToken)
        {
            var result = await dropboxClient.Files.ListFolderAsync(new ListFolderArg(parentPath));
            var entries = result.Entries.ToList();
            while (result.HasMore)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await dropboxClient.Files.ListFolderContinueAsync(result.Cursor);
                entries.AddRange(result.Entries);
            }

            var matches = entries
                .Where(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                .Where(entry => allowFiles || entry.IsFolder)
                .ToList();
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple Dropbox items named '{name}' exist under '{parentPath}'.");
            }

            return matches.SingleOrDefault();
        }

        // ----------------------------------------------------------
        // REST API
        // ----------------------------------------------------------

        public async Task<StorageQuotaDto> GetRemainingStorageQuotaAsync(string accountId, CancellationToken cancellationToken=default)
        {
            DropboxClient dropboxClient = CreateDropboxClient(_tokenManager.UnencryptRefreshToken(await _tokenManager.GetRefreshTokenAsync(
                Providers.Dropbox,
                accountId)
            ?? throw new NullReferenceException(_tokenManager.NullReferenceExceptionMsg(Providers.Dropbox, accountId))));

            var spaceUsage = await dropboxClient.Users.GetSpaceUsageAsync();

            ulong bytesUsed = spaceUsage.Used;
            ulong totalAllocatedBytes = 0;

            if (spaceUsage.Allocation.IsIndividual)
            {
                totalAllocatedBytes = spaceUsage.Allocation.AsIndividual.Value.Allocated;
            }
            else if (spaceUsage.Allocation.IsTeam)
            {
                totalAllocatedBytes = spaceUsage.Allocation.AsTeam.Value.Allocated;
            }

            return new StorageQuotaDto{
                TotalBytes=totalAllocatedBytes,
                UsedBytes=bytesUsed,
            };

        }

        public Task<List<ConnectedAccountDto>> GetConnectedAccounts()
        {
            return _db.CloudTokens
                .Where(account => account.ProviderName == Providers.Dropbox)
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
            string clientId = _configuration["DropboxOAuth:AppKey"]
                ?? throw new InvalidOperationException("Dropbox client ID not configured.");
            int port = ListenerPortHelper.GetAvailablePort(5431);
            string redirectUri = $"http://localhost:{port}/callback";
            string state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var pkceFlow = new PKCEOAuthFlow();
            Uri authorizeUri = pkceFlow.GetAuthorizeUri(
                OAuthResponseType.Code,
                clientId,
                redirectUri,
                state: state,
                tokenAccessType: TokenAccessType.Offline,
                scopeList:
                [
                    "account_info.read",
                    "files.content.read",
                    "files.content.write",
                    "files.metadata.read",
                    "files.metadata.write",
                    "openid",
                    "profile",
                    "email",
                ]);

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri + "/");

            try
            {
                listener.Start();
                Process.Start(new ProcessStartInfo
                {
                    FileName = authorizeUri.ToString(),
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
                        Message = $"Dropbox OAuth error: {error ?? "No authorization code returned."}"
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
                OAuth2Response tokenResponse = await pkceFlow.ProcessCodeFlowAsync(
                    code,
                    clientId,
                    redirectUri,
                    _httpClient);
                if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
                {
                    return new AuthResult { Success = false, Message = "Dropbox did not return a refresh token." };
                }

                string accountId = tokenResponse.Uid
                    ?? throw new InvalidOperationException("Dropbox did not return an account ID.");
                string? email = null;
                string? name = null;
                using (var accountRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.dropboxapi.com/2/users/get_current_account"))
                {
                    accountRequest.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue(
                            "Bearer", tokenResponse.AccessToken);
                    accountRequest.Content = new StringContent("null", Encoding.UTF8, "application/json");
                    using var accountResponse = await _httpClient.SendAsync(accountRequest);
                    await EnsureSuccessAsync(accountResponse);
                    using JsonDocument accountDocument = JsonDocument.Parse(
                        await accountResponse.Content.ReadAsStringAsync());
                    JsonElement account = accountDocument.RootElement;
                    email = account.TryGetProperty("email", out JsonElement emailProperty)
                        ? emailProperty.GetString()
                        : null;
                    name = account.TryGetProperty("name", out JsonElement nameProperty)
                        && nameProperty.TryGetProperty("display_name", out JsonElement displayName)
                        ? displayName.GetString()
                        : null;
                }

                string encryptedRefreshToken = _dataProtector
                    .CreateProtector("DropboxTokenProtector")
                    .Protect(tokenResponse.RefreshToken);
                await _tokenManager.SaveRefreshTokenAsync(
                    Providers.Dropbox,
                    encryptedRefreshToken,
                    accountId,
                    email,
                    name);

                return new AuthResult { Success = true, Message = "Dropbox connection succeeded." };
            }
            catch (Exception ex)
            {
                return new AuthResult { Success = false, Message = "Dropbox authentication failed: " + ex.Message };
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Dropbox API returned {(int)response.StatusCode}: {body}");
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
    }
}
