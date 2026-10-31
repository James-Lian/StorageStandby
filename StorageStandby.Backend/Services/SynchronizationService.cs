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
        readonly BackupEngineState _state;
        readonly object _activeSyncLock = new();
        CancellationTokenSource? _activeSyncCancellation;

        // Creates the service dependencies used to coordinate queue execution and cloud providers.
        public SynchronizationService(
            IServiceScopeFactory scopeFactory,
            WatchedFolderService watchedFolderService,
            LocalFileSystemService fileSystem,
            TokenManager tokenManager,
            BackupEngineState state)
        {
            _scopeFactory = scopeFactory;
            _watchedFolderService = watchedFolderService;
            _fileSystem = fileSystem;
            _tokenManager = tokenManager;
            _state = state;
        }

        // ----------------------------------------------------------------------------------------------------
        // Sync & Queue Methods

        // Runs synchronization work while holding the single active-execution lease (cancellations occur here)
        public async Task ExecuteSyncQueue(
            CancellationToken cancellationToken = default,
            long? watchedFolderId = null,
            Providers? provider = null,
            string? accountId = null,
            long? syncEventId = null)
        {
            // thread lock
            using var executionLease = await _state.AcquireSyncExecutionAsync(cancellationToken);
            Guid jobId = _state.BeginSyncJob(
                watchedFolderId,
                provider,
                accountId,
                syncEventId);
            using var activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_activeSyncLock)
            {
                _activeSyncCancellation = activeCancellation;
            }

            try
            {
                await ExecuteSyncQueueCore(
                    activeCancellation.Token,
                    watchedFolderId,
                    provider,
                    accountId,
                    syncEventId);
            }
            catch (OperationCanceledException) when (
                activeCancellation.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                // A user-paused run has already persisted its interrupted event.
            }
            finally
            {
                _state.EndSyncJob(jobId);
                lock (_activeSyncLock)
                {
                    if (ReferenceEquals(_activeSyncCancellation, activeCancellation))
                    {
                        _activeSyncCancellation = null;
                    }
                }
            }
        }

        // Requests cancellation of the currently running provider operation, if one exists
        public bool PauseCurrentSync()
        {
            lock (_activeSyncLock)
            {
                if (_activeSyncCancellation is null)
                {
                    return false;
                }

                _activeSyncCancellation.Cancel();
                return true;
            }
        }

        // Finds altered folders, processes assignment lifecycle work, and synchronizes destinations
        private async Task ExecuteSyncQueueCore(
            CancellationToken cancellationToken,
            // identifiers for targeted syncs
            long? watchedFolderId = null,
            Providers? provider = null,
            string? accountId = null,
            long? syncEventId = null)
        {
            // 1. Build list of altered WatchedFolders

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // pplling the sync queue for folder ids who've been altered
            var alteredFoldersIds = await db.PendingSyncQueue
                .Select(i => i.WatchedFolderId) // projecting new sequence of ids
                .Union(db.WatchedFolderUserCloudAssignments
                    .Select(cloud => cloud.WatchedFolderId)) // combine with ids of folders from pending cloud assignments
                .Union(db.WatchedFolderUserCloudRemovalAssignments
                    .Select(cloud => cloud.WatchedFolderId)) // combine with ids of folders from pending removal cloud assignments
                .Union(db.WatchedFolderCloudMetadata
                    .Where(cloud => cloud.State == CloudAssignmentState.PendingBootstrap
                        || cloud.State == CloudAssignmentState.Failed
                        || cloud.State == CloudAssignmentState.Removing)
                    .Select(cloud => cloud.WatchedFolderId)) // combine with ids of folders from registered cloud assignments that are stuck in limbo
                .Distinct() // returns only unique values
                .ToListAsync(cancellationToken);

            if (watchedFolderId.HasValue)
            {
                alteredFoldersIds = alteredFoldersIds
                    .Where(id => id == watchedFolderId.Value)
                    .ToList();
                
                if (alteredFoldersIds.Count == 0)
                {
                    throw new ArgumentException($"WatchedFolderId [{watchedFolderId}] has no pending sync operations.");
                }
            }

            if (syncEventId.HasValue)
            {
                var eventFolderId = await db.SyncEvents
                    .Where(item => item.Id == syncEventId.Value)
                    .Select(item => (long?)item.WatchedFolderId)
                    .SingleOrDefaultAsync(cancellationToken);
                if (eventFolderId.HasValue)
                {
                    alteredFoldersIds = [eventFolderId.Value];
                } else
                {
                    throw new ArgumentException($"SyncEventId [{syncEventId}] does not seem to exist.");
                }
            }

            // 2. Iterate through them:
            // 2b. screen for cloud assignments changes (new additions/removals)
            // |--> removal cloud assignments are performed at the start
            // |--> new cloud assignments are bootstrapped
            // 2a. choose a provider and an account, perform upload
            // |--> 1 SyncEvent per folder-provider-account combo
            // |--> SyncEvent created with: WatchedFolderId, Provider, AccountId
            // |--> call appropriate Provider ExecuteAsync() 

            foreach (long id in alteredFoldersIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var watchedFolder = await db.WatchedFolders
                    .Include(folder => folder.AssignedClouds)
                    .Include(folder => folder.UserAssignedClouds)
                    .Include(folder => folder.UserCloudRemovalAssignments)
                    .SingleOrDefaultAsync(folder => folder.Id == id, cancellationToken);
                if (watchedFolder is null)
                {
                    continue;
                }

                Providers? effectiveProvider = provider;
                string? effectiveAccountId = accountId;
                if (syncEventId is not null)
                {
                    var syncEvent = await db.SyncEvents.SingleOrDefaultAsync(i => i.Id == syncEventId);
                    if (syncEvent is not null)
                    {
                        effectiveProvider = syncEvent.Provider;
                        effectiveAccountId = syncEvent.AccountId;
                    }
                }

                // if an item is queued for a cloud removal, remove it now and delete local data (destinations)
                // this way, we don't need to waste bandwidth updating a remote backup that is queued for deletion
                await ProcessRemovingCloudsAsync(db, watchedFolder, cancellationToken, effectiveProvider, effectiveAccountId);
                
                if (string.IsNullOrWhiteSpace(watchedFolder.LocalPath))
                {
                    continue;
                }

                // grab applicable PendingSyncItems for folderId
                var queue = await db.PendingSyncQueue
                    .Where(item => item.WatchedFolderId == id)
                    .ToListAsync(cancellationToken);
                var ignoredQueueItems = queue
                    .Where(item => _fileSystem.IsFileIgnored(
                        watchedFolder.IgnoreRules,
                        item.LocalPath,
                        watchedFolder.LocalPath))
                    .ToList();
                if (ignoredQueueItems.Count > 0)
                {
                    db.PendingSyncQueue.RemoveRange(ignoredQueueItems);
                    queue = queue.Except(ignoredQueueItems).ToList();
                    await db.SaveChangesAsync(cancellationToken);
                }
                // Returns existing remote locations and new locations awaiting bootstrap
                var destinations = await DetermineSyncDestinations(watchedFolder, cancellationToken);
                if (destinations.Count == 0)
                {
                    continue;
                }

                // contains only registered remote locations
                var activeDestinations = watchedFolder.AssignedClouds
                    .Where(cloud => cloud.State == CloudAssignmentState.Active)
                    .Select(cloud => new ProviderAccountCloud
                    {
                        Provider = cloud.Provider,
                        AccountId = cloud.AccountId
                    })
                    .DistinctBy(cloud => (cloud.Provider, cloud.AccountId))
                    .ToList();
                // at least one destination has oustanding work to upload
                bool hasDestinationWork = false;
                bool completedDestination = false;

                // for every destination
                // 1. check cancellationToken
                // 2. 
                foreach (var destination in destinations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // skip this destination if either its provider or account does not match the requested scope
                    bool providerMismatch = effectiveProvider.HasValue
                        && destination.Provider != effectiveProvider.Value;
                    bool accountMismatch = effectiveAccountId is not null
                        && !string.Equals(
                            destination.AccountId,
                            effectiveAccountId,
                            StringComparison.OrdinalIgnoreCase);
                    if (providerMismatch || accountMismatch) 
                    {
                        continue;
                    }

                    // applicable cloud metadata for destination
                    var metadata = watchedFolder.AssignedClouds.SingleOrDefault(cloud =>
                        cloud.Provider == destination.Provider
                        && string.Equals(cloud.AccountId, destination.AccountId, StringComparison.OrdinalIgnoreCase));
                    if (metadata?.State is CloudAssignmentState.Removing or CloudAssignmentState.Unlinked)
                    {
                        continue;
                    }

                    if (metadata is null)
                    {
                        metadata = new WatchedFolderCloudMetadata
                        {
                            Provider = destination.Provider,
                            AccountId = destination.AccountId,
                            WatchedFolderId = watchedFolder.Id,
                            State = CloudAssignmentState.PendingBootstrap
                        };
                        watchedFolder.AssignedClouds.Add(metadata);
                        db.WatchedFolderCloudMetadata.Add(metadata);
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    bool isBootstrap = destination.Kind == SyncDestinationKind.Bootstrap;
                    await ReconcileRemoteFolderAsync(
                        db,
                        watchedFolder,
                        destination,
                        cancellationToken);

                    // if bootstrap:
                    // |--> builds queue of PendingSyncItems with Created = true
                    // else:
                    // |--> finds the latest SyncEvent for the applicable folder/provider/account (cloud)
                    // |--> reads SyncedItemIds, and removes those IDs from the folder's pending queue
                    // |--> returns only items this specific cloud has not successfully received
                    
                    // Each cloud receives only it's own outstanding items
                    // |--> useful for if a previous SyncEvent was interrupted or unfinished
                    List<PendingSyncItem> destinationQueue = isBootstrap
                        ? BuildBootstrapQueue(watchedFolder)
                        : await GetOutstandingQueueForCloudAsync(
                            db,
                            id,
                            destination,
                            queue,
                            cancellationToken);
                    // if the bootstrapped folder is empty OR the sync for that SyncEvent is complete
                    if (destinationQueue.Count == 0)
                    {
                        if (isBootstrap && Directory.Exists(watchedFolder.LocalPath))
                        {
                            metadata.State = CloudAssignmentState.Active;
                            metadata.LastError = null;
                            var completedAssignments = watchedFolder.UserAssignedClouds.Where(item =>
                                item.WatchedFolderId == id
                                && item.Provider == destination.Provider
                                && string.Equals(
                                    item.AccountId,
                                    destination.AccountId,
                                    StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            db.WatchedFolderUserCloudAssignments.RemoveRange(completedAssignments);
                            await db.SaveChangesAsync(cancellationToken);
                        }
                        continue;
                    }

                    metadata.State = isBootstrap
                        ? CloudAssignmentState.Bootstrapping
                        : CloudAssignmentState.Active;
                    metadata.LastError = null;

                    SyncEvent? syncEvent = syncEventId.HasValue
                        ? await db.SyncEvents.SingleOrDefaultAsync(
                            item => item.Id == syncEventId.Value
                                && item.WatchedFolderId == id
                                && item.Provider == destination.Provider
                                && item.AccountId == destination.AccountId,
                            cancellationToken)
                        : null;
                    if (syncEventId.HasValue && syncEvent is null)
                    {
                        continue;
                    }

                    if (syncEvent is null) {
                        syncEvent = new SyncEvent
                        {
                            WatchedFolderId = id,
                            Provider = destination.Provider,
                            AccountId = destination.AccountId,
                            ScheduledTimestamp = DateTime.UtcNow,
                            CompletionType = SyncEventType.Upcoming
                        };
                    }
                    if (syncEvent.Id == 0)
                    {
                        db.SyncEvents.Add(syncEvent);
                    }
                    await db.SaveChangesAsync(cancellationToken);
                    hasDestinationWork = true;
                    db.TimelineEvents.Add(new TimelineEvent
                    {
                        WatchedFolderId = id,
                        SyncEventId = syncEvent.Id,
                        Type = EventType.Info,
                        Action = isBootstrap
                            ? TimelineEventAction.CloudBootstrapStarted
                            : TimelineEventAction.SyncStarted,
                        Provider = destination.Provider,
                        AccountId = destination.AccountId,
                        Message = isBootstrap
                            ? "Cloud bootstrap started."
                            : "Cloud synchronization started."
                    });

                    try
                    {
                        await EnsureDestinationHasCapacityAsync(
                            destination,
                            destinationQueue,
                            cancellationToken);

                        _state.SetUploading(true);
                        SyncResult result;
                        try
                        {
                            result = destination.Provider switch
                            {
                                Providers.Google => await ExecuteGoogleSyncAsync(
                                    syncEvent,
                                    destinationQueue,
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
                                _ => throw new NotSupportedException(
                                    $"Queue execution is not implemented for {destination.Provider}.")
                            };
                        }
                        finally
                        {
                            _state.SetUploading(false);
                        }

                        syncEvent.CompletionType = result.Status;
                        syncEvent.CompletedTimestamp = DateTime.UtcNow;
                        if (result.Status == SyncEventType.Succeeded)
                        {
                            completedDestination = true;
                            metadata.State = CloudAssignmentState.Active;
                            foreach (string path in ParsePaths(syncEvent.SyncedItems))
                            {
                                db.TimelineEvents.Add(new TimelineEvent
                                {
                                    WatchedFolderId = id,
                                    SyncEventId = syncEvent.Id,
                                    Type = EventType.Success,
                                    Action = TimelineEventAction.ItemUploaded,
                                    Provider = destination.Provider,
                                    AccountId = destination.AccountId,
                                    LocalPath = path,
                                    Message = "Item synchronized successfully."
                                });
                            }
                            db.TimelineEvents.Add(new TimelineEvent
                            {
                                WatchedFolderId = id,
                                SyncEventId = syncEvent.Id,
                                Type = EventType.Success,
                                Action = isBootstrap
                                    ? TimelineEventAction.CloudBootstrapCompleted
                                    : TimelineEventAction.SyncCompleted,
                                Provider = destination.Provider,
                                AccountId = destination.AccountId,
                                Message = isBootstrap
                                    ? "Cloud bootstrap completed."
                                    : "Cloud synchronization completed."
                            });
                            var completedAssignments = watchedFolder.UserAssignedClouds.Where(item =>
                                item.WatchedFolderId == id
                                && item.Provider == destination.Provider
                                && string.Equals(
                                    item.AccountId,
                                    destination.AccountId,
                                    StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            db.WatchedFolderUserCloudAssignments.RemoveRange(completedAssignments);
                        }
                        else
                        {
                            metadata.State = CloudAssignmentState.Failed;
                            foreach (string path in ParsePaths(syncEvent.SyncedItems))
                            {
                                db.TimelineEvents.Add(new TimelineEvent
                                {
                                    WatchedFolderId = id,
                                    SyncEventId = syncEvent.Id,
                                    Type = EventType.Success,
                                    Action = TimelineEventAction.ItemUploaded,
                                    Provider = destination.Provider,
                                    AccountId = destination.AccountId,
                                    LocalPath = path,
                                    Message = "Item synchronized before the destination reported failures."
                                });
                            }
                            foreach (var failedItem in syncEvent.FailedItems)
                            {
                                db.TimelineEvents.Add(new TimelineEvent
                                {
                                    WatchedFolderId = id,
                                    SyncEventId = syncEvent.Id,
                                    Type = EventType.Error,
                                    Action = TimelineEventAction.ItemFailed,
                                    Provider = destination.Provider,
                                    AccountId = destination.AccountId,
                                    LocalPath = failedItem.FailedItem,
                                    Message = "Item failed to synchronize.",
                                    Details = failedItem.Details
                                });
                            }
                            db.TimelineEvents.Add(new TimelineEvent
                            {
                                WatchedFolderId = id,
                                SyncEventId = syncEvent.Id,
                                Type = EventType.Warning,
                                Action = TimelineEventAction.ItemFailed,
                                Provider = destination.Provider,
                                AccountId = destination.AccountId,
                                Message = "Cloud synchronization completed with failures.",
                                Details = $"Succeeded: {result.Succeeded}; failed: {result.Failed}; unfinished: {result.Unfinished}."
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        db.TimelineEvents.Add(new TimelineEvent
                        {
                            WatchedFolderId = id,
                            SyncEventId = syncEvent.Id,
                            Type = EventType.Warning,
                            Action = TimelineEventAction.SyncInterrupted,
                            Provider = destination.Provider,
                            AccountId = destination.AccountId,
                            Message = "Cloud synchronization was interrupted."
                        });
                        await db.SaveChangesAsync(CancellationToken.None);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        metadata.State = CloudAssignmentState.Failed;
                        metadata.LastError = ex.Message;
                        syncEvent.CompletionType = SyncEventType.Unfinished;
                        syncEvent.CompletedTimestamp = DateTime.UtcNow;
                        syncEvent.FailedItems.Add(new FailedItemDetails
                        {
                            Id = syncEvent.GetNextFailedItemId(),
                            FailedItem = string.Empty,
                            Details = ex.Message
                        });
                        db.TimelineEvents.Add(new TimelineEvent
                        {
                            WatchedFolderId = id,
                            SyncEventId = syncEvent.Id,
                            Type = EventType.Error,
                            Action = TimelineEventAction.ItemFailed,
                            Provider = destination.Provider,
                            AccountId = destination.AccountId,
                            Message = "Cloud synchronization failed.",
                            Details = ex.ToString()
                        });
                    }

                    await db.SaveChangesAsync(cancellationToken);
                }

                if (hasDestinationWork && activeDestinations.Count > 0)
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

                if (completedDestination && watchedFolder.AssignedClouds.Any(cloud =>
                        cloud.State == CloudAssignmentState.Active))
                {
                    watchedFolder.LastSync = DateTime.UtcNow;
                    watchedFolder.NextSyncAt = GetNextSyncAt(
                        DateTime.UtcNow,
                        watchedFolder.SyncFrequency);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        // Builds a full-folder upload queue for a newly assigned cloud destination
        private List<PendingSyncItem> BuildBootstrapQueue(WatchedFolder folder)
        {
            if (string.IsNullOrWhiteSpace(folder.LocalPath) || !Directory.Exists(folder.LocalPath))
            {
                return [];
            }

            var items = new List<PendingSyncItem>();
            foreach (string path in Directory.EnumerateDirectories(folder.LocalPath, "*", SearchOption.AllDirectories)
                .Where(path => !_fileSystem.IsFileIgnored(folder.IgnoreRules, path, folder.LocalPath)))
            {
                items.Add(new PendingSyncItem
                {
                    LocalPath = path,
                    IsFolder = true,
                    Created = true
                });
            }

            foreach (string path in Directory.EnumerateFiles(folder.LocalPath, "*", SearchOption.AllDirectories)
                .Where(path => !_fileSystem.IsFileIgnored(folder.IgnoreRules, path, folder.LocalPath)))
            {
                items.Add(new PendingSyncItem
                {
                    LocalPath = path,
                    Created = true
                });
            }

            return items;
        }

        private static IEnumerable<string> ParsePaths(string? value)
        {
            return (value ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
        }

        // Removes items already recorded as uploaded for one destination from the incremental queue
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

        // Loads the uploaded item IDs for each active destination to determine shared completion
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

        // Parses the semicolon-delimited item ID format stored on sync events
        private static HashSet<long> ParseIds(string? value)
        {
            return (value ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => long.TryParse(id, out long parsed) ? parsed : 0)
                .Where(id => id > 0)
                .ToHashSet();
        }

        // Ensures the destination has a valid remote root folder before uploading
        private async Task ReconcileRemoteFolderAsync(
            AppDbContext db,
            WatchedFolder watchedFolder,
            ProviderAccountCloud destination,
            CancellationToken cancellationToken)
        {
            var cloud = watchedFolder.AssignedClouds.SingleOrDefault(metadata =>
                metadata.Provider == destination.Provider
                && string.Equals(
                    metadata.AccountId,
                    destination.AccountId,
                    StringComparison.OrdinalIgnoreCase));

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
                Providers.Microsoft => await scope.ServiceProvider
                    .GetRequiredService<OneDriveProvider>()
                    .RemoteFolderExistsAsync(destination.AccountId, cloud.RemoteFolderId, cancellationToken),
                Providers.Dropbox => await scope.ServiceProvider
                    .GetRequiredService<DropboxProvider>()
                    .RemoteFolderExistsAsync(destination.AccountId, cloud.RemoteFolderId, cancellationToken),
                _ => throw new NotSupportedException(
                    $"Remote folder verification is not implemented for {destination.Provider}.")
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

        // Deletes queued remote assignments and removes local metadata only after success
        private async Task ProcessRemovingCloudsAsync(
            AppDbContext db,
            WatchedFolder watchedFolder,
            CancellationToken cancellationToken,
            Providers? provider = null,
            string? accountId = null)
        {
            var pendingRemovals = watchedFolder.UserCloudRemovalAssignments
                .Where(removal => !provider.HasValue
                    || removal.Provider == provider.Value
                    && string.Equals(removal.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            await using var providerScope = _scopeFactory.CreateAsyncScope();

            foreach (var removal in pendingRemovals)
            {
                var cloud = watchedFolder.AssignedClouds.SingleOrDefault(item =>
                    item.Provider == removal.Provider
                    && string.Equals(item.AccountId, removal.AccountId, StringComparison.OrdinalIgnoreCase));
                if (cloud is null)
                {
                    db.WatchedFolderUserCloudRemovalAssignments.Remove(removal);
                    continue;
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(cloud.RemoteFolderId))
                    {
                        switch (cloud.Provider)
                        {
                            case Providers.Google:
                                await providerScope.ServiceProvider
                                    .GetRequiredService<GoogleDriveProvider>()
                                    .DeleteRemoteRootFolderAsync(cloud.AccountId, cloud.RemoteFolderId, cancellationToken);
                                break;
                            case Providers.Microsoft:
                                await providerScope.ServiceProvider
                                    .GetRequiredService<OneDriveProvider>()
                                    .DeleteRemoteRootFolderAsync(cloud.AccountId, cloud.RemoteFolderId, cancellationToken);
                                break;
                            case Providers.Dropbox:
                                await providerScope.ServiceProvider
                                    .GetRequiredService<DropboxProvider>()
                                    .DeleteRemoteRootFolderAsync(cloud.AccountId, cloud.RemoteFolderId, cancellationToken);
                                break;
                            default:
                                throw new NotSupportedException(
                                    $"Removing an assigned cloud is not implemented for {cloud.Provider}.");
                        }
                    }

                    db.WatchedFolderCloudMetadata.Remove(cloud);
                    db.WatchedFolderUserCloudRemovalAssignments.Remove(removal);
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    cloud.State = CloudAssignmentState.Removing;
                    cloud.LastError = ex.Message;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        // Executes a queued destination through the Microsoft provider
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

        // Executes a queued destination through the Google provider
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

        // Executes a queued destination through the Dropbox provider
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

        // Verifies that the destination has enough storage for the requested upload
        private async Task EnsureDestinationHasCapacityAsync(
            ProviderAccountCloud destination,
            IReadOnlyList<PendingSyncItem> queue,
            CancellationToken cancellationToken)
        {
            long requiredBytes = 0;
            foreach (var item in queue.Where(item => !item.Deleted && !item.IsFolder))
            {
                if (!File.Exists(item.LocalPath))
                {
                    continue;
                }

                requiredBytes = checked(requiredBytes + new FileInfo(item.LocalPath).Length);
            }
            if (requiredBytes == 0)
            {
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            StorageQuotaDto quota = destination.Provider switch
            {
                Providers.Google => await scope.ServiceProvider
                    .GetRequiredService<GoogleDriveProvider>()
                    .GetRemainingStorageQuotaAsync(destination.AccountId, cancellationToken),
                Providers.Microsoft => await scope.ServiceProvider
                    .GetRequiredService<OneDriveProvider>()
                    .GetRemainingStorageQuotaAsync(destination.AccountId, cancellationToken),
                Providers.Dropbox => await scope.ServiceProvider
                    .GetRequiredService<DropboxProvider>()
                    .GetRemainingStorageQuotaAsync(destination.AccountId, cancellationToken),
                _ => throw new NotSupportedException(
                    $"Storage quota is not implemented for {destination.Provider}.")
            };

            if (quota.RemainingBytes < (ulong)requiredBytes)
            {
                throw new IOException(
                    $"Cloud {destination.Provider}/{destination.AccountId} has insufficient storage. "
                    + $"Required {requiredBytes} bytes, remaining {quota.RemainingBytes} bytes.");
            }
        }

        // Returns distinct existing and bootstrap destinations configured for the folder
        public async Task<List<SyncDestination>> DetermineSyncDestinations(
            WatchedFolder folder,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(folder);
            await Task.CompletedTask;
            return folder.AssignedClouds
                .Where(cloud => cloud.State != CloudAssignmentState.Removing
                    && cloud.State != CloudAssignmentState.Unlinked
                    && !string.IsNullOrWhiteSpace(cloud.AccountId))
                .Select(cloud => new SyncDestination
                {
                    Provider = cloud.Provider,
                    AccountId = cloud.AccountId,
                    Kind = cloud.State == CloudAssignmentState.Active
                        ? SyncDestinationKind.Existing
                        : SyncDestinationKind.Bootstrap
                })
                .Concat(folder.UserAssignedClouds
                    .Where(cloud => !string.IsNullOrWhiteSpace(cloud.AccountId))
                    .Select(cloud => new SyncDestination
                    {
                        Provider = cloud.Provider,
                        AccountId = cloud.AccountId,
                        Kind = SyncDestinationKind.Bootstrap
                    }))
                .GroupBy(cloud => (cloud.Provider, AccountId: cloud.AccountId.ToUpperInvariant()))
                .Select(group => group
                    .OrderByDescending(cloud => cloud.Kind)
                    .First())
                .ToList();
        }

        // local-to-cloud force-queue operation (scans selected local folder/file and creates PendingSyncItem records so the normal synchronization pipeline will upload them again)
        // Note: does NOT immediately synchronize anything itself
        // 1. Load WatchedFolder
        // 2. Choose scan root (if localPath is omitted, use folder path)
            // Root chosen can be a subdirectory of WatchedFolder
        // 3. Enumerate local paths (all nested files and directories, or a single file)
        // 4. Remove ignored paths + update UntrackedPaths
        // 5. Load existing active queue items
        // 6. Update or create queue entries for existing files
        // Rebuilds pending file work for a folder or a selected subtree without uploading immediately
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
            string watchedRoot = folder.LocalPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (!string.Equals(root, watchedRoot, StringComparison.OrdinalIgnoreCase)
                && !root.StartsWith(
                    watchedRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
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

            // if request path is neither directory nor file, an empty path list is produced
            if (paths.Count == 0)
            {
                throw new FileNotFoundException($"{root} is not found");
            }

            paths = paths
                .Where(item => !_fileSystem.IsFileIgnored(folder.IgnoreRules, item.Path, folder.LocalPath))
                .ToList();
            var pathValues = paths.Select(item => item.Path).ToList();
            var activeItems = await db.PendingSyncQueue
                .Where(item => item.WatchedFolderId == watchedFolderId
                    && pathValues.Contains(item.LocalPath)
                    && !item.Deleted)
                .ToListAsync(cancellationToken);
            var activeByPath = activeItems.ToDictionary(
                item => item.LocalPath,
                StringComparer.OrdinalIgnoreCase);

            // update untracked paths
            var untrackedRecords = await db.UntrackedPaths
                .Where(item =>
                    item.WatchedFolderId == watchedFolderId
                    && pathValues.Contains(item.LocalPath)
                    && item.Status == UntrackedPathStatus.Untracked)
                .ToListAsync(cancellationToken);
            var untrackedByPath = untrackedRecords.ToDictionary(
                item => item.LocalPath,
                StringComparer.OrdinalIgnoreCase);
            DateTime now = DateTime.UtcNow;

            foreach (var (path, isFolder) in paths)
            {
                if (untrackedByPath.TryGetValue(path, out var untrackedRecord))
                {
                    untrackedRecord.Status = UntrackedPathStatus.Resolved;
                    untrackedRecord.LastSeen = now;
                }

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

        // Reconciles filesystem contents with the records tracked as untracked paths
        public async Task<List<UntrackedPath>> ReconcileUntrackedPathsAsync(
            long watchedFolderId,
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

            var paths = Directory.EnumerateDirectories(folder.LocalPath, "*", SearchOption.AllDirectories)
                .Select(path => (Path: path, IsFolder: true))
                .Concat(Directory.EnumerateFiles(folder.LocalPath, "*", SearchOption.AllDirectories)
                    .Select(path => (Path: path, IsFolder: false)))
                .Where(item => !_fileSystem.IsFileIgnored(folder.IgnoreRules, item.Path, folder.LocalPath))
                .Where(item => !_watchedFolderService.IsPathOwnedByAnotherWatchedFolder(
                    watchedFolderId,
                    item.Path))
                .ToList();
            var currentPaths = paths.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var queuedPaths = await db.PendingSyncQueue
                .Where(item => item.WatchedFolderId == watchedFolderId && !item.Deleted)
                .Select(item => item.LocalPath)
                .ToListAsync(cancellationToken);
            var queued = queuedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var records = await db.UntrackedPaths
                .Where(item => item.WatchedFolderId == watchedFolderId)
                .ToListAsync(cancellationToken);
            var byPath = records.ToDictionary(item => item.LocalPath, StringComparer.OrdinalIgnoreCase);
            DateTime now = DateTime.UtcNow;

            foreach (var (path, isFolder) in paths)
            {
                if (queued.Contains(path))
                {
                    if (byPath.TryGetValue(path, out var queuedRecord))
                    {
                        queuedRecord.Status = UntrackedPathStatus.Resolved;
                        queuedRecord.LastSeen = now;
                    }
                    continue;
                }

                if (byPath.TryGetValue(path, out var record))
                {
                    record.IsFolder = isFolder;
                    record.LastSeen = now;
                    record.Status = UntrackedPathStatus.Untracked;
                }
                else
                {
                    db.UntrackedPaths.Add(new UntrackedPath
                    {
                        WatchedFolderId = watchedFolderId,
                        LocalPath = path,
                        IsFolder = isFolder,
                        FirstSeen = now,
                        LastSeen = now,
                        Status = UntrackedPathStatus.Untracked
                    });
                }
            }

            foreach (var record in records.Where(record =>
                !currentPaths.Contains(record.LocalPath)
                && record.Status == UntrackedPathStatus.Untracked))
            {
                record.Status = UntrackedPathStatus.Resolved;
                record.LastSeen = now;
            }

            await db.SaveChangesAsync(cancellationToken);
            return await db.UntrackedPaths
                .Where(item => item.WatchedFolderId == watchedFolderId
                    && item.Status == UntrackedPathStatus.Untracked)
                .OrderBy(item => item.LocalPath)
                .ToListAsync(cancellationToken);
        }

        // Advances due folder schedules and runs normal work plus queued cloud removals
        public async Task ExecuteDueAutomaticSyncs(
            CancellationToken cancellationToken = default)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.SystemSettings.SingleOrDefaultAsync(cancellationToken);
            if (settings is null
                || !settings.AutomaticSyncs
                || _state.GetSyncPause())
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            var removingFolderIds = await db.WatchedFolderCloudMetadata
                .Where(cloud => cloud.State == CloudAssignmentState.Removing)
                .Select(cloud => cloud.WatchedFolderId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var dueFolders = (await db.WatchedFolders
                .Where(folder => folder.NextSyncAt == null || folder.NextSyncAt <= now)
                .ToListAsync(cancellationToken))
                .Where(folder => folder.SyncFrequency.SyncIncrements != SyncIncrements.Inactive)
                .ToList();
            var foldersToRun = new List<WatchedFolder>();

            foreach (var folder in dueFolders)
            {
                folder.NextSyncAt = GetNextSyncAt(now, folder.SyncFrequency);
                if (folder.SyncsToSkip > 0)
                {
                    folder.SyncsToSkip--;
                    continue;
                }

                foldersToRun.Add(folder);
            }

            await db.SaveChangesAsync(cancellationToken);
            var folderIdsToRun = foldersToRun
                .Select(folder => folder.Id)
                .Concat(removingFolderIds)
                .Distinct()
                .ToList();
            foreach (var folderId in folderIdsToRun)
            {
                await ExecuteSyncQueue(cancellationToken, folderId);
            }
        }

        // Marks an upcoming event skipped and records any additional folder-level skips
        public async Task SkipSyncEvent(
            long syncEventId,
            int additionalSyncsToSkip,
            CancellationToken cancellationToken = default)
        {
            if (additionalSyncsToSkip < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(additionalSyncsToSkip));
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var syncEvent = await db.SyncEvents
                .SingleOrDefaultAsync(item => item.Id == syncEventId, cancellationToken)
                ?? throw new InvalidOperationException($"Sync event does not exist: {syncEventId}");
            if (syncEvent.CompletionType is not (SyncEventType.Upcoming or SyncEventType.Paused))
            {
                throw new InvalidOperationException("Only upcoming or paused sync events can be skipped.");
            }

            syncEvent.CompletionType = SyncEventType.Skipped;
            syncEvent.CompletedTimestamp = DateTime.UtcNow;
            var folder = await db.WatchedFolders
                .SingleAsync(item => item.Id == syncEvent.WatchedFolderId, cancellationToken);
            folder.SyncsToSkip += additionalSyncsToSkip;
            folder.NextSyncAt = GetNextSyncAt(DateTime.UtcNow, folder.SyncFrequency);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Moves an upcoming event into a postponed state until its requested time
        public async Task PostponeSyncEvent(
            long syncEventId,
            DateTime scheduledTimestamp,
            CancellationToken cancellationToken = default)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var syncEvent = await db.SyncEvents
                .SingleOrDefaultAsync(item => item.Id == syncEventId, cancellationToken)
                ?? throw new InvalidOperationException($"Sync event does not exist: {syncEventId}");
            if (syncEvent.CompletionType is not (SyncEventType.Upcoming or SyncEventType.Paused))
            {
                throw new InvalidOperationException("Only upcoming or paused sync events can be postponed.");
            }

            syncEvent.ScheduledTimestamp = scheduledTimestamp.ToUniversalTime();
            syncEvent.CompletionType = SyncEventType.Paused;
            await db.SaveChangesAsync(cancellationToken);
        }

        // Makes a postponed event eligible to run immediately
        public async Task ResumeSyncEvent(
            long syncEventId,
            CancellationToken cancellationToken = default)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var syncEvent = await db.SyncEvents
                .SingleOrDefaultAsync(item => item.Id == syncEventId, cancellationToken)
                ?? throw new InvalidOperationException($"Sync event does not exist: {syncEventId}");
            if (syncEvent.CompletionType != SyncEventType.Paused)
            {
                throw new InvalidOperationException("Only paused sync events can be resumed.");
            }

            syncEvent.CompletionType = SyncEventType.Upcoming;
            syncEvent.ScheduledTimestamp = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        // Calculates the next occurrence from a folder's persisted frequency settings
        private static DateTime? GetNextSyncAt(DateTime fromUtc, SyncFrequency frequency)
        {
            if (frequency.SyncIncrements == SyncIncrements.Inactive || frequency.Multiplier < 1)
            {
                return null;
            }

            int multiplier = Math.Max(1, frequency.Multiplier);
            return frequency.SyncIncrements switch
            {
                SyncIncrements.Daily => fromUtc.AddDays(multiplier),
                SyncIncrements.Weekly => GetNextWeeklyOccurrence(fromUtc, frequency, multiplier),
                SyncIncrements.Monthly => GetNextMonthlyOccurrence(fromUtc, frequency, multiplier),
                _ => null
            };
        }

        // Calculates the next configured weekly occurrence
        private static DateTime GetNextWeeklyOccurrence(
            DateTime fromUtc,
            SyncFrequency frequency,
            int multiplier)
        {
            var identifiers = frequency.Identifiers
                .Where(day => day >= 1 && day <= 7)
                .Distinct()
                .OrderBy(day => day)
                .ToList();
            if (identifiers.Count == 0)
            {
                return fromUtc.AddDays(7 * multiplier);
            }

            for (int dayOffset = 1; dayOffset <= 7 * multiplier; dayOffset++)
            {
                var candidate = fromUtc.AddDays(dayOffset);
                int mondayBasedDay = candidate.DayOfWeek == DayOfWeek.Sunday
                    ? 7
                    : (int)candidate.DayOfWeek;
                if (identifiers.Contains(mondayBasedDay))
                {
                    return candidate;
                }
            }

            return fromUtc.AddDays(7 * multiplier);
        }

        // Calculates the next configured monthly occurrence, ignoring invalid month days
        private static DateTime GetNextMonthlyOccurrence(
            DateTime fromUtc,
            SyncFrequency frequency,
            int multiplier)
        {
            int year = fromUtc.Year;
            int month = fromUtc.Month;
            for (int monthOffset = 0; monthOffset <= 120; monthOffset++)
            {
                var candidateMonth = new DateTime(year, month, 1).AddMonths(monthOffset * multiplier);
                foreach (int day in frequency.Identifiers.Distinct().OrderBy(day => day))
                {
                    if (day < 1 || day > DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month))
                    {
                        continue;
                    }

                    var candidate = candidateMonth.AddDays(day - 1).Add(fromUtc.TimeOfDay);
                    if (candidate > fromUtc)
                    {
                        return candidate;
                    }
                }
            }

            return fromUtc.AddMonths(multiplier);
        }

        // Queues a pending assignment cancellation or an established cloud removal
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
            var persistedFolder = await db.WatchedFolders
                .Include(item => item.AssignedClouds)
                .Include(item => item.UserAssignedClouds)
                .Include(item => item.UserCloudRemovalAssignments)
                .SingleOrDefaultAsync(item => item.Id == folder.Id, cancellationToken);
            if (persistedFolder is null)
            {
                return;
            }

            var pendingAssignment = persistedFolder.UserAssignedClouds.SingleOrDefault(item =>
                item.Provider == provider
                && string.Equals(item.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
            if (pendingAssignment is not null)
            {
                persistedFolder.UserAssignedClouds.Remove(pendingAssignment);
                db.TimelineEvents.Add(new TimelineEvent
                {
                    WatchedFolderId = persistedFolder.Id,
                    Type = EventType.Info,
                    Action = TimelineEventAction.CloudRemovalRequested,
                    Provider = provider,
                    AccountId = accountId,
                    Message = "Pending cloud assignment removed."
                });
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var cloudMetadataToRemove = persistedFolder.AssignedClouds.SingleOrDefault(cloud =>
                cloud.Provider == provider
                && string.Equals(cloud.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
            if (cloudMetadataToRemove is null)
            {
                return;
            }

            var existingRemoval = persistedFolder.UserCloudRemovalAssignments.SingleOrDefault(item =>
                item.Provider == provider
                && string.Equals(item.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
            if (existingRemoval is not null)
            {
                db.WatchedFolderUserCloudRemovalAssignments.Remove(existingRemoval);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            db.WatchedFolderUserCloudRemovalAssignments.Add(new WatchedFolderUserCloudRemovalAssignment
            {
                WatchedFolderId = persistedFolder.Id,
                Provider = provider,
                AccountId = accountId
            });
            db.TimelineEvents.Add(new TimelineEvent
            {
                WatchedFolderId = persistedFolder.Id,
                Type = EventType.Info,
                Action = TimelineEventAction.CloudRemovalRequested,
                Provider = provider,
                AccountId = accountId,
                Message = "Cloud removal requested."
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        // Adds a durable user assignment request that is promoted after bootstrap succeeds
        public async Task AddToUserAssignedCloud(
            WatchedFolder folder,
            Providers provider,
            string accountId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(folder);
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var persistedFolder = await db.WatchedFolders
                .Include(watchedFolder => watchedFolder.AssignedClouds)
                .Include(watchedFolder => watchedFolder.UserAssignedClouds)
                .Include(watchedFolder => watchedFolder.UserCloudRemovalAssignments)
                .SingleOrDefaultAsync(watchedFolder => watchedFolder.Id == folder.Id, cancellationToken);

            if (persistedFolder is null)
            {
                throw new InvalidOperationException($"Watched folder does not exist: {folder.Id}");
            }

            var assignedCloud = persistedFolder.AssignedClouds.SingleOrDefault(cloud =>
                    cloud.Provider == provider
                    && string.Equals(cloud.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
            var pendingRemoval = persistedFolder.UserCloudRemovalAssignments.SingleOrDefault(item =>
                item.Provider == provider
                && string.Equals(item.AccountId, accountId, StringComparison.OrdinalIgnoreCase));

            if (assignedCloud is not null)
            {
                if (pendingRemoval is not null)
                {
                    persistedFolder.UserCloudRemovalAssignments.Remove(pendingRemoval);
                }

                if (assignedCloud.State is CloudAssignmentState.Failed
                    or CloudAssignmentState.PendingBootstrap
                    or CloudAssignmentState.Bootstrapping
                    or CloudAssignmentState.Unlinked)
                {
                    assignedCloud.State = CloudAssignmentState.PendingBootstrap;
                    assignedCloud.LastError = null;
                    if (!persistedFolder.UserAssignedClouds.Any(cloud =>
                            cloud.Provider == provider
                            && string.Equals(cloud.AccountId, accountId, StringComparison.OrdinalIgnoreCase)))
                    {
                        persistedFolder.UserAssignedClouds.Add(new WatchedFolderUserCloudAssignment
                        {
                            Provider = provider,
                            AccountId = accountId,
                            WatchedFolderId = persistedFolder.Id
                        });
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            if (pendingRemoval is not null)
            {
                db.WatchedFolderUserCloudRemovalAssignments.Remove(pendingRemoval);
            }

            if (!persistedFolder.UserAssignedClouds.Any(cloud =>
                    cloud.Provider == provider
                    && string.Equals(cloud.AccountId, accountId, StringComparison.OrdinalIgnoreCase)))
            {
                persistedFolder.UserAssignedClouds.Add(new WatchedFolderUserCloudAssignment
                {
                    Provider = provider,
                    AccountId = accountId,
                    WatchedFolderId = persistedFolder.Id
                });
                db.TimelineEvents.Add(new TimelineEvent
                {
                    WatchedFolderId = persistedFolder.Id,
                    Type = EventType.Info,
                    Action = TimelineEventAction.CloudAssignmentRequested,
                    Provider = provider,
                    AccountId = accountId,
                    Message = "Cloud assignment requested."
                });
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}