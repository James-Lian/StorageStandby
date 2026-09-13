using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace StorageStandby.Backend.Services
{
    // For now: stateless method reference
    public class WatchedFolderService
    {
        readonly IServiceScopeFactory _scopeFactory;
        readonly LocalFileSystemService _fileSystem;
        public WatchedFolderService(
            IServiceScopeFactory scopeFactory,
            LocalFileSystemService fileSystem
        )
        {
            _scopeFactory = scopeFactory;
            _fileSystem = fileSystem;
        }

        // ----------------------------------------------------------------------------------------------------
        // Local-Adjacent Methods
        // TODO: I don't like this CreateWatchedFolderResult enum and will probably replace it
        public enum CreateWatchedFolderResultStatus
        {
            Success,
            Failure,
            AlreadyExists
        }
        public class CreateWatchedFolderResultDto
        {
            public CreateWatchedFolderResultStatus Status { get; set; }
            public string? Details { get; set; }
        }
        public async Task<CreateWatchedFolderResultDto> CreateWatchedFolder(string path)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                bool exists = await db.WatchedFolders.AnyAsync(f => f.LocalPath == path);

                if (exists)
                {
                    return new CreateWatchedFolderResultDto
                    {
                        Status = CreateWatchedFolderResultStatus.AlreadyExists,
                    };
                }

                db.WatchedFolders.Add(new WatchedFolder
                {
                    LocalPath = path,
                    DateAdded = DateTime.UtcNow,
                });
            }
            catch (Exception ex)
            {
                return new CreateWatchedFolderResultDto
                {
                    Status = CreateWatchedFolderResultStatus.Failure,
                    Details = ex.Message
                };
            }

            _ = await db.SaveChangesAsync();

            return new CreateWatchedFolderResultDto
            {
                Status = CreateWatchedFolderResultStatus.Success,
            };
        }

        public async Task DeleteWatchedFolder(string path)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var folder = await db.WatchedFolders
                .SingleOrDefaultAsync(watchedFolder => watchedFolder.LocalPath == path);
            if (folder is null)
            {
                return;
            }

            var pendingItems = await db.PendingSyncQueue
                .Where(item => item.WatchedFolderId == folder.Id)
                .ToListAsync();
            var syncEvents = await db.SyncEvents
                .Where(syncEvent => syncEvent.WatchedFolderId == folder.Id)
                .ToListAsync();
            db.PendingSyncQueue.RemoveRange(pendingItems);
            db.WatchedFolders.Remove(folder);

            await db.SaveChangesAsync();
        }

        public async Task SetProblemAsync(
            long watchedFolderId,
            ProblematicFolderType problem,
            CancellationToken cancellationToken = default)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var folder = await db.WatchedFolders
                .SingleOrDefaultAsync(item => item.Id == watchedFolderId, cancellationToken);
            if (folder is null)
            {
                return;
            }

            folder.Problem = problem;
            await db.SaveChangesAsync(cancellationToken);
        }

        public long? GetWatchedFolderIdFromPath(string path)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var folder = db.WatchedFolders.FirstOrDefault(f => f.LocalPath == path);
            if (folder is not null)
            {
                return folder.Id;
            }
            return null;
        }

        public WatchedFolder? GetWatchedFolderFromPath(string path)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var folder = db.WatchedFolders.FirstOrDefault(f => f.LocalPath == path);
            if (folder is not null)
            {
                return folder;
            }
            return null;
        }

        public string? GetWatchedFolderPathFromId(long id)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var folder = db.WatchedFolders.FirstOrDefault(f => f.Id == id);
            if (folder is not null)
            {
                return folder.LocalPath;
            }
            return null;
        }

        public WatchedFolder? GetWatchedFolderFromId(long id)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var folder = db.WatchedFolders.FirstOrDefault(f => f.Id == id);
            if (folder is not null)
            {
                return folder;
            }
            return null;
        }

        // https://code.visualstudio.com/docs/editor/glob-patterns <-- Glob patterns reference
        public async Task ReconcileIgnoreRulesAsync(
            long watchedFolderId,
            string oldIgnoreRules,
            string newIgnoreRules,
            CancellationToken stoppingToken = default)
        {
            ArgumentNullException.ThrowIfNull(oldIgnoreRules);
            ArgumentNullException.ThrowIfNull(newIgnoreRules);

            WatchedFolder? folder = GetWatchedFolderFromId(watchedFolderId);
            if (folder is null || string.IsNullOrWhiteSpace(folder.LocalPath))
            {
                throw new InvalidOperationException($"Watched folder id does not exist: {watchedFolderId}");
            }

            var children = _fileSystem.GetAllNestedChildren(folder.LocalPath);
            if (children is null)
            {
                return;
            }

            var childPaths = children.Files
                .Select(path => new { Path = path, IsFolder = false })
                .Concat(children.Folders.Select(path => new { Path = path, IsFolder = true }))
                .ToList();

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var paths = childPaths.Select(child => child.Path).ToList();
            var existingItems = await db.PendingSyncQueue
                .Where(item => item.WatchedFolderId == watchedFolderId && paths.Contains(item.LocalPath))
                .ToListAsync(stoppingToken);

            var activeItems = existingItems
                .Where(item => !item.IsTerminal())
                .GroupBy(item => item.LocalPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var child in childPaths)
            {
                bool wasIgnored = _fileSystem.IsFileIgnored(oldIgnoreRules, child.Path);
                bool isIgnored = _fileSystem.IsFileIgnored(newIgnoreRules, child.Path);

                if (wasIgnored == isIgnored)
                {
                    continue;
                }

                if (activeItems.TryGetValue(child.Path, out var existingItem))
                {
                    existingItem.IsFolder = child.IsFolder;
                    if (isIgnored)
                    {
                        existingItem.Deleted = true;
                    }
                    else
                    {
                        existingItem.Created = true;
                    }

                    continue;
                }

                var pendingItem = new PendingSyncItem
                {
                    WatchedFolderId = watchedFolderId,
                    LocalPath = child.Path,
                    IsFolder = child.IsFolder,
                    Created = !isIgnored,
                    Deleted = isIgnored
                };

                db.PendingSyncQueue.Add(pendingItem);
                activeItems[child.Path] = pendingItem;
            }

            await db.SaveChangesAsync(stoppingToken);
        }
    }
}