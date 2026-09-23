using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory; // for debouncing file-saving
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SQLitePCL;
using StorageStandby.Backend.Core;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using StorageStandby.Backend.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// The intuition is that checking a folder's last-modified date is fast ("polling"). However, on Windows, file systems don't update top-level folder dates recursively when a file inside them changes. Therefore, we cannot make our polling more efficient by choosing subdirectories through elimination (date-checking and comparing with last synced date). If we were polling, we would have to check every single file in every single watched folder, which is inefficient. Instead, we use FileSystemWatcher, which is event-driven and only triggers when a file changes. This is more efficient than polling, especially for large directories.


// TODO: Add FileSystemWatcher's to WatchedFolder parents as well just to check for renames and deletions - the root folders of FileSystemWatchers don't fire for such events

namespace StorageStandby.Backend.Workers
{
    public class FileSystemWatcherWorker : BackgroundService // DEV: BackgroundService implements the IHostedService and provides a managed loop via ExecuteAsync
    {
        private readonly ILogger<FileSystemWatcherWorker> _logger; // built-in interface used to write log messages - provides a consistent abstraction layer, meaning you can write your logging code once and easily swap out the destination
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BackupEngineState _state;
        private readonly IMemoryCache _cache; // built-in in-memory caching service
        // thread-safe, key-value dictionary stored directly in your server's RAM
        // updating/saving a file can trigger multiple events in quick succession, so we use this cache to "debounce" the events and avoid redundant uploads
        private readonly LocalFileSystemService _fileSystem;
        private readonly WatchedFolderService _watchedFolderService;
        private CancellationToken _stoppingToken;

        private readonly ConcurrentDictionary<long, FileSystemWatcher> _activeWatchers = new(); // WatchedFolderId, FileSystemWatcher
        private readonly ConcurrentDictionary<string, bool> _trackedFolders = new(); // true is folder, false is file 

        // ASP.NET natively injects the state and a logger
        public FileSystemWatcherWorker(
            ILogger<FileSystemWatcherWorker> logger,
            IServiceScopeFactory scopeFactory,
            BackupEngineState state,
            IMemoryCache cache,
            LocalFileSystemService fileSystem,
            WatchedFolderService watchedFolderService)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _state = state;
            _cache = cache;
            _fileSystem = fileSystem;
            _watchedFolderService = watchedFolderService;
        }

        // DEV: protected override - a method that can only be accessed by this class or derived classes, and overrides a base class method
        // DEV: Task - representing an asyncrhonous operation
        // ExecuteAsync: runs for the lifetime of the hosted worker
        // 1. Every 5 seconds, it reconciles the active parent folder watchers with the folders currently configured in the database
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        // stoppingToken: cancellation signal - you pass it into cancellable operations and check it in loops
        // When the host stops, the token is cancelled and Task.Delay throws OperationCanceledException. That exits ExecuteAsync, which is normal for a BackgroundService.
        {
            _logger.LogInformation("FileSystemWatcherWorker starting up.");
            _stoppingToken = stoppingToken;

            await StartupLogic(stoppingToken);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await SyncWatchersFromDatabaseAsync(stoppingToken);
                    await Task.Delay(5000, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("FileSystemWatcherWorker stopping.");
            }
            finally
            {
                // Dispose of watchers when ExecuteAsync is paused
                foreach (var watcher in _activeWatchers.Values)
                {
                    watcher.Dispose();
                }

                _activeWatchers.Clear();
            }
        }

        // reconciling any interrupted SyncQueues
        private async Task StartupLogic(CancellationToken stoppingToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // reconcile any interrupted syncevents
            var lastEvent = await db.SyncEvents.LastOrDefaultAsync(stoppingToken);
            if (lastEvent?.CompletionType == SyncEventType.InProgress)
            {
                lastEvent.CompletionType = SyncEventType.Interrupted;
            }

            _ = await db.SaveChangesAsync(stoppingToken);
        }

        private async Task SyncWatchersFromDatabaseAsync(
            CancellationToken stoppingToken
        )
        {
            List<long> configuredFolders;

            // Rent a short-lived scope to safely query AppDbContext
            using (var scope = _scopeFactory.CreateScope())
            {
                // scope factory creates a new scope for dependency injection, allowing us to safely resolve services like AppDbContext without risking memory leaks or conflicts with other parts of the application
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Read active paths from SQLite
                configuredFolders = await db.WatchedFolders
                    .Select(f => f.Id)
                    .ToListAsync(stoppingToken);
            }

            // 1. Attach watchers for newly added database paths
            foreach (var id in configuredFolders)
            {

                WatchedFolder folder = _watchedFolderService.GetWatchedFolderFromId(id);
                if (folder is null)
                {
                    _logger.LogWarning("Watched folder id invalid (no folder exists): {Id}", id);
                    continue;
                }

                stoppingToken.ThrowIfCancellationRequested();
                // If the folder is not already watched, and it exists
                if (!_activeWatchers.ContainsKey(id) && Directory.Exists(folder.LocalPath))
                {
                    await AttachWatcherAsync(folder, stoppingToken);
                }
                else if (!Directory.Exists(folder.LocalPath))
                {
                    _logger.LogWarning("Watched folder path no longer exists: {Path}", folder.LocalPath);
                }
            }

            // 2. Detach watchers for paths deleted from the database
            foreach (var existingId in _activeWatchers.Keys)
            {
                if (!configuredFolders.Contains(existingId))
                {
                    DetachWorker(_watchedFolderService.GetWatchedFolderFromId(existingId));
                }
            }
        }

        private async Task AttachWatcherAsync(WatchedFolder folder, CancellationToken stoppingToken)
        {
            try
            {
                if (folder.LocalPath is null) throw new NullReferenceException("WatchedFolder LocalPath is null: " + folder);
                if (!Directory.Exists(folder.LocalPath)) 
                { 
                    folder.Problem = ProblematicFolderType.InvalidPath;
                    await _watchedFolderService.SetProblemAsync(
                        folder.Id,
                        ProblematicFolderType.InvalidPath,
                        stoppingToken);
                    throw new FileNotFoundException("WatchedFolder LocalPath: " + folder.LocalPath + " does not exist.");
                }

                // System.IO.FileSystemWatcher monitors specific directory and raises events when files/subdirectories are created, modified, renamed, or deleted
                // more efficient than polling because it uses OS-level notifications
                var watcher = new FileSystemWatcher(folder.LocalPath!)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.Size,
                };

                if (!_activeWatchers.TryAdd(folder.Id, watcher))
                {
                    watcher.Dispose();
                    return;
                }

                watcher.Changed += (sender, e) =>
                    OnFileSystemEvent(folder.Id, folder.LocalPath!, e);
                watcher.Created += (sender, e) =>
                    OnFileSystemEvent(folder.Id, folder.LocalPath!, e);
                watcher.Deleted += (sender, e) =>
                    OnFileSystemEvent(folder.Id, folder.LocalPath!, e); // external moves are a delete
                watcher.Renamed += (sender, e) =>
                    OnFileRenamedEvent(folder.Id, folder.LocalPath!, e); // internal moves within the same overarching directory are treated as a rename

                watcher.EnableRaisingEvents = true;

                _logger.LogInformation($"Started FileSystemWatcher on: {folder.LocalPath}");

                // tracking folders and files in memory
                _trackedFolders.TryAdd(folder.LocalPath!, true);
                var children = _fileSystem.GetAllNestedChildren(folder.LocalPath!);
                if (children is not null)
                {
                    foreach (var childFolder in children.Folders)
                    {
                        _trackedFolders.TryAdd(childFolder, true);
                    }
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to attach watcher to: {folder.LocalPath}");

                if (_activeWatchers.TryRemove(folder.Id, out var watcher))
                {
                    watcher.Dispose();
                }
            }
        }

        private void DetachWorker(WatchedFolder folder)
        {
            if (_activeWatchers.TryRemove(folder.Id, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _logger.LogInformation($"Detached FileSystemWatcher from: {folder.LocalPath}");
            }
        }

        // Event types: changed, created, deleted (moves outside of the WatchedFolder parent can also be deleted events)
        private void OnFileSystemEvent(
            long watchedFolderId,
            object sender,
            FileSystemEventArgs e)
        {
            // checks if this is a duplicate event for the same file path within a short time frame (debouncing)
            if (IsDebounced(e.FullPath)) return;
            // check if the changed file matches the ignore rules glob
            if (_fileSystem.IsFileIgnored(_watchedFolderService.GetWatchedFolderFromId(watchedFolderId).IgnoreRules, e.FullPath)) return;

            if (_trackedFolders.TryGetValue(e.FullPath, out _) || Directory.Exists(e.FullPath))
            {
                _ = ProcessChangedFolderAsync(watchedFolderId, e.FullPath, e.ChangeType.ToString(), stoppingToken: _stoppingToken);
            }
            else
            {
                _ = ProcessChangedFileAsync(watchedFolderId, e.FullPath, e.ChangeType.ToString(), stoppingToken: _stoppingToken);
            }
        }

        private void OnFileRenamedEvent(
            long watchedFolderId,
            object sender,
            RenamedEventArgs e)
        {
            if (IsDebounced(e.FullPath)) return;
            if (_fileSystem.IsFileIgnored(_watchedFolderService.GetWatchedFolderFromId(watchedFolderId).IgnoreRules, e.FullPath)) return;

            _logger.LogInformation("File renamed: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);

            bool isFile = File.Exists(e.FullPath);
            bool isDirectory = Directory.Exists(e.FullPath);
            // 1. file rename/move
            if (isFile)
            {
                _ = ProcessChangedFileAsync(watchedFolderId, e.FullPath, "Renamed", e.OldFullPath, _stoppingToken);
            }
            // 2. directory rename/move
            else if (isDirectory)
            {
                _ = ProcessChangedFolderAsync(watchedFolderId, e.FullPath, "Renamed", e.OldFullPath, _stoppingToken);
                // update directory tracking
                if (_trackedFolders.TryGetValue(e.OldFullPath, out _))
                {
                    _trackedFolders.TryRemove(e.OldFullPath, out _);
                    _trackedFolders.TryAdd(e.FullPath, true);
                }
            }
            // 3. If the renamed target is unavailable, record the old path as deleted
            else
            {
                _logger.LogWarning("Renamed target is unavailable. Treating the old path as deleted: {Path}", e.OldFullPath);

                if (_trackedFolders.TryGetValue(e.OldFullPath, out bool found) && found)
                {
                    _ = ProcessChangedFolderAsync(watchedFolderId, e.OldFullPath, "Deleted", e.FullPath, _stoppingToken);
                }
                else
                {
                    _ = ProcessChangedFileAsync(watchedFolderId, e.OldFullPath, "Deleted", e.FullPath, _stoppingToken);
                }
            }
        }

        private bool IsDebounced(string filePath)
        {
            // 2000ms rolling window debounce key - finite state machine debounce to avoid multiple events for the same file in quick succession
            string cacheKey = $"fsw_debounce_{filePath.ToLowerInvariant()}";

            if (_cache.TryGetValue(cacheKey, out _))
            {
                return true; // already processed recently

            }
            // set cache entry with 2-second expiration
            _cache.Set(cacheKey, true, TimeSpan.FromMilliseconds(2000));
            return false;
        }

        private async Task SetDeletePendingSyncItem(
            Func<PendingSyncItem, bool> searchConditions,
            Action<PendingSyncItem> callback,
            CancellationToken stoppingToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var item = await db.PendingSyncQueue.FirstOrDefaultAsync(
                i => searchConditions(i),
                stoppingToken);
            // PendingSyncItem exists
            if (item is not null)
            {
                callback(item);
                _ = await db.SaveChangesAsync(stoppingToken);
            }
        }
        // if it finds existing pending sync item, it'll update, otherwise, it'll create a new one
        private async Task UpdateOrCreateNewPendingSyncItem(
            Func<PendingSyncItem, bool> searchConditions,
            Action<PendingSyncItem> callback,
            PendingSyncItem syncItem,
            CancellationToken stoppingToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var item = await db.PendingSyncQueue.FirstOrDefaultAsync(
                i => searchConditions(i),
                stoppingToken);
            // PendingSyncItem exists
            if (item is not null)
            {
                callback(item);
                _ = await db.SaveChangesAsync(stoppingToken);
            }
            // PendingSyncItem does not exist, create a new one
            else
            {
                db.PendingSyncQueue.Add(syncItem);
                _ = await db.SaveChangesAsync(stoppingToken);
            }
        }

        private async Task ProcessChangedFileAsync(
            long watchedFolderId,
            string filePath,
            string changeType,
            string? oldFilePath = null,
            CancellationToken stoppingToken = default)
        {
            // skip processing directories
            if (Directory.Exists(filePath))
            {
                _ = ProcessChangedFolderAsync(watchedFolderId, filePath, changeType, oldFilePath);
                return;
            }

            // Event Types processing
            if (changeType == "Deleted") // deletions
            {
                bool syncItemSearch(PendingSyncItem i) =>
                    i.WatchedFolderId == watchedFolderId
                    && i.LocalPath == filePath
                    && !i.IsTerminal();
                void syncItemUpdate(PendingSyncItem item)
                {
                    item.Deleted = true;
                }
                await SetDeletePendingSyncItem(syncItemSearch, syncItemUpdate, stoppingToken);
            }
            else if (changeType == "Renamed" && oldFilePath != null) // renames or moves
            {
                // TODO: handle MOVES which count as file renames
                // differentiate between moves and renames

                string oldDirectory = Path.GetDirectoryName(oldFilePath);
                string newDirectory = Path.GetDirectoryName(filePath);

                bool isSameDirectory = string.Equals(oldDirectory, newDirectory, StringComparison.OrdinalIgnoreCase);

                if (isSameDirectory)
                {
                    // rename

                    bool syncItemSearch(PendingSyncItem i) =>
                        i.WatchedFolderId == watchedFolderId
                        && i.LocalPath == oldFilePath
                        && !i.IsTerminal();
                    void syncItemUpdate(PendingSyncItem item)
                    {
                        item.LocalPath = filePath;
                        item.Renamed = true;
                        if (string.IsNullOrEmpty(item.OriginalLocalPath))
                        {
                            item.OriginalLocalPath = oldFilePath;
                        }
                    }
                    PendingSyncItem newSyncItem = new PendingSyncItem
                    {
                        WatchedFolderId = watchedFolderId,
                        OriginalLocalPath = oldFilePath,
                        LocalPath = filePath,
                        Renamed = true,
                    };
                    await UpdateOrCreateNewPendingSyncItem(syncItemSearch, syncItemUpdate, newSyncItem, stoppingToken);
                }
                else
                {
                    // move

                    bool syncItemSearch(PendingSyncItem i) =>
                        i.WatchedFolderId == watchedFolderId
                        && i.LocalPath == oldFilePath
                        && !i.IsTerminal();
                    void syncItemUpdate(PendingSyncItem item)
                    {
                        item.LocalPath = filePath;
                        item.Moved = true;
                        if (string.IsNullOrEmpty(item.OriginalLocalPath))
                        {
                            item.OriginalLocalPath = oldFilePath;
                        }
                    }
                    PendingSyncItem newSyncItem = new PendingSyncItem
                    {
                        WatchedFolderId = watchedFolderId,
                        OriginalLocalPath = oldFilePath,
                        LocalPath = filePath,
                        Moved = true,
                    };
                    await UpdateOrCreateNewPendingSyncItem(syncItemSearch, syncItemUpdate, newSyncItem, stoppingToken);
                }
            }
            else if (changeType == "Changed") // file edits
            {
                bool syncItemSearch(PendingSyncItem i) =>
                    i.WatchedFolderId == watchedFolderId
                    && i.LocalPath == filePath
                    && !i.IsTerminal();
                void syncItemUpdate(PendingSyncItem item)
                {
                    item.Changed = true;
                }
                PendingSyncItem newSyncItem = new PendingSyncItem
                {
                    WatchedFolderId = watchedFolderId,
                    LocalPath = filePath,
                    Changed = true,
                };
                await UpdateOrCreateNewPendingSyncItem(syncItemSearch, syncItemUpdate, newSyncItem, stoppingToken);
            }
            else if (changeType == "Created") // file creations
            {
                bool syncItemSearch(PendingSyncItem i) =>
                    i.WatchedFolderId == watchedFolderId
                    && i.LocalPath == filePath
                    && !i.IsTerminal();
                void syncItemUpdate(PendingSyncItem item)
                {
                    item.Created = true;
                }
                PendingSyncItem newSyncItem = new PendingSyncItem
                {
                    WatchedFolderId = watchedFolderId,
                    LocalPath = filePath,
                    Created = true,
                };
                await UpdateOrCreateNewPendingSyncItem(syncItemSearch, syncItemUpdate, newSyncItem, stoppingToken);
            }
        }

        private async Task RenameAllChildPathsAsync(
            long watchedFolderId,
            string newFolderPath,
            string oldFolderPath,
            CancellationToken stoppingToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var pendingItems = await db.PendingSyncQueue
                .Where(item =>
                    item.WatchedFolderId == watchedFolderId &&
                    !item.IsTerminal())
                .ToListAsync(stoppingToken);

            foreach (var item in pendingItems)
            {
                if (!item.LocalPath.StartsWith(
                        oldFolderPath + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(oldFolderPath, item.LocalPath);
                item.LocalPath = Path.Combine(newFolderPath, relativePath);
            }

            await db.SaveChangesAsync(stoppingToken);
        }

        private async Task ProcessChangedFolderAsync(
            long watchedFolderId,
            string folderPath,
            string changeType,
            string? oldFolderPath = null,
            CancellationToken stoppingToken = default)
        {
            // skip processing files
            if (File.Exists(folderPath))
            {
                await ProcessChangedFileAsync(watchedFolderId, folderPath, changeType, oldFolderPath, stoppingToken);
                return;
            }

            if (changeType == "Deleted")
            {
                bool syncItemSearch(PendingSyncItem i) =>
                    i.WatchedFolderId == watchedFolderId
                    && i.LocalPath == folderPath
                    && !i.IsTerminal();
                void syncItemUpdate(PendingSyncItem item)
                {
                    item.IsFolder = true;
                    item.Deleted = true;
                }
                await SetDeletePendingSyncItem(syncItemSearch, syncItemUpdate, stoppingToken);
                await MarkDeletedFolderContentsAsync(watchedFolderId, folderPath, stoppingToken);
            }
            else if (changeType == "Renamed")
            {
                // update all children's LocalPaths
                string? oldParentPath = Path.GetDirectoryName(oldFolderPath);
                string? newParentPath = Path.GetDirectoryName(folderPath);
                bool isRename = string.Equals(
                    oldParentPath,
                    newParentPath,
                    StringComparison.OrdinalIgnoreCase);

                if (isRename && !string.IsNullOrEmpty(oldFolderPath))
                {
                    bool syncItemSearch(PendingSyncItem i) =>
                        i.WatchedFolderId == watchedFolderId
                        && i.LocalPath == oldFolderPath
                        && !i.IsTerminal();
                    void syncItemUpdate(PendingSyncItem item)
                    {
                        item.IsFolder = true;
                        item.LocalPath = folderPath;
                        item.Renamed = true;
                        if (string.IsNullOrEmpty(item.OriginalLocalPath))
                        {
                            item.OriginalLocalPath = oldFolderPath;
                        }
                    }
                    PendingSyncItem newSyncItem = new PendingSyncItem
                    {
                        IsFolder = true,
                        WatchedFolderId = watchedFolderId,
                        OriginalLocalPath = oldFolderPath,
                        LocalPath = folderPath,
                        Renamed = true,
                    };
                    await UpdateOrCreateNewPendingSyncItem(syncItemSearch, syncItemUpdate, newSyncItem, stoppingToken);

                    await RenameAllChildPathsAsync(watchedFolderId, folderPath, oldFolderPath, stoppingToken);
                }
                else if (!string.IsNullOrEmpty(oldFolderPath))
                {
                    bool syncItemSearch(PendingSyncItem i) =>
                        i.WatchedFolderId == watchedFolderId
                        && i.LocalPath == oldFolderPath
                        && !i.IsTerminal();
                    void syncItemUpdate(PendingSyncItem item)
                    {
                        item.IsFolder = true;
                        item.LocalPath = folderPath;
                        item.Moved = true;
                        if (string.IsNullOrEmpty(item.OriginalLocalPath))
                        {
                            item.OriginalLocalPath = oldFolderPath;
                        }
                    }
                    PendingSyncItem newSyncItem = new PendingSyncItem
                    {
                        IsFolder = true,
                        WatchedFolderId = watchedFolderId,
                        OriginalLocalPath = oldFolderPath,
                        LocalPath = folderPath,
                        Moved = true,
                    };
                    await UpdateOrCreateNewPendingSyncItem(syncItemSearch, syncItemUpdate, newSyncItem, stoppingToken);

                    await RenameAllChildPathsAsync(watchedFolderId, folderPath, oldFolderPath, stoppingToken);
                }
            }
            else if (changeType == "Created")
            {
                bool syncItemSearch(PendingSyncItem i) =>
                    i.WatchedFolderId == watchedFolderId
                    && i.LocalPath == folderPath
                    && !i.IsTerminal();
                void syncItemUpdate(PendingSyncItem item)
                {
                    item.IsFolder = true;
                    item.Created = true;
                }
                PendingSyncItem newSyncItem = new PendingSyncItem
                {
                    IsFolder = true,
                    WatchedFolderId = watchedFolderId,
                    LocalPath = folderPath,
                    Created = true,
                };
                await UpdateOrCreateNewPendingSyncItem(syncItemSearch, syncItemUpdate, newSyncItem, stoppingToken);

                await QueueCreatedFolderContentsAsync(watchedFolderId, folderPath, stoppingToken);
            }
        }

        private async Task MarkDeletedFolderContentsAsync(
            long watchedFolderId,
            string deletedFolderPath,
            CancellationToken stoppingToken)
        {
            string deletedFolderPrefix = deletedFolderPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var pendingItems = await db.PendingSyncQueue
                .Where(item =>
                    item.WatchedFolderId == watchedFolderId
                    && !item.IsTerminal()
                    && item.LocalPath.StartsWith(deletedFolderPath))
                .ToListAsync(stoppingToken);

            foreach (var item in pendingItems)
            {
                if (item.LocalPath.StartsWith(
                        deletedFolderPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    item.Deleted = true;
                }
            }

            await db.SaveChangesAsync(stoppingToken);
        }

        private async Task QueueCreatedFolderContentsAsync(
            long watchedFolderId,
            string folderPath,
            CancellationToken stoppingToken)
        {
            var children = _fileSystem.GetAllNestedChildren(folderPath);
            if (children is null)
            {
                return;
            }

            var childPaths = children.Files
                .Select(path => new { Path = path, IsFolder = false })
                .Concat(children.Folders.Select(path => new { Path = path, IsFolder = true }))
                .ToList();

            if (childPaths.Count == 0)
            {
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var paths = childPaths.Select(child => child.Path).ToList();

            var existingItems = await db.PendingSyncQueue
                .Where(item => item.WatchedFolderId == watchedFolderId && paths.Contains(item.LocalPath))
                .ToListAsync(stoppingToken);

            var existingActiveItems = existingItems
                .Where(item => !item.IsTerminal())
                .GroupBy(item => item.LocalPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var child in childPaths)
            {
                if (existingActiveItems.TryGetValue(child.Path, out var existingItem))
                {
                    existingItem.IsFolder = child.IsFolder;
                    existingItem.Created = true;
                    continue;
                }

                db.PendingSyncQueue.Add(new PendingSyncItem
                {
                    WatchedFolderId = watchedFolderId,
                    LocalPath = child.Path,
                    IsFolder = child.IsFolder,
                    Created = true
                });
            }

            await db.SaveChangesAsync(stoppingToken);
        }

        private async Task<bool> WaitForFileUnlockAsync(
            string filePath,
            int maxTimeoutMs,
            CancellationToken stoppingToken)
        {
            int elapsed = 0;
            int delay = 250;

            while (elapsed < maxTimeoutMs)
            {
                stoppingToken.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(filePath)) return true; // File deleted before read

                    // attempt to open file exclusively (if it succeeds, it's unlocked)
                    using (var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        if (stream != null) return true;
                    }
                }
                catch (IOException)
                {
                    // Ignore IOException and continue - Locked by OS/other app, wait and retry
                }
                await Task.Delay(delay);
                elapsed += delay;
            }

            return false; // still locked after timeout
        }

        public override void Dispose()
        {
            // Clean up memory when the Windows Service stops
            foreach (var watcher in _activeWatchers.Values)
            {
                watcher.Dispose();
            }
            _activeWatchers.Clear();

            base.Dispose();
        }
    }
}
