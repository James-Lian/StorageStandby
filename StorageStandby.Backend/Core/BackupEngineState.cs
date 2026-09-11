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
        private readonly WatchedFolderService _watchedFolderService;
        private readonly AppDbContext _db;

        public BackupEngineState(
            WatchedFolderService watchedFolderService,
            AppDbContext db
        )
        {
            _watchedFolderService = watchedFolderService;
            _db = db;
        }

        // *** high level state ***
        public string Status { get; set; } = "Initializing";
        private bool _isSyncPaused { get; set; } = false;
        private DateTime? _pauseSyncUntil { get; set; } = null;
        private readonly object _IsSyncPausedLock = new object();
        public void LoadSyncPauseFromDb()
        {
            var settings = _db.SystemSettings.FirstOrDefault();
            if (settings != null)
            {
                _isSyncPaused = settings.IsSyncPaused;
                _pauseSyncUntil = settings.PauseSyncUntil;
            }
        }
        public void SetSyncPause(bool isPaused, DateTime? pauseUntil = null)
        {
            lock (_IsSyncPausedLock)
            {
                _isSyncPaused = isPaused;
                _pauseSyncUntil = pauseUntil;
                
                // Updates database too
                var systemSettings = _db.SystemSettings.FirstOrDefault();
                if (systemSettings != null)
                {
                    systemSettings.IsSyncPaused = isPaused;
                    systemSettings.PauseSyncUntil = pauseUntil;
                    _db.SaveChanges();
                }
            }
        }
        public bool GetSyncPause()
        {
            // Check if pause period has expired
            if (_pauseSyncUntil.HasValue && DateTime.Now < _pauseSyncUntil.Value)
            {
                return true;
            }
            return _isSyncPaused;
        }

        // *** transient UI state (dashboard instruments) ***
        //public Dictionary<Providers, ConnectionStatus> ConnectionStatuses = new() {
        //    { Providers.Google, ConnectionStatus.Unconnected },
        //    { Providers.Microsoft, ConnectionStatus.Unconnected },
        //    { Providers.Dropbox, ConnectionStatus.Unconnected },
        //    { Providers.pDrive, ConnectionStatus.Unconnected }
        //};
 

        public string CurrentOperation { get; set; } = "Idle";
        public string LastSyncedFile { get; set; } = "None";
        public int ActiveUploadQueueCount { get; set; } = 0;

        // 1. Thread-safety padlock. Threads must grab this before touching the list.
        private readonly object _ActiveWatchedPathsLock = new object();
        // in-memory list of active paths, loaded from DB on boot
        public List<string> ActiveWatchedPaths { get; set; } = new();

        //trigger to tell the background worker a new folder was added
        public event Action<WatchedFolder>? OnNewFolderAdded;


        // ----------------------------------------------------------------------
        // Sync-related members

        // stored in memory: rebuilt on reboot
        public List<string> PendingSyncItems { get; set; } = new();

        public void NotifyNewFolderAdded(string path)
        {
            if (!ActiveWatchedPaths.Contains(path))
            {
                ActiveWatchedPaths.Add(path);
                OnNewFolderAdded?.Invoke(_watchedFolderService.GetWatchedFolderFromPath(path));
            }
        }

        public bool IsSafeToSync()
        {
            if (_isSyncPaused || IsOnMeteredConnection() || IsHeavyProcessRunning())
            {
                return false;
            }
            return true;
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
