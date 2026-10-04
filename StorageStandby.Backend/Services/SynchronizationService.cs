using Microsoft.EntityFrameworkCore;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using StorageStandby.Backend.Services;

namespace StorageStandby.Backend.Core
{
    public class SynchronizationService
    {
        readonly IServiceScopeFactory _scopeFactory;
        readonly WatchedFolderService _watchedFolderService;
        readonly LocalFileSystemService _fileSystem;
        readonly TokenManager _tokenManager;
        public SynchronizationService(
            IServiceScopeFactory scopeFactory,
            WatchedFolderService watchedFolderService,
            LocalFileSystemService fileSystem,
            TokenManager tokenManager)
        {
            _scopeFactory = scopeFactory;
            _watchedFolderService = watchedFolderService;
            _fileSystem = fileSystem;
            _tokenManager = tokenManager;
        }

        // ----------------------------------------------------------------------------------------------------
        // Sync & Queue Methods

        public async Task ExecuteSyncQueue(CancellationToken cancellationToken = default)
        {
            // 1. Build list of altered WatchedFolders

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var alteredFoldersIds = await db.PendingSyncQueue
                .Select(i => i.WatchedFolderId)
                .Union(db.WatchedFolderCloudMetadata
                    .Where(cloud => cloud.State == CloudAssignmentState.PendingBootstrap
                        || cloud.State == CloudAssignmentState.Failed)
                    .Select(cloud => cloud.WatchedFolderId))
                .Distinct()
                .ToListAsync(cancellationToken);

            // 2. Iterate through them: choose a provider and an account, perform upload
            // |--> 1 SyncEvent per folder-provider-account combo
            // |--> SyncEvent created with: WatchedFolderId, Provider, AccountId

            foreach (long id in alteredFoldersIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var watchedFolder = await db.WatchedFolders
                    .Include(folder => folder.AssignedClouds)
                    .SingleOrDefaultAsync(folder => folder.Id == id, cancellationToken);
                if (watchedFolder is null || string.IsNullOrWhiteSpace(watchedFolder.LocalPath))
                {
                    continue;
                }

                var queue = await db.PendingSyncQueue
                    .Where(item => item.WatchedFolderId == id)
                    .ToListAsync(cancellationToken);
                var destinations = await DetermineSyncDestinations(watchedFolder, cancellationToken);
                if (destinations.Count == 0)
                {
                    continue;
                }

                var activeDestinations = watchedFolder.AssignedClouds
                    .Where(cloud => cloud.State == CloudAssignmentState.Active)
                    .Select(cloud => new ProviderAccountCloud
                    {
                        Provider = cloud.Provider,
                        AccountId = cloud.AccountId
                    })
                    .DistinctBy(cloud => (cloud.Provider, cloud.AccountId))
                    .ToList();
                var completedForAllDestinations = new HashSet<long>();
                bool processedDestination = false;

                foreach (var destination in destinations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var metadata = watchedFolder.AssignedClouds.Single(cloud =>
                        cloud.Provider == destination.Provider
                        && cloud.AccountId == destination.AccountId);
                    bool isBootstrap = metadata.State != CloudAssignmentState.Active;
                    List<PendingSyncItem> destinationQueue = isBootstrap
                        ? BuildBootstrapQueue(watchedFolder)
                        : await GetOutstandingQueueForCloudAsync(
                            db,
                            id,
                            destination,
                            queue,
                            cancellationToken);
                    if (destinationQueue.Count == 0)
                    {
                        continue;
                    }

                    processedDestination = true;
                    metadata.State = isBootstrap
                        ? CloudAssignmentState.Bootstrapping
                        : CloudAssignmentState.Active;
                    metadata.LastError = null;

                    var syncEvent = new SyncEvent
                    {
                        WatchedFolderId = id,
                        Provider = destination.Provider,
                        AccountId = destination.AccountId,
                        CompletionType = SyncEventType.Upcoming
                    };
                    db.SyncEvents.Add(syncEvent);
                    await db.SaveChangesAsync(cancellationToken);

                    try
                    {
                        await ReconcileRemoteFolderAsync(
                            db,
                            watchedFolder,
                            destination,
                            cancellationToken);

                        SyncResult result = destination.Provider switch
                        {
                            Providers.Google => await ExecuteGoogleSyncAsync(
                                syncEvent,
                                queue,
                                destination.AccountId,
                                cancellationToken),
                            Providers.Microsoft => await ExecuteMicrosoftSyncAsync(
                                syncEvent,
                                destinationQueue,
                                destination.AccountId,
                                cancellationToken),
                            Providers.Dropbox => await ExecuteDropboxSyncAsync(
                                syncEvent,
                                destinationQueue,
                                destination.AccountId,
                                cancellationToken),
                            // _ => catch-all
                            _ => throw new NotSupportedException(
                                $"Queue execution is not implemented for {destination.Provider}.")
                        };

                        syncEvent.CompletionType = result.Status;
                        syncEvent.CompletedTimestamp = DateTime.UtcNow;
                        if (result.Status == SyncEventType.Succeeded)
                        {
                            metadata.State = CloudAssignmentState.Active;
                            if (!isBootstrap)
                            {
                                foreach (var item in destinationQueue)
                                {
                                    completedForAllDestinations.Add(item.Id);
                                }
                            }
                        }
                        else
                        {
                            metadata.State = CloudAssignmentState.Failed;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        metadata.State = CloudAssignmentState.Failed;
                        metadata.LastError = ex.Message;
                        syncEvent.CompletionType = SyncEventType.Unfinished;
                        syncEvent.CompletedTimestamp = DateTime.UtcNow;
                        syncEvent.FailedItems.Add(new FailedItemsDetails
                        {
                            FailedItem = string.Empty,
                            Details = ex.Message
                        });
                    }

                    await db.SaveChangesAsync(cancellationToken);
                }

                if (processedDestination && activeDestinations.Count > 0)
                {
                    var successfulClouds = await GetSuccessfulCloudsForItemsAsync(
                        db,
                        id,
                        activeDestinations,
                        queue,
                        cancellationToken);
                    var completedItemIds = queue
                        .Where(item => successfulClouds.All(cloud => cloud.Contains(item.Id)))
                        .Select(item => item.Id)
                        .ToHashSet();
                    db.PendingSyncQueue.RemoveRange(queue.Where(item => completedItemIds.Contains(item.Id)));
                }

                if (processedDestination && watchedFolder.AssignedClouds.Any(cloud =>
                        cloud.State == CloudAssignmentState.Active))
                {
                    watchedFolder.LastSync = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

        }

        private List<PendingSyncItem> BuildBootstrapQueue(WatchedFolder folder)
        {
            if (string.IsNullOrWhiteSpace(folder.LocalPath) || !Directory.Exists(folder.LocalPath))
            {
                return [];
            }

            var items = new List<PendingSyncItem>();
            foreach (string path in Directory.EnumerateDirectories(folder.LocalPath, "*", SearchOption.AllDirectories)
                .Where(path => !_fileSystem.IsFileIgnored(folder.IgnoreRules, path)))
            {
                items.Add(new PendingSyncItem
                {
                    LocalPath = path,
                    IsFolder = true,
                    Created = true
                });
            }

            foreach (string path in Directory.EnumerateFiles(folder.LocalPath, "*", SearchOption.AllDirectories)
                .Where(path => !_fileSystem.IsFileIgnored(folder.IgnoreRules, path)))
            {
                items.Add(new PendingSyncItem
                {
                    LocalPath = path,
                    Created = true
                });
            }

            return items;
        }

        private async Task<List<PendingSyncItem>> GetOutstandingQueueForCloudAsync(
            AppDbContext db,
            long watchedFolderId,
            ProviderAccountCloud destination,
            IReadOnlyList<PendingSyncItem> queue,
            CancellationToken cancellationToken)
        {
            var completedIds = await db.SyncEvents
                .Where(syncEvent => syncEvent.WatchedFolderId == watchedFolderId
                    && syncEvent.Provider == destination.Provider
                    && syncEvent.AccountId == destination.AccountId)
                .OrderByDescending(syncEvent => syncEvent.Id)
                .Select(syncEvent => syncEvent.SyncedItemIds)
                .FirstOrDefaultAsync(cancellationToken);

            var completed = ParseIds(completedIds);
            return queue.Where(item => !completed.Contains(item.Id)).ToList();
        }

        private async Task<List<HashSet<long>>> GetSuccessfulCloudsForItemsAsync(
            AppDbContext db,
            long watchedFolderId,
            IReadOnlyList<ProviderAccountCloud> destinations,
            IReadOnlyList<PendingSyncItem> queue,
            CancellationToken cancellationToken)
        {
            var results = new List<HashSet<long>>();
            foreach (var destination in destinations)
            {
                string? ids = await db.SyncEvents
                    .Where(syncEvent => syncEvent.WatchedFolderId == watchedFolderId
                        && syncEvent.Provider == destination.Provider
                        && syncEvent.AccountId == destination.AccountId)
                    .OrderByDescending(syncEvent => syncEvent.Id)
                    .Select(syncEvent => syncEvent.SyncedItemIds)
                    .FirstOrDefaultAsync(cancellationToken);
                results.Add(ParseIds(ids));
            }

            return results;
        }

        private static HashSet<long> ParseIds(string? value)
        {
            return (value ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => long.TryParse(id, out long parsed) ? parsed : 0)
                .Where(id => id > 0)
                .ToHashSet();
        }

        private async Task ReconcileRemoteFolderAsync(
            AppDbContext db,
            WatchedFolder watchedFolder,
            ProviderAccountCloud destination,
            CancellationToken cancellationToken)
        {
            var cloud = watchedFolder.AssignedClouds.SingleOrDefault(metadata =>
                metadata.Provider == destination.Provider
                && metadata.AccountId == destination.AccountId);

            if (cloud is null)
            {
                cloud = new WatchedFolderCloudMetadata
                {
                    Provider = destination.Provider,
                    AccountId = destination.AccountId,
                    WatchedFolderId = watchedFolder.Id
                };
                watchedFolder.AssignedClouds.Add(cloud);
                db.WatchedFolderCloudMetadata.Add(cloud);
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            bool exists = destination.Provider switch
            {
                Providers.Google => await scope.ServiceProvider
                    .GetRequiredService<GoogleDriveProvider>()
                    .RemoteFolderExistsAsync(destination.AccountId, cloud.RemoteFolderId, cancellationToken),
                _ => !string.IsNullOrWhiteSpace(cloud.RemoteFolderId)
            };
            if (exists)
            {
                return;
            }

            string folderName = Path.GetFileName(
                watchedFolder.LocalPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            cloud.RemoteFolderId = destination.Provider switch
            {
                Providers.Google => await scope.ServiceProvider
                    .GetRequiredService<GoogleDriveProvider>()
                    .CreateRemoteRootFolderAsync(destination.AccountId, folderName, cancellationToken),
                Providers.Microsoft => await scope.ServiceProvider
                    .GetRequiredService<OneDriveProvider>()
                    .CreateRemoteRootFolderAsync(destination.AccountId, folderName, cancellationToken),
                Providers.Dropbox => await scope.ServiceProvider
                    .GetRequiredService<DropboxProvider>()
                    .CreateRemoteRootFolderAsync(destination.AccountId, folderName, cancellationToken),
                _ => throw new NotSupportedException(
                    $"Remote root creation is not implemented for {destination.Provider}.")
            };
            cloud.State = CloudAssignmentState.Bootstrapping;
            await db.SaveChangesAsync(cancellationToken);
        }

        private async Task<SyncResult> ExecuteMicrosoftSyncAsync(
            SyncEvent syncEvent,
            IReadOnlyList<PendingSyncItem> queue,
            string accountId,
            CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var provider = scope.ServiceProvider.GetRequiredService<OneDriveProvider>();
            return await provider.ExecuteSyncQueueAsync(
                syncEvent,
                accountId,
                queue,
                cancellationToken);
        }

        private async Task<SyncResult> ExecuteGoogleSyncAsync(
            SyncEvent syncEvent,
            IReadOnlyList<PendingSyncItem> queue,
            string accountId,
            CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var provider = scope.ServiceProvider.GetRequiredService<GoogleDriveProvider>();
            return await provider.ExecuteSyncQueueAsync(
                syncEvent,
                accountId,
                queue,
                cancellationToken);
        }

        private async Task<SyncResult> ExecuteDropboxSyncAsync(
            SyncEvent syncEvent,
            IReadOnlyList<PendingSyncItem> queue,
            string accountId,
            CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var provider = scope.ServiceProvider.GetRequiredService<DropboxProvider>();
            return await provider.ExecuteSyncQueueAsync(
                syncEvent,
                accountId,
                queue,
                cancellationToken);
        }

        public async Task<List<ProviderAccountCloud>> DetermineSyncDestinations(
            WatchedFolder folder,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(folder);
            await Task.CompletedTask;
            return folder.AssignedClouds
                .Where(cloud => !string.IsNullOrWhiteSpace(cloud.AccountId))
                .Select(cloud => new ProviderAccountCloud
                {
                    Provider = cloud.Provider,
                    AccountId = cloud.AccountId
                })
                .DistinctBy(cloud => (cloud.Provider, cloud.AccountId))
                .ToList();
        }

        public async Task QueueResyncAsync(
            long watchedFolderId,
            string? localPath = null,
            CancellationToken cancellationToken = default)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var folder = await db.WatchedFolders
                .SingleOrDefaultAsync(item => item.Id == watchedFolderId, cancellationToken)
                ?? throw new InvalidOperationException($"Watched folder does not exist: {watchedFolderId}");
            if (string.IsNullOrWhiteSpace(folder.LocalPath) || !Directory.Exists(folder.LocalPath))
            {
                throw new DirectoryNotFoundException(folder.LocalPath);
            }

            string root = string.IsNullOrWhiteSpace(localPath)
                ? folder.LocalPath
                : Path.GetFullPath(localPath);
            if (!root.StartsWith(folder.LocalPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The resync path must be inside the watched folder.", nameof(localPath));
            }

            var paths = new List<(string Path, bool IsFolder)>();
            if (Directory.Exists(root))
            {
                paths.Add((root, true));
                paths.AddRange(Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                    .Select(path => (path, true)));
                paths.AddRange(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Select(path => (path, false)));
            }
            else if (File.Exists(root))
            {
                paths.Add((root, false));
            }

            paths = paths
                .Where(item => !_fileSystem.IsFileIgnored(folder.IgnoreRules, item.Path))
                .ToList();
            var pathValues = paths.Select(item => item.Path).ToList();
            var activeItems = await db.PendingSyncQueue
                .Where(item => item.WatchedFolderId == watchedFolderId
                    && pathValues.Contains(item.LocalPath)
                    && !item.IsTerminal())
                .ToListAsync(cancellationToken);
            var activeByPath = activeItems.ToDictionary(
                item => item.LocalPath,
                StringComparer.OrdinalIgnoreCase);

            foreach (var (path, isFolder) in paths)
            {
                if (activeByPath.TryGetValue(path, out var existing))
                {
                    existing.IsFolder = isFolder;
                    existing.Changed = !isFolder;
                    existing.Created = isFolder;
                    continue;
                }

                db.PendingSyncQueue.Add(new PendingSyncItem
                {
                    WatchedFolderId = watchedFolderId,
                    LocalPath = path,
                    IsFolder = isFolder,
                    Created = true
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        public async Task<TimeSpan?> GetAutomaticSyncIntervalAsync(
            CancellationToken cancellationToken = default)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.SystemSettings.SingleOrDefaultAsync(cancellationToken);
            if (settings is null || !settings.AutomaticSyncs)
            {
                return null;
            }

            return TimeSpan.FromMinutes(Math.Max(1, settings.AutomaticSyncIntervalMinutes));
        }

        // 1. Remove from AssignedClouds
        // 2. Delete the WatchedFolder folder from the remote location
        public async Task RemoveFromAssignedCloud(
            WatchedFolder folder,
            Providers provider,
            string accountId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(folder);
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cloudMetadataToRemove = await db.WatchedFolderCloudMetadata
                .SingleOrDefaultAsync(cloud =>
                    cloud.WatchedFolderId == folder.Id
                    && cloud.Provider == provider
                    && cloud.AccountId == accountId,
                    cancellationToken);

            if (cloudMetadataToRemove is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(cloudMetadataToRemove.RemoteFolderId))
            {
                switch (provider)
                {
                    case Providers.Google:
                        await scope.ServiceProvider.GetRequiredService<GoogleDriveProvider>()
                            .DeleteRemoteRootFolderAsync(accountId, cloudMetadataToRemove.RemoteFolderId, cancellationToken);
                        break;
                    case Providers.Microsoft:
                        await scope.ServiceProvider.GetRequiredService<OneDriveProvider>()
                            .DeleteRemoteRootFolderAsync(accountId, cloudMetadataToRemove.RemoteFolderId, cancellationToken);
                        break;
                    case Providers.Dropbox:
                        await scope.ServiceProvider.GetRequiredService<DropboxProvider>()
                            .DeleteRemoteRootFolderAsync(accountId, cloudMetadataToRemove.RemoteFolderId, cancellationToken);
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Removing an assigned cloud is not implemented for {provider}.");
                }
            }

            db.WatchedFolderCloudMetadata.Remove(cloudMetadataToRemove);
            await db.SaveChangesAsync(cancellationToken);

            folder.AssignedClouds.RemoveAll(cloud =>
                cloud.Provider == provider && cloud.AccountId == accountId);
        }

        // 1. Add to AssignedClouds
        public async Task AddToAssignedCloud(
            WatchedFolder folder,
            Providers provider,
            string accountId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(folder);
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

            if (folder.AssignedClouds.Any(cloud =>
                    cloud.Provider == provider
                    && string.Equals(cloud.AccountId, accountId, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var persistedFolder = await db.WatchedFolders
                .Include(watchedFolder => watchedFolder.AssignedClouds)
                .SingleOrDefaultAsync(watchedFolder => watchedFolder.Id == folder.Id, cancellationToken);

            if (persistedFolder is null)
            {
                throw new InvalidOperationException($"Watched folder does not exist: {folder.Id}");
            }

            if (persistedFolder.AssignedClouds.Any(cloud =>
                    cloud.Provider == provider
                    && string.Equals(cloud.AccountId, accountId, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var cloudMetadata = new WatchedFolderCloudMetadata
            {
                Provider = provider,
                AccountId = accountId,
                WatchedFolderId = persistedFolder.Id,
                RemoteFolderId = string.Empty, // TODO: <-- Wrong: RemoteFolderId should not be string.Empty
                ConfigFileId = string.Empty
            };

            persistedFolder.AssignedClouds.Add(cloudMetadata);
            await db.SaveChangesAsync(cancellationToken);

            folder.AssignedClouds.Add(new WatchedFolderCloudMetadata
            {
                Provider = cloudMetadata.Provider,
                AccountId = cloudMetadata.AccountId,
                RemoteFolderId = cloudMetadata.RemoteFolderId,
                ConfigFileId = cloudMetadata.ConfigFileId,
                WatchedFolderId = folder.Id,
            });
        }

        // Implement in the future: manual selection of files/directories to be re-synced

        // Implement in the future: reconcile untracked changes (e.g. a renamed file that is not tracked, the old path no longer exists, while the new path seemingly was "uncreated")
        // Note: MIGHT NOT consider untracked "changed files", just changes to the directory tree

        // Implement in the future: for user-controlled staging/unstaging changes
        // 1. see which queue actions have previous dependencies - allow for user to stage certain changes
        // 2. remove useless dependencies that don't change anything
        public void QueueDependencyChecker()
        {
            throw new NotImplementedException();
        }

        // TODO:
        public void DeleteWatchedFolder(bool unlinkOnly)
        {
            // uhh
            // TODO: figure out constraints later
            if (unlinkOnly)
            {
                
            }
            else
            {
                
            }
            // both from storage and from cloud
        }
    }
}