using System;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq; 
using System.Net.NetworkInformation;
using StorageStandby.Backend.Models;

namespace StorageStandby.Backend.Core
{
    public class BackupEngineState
    {
        // *** high level state ***
        public string Status { get; set; } = "Initializing";
        public bool IsPaused { get; set; } = false;

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
        public event Action<string>? OnNewFolderAdded;


        // ----------------------------------------------------------------------
        // Sync-related members

        // stored in memory: rebuilt on reboot
        public List<string> PendingSyncItems { get; set; } = new();

        public void NotifyNewFolderAdded(string path)
        {
            if (!ActiveWatchedPaths.Contains(path))
            {
                ActiveWatchedPaths.Add(path);
                OnNewFolderAdded?.Invoke(path);
            }
        }

        public bool IsSafeToSync()
        {
            if (IsPaused || IsOnMeteredConnection() || IsHeavyProcessRunning())
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
