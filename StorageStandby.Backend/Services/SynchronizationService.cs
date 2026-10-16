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

        // main logic method with sync cancellation logic
        public async Task ExecuteSyncQueue(
            CancellationToken cancellationToken = default,
            long? watchedFolderId = null,
            Providers? provider = null,
            string? accountId = null,
            long? syncEventId = null)
        {
            using var executionLease = await _state.AcquireSyncExecutionAsync(cancellationToken);
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
                lock (_activeSyncLock)
                {
                    if (ReferenceEquals(_activeSyncCancellation, activeCancellation))
                    {
                        _activeSyncCancellation = null;
                    }
                }
            }
        }

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

        private async Task ExecuteSyncQueueCore(
            CancellationToken cancellationToken,
            long? watchedFolderId = null,
            Providers? provider = null,
            string? accountId = null,
            long? syncEventId = null)
        {
            // 1. Build list of altered WatchedFolders

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var alteredFoldersIds = await db.PendingSyncQueue
                .Select(i => i.WatchedFolderId)
                .Union(db.WatchedFolderUserCloudAssignments
                    .Select(cloud => cloud.WatchedFolderId))
                .Union(db.WatchedFolderCloudMetadata
                    .Where(cloud => cloud.State == CloudAssignmentState.PendingBootstrap
                        || cloud.State == CloudAssignmentState.Failed)
                    .Select(cloud => cloud.WatchedFolderId))
                .Distinct()
                .ToListAsync(cancellationToken);

            if (watchedFolderId.HasValue)
            {
                alteredFoldersIds = alteredFoldersIds
                    .Where(id => id == watchedFolderId.Value)
                    .ToList();
            }

            if (syncEventId.HasValue)
            {
                var eventFolderId = await db.SyncEvents
                    .Where(item => item.Id == syncEventId.Value)
                    .Select(item => (long?)item.WatchedFolderId)
                    .SingleOrDefaultAsync(cancellationToken);
                if (eventFolderId.HasValue && !alteredFoldersIds.Contains(eventFolderId.Value))
                {
                    alteredFoldersIds.Add(eventFolderId.Value);
                }
            }

            // 2. Iterate through them: choose a provider and an account, perform upload
            // |--> 1 SyncEvent per folder-provider-account combo
            // |--> SyncEvent created with: WatchedFolderId, Provider, AccountId

            foreach (long id in alteredFoldersIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var watchedFolder = await db.WatchedFolders
                    .Include(folder => folder.AssignedClouds)
                    .Include(folder => folder.UserAssignedClouds)
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

                    if (provider.HasValue && destination.Provider != provider.Value
                        || accountId is not null
                        && !string.Equals(destination.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var metadata = watchedFolder.AssignedClouds.SingleOrDefault(cloud =>
                        cloud.Provider == destination.Provider
                        && string.Equals(cloud.AccountId, destination.AccountId, StringComparison.OrdinalIgnoreCase));
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

                    syncEvent ??= new SyncEvent
                    {
                        WatchedFolderId = id,
                        Provider = destination.Provider,
                        AccountId = destination.AccountId,
                        ScheduledTimestamp = DateTime.UtcNow,
                        CompletionType = SyncEventType.Upcoming
                    };
                    if (syncEvent.Id == 0)
                    {
                        db.SyncEvents.Add(syncEvent);
                    }
                    await db.SaveChangesAsync(cancellationToken);

                    try
                    {
                        await EnsureDestinationHasCapacityAsync(
                            destination,
                            destinationQueue,
                            cancellationToken);
                        await ReconcileRemoteFolderAsync(
                            db,
                            watchedFolder,
                            destination,
                            cancellationToken);

                        SyncResult result = destination.Provider switch
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
                            // _ => catch-all
                            _ => throw new NotSupportedException(
                                $"Queue execution is not implemented for {destination.Provider}.")
                        };

                        syncEvent.CompletionType = result.Status;
                        syncEvent.CompletedTimestamp = DateTime.UtcNow;
                        if (result.Status == SyncEventType.Succeeded)
                        {
                            metadata.State = CloudAssignmentState.Active;
                            var completedAssignments = watchedFolder.UserAssignedClouds.Where(item =>
                                item.WatchedFolderId == id
                                && item.Provider == destination.Provider
                                && string.Equals(
                                    item.AccountId,
                                    destination.AccountId,
                                    StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            db.WatchedFolderUserCloudAssignments.RemoveRange(completedAssignments);
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
                    watchedFolder.NextSyncAt = GetNextSyncAt(
                        DateTime.UtcNow,
                        watchedFolder.SyncFrequency);
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

        public async Task<List<ProviderAccountCloud>> DetermineSyncDestinations(
            WatchedFolder folder,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(folder);
            await Task.CompletedTask;
            return folder.AssignedClouds
                .Where(cloud => cloud.State != CloudAssignmentState.Removing
                    && cloud.State != CloudAssignmentState.Unlinked
                    && !string.IsNullOrWhiteSpace(cloud.AccountId))
                .Select(cloud => new ProviderAccountCloud
                {
                    Provider = cloud.Provider,
                    AccountId = cloud.AccountId
                })
                .Concat(folder.UserAssignedClouds
                    .Where(cloud => !string.IsNullOrWhiteSpace(cloud.AccountId))
                    .Select(cloud => new ProviderAccountCloud
                    {
                        Provider = cloud.Provider,
                        AccountId = cloud.AccountId
                    }))
                .GroupBy(cloud => (cloud.Provider, AccountId: cloud.AccountId.ToUpperInvariant()))
                .Select(group => group.First())
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
                .Where(item => !_fileSystem.IsFileIgnored(folder.IgnoreRules, item.Path))
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
                .Where(item => !_fileSystem.IsFileIgnored(folder.IgnoreRules, item.Path))
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

        public async Task ExecuteDueAutomaticSyncs(
            CancellationToken cancellationToken = default)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.SystemSettings.SingleOrDefaultAsync(cancellationToken);
            if (settings is null
                || !settings.AutomaticSyncs
                || settings.IsSyncPaused
                && (!settings.PauseSyncUntil.HasValue || settings.PauseSyncUntil > DateTime.UtcNow))
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
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
            foreach (var folder in foldersToRun)
            {
                await ExecuteSyncQueue(cancellationToken, folder.Id);
            }
        }

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

        private static DateTime GetNextWeeklyOccurrence(
            DateTime fromUtc,
            SyncFrequency frequency,
            int multiplier)
        {
            var identifiers = frequency.Identifiers
                .Where(day => day >= 0 && day <= 6)
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
                if (identifiers.Contains((int)candidate.DayOfWeek))
                {
                    return candidate;
                }
            }

            return fromUtc.AddDays(7 * multiplier);
        }

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

        // Remove a pending request or an established assignment.
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

            var requestedAssignment = await db.WatchedFolderUserCloudAssignments
                .SingleOrDefaultAsync(item =>
                    item.WatchedFolderId == folder.Id
                    && item.Provider == provider
                    && item.AccountId == accountId,
                    cancellationToken);
            if (requestedAssignment is not null)
            {
                db.WatchedFolderUserCloudAssignments.Remove(requestedAssignment);
            }

            if (cloudMetadataToRemove is null)
            {
                await db.SaveChangesAsync(cancellationToken);
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

        // Add a durable user request. The assignment is promoted after bootstrap succeeds.
        public async Task AddToUserAssignedCloud(
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
                .Include(watchedFolder => watchedFolder.UserAssignedClouds)
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
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}