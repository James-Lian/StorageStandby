using System;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq; 
using System.Net.NetworkInformation;
using StorageStandby.Backend.Models;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Services;

namespace StorageStandby.Backend.Core
{
    public class BackupEngineState
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SemaphoreSlim _syncExecutionLock = new(1, 1);
        private readonly object _currentJobLock = new();
        private Guid? _currentJobId;
        private DateTime? _currentJobStartedAtUtc;
        private long? _currentJobWatchedFolderId;
        private Providers? _currentJobProvider;
        private string? _currentJobAccountId;
        private long? _currentJobSyncEventId;

        public BackupEngineState(
            IServiceScopeFactory scopeFactory
        )
        {
            _scopeFactory = scopeFactory;
        }

        // *** high level state ***
        public string Status { get; set; } = "Initializing";
        public bool IsSyncPaused { get; private set; } = false;
        private DateTime? _pauseSyncUntil { get; set; } = null;
        private readonly object _IsSyncPausedLock = new object();
        public void LoadSyncPauseFromDb()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = db.SystemSettings.FirstOrDefault();
            if (settings != null)
            {
                IsSyncPaused = settings.IsSyncPaused;
                _pauseSyncUntil = settings.PauseSyncUntil;
            }
        }
        public void SetSyncPause(bool isPaused, DateTime? pauseUntil = null)
        {
            lock (_IsSyncPausedLock)
            {
                IsSyncPaused = isPaused;
                _pauseSyncUntil = pauseUntil;
                
                // Updates database too
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var systemSettings = db.SystemSettings.FirstOrDefault();
                if (systemSettings != null)
                {
                    systemSettings.IsSyncPaused = isPaused;
                    systemSettings.PauseSyncUntil = pauseUntil;
                    db.SaveChanges();
                }
            }
        }
        public bool GetSyncPause()
        {
            lock (_IsSyncPausedLock)
            {
                return IsSyncPaused &&
                    (!_pauseSyncUntil.HasValue || _pauseSyncUntil.Value > DateTime.UtcNow);
            }
        }

        // *** transient UI state (dashboard instruments) ***
        //public Dictionary<Providers, ConnectionStatus> ConnectionStatuses = new() {
        //    { Providers.Google, ConnectionStatus.Unconnected },
        //    { Providers.Microsoft, ConnectionStatus.Unconnected },
        //    { Providers.Dropbox, ConnectionStatus.Unconnected },
        //    { Providers.pDrive, ConnectionStatus.Unconnected }
        //};

        public string CurrentOperation { get; set; } = "Idle";
        public bool IsUploading { get; private set; }
        public string LastSyncedFile { get; set; } = "None";
        public int ActiveUploadQueueCount { get; set; } = 0;

        public Guid? CurrentJobId
        {
            get
            {
                lock (_currentJobLock)
                {
                    return _currentJobId;
                }
            }
        }

        public DateTime? CurrentJobStartedAtUtc
        {
            get
            {
                lock (_currentJobLock)
                {
                    return _currentJobStartedAtUtc;
                }
            }
        }

        public long? CurrentJobWatchedFolderId
        {
            get
            {
                lock (_currentJobLock)
                {
                    return _currentJobWatchedFolderId;
                }
            }
        }

        public Providers? CurrentJobProvider
        {
            get
            {
                lock (_currentJobLock)
                {
                    return _currentJobProvider;
                }
            }
        }

        public string? CurrentJobAccountId
        {
            get
            {
                lock (_currentJobLock)
                {
                    return _currentJobAccountId;
                }
            }
        }

        public long? CurrentJobSyncEventId
        {
            get
            {
                lock (_currentJobLock)
                {
                    return _currentJobSyncEventId;
                }
            }
        }

        public Guid BeginSyncJob(
            long? watchedFolderId,
            Providers? provider,
            string? accountId,
            long? syncEventId)
        {
            lock (_currentJobLock)
            {
                var jobId = Guid.NewGuid();
                _currentJobId = jobId;
                _currentJobStartedAtUtc = DateTime.UtcNow;
                _currentJobWatchedFolderId = watchedFolderId;
                _currentJobProvider = provider;
                _currentJobAccountId = accountId;
                _currentJobSyncEventId = syncEventId;
                return jobId;
            }
        }

        public void EndSyncJob(Guid jobId)
        {
            lock (_currentJobLock)
            {
                if (_currentJobId != jobId)
                {
                    return;
                }

                _currentJobId = null;
                _currentJobStartedAtUtc = null;
                _currentJobWatchedFolderId = null;
                _currentJobProvider = null;
                _currentJobAccountId = null;
                _currentJobSyncEventId = null;
            }
        }

        public void SetUploading(bool isUploading)
        {
            IsUploading = isUploading;
            CurrentOperation = isUploading ? "Uploading" : "Idle";
        }

        // 1. Thread-safety padlock. Threads must grab this before touching the list.
        private readonly object _ActiveWatchedPathsLock = new object();
        // in-memory list of active paths, loaded from DB on boot
        public List<string> ActiveWatchedPaths { get; set; } = new();


        // ----------------------------------------------------------------------
        // Sync-related members

        // stored in memory: rebuilt on reboot
        public List<string> PendingSyncItems { get; set; } = new();

        public bool IsSafeToSync()
        {
            if (GetSyncPause() || IsOnMeteredConnection() || IsHeavyProcessRunning())
            {
                return false;
            }
            return true;
        }

        public void NotifyNewFolderAdded(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && !ActiveWatchedPaths.Contains(path))
            {
                ActiveWatchedPaths.Add(path);
            }
        }

        public void NotifyFolderRemoved(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            lock (_ActiveWatchedPathsLock)
            {
                ActiveWatchedPaths.RemoveAll(activePath =>
                    string.Equals(activePath, path, StringComparison.OrdinalIgnoreCase));
            }
        }

        public async Task<IDisposable> AcquireSyncExecutionAsync(CancellationToken cancellationToken)
        {
            await _syncExecutionLock.WaitAsync(cancellationToken);
            return new SyncExecutionLease(_syncExecutionLock);
        }

        private sealed class SyncExecutionLease : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            public SyncExecutionLease(SemaphoreSlim semaphore) => _semaphore = semaphore;
            public void Dispose() => _semaphore.Release();
        }

        private bool IsHeavyProcessRunning()
        {
            // Example: Suppress syncs if gaming or in a meeting
            string[] heavyApps = { "Zoom", "Teams", "csgo", "Cyberpunk2077" };
            var activeProcesses = Process.GetProcesses();

            return activeProcesses.Any(p => heavyApps.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase));
        }

        private bool IsOnMeteredConnection()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(ni => ni.OperationalStatus == OperationalStatus.Up &&
                           ni.NetworkInterfaceType == NetworkInterfaceType.Wman); // Mobile broadband
        }
    }
}
