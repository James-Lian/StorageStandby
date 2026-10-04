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
            // Check if pause period has expired
            if (_pauseSyncUntil.HasValue && DateTime.Now < _pauseSyncUntil.Value)
            {
                return true;
            }
            return IsSyncPaused;
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
