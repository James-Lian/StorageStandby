using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Internal;
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

        // TODO:
        // run on App startup, e.g. on frontend load? Setup function in API?
        public async Task ReconcileSyncQueueAsync()
        {
            
        }

        public async Task ExecuteSyncQueue(CancellationToken cancellationToken = default)
        {
            // 1. Build list of altered WatchedFolders

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var alteredFoldersIds = await db.PendingSyncQueue
                .Select(i => i.WatchedFolderId) // project only WatchedFolderId property
                .Distinct() // Ensure uniqueness
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

                bool allDestinationsSucceeded = true;
                foreach (var destination in destinations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

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
                                queue,
                                destination.AccountId,
                                cancellationToken),
                            // _ => catch-all
                            _ => throw new NotSupportedException(
                                $"Queue execution is not implemented for {destination.Provider}.")
                        };

                        allDestinationsSucceeded &= result.Status == SyncEventType.Succeeded;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        allDestinationsSucceeded = false;
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

                if (allDestinationsSucceeded)
                {
                    db.PendingSyncQueue.RemoveRange(queue);
                    watchedFolder.LastSync = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

        }

        private async Task ReconcileRemoteFolderAsync(
            AppDbContext db,
            WatchedFolder watchedFolder,
            ProviderAccountCloud destination,
            CancellationToken cancellationToken)
        {
            if (destination.Provider != Providers.Google)
            {
                return;
            }

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
            var provider = scope.ServiceProvider.GetRequiredService<GoogleDriveProvider>();

            if (!string.IsNullOrWhiteSpace(cloud.RemoteFolderId)
                && await provider.RemoteFolderExistsAsync(
                    destination.AccountId,
                    cloud.RemoteFolderId,
                    cancellationToken))
            {
                return;
            }

            string folderName = Path.GetFileName(
                watchedFolder.LocalPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            cloud.RemoteFolderId = await provider.CreateRemoteRootFolderAsync(
                destination.AccountId,
                folderName,
                cancellationToken);
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

        // 1. Check the AssignedClouds for previous syncs and add them to the list
        // 2. Check the preferred provider (if applicable; if not added to assigned clouds, add)
        // 3. Check global preferred provider (if applicable)
        // 4. Else (or if not enough space in any of the other clouds), find the cloud with the least amount of space
        public async Task<List<ProviderAccountCloud>> DetermineSyncDestinations(
            WatchedFolder folder,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(folder);

            List<ProviderAccountCloud> destinations = [];
            if (folder.AssignedClouds.Count == 0)
            {
                return destinations;
            }

            long requiredBytes = _fileSystem.GetWatchedFolderSize(folder, cancellationToken);
            var systemSettings = await ReadSystemSettingsAsync(cancellationToken);

            var availableClouds = folder.AssignedClouds
                .Select(cloud => new ProviderAccountCloud
                {
                    Provider = cloud.Provider,
                    AccountId = cloud.AccountId
                })
                .Where(cloud => !string.IsNullOrWhiteSpace(cloud.AccountId))
                .DistinctBy(cloud => (cloud.Provider, cloud.AccountId))
                .ToList();

            var preferredProviders = new[]
            {
                folder.PreferredProvider,
                systemSettings?.GlobalPreferredProvider
            }
                .Where(provider => provider.HasValue)
                .Select(provider => provider!.Value)
                .Distinct()
                .ToList();

            availableClouds.AddRange(await ReadConnectedCloudsAsync(
                preferredProviders,
                cancellationToken));
            availableClouds = availableClouds
                .DistinctBy(cloud => (cloud.Provider, cloud.AccountId))
                .ToList();

            var quotaByCloud = new Dictionary<(Providers Provider, string AccountId), long>();
            foreach (var cloud in availableClouds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    quotaByCloud[(cloud.Provider, cloud.AccountId)] =
                        await GetRemainingStorageQuotaAsync(cloud, cancellationToken);
                }
                catch (NotSupportedException)
                {
                    // Providers without a quota implementation are not eligible for automatic selection.
                }
            }

            bool HasCapacity(ProviderAccountCloud cloud) =>
                quotaByCloud.TryGetValue((cloud.Provider, cloud.AccountId), out long remaining)
                && remaining >= requiredBytes;

            void AddIfEligible(Providers? provider)
            {
                if (!provider.HasValue) return;

                foreach (var cloud in availableClouds.Where(cloud => cloud.Provider == provider.Value))
                {
                    if (HasCapacity(cloud) && !destinations.Any(destination =>
                            destination.Provider == cloud.Provider && destination.AccountId == cloud.AccountId))
                    {
                        destinations.Add(cloud);
                    }
                }
            }

            AddIfEligible(folder.PreferredProvider);
            AddIfEligible(systemSettings?.GlobalPreferredProvider);

            foreach (var cloud in availableClouds
                .Where(HasCapacity)
                .OrderBy(cloud => quotaByCloud[(cloud.Provider, cloud.AccountId)]))
            {
                if (!destinations.Any(destination =>
                        destination.Provider == cloud.Provider && destination.AccountId == cloud.AccountId))
                {
                    destinations.Add(cloud);
                }
            }

            return destinations;
        }

        private async Task<SystemSettings?> ReadSystemSettingsAsync(CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.SystemSettings.SingleOrDefaultAsync(cancellationToken);
        }

        private async Task<List<ProviderAccountCloud>> ReadConnectedCloudsAsync(
            IReadOnlyCollection<Providers> providers,
            CancellationToken cancellationToken)
        {
            if (providers.Count == 0)
            {
                return [];
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.CloudTokens
                .Where(token => providers.Contains(token.ProviderName)
                    && token.Status == ConnectionStatus.Connected)
                .Select(token => new ProviderAccountCloud
                {
                    Provider = token.ProviderName,
                    AccountId = token.AccountId
                })
                .ToListAsync(cancellationToken);
        }

        private Task<StorageQuotaDto> GetRemainingStorageQuotaAsync(
            ProviderAccountCloud cloud,
            CancellationToken cancellationToken)
        {
            return cloud.Provider switch
            {
                Providers.Google => GetGoogleRemainingStorageAsync(cloud.AccountId, cancellationToken),
                Providers.Microsoft => GetOneDriveRemainingStorageAsync(cloud.AccountId),
                _ => throw new NotSupportedException($"Storage quota is not implemented for {cloud.Provider}.")
            };
        }

        private async Task<StorageQuotaDto> GetGoogleRemainingStorageAsync(
            string accountId,
            CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var provider = scope.ServiceProvider.GetRequiredService<GoogleDriveProvider>();
            return await provider.GetRemainingStorageQuotaAsync(accountId, cancellationToken);
        }

        private async Task<long> GetOneDriveRemainingStorageAsync(string accountId)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var provider = scope.ServiceProvider.GetRequiredService<OneDriveProvider>();
            var quota = await provider.GetRemainingStorageQuotaAsync(accountId);
            return Math.Max(0, quota.RemainingBytes);
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

            if (provider == Providers.None)
            {
                throw new ArgumentException("A real cloud provider is required.", nameof(provider));
            }
            if (provider != Providers.Google)
            {
                throw new NotSupportedException(
                    $"Removing an assigned cloud is not implemented for {provider}.");
            }

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
                var providerService = scope.ServiceProvider.GetRequiredService<GoogleDriveProvider>();
                await providerService.DeleteRemoteRootFolderAsync(
                    accountId,
                    cloudMetadataToRemove.RemoteFolderId,
                    cancellationToken);
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

            if (provider == Providers.None)
            {
                throw new ArgumentException("A real cloud provider is required.", nameof(provider));
            }

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
                RemoteFolderId = string.Empty,
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

        // 1. see which queue actions have previous dependencies - allow for user to stage certain changes
        // 2. remove useless dependencies that don't change anything
        public void QueueDependencyChecker()
        {
            throw new NotImplementedException();
        }
    }
}