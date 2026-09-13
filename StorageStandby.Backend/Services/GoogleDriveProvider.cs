using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StorageStandby.Backend.Core;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using StorageStandby.Backend.Services;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using GoogleApiException = Google.GoogleApiException;


namespace StorageStandby.Backend.Services
{
    public class GoogleDriveProvider
    {
        private readonly AppDbContext _db;
        private readonly BackupEngineState _state;
        private readonly IDataProtectionProvider _dataProtector;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly TokenManager _tokenManager;
        public string ProviderName => "GoogleDrive";
        public GoogleDriveProvider(
            AppDbContext db,
            BackupEngineState state,
            IDataProtectionProvider dataProtector,
            IConfiguration configuration,
            HttpClient httpClient,
            TokenManager tokenManager,
            DriveService driveService)
        {
            _db = db;
            _state = state;
            _dataProtector = dataProtector;
            _httpClient = httpClient;
            _configuration = configuration;
            _tokenManager = tokenManager;
        }

        public sealed class GoogleSyncResult
        {
            public SyncEventType Status { get; init; }
            public int Succeeded { get; init; }
            public int Failed { get; init; }
            public int Unfinished { get; init; }
        }

        // The items to be synced are passed into this function
        public async Task<GoogleSyncResult> ExecuteSyncQueueAsync(
            SyncEvent syncEvent, // pre-created and passed in
            string currentAccessToken, // accessToken for API services
            IReadOnlyList<PendingSyncItem> queue, // queue to be persisted
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(syncEvent);
            ArgumentException.ThrowIfNullOrWhiteSpace(currentAccessToken);
            ArgumentNullException.ThrowIfNull(queue);

            // add unfinished items
            syncEvent.UnfinishedItems = string.Join(";", queue.Select(i => i.LocalPath));

            // building a Drive Client with currentAccessToken
            using var driveService = BuildDriveClient(currentAccessToken);

            var watchedFolder = await _db.WatchedFolders
                .Include(folder => folder.AssignedClouds)
                .SingleAsync(folder => folder.Id == syncEvent.WatchedFolderId, cancellationToken);
            // assigned cloud
            var cloud = watchedFolder.AssignedClouds.Single(metadata =>
                metadata.Provider == Providers.Google && metadata.AccountId == syncEvent.AccountId);

            // syncEvent metadata
            syncEvent.CompletionType = SyncEventType.InProgress;
            syncEvent.CompletedTimestamp = null;
            await _db.SaveChangesAsync(cancellationToken);

            // success counters
            int initialCount = syncEvent.UnfinishedItems.Count();
            int succeeded = 0;
            int failed = 0;

            // iterate through queue
            foreach (var item in queue)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await ExecuteItemAsync(driveService, watchedFolder.LocalPath!, cloud.RemoteFolderId, item, cancellationToken);
                    succeeded++;
                    syncEvent.SyncedItems = AppendPath(syncEvent.SyncedItems, item.LocalPath);
                    syncEvent.UnfinishedItems = RemovePath(syncEvent.SyncedItems, item.LocalPath);
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

            return new GoogleSyncResult
            {
                Status = syncEvent.CompletionType,
                Succeeded = succeeded,
                Failed = failed,
                Unfinished = initialCount - succeeded - failed
            };
        }

        private async Task ExecuteItemAsync(
            DriveService driveService,
            string localRootPath,
            string remoteRootId,
            PendingSyncItem item,
            CancellationToken cancellationToken)
        {
            if (item.Deleted)
            {
                var remoteItem = await FindByLocalPathAsync(driveService, remoteRootId,
                    localRootPath, item.OriginalLocalPath ?? item.LocalPath, cancellationToken)
                    ?? throw new FileNotFoundException("Remote item to delete was not found.", item.LocalPath);
                await driveService.Files.Delete(remoteItem.Id).ExecuteAsync(cancellationToken);
                return;
            }

            if (item.Moved)
            {
                var remoteItem = await FindByLocalPathAsync(driveService, remoteRootId,
                    localRootPath, item.OriginalLocalPath ?? item.LocalPath, cancellationToken)
                    ?? throw new FileNotFoundException("Remote item to move was not found.", item.LocalPath);
                await MoveAsync(driveService, localRootPath, remoteRootId, remoteItem, item.LocalPath, cancellationToken);
            }

            if (item.Renamed)
            {
                var remoteItem = await FindByLocalPathAsync(driveService, remoteRootId,
                    localRootPath, item.OriginalLocalPath ?? item.LocalPath, cancellationToken)
                    ?? throw new FileNotFoundException("Remote item to rename was not found.", item.LocalPath);
                await RenameAsync(driveService, remoteItem.Id, Path.GetFileName(item.LocalPath), cancellationToken);
            }

            if (item.Changed || item.Created)
            {
                if (item.IsFolder)
                {
                    await CreateFolderForPathAsync(driveService, localRootPath, remoteRootId, item.LocalPath, cancellationToken);
                    return;
                }

                var existing = await FindByLocalPathAsync(driveService, remoteRootId, localRootPath, item.LocalPath, cancellationToken);
                await UploadAsync(driveService, localRootPath, remoteRootId, item.LocalPath, existing?.Id, cancellationToken);
            }
        }

        // Adds a local path to a semicolon-delimited event path list.
        private static string AppendPath(string current, string path)
        {
            if (string.IsNullOrEmpty(current)) return string.Empty;

            return string.IsNullOrEmpty(current) ? path : $"{current};{path}";
        }

        // Removes a local path from a semicolon-delimited event path list.
        private static string RemovePath(string current, string path)
        {
            if (string.IsNullOrEmpty(current)) return string.Empty;
            
            return string.Join(";", current
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)));
        }

        // Resolves a local path beneath the watched folder to its current Drive item.
        private async Task<Google.Apis.Drive.v3.Data.File?> FindByLocalPathAsync(
            DriveService driveService,
            string remoteRootId,
            string localRootPath,
            string localPath,
            CancellationToken cancellationToken)
        {
            string relativePath = Path.GetRelativePath(localRootPath, localPath);
            if (relativePath == ".") return null;

            string parentId = remoteRootId;
            Google.Apis.Drive.v3.Data.File? current = null;
            string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(segment => !string.IsNullOrWhiteSpace(segment) && segment != ".")
                .ToArray();
            // iteratively searches through the folders and files using the path segments
            // throws if there is an ambiguous naming - multiple files/folders with the same name (impossible in this case to differentiate)
            for (int index = 0; index < segments.Length; index++)
            {
                string segment = segments[index];
                bool isFinalSegment = index == segments.Length - 1;
                var matchingItems = await FindChildrenByNameAsync(
                    driveService, parentId, segment, allowFiles: isFinalSegment, cancellationToken);
                if (matchingItems.Count == 0) return null;
                if (matchingItems.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"The remote path '{localPath}' is ambiguous: multiple items named '{segment}' exist under the same folder.");
                }

                current = matchingItems[0];
                parentId = current.Id;
            }

            return current;
        }

        // Finds non-trashed Drive children with an exact name under a parent.
        private static async Task<IList<Google.Apis.Drive.v3.Data.File>> FindChildrenByNameAsync(
            DriveService driveService,
            string parentId,
            string name,
            bool allowFiles,
            CancellationToken cancellationToken)
        {
            string escapedName = name.Replace("'", "\\'");
            var request = driveService.Files.List();
            // uses Google Drive API queries to search for specific matching item
            request.Q = $"'{parentId}' in parents and name = '{escapedName}' and trashed = false";
            if (!allowFiles)
            {
                request.Q += " and mimeType = 'application/vnd.google-apps.folder'";
            }
            request.Spaces = "drive";
            request.Fields = "files(id,name,mimeType,parents)";
            return (await request.ExecuteAsync(cancellationToken)).Files;
        }

        // Moves a resolved Drive item to the folder represented by its destination path.
        private async Task MoveAsync(
            DriveService driveService,
            string localRootPath,
            string remoteRootId,
            Google.Apis.Drive.v3.Data.File remoteItem,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            string destinationParentPath = Path.GetDirectoryName(destinationPath) ?? localRootPath;

            // Ensure the destination folder and any missing ancestor folders exist.
            await CreateFolderForPathAsync(driveService, localRootPath, remoteRootId,
                destinationParentPath, cancellationToken);

            var destinationParent = await FindByLocalPathAsync(driveService, remoteRootId, localRootPath,
                destinationParentPath, cancellationToken)
                ?? throw new DirectoryNotFoundException("Remote destination folder was not found.");
            string? oldParent = remoteItem.Parents?.SingleOrDefault();
            var request = driveService.Files.Update(new Google.Apis.Drive.v3.Data.File(), remoteItem.Id);
            request.AddParents = destinationParent.Id;
            request.RemoveParents = oldParent;
            await request.ExecuteAsync(cancellationToken);
        }

        // Changes the name of a resolved Drive item.
        private static async Task RenameAsync(
            DriveService driveService,
            string remoteId,
            string newName,
            CancellationToken cancellationToken)
        {
            // 
            await driveService.Files.Update(
                new Google.Apis.Drive.v3.Data.File { Name = newName }, remoteId)
                .ExecuteAsync(cancellationToken);
        }

        // Creates each missing folder in a local path beneath the remote root.
        private async Task CreateFolderForPathAsync(
            DriveService driveService,
            string localRootPath,
            string remoteRootId,
            string localPath,
            CancellationToken cancellationToken)
        {
            string relativePath = Path.GetRelativePath(localRootPath, localPath);
            string parentId = remoteRootId;
            foreach (string segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == ".") continue;
                var matches = await FindChildrenByNameAsync(driveService, parentId, segment, false, cancellationToken);
                if (matches.Count > 1) throw new InvalidOperationException($"Multiple remote folders match '{localPath}'.");
                if (matches.Count == 1)
                {
                    parentId = matches[0].Id;
                    continue;
                }

                var metadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = segment,
                    MimeType = "application/vnd.google-apps.folder",
                    Parents = new List<string> { parentId }
                };
                var created = await driveService.Files.Create(metadata).ExecuteAsync(cancellationToken);
                parentId = created.Id;
            }
        }

        // Creates or replaces a Drive file using the local file contents.
        private async Task UploadAsync(
            DriveService driveService,
            string localRootPath,
            string remoteRootId,
            string localPath,
            string? existingId,
            CancellationToken cancellationToken)
        {
            string parentPath = Path.GetDirectoryName(localPath) ?? localRootPath;
            var parent = await FindByLocalPathAsync(driveService, remoteRootId, localRootPath,
                parentPath, cancellationToken);
            string parentId = parent?.Id ?? remoteRootId;
            var metadata = new Google.Apis.Drive.v3.Data.File { Name = Path.GetFileName(localPath) };
            if (existingId == null) metadata.Parents = new List<string> { parentId };

            await using var stream = File.OpenRead(localPath);
            IUploadProgress progress;
            if (existingId == null)
            {
                var request = driveService.Files.Create(metadata, stream, "application/octet-stream");
                progress = await request.UploadAsync(cancellationToken);
            }
            else
            {
                var request = driveService.Files.Update(metadata, existingId, stream, "application/octet-stream");
                progress = await request.UploadAsync(cancellationToken);
            }

            if (progress.Status == UploadStatus.Failed)
                throw progress.Exception ?? new IOException("Google Drive upload failed.");
        }
        // Builds a short-lived Drive client authenticated with the supplied access token.
        private DriveService BuildDriveClient(string currentAccessToken)
        {
            var credential = GoogleCredential.FromAccessToken(currentAccessToken);

            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "StorageStandby"
            });
        }

        // Gets the remaining storage quota for the authenticated Drive account.
        public async Task GetRemainingStorageQuotaAsync()
        {
            // Implement logic to get remaining storage quota from Google Drive API
            throw new NotImplementedException();
        }

        // Creates one folder in Drive, optionally beneath the supplied parent.
        public async Task<string> CreateFolderAsync(DriveService driveService, string currentAccessToken, string folderName, string parentId = null)
        {
            var folderMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder"
            };

            if (!string.IsNullOrEmpty(parentId))
            {
                folderMetadata.Parents = new List<string> { parentId };
            }

            var request = driveService.Files.Create(folderMetadata);
            request.Fields = "id";

            var folder = await request.ExecuteAsync();
            return folder.Id;
        }

        // Uploads one local file as a new Drive file using the supplied access token.
        public async Task<string> UploadSingleFileAsync(string currentAccessToken, string localPath, string remoteName, string parentId = null)
        {
            using var driveService = BuildDriveClient(currentAccessToken);

            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = remoteName
            };

            if (!string.IsNullOrEmpty(parentId))
            {
                fileMetadata.Parents = new List<string> { parentId };
            }

            using (var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read))
            {
                var request = driveService.Files.Create(fileMetadata, stream, "application/octet-stream");
                request.Fields = "id";

                var progress = await request.UploadAsync(CancellationToken.None);

                if (progress.Status == UploadStatus.Failed)
                {
                    throw new Exception($"Upload failed for {remoteName}: {progress.Exception?.Message}");
                }

                return request.ResponseBody?.Id;
            }
        }

        // ----------------------------------------------------------
        // USERS/AUTH/SIGN-IN
        // ----------------------------------------------------------
        public class ConnectedAccountDto
        {
            public string Id { get; set; }
            public ConnectionStatus Status { get; set; }
            public string Email { get; set; }
            public string Name { get; set; }
        }
        public async Task<List<ConnectedAccountDto>> GetConnectedAccounts()
        {
            return await _db.CloudTokens
                .Where(p => p.ProviderName == Providers.Google)
                .Select(account => new ConnectedAccountDto
                {
                    Id = account.AccountId,
                    Status = account.Status,
                    Email = string.IsNullOrEmpty(account.Email) ? null : account.Email,
                    Name = string.IsNullOrEmpty(account.AccountName) ? null : account.AccountName,
                })
                .ToListAsync();
        }

        // OAuth 2.0 Authorization using a Loopback Protocol
        // 1. C# backgroud service generates OAuth url
        // 2. Spins up an HttpListener on an open local port (this is our redirect uri)
        // 3. OAuth URL is opened in user's default web browser
        // 4. After user logs in, Google redirects them back to our redirect uri via http://our.url/callback/?code=XYZ
        // 5. HttpListener catches this code, serves a success HTML page, and immediately shuts itself down to close the port
        // 6. C# exchanges authorization code from previous step for a real refresh_token via Google's token API, encryptes it using the Windows Data Protection API (DPAPI) and saves it to SQLite, and returns a success response to React
        public async Task<AuthResult> StartOAuthAsync()
        {
            // Load oAuth credentials from configuration
            string clientId = _configuration["GoogleOAuth:ClientId"]
                ?? throw new InvalidOperationException("Google Client ID not configured.");
            string clientSecret = _configuration["GoogleOAuth:ClientSecret"]
                ?? throw new InvalidOperationException("Google Client Secret not configured.");

            // Find an available port dynamically
            int port = GetAvailablePort(5431);
            string redirectUri = $"http://localhost:{port}/callback";

            // PKCE binds the authorization code to this authorization request.
            string codeVerifier = CreateCodeVerifier();
            string codeChallenge = CreateCodeChallenge(codeVerifier);
            string state = CreateCodeVerifier();

            // Build authorization URL requesting offline access (gives us a refresh token)
            string scope = Uri.EscapeDataString(
                "https://www.googleapis.com/auth/drive.file " +
                "https://www.googleapis.com/auth/drive.activity " +
                "https://www.googleapis.com/auth/userinfo.email " +
                "https://www.googleapis.com/auth/userinfo.profile " +
                "openid");
            string authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                $"client_id={clientId}&" +
                $"redirect_uri={redirectUri}&" +
                $"response_type=code&" +
                $"scope={scope}&" +
                $"code_challenge={codeChallenge}&" +
                $"code_challenge_method=S256&" +
                $"state={state}&" +
                $"access_type=offline&" +
                $"prompt=consent"; // force consent to guarantee refresh_token is returned

            // DEV: authUrl reference: https://developers.google.com/identity/protocols/oauth2/native-app
            // DEV: good examples on the web server page https://developers.google.com/identity/protocols/oauth2/web-server
            // DEV: scopes https://developers.google.com/identity/protocols/oauth2/scopes

            // Start an ephermeral HTTP listener for this request
            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri + "/");

            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = $"Failed to start HTTP listener on {redirectUri}. Port may be in use. Error: {ex.Message}"
                };
            }

            // Open oauth url in the user's default OS web browser (Escaping WebView2)
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            try
            {

                // DEV;TODO: refresh token already exists
                // --> DEV;TODO: is it valid? (expiration)

                // Wait for Google to redirect back to us - code pauses here until user logs in
                // times out after 120s if user cancels
                var listenerTask = listener.GetContextAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(120));

                var completedTask = await Task.WhenAny(listenerTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    return new AuthResult
                    {
                        Success = false,
                        Message = "Authentication timed out. Please try again."
                    };
                }

                var context = await listenerTask;

                // Extract authorization code
                string? code = context.Request.QueryString["code"];
                string? error = context.Request.QueryString["error"];
                string? returnedState = context.Request.QueryString["state"];

                if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
                {
                    byte[] errBytes = System.Text.Encoding.UTF8.GetBytes("<html><body><h2>Authentication Cancelled or Failed</h2></body></html>");
                    context.Response.OutputStream.Write(errBytes, 0, errBytes.Length);
                    context.Response.Close();
                    return new AuthResult
                    {
                        Success = false,
                        Message = $"Google OAuth error: {error ?? "No code returned"}"
                    };
                }

                if (string.IsNullOrEmpty(returnedState) ||
                    !CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.UTF8.GetBytes(state),
                        System.Text.Encoding.UTF8.GetBytes(returnedState)))
                {
                    byte[] errBytes = System.Text.Encoding.UTF8.GetBytes("<html><body><h2>Authentication response rejected</h2></body></html>");
                    context.Response.OutputStream.Write(errBytes, 0, errBytes.Length);
                    context.Response.Close();
                    return new AuthResult
                    {
                        Success = false,
                        Message = "OAuth state validation failed."
                    };
                }

                // Send a friendly "Success" page to the user's browser
                string responseHtml = "<html><body><h1>Authentication successful! You may close this tab.</h1></body><script>window.close();</script></html>";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseHtml);
                context.Response.ContentType = "text/html";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();

                // REAL TOKEN EXCHANGE: Post the code to Google's Token Endpoint
                // Exchange 'code' for a Refresh Token, using Google.Apis SDK (long-lived credential used to obtain new short-lived access tokens without re-authenticating)

                // 1. Code --> Refresh Token Exchange
                var tokenRequestParams = new Dictionary<string, string>
                {
                    { "client_id", clientId },
                    { "client_secret", clientSecret  },
                    { "code", code },
                    { "code_verifier", codeVerifier },
                    { "grant_type", "authorization_code" },
                    { "redirect_uri", redirectUri },
                };
                var tokenRequestContent = new FormUrlEncodedContent(tokenRequestParams); // form-urlencoded data
                // DEV: for endpoints that expect JSON data, await httpClient.PostAsJsonAsync(baseUrl + endpoint, jsonData);
                var tokenResponse = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", tokenRequestContent);

                string jsonContent = await tokenResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonContent);

                // 2. Unsuccessful token acquisition
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    return new AuthResult
                    {
                        Success = false,
                        Message = $"Token exchange failed: {jsonContent}"
                    };
                }

                // 3. Extract tokens from Google's response
                string? refreshToken = null;
                if (doc.RootElement.TryGetProperty("refresh_token", out var rt))
                {
                    refreshToken = rt.GetString();
                }
                string accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
                int expiresIn = 3600; // default to 1 hour if not provided
                if (doc.RootElement.TryGetProperty("expires_in", out var ex))
                {
                    expiresIn = ex.GetInt32();
                }

                // 3.5 Refresh token is missing
                if (string.IsNullOrEmpty(refreshToken))
                {
                    return new AuthResult
                    {
                        Success = false,
                        Message = "No refresh token returned."
                    };
                }

                // 4.5 ENCRYPT real refresh token using Windows DPAPI + store securely
                var protector = _dataProtector.CreateProtector("GoogleTokenProtector");
                string encryptedRefreshToken = protector.Protect(refreshToken);

                // 5. Parse id_token from response
                if (doc.RootElement.TryGetProperty("id_token", out var idTokenElement))
                {
                    string idToken = idTokenElement.GetString()!;
                    // decode JWT to extract user's sub claim
                    var parts = idToken.Split('.');
                    var decodedPayload = parts[1];
                    decodedPayload += new string('=', (4 - decodedPayload.Length % 4) % 4); // Pad base64 string
                    var json = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(decodedPayload));

                    using var payloadDoc = JsonDocument.Parse(json);
                    string accountId = payloadDoc.RootElement.GetProperty("sub").GetString()!;
                    string? email = null;
                    if (payloadDoc.RootElement.TryGetProperty("email", out var em))
                    {
                        email = em.GetString();
                    }
                    string? name = null;
                    if (payloadDoc.RootElement.TryGetProperty("name", out var nm))
                    {
                        name = nm.GetString();
                    }

                    // 6. Persist to SQLite and update live memory state
                    await _tokenManager.SaveRefreshTokenAsync(Providers.Google, encryptedRefreshToken, accountId, email, name);

                    return new AuthResult
                    {
                        Success = true,
                        Message = "Google Drive OAuth connection success."
                    };
                }
                // 6.5 id_token is missing
                else
                {
                    return new AuthResult
                    {
                        Success = false,
                        Message = "No id_token returned."
                    };
                }

            }
            catch (Exception ex)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Authentication failed: " + ex.Message
                };
            }
            finally
            {
                listener.Stop();
            }
        }

        private int GetAvailablePort(int preferredPort)
        {
            // try preferred port first
            if (IsPortAvailable(preferredPort))
            {
                return preferredPort;
            }
            // find any available port
            using (var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp))
            {
                socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)); // Bind to any available port
                return ((System.Net.IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        private static string CreateCodeVerifier()
        {
            return Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        }

        private static string CreateCodeChallenge(string codeVerifier)
        {
            byte[] verifierBytes = System.Text.Encoding.ASCII.GetBytes(codeVerifier);
            return Base64UrlEncode(SHA256.HashData(verifierBytes));
        }

        private static string Base64UrlEncode(byte[] value)
        {
            return Convert.ToBase64String(value)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        // checks whether a specific TCP port is available for use on the local machine
        private bool IsPortAvailable(int port)
        {
            try
            {
                using (var socket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork, // Specifies that the socket uses IPv4
                    System.Net.Sockets.SocketType.Stream, // Specifies that the socket is a stream socket
                    System.Net.Sockets.ProtocolType.Tcp)) // Explicitly sets the protocol to TCP
                {
                    socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<AuthResult> RevokeOAuthAsync(string accountId)
        {

            // 1. Get refresh token from SQLite + decrypt it using Windows DPAPI
            CloudToken token = await _tokenManager.GetRefreshTokenAsync(Providers.Google, accountId);
            string unencryptedToken = string.Empty;
            if (token is not null)
            {
                unencryptedToken = _dataProtector.CreateProtector("GoogleTokenProtector").Unprotect(token.EncryptedRefreshToken);
            }
            else
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "No refresh token found."
                };
            }

            // 2. POST to Google revocation endpoint 
            var tokenRequestParams = new Dictionary<string, string>
                {
                    { "token",  unencryptedToken},
                };
            var tokenRequestContent = new FormUrlEncodedContent(tokenRequestParams); // form-urlencoded data

            var tokenResponse = await _httpClient.PostAsync("https://oauth2.googleapis.com/revoke", tokenRequestContent);

            if (tokenResponse.IsSuccessStatusCode)
            {
                // 3. Delete CloudToken
                await _tokenManager.DeleteRefreshTokenAsync(Providers.Google, accountId);
                return new AuthResult
                {
                    Success = true,
                    Message = "Google Drive OAuth connection revoked."
                };
            }
            // 3.5 Handle errors (e.g., 400 Bad Request) safely by checking for content
            else if (tokenResponse.Content.Headers.ContentLength > 0)
            {
                string jsonContent = await tokenResponse.Content.ReadAsStringAsync();

                try
                {
                    using var doc = JsonDocument.Parse(jsonContent);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        string error = errorElement.GetString();
                        string description = root.TryGetProperty("error_description", out var descElement)
                            ? descElement.GetString()
                            : "No description provided.";

                        return new AuthResult
                        {
                            Success = false,
                            Message = $"Failed to revoke token: {error} - {description}"
                        };
                    }

                    return new AuthResult
                    {
                        Success = false,
                        Message = $"Failed to revoke token: {jsonContent}"
                    };
                }
                catch (JsonException)
                {
                    return new AuthResult
                    {
                        Success = false,
                        Message = "Failed to revoke token: parse error response JSON."
                    };
                }
            }
            else
            {
                return new AuthResult
                {
                    Success = false,
                    Message = $"Failed to revoke token: no response body with status code: {tokenResponse.StatusCode}."
                };
            }

        }
    }
}
