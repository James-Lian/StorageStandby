using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory; // for debouncing file-saving
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StorageStandby.Backend.Core;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// The intuition is that checking a folder's last-modified date is fast ("polling"). However, on Windows, file systems don't update top-level folder dates recursively when a file inside them changes. Therefore, we cannot make our polling more efficient by choosing subdirectories through elimination (date-checking and comparing with last synced date). If we were polling, we would have to check every single file in every single watched folder, which is inefficient. Instead, we use FileSystemWatcher, which is event-driven and only triggers when a file changes. This is more efficient than polling, especially for large directories.


// TODO: When sanity-checking last-modified dates; any files not already in the upload queue get their date checked as well. This catches any files we missed if the background polling failed. 
// TODO: If restarting, attach new workers to files based on WatchedFolder ignore rules
// TODO: When checking whether updates are valid - check if any folder paths changed (directory or subdirectory or otherwise)

namespace StorageStandby.Backend.Workers
{
    public class FileSystemWatcherWorker : BackgroundService
    {
        private readonly ILogger<FileSystemWatcherWorker> _logger; // built-in interface used to write log messages - provides a consistent abstraction layer, meaning you can write your logging code once and easily swap out the destination
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BackupEngineState _state;
        private readonly IMemoryCache _cache; // built-in in-memory caching service
        // thread-safe, key-value dictionary stored directly in your server's RAM
        // updating/saving a file can trigger multiple events in quick succession, so we use this cache to "debounce" the events and avoid redundant uploads
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _activeWatchers = new(); 

        // ASP.NET natively injects the state and a logger
        public FileSystemWatcherWorker(
            ILogger<FileSystemWatcherWorker> logger,
            IServiceScopeFactory scopeFactory,
            BackupEngineState state, 
            IMemoryCache cache)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _state = state;
            _cache = cache;

            // TODO: wtf is this
            _state.OnNewFolderAdded += AttachWatcher;
        }

        // DEV: protected override - a method that can only be accessed by this class or derived classes, and overrides a base class method
        // DEV: Task - representing an asyncrhonous operation
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FileSystemWatcherWorker starting up.");

            // TODO: wtf is this
            _state.Status = "Running";

            while (!stoppingToken.IsCancellationRequested)
            {
                await SyncWatchersFromDatabaseAsync();
                await Task.Delay(5000, stoppingToken);
            }

            //foreach (var path in _state.ActiveWatchedPaths)
            //{
            //    AttachNewWatcher(path);

            //}
        }

        private async Task SyncWatchersFromDatabaseAsync()
        {
            List<string?> configuredPaths;

            // Rent a short-lived scope to safely query AppDbContext
            using (var scope = _scopeFactory.CreateScope())
            {
                // scope factory creates a new scope for dependency injection, allowing us to safely resolve services like AppDbContext without risking memory leaks or conflicts with other parts of the application
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                // Read active paths from SQLite
                configuredPaths = await db.WatchedFolders
                    .Select(f => f.LocalPath)
                    .ToListAsync();
            }

            // TODO: refer to WatchedFolderParent InitializeWatchedChildren()

            // 1. Attach watchers for newly added database paths
            foreach (var path in configuredPaths)
            {
                if (!_activeWatchers.ContainsKey(path) && Directory.Exists(path))
                {
                    AttachWatcher(path);
                }
            }

            // 2. Detach watchers for paths deleted from the database
            foreach (var existingPath in _activeWatchers.Keys)
            {
                if (!configuredPaths.Contains(existingPath))
                {
                    if (_activeWatchers.TryRemove(existingPath, out var watcher))
                    {
                        watcher.Dispose();
                        _logger.LogInformation($"Detached watcher from: {existingPath}");
                    }
                }
            }
        }

        private void AttachWatcher(string folderPath) 
        {
            try
            {
                // System.IO.FileSystemWatcher monitors specific directory and raises events when files/subdirectories are created, modified, renamed, or deleted
                // more efficient than polling because it uses OS-level notifications
                var watcher = new FileSystemWatcher(folderPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName 
                        | NotifyFilters.DirectoryName 
                        | NotifyFilters.LastWrite 
                        | NotifyFilters.Size,
                };

                watcher.Changed += OnFileSystemEvent;
                watcher.Created += OnFileSystemEvent;
                watcher.Deleted += OnFileSystemEvent; // external moves are a delete
                watcher.Renamed += OnFileRenamed; // internal moves within the same overarching directory are treated as a rename

                watcher.EnableRaisingEvents = true;

                if (_activeWatchers.TryAdd(folderPath, watcher))
                {
                    _logger.LogInformation($"Started FileSystemWatcher on: {folderPath}");
                }
            } catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to attach watcher to: {folderPath}");
            }
        }

        private void DetachWorker(string folderPath)
        {
            if (_activeWatchers.TryRemove(folderPath, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _logger.LogInformation($"Detached FileSystemWatcher from: {folderPath}");
            }
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            // checks if this is a duplicate event for the same file path within a short time frame (debouncing)
            if (IsDebounced(e.FullPath)) return;
            _ = ProcessChangedFileAsync(e.FullPath, e.ChangeType.ToString());
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (IsDebounced(e.FullPath)) return;

            _logger.LogInformation("File renamed: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);

            bool isDirectory = Directory.Exists(e.FullPath);
            // file rename
            if (!isDirectory && e.ChangeType != WatcherChangeTypes.Deleted)
            {
                _ = ProcessChangedFileAsync(e.FullPath, "Renamed", e.OldFullPath);
            } 
            // directory deleted
            else if (!isDirectory) {
                // TODO: add error-checking in sync to recheck stuff
                _ = ProcessChangedFolderAsync(e.FullPath, "Deleted", e.OldFullPath);
            }
            // directory renamed
            else
            {
                _ = ProcessChangedFolderAsync(e.FullPath, "Renamed", e.OldFullPath);
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


        // TODO: rewrite/refactor
        private async Task ProcessChangedFolderAsync(string folderPath, string changeType, string? oldFolderPath = null)
        {
            // skip processing files
            if (File.Exists(folderPath)) ProcessChangedFileAsync(folderPath, changeType, oldFolderPath);

            if (changeType == "Deleted")
            {
            } else
            {
                // TODO: idfk if WaitForFileUnlockAsync works for folders
                //bool unlocked = await WaitForFileUnlockAsync(folderPath, maxTimeoutMs: 5000);
                //// Ensure folder isn't currently locked by a process (e.g. Word, Photoshop)
                //if (!unlocked)
                //{
                //    _logger.LogWarning("Folder was locked by another process. Deferring: {Path}", folderPath);
                //    return;
                //}

                if (changeType == "Renamed")
                {
                    // differentiate between actual renames and moves

                } else if (changeType == "Created")
                {

                }
                else
                {
                    return;
                }
            }

            // Create short-lived DB scope to save sync staging state
            //using var scope = _scopeFactory.CreateScope();
            //var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            //var existingRecord = await db.FileSyncRecords
            //    .FirstOrDefaultAsync(f => f.WatchedFolderId == folder.Id && f.LocalPath == folderPath);
            //if (existingRecord == null)
            //{
            //    db.FileSyncRecords.Add(new FileSyncRecord
            //    {
            //        WatchedFolderId = folder.Id,
            //        LocalPath = folderPath,
            //        CloudId = null,
            //        FileHash = string.Empty,
            //        LastModifiedLocal = Directory.Exists(folderPath) ? Directory.GetLastWriteTimeUtc(folderPath) : DateTime.UtcNow,
            //        LastSyncedToCloud = DateTime.MinValue // Flags for upload
            //    });
            //}
            //else
            //{
            //    existingRecord.LastModifiedLocal = Directory.Exists(folderPath) ? Directory.GetLastWriteTimeUtc(folderPath) : DateTime.UtcNow;
            //}
            //await db.SaveChangesAsync();
            //_logger.LogInformation("Staged [{ChangeType}] for folder: {Path}", changeType, folderPath);
        }

        private async Task ProcessChangedFileAsync(string filePath, string changeType, string? oldFilePath = null)
        {
            // skip processing directories
            if (Directory.Exists(filePath)) 
            { 
                _ = ProcessChangedFolderAsync(filePath, changeType, oldFilePath);
                return;
            }

            if (changeType != "Deleted")
            {
                bool unlocked = await WaitForFileUnlockAsync(filePath, maxTimeoutMs: 5000);
                // Ensure file isn't currently locked by a process (e.g. Word, Photoshop)
                if (!unlocked)
                {
                    // TODO: re-add to queue?? Maybe??
                    _logger.LogWarning("File was locked by another process. Deferring: {Path}", filePath);
                    return;
                }
            }
            else if (changeType == "Renamed" && oldFilePath != null)
            {
                // TODO: handle MOVES which count as file renames
                // differentiate between moves and renames
            } 

            // -----------------------------------------------------------
            // Sync/Object Updates

            // Create short-lived DB scope to save sync staging state
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var existingRecord = await db.FileSyncRecords
                .FirstOrDefaultAsync(f => f.WatchedFolderId == folder.Id && f.LocalPath == filePath);

            if (existingRecord == null)
            {
                db.FileSyncRecords.Add(new FileSyncRecord
                {
                    WatchedFolderId = folder.Id,
                    LocalPath = filePath,
                    CloudId = null,
                    FileHash = string.Empty,
                    LastModifiedLocal = File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath) : DateTime.UtcNow,
                    LastSyncedToCloud = DateTime.MinValue // Flags for upload
                });
            }
            else
            {
                existingRecord.LastModifiedLocal = File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath) : DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("Staged [{ChangeType}] for file: {Path}", changeType, filePath);

            // TODO: Push record into SQLite PendingSyncQueue
        }

        private async Task<bool> WaitForFileUnlockAsync(string filePath, int maxTimeoutMs)
        {
            int elapsed = 0;
            int delay = 250;

            while (elapsed < maxTimeoutMs)
            {
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
            foreach (var watcher in _activeWatchers)
            {
                //watcher.Dispose();
            }
            // TODO: wtf is this
            _state.OnNewFolderAdded -= AttachWatcher;
            base.Dispose();
        }
    }
}
