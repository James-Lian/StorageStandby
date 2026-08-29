//// StorageStandby.Backend\Core\BackupEngineState.cs

//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Net.NetworkInformation;
//using System.Threading;
//using StorageStandby.Backend.Models;

//namespace StorageStandby.Backend.Core
//{
//    /// <summary>
//    /// Central state machine for the backup engine. 
//    /// Thread-safe via ReaderWriterLockSlim for concurrent reads with exclusive writes.
//    /// </summary>
//    public class threadsafebackupengine
//    {
//        // =========== LOCKING MECHANISM ===========
//        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

//        // =========== STATE PROPERTIES ===========
//        private string _status = "Initializing";
//        public string Status
//        {
//            get { _lock.EnterReadLock(); try { return _status; } finally { _lock.ExitReadLock(); } }
//            set { _lock.EnterWriteLock(); try { _status = value; } finally { _lock.ExitWriteLock(); } }
//        }

//        private bool _isPaused = false;
//        public bool IsPaused
//        {
//            get { _lock.EnterReadLock(); try { return _isPaused; } finally { _lock.ExitReadLock(); } }
//            set { _lock.EnterWriteLock(); try { _isPaused = value; } finally { _lock.ExitWriteLock(); } }
//        }

//        private ConnectionStatus _isGoogleDriveConnected = ConnectionStatus.Unconnected;
//        public ConnectionStatus IsGoogleDriveConnected
//        {
//            get { _lock.EnterReadLock(); try { return _isGoogleDriveConnected; } finally { _lock.ExitReadLock(); } }
//            set { _lock.EnterWriteLock(); try { _isGoogleDriveConnected = value; } finally { _lock.ExitWriteLock(); } }
//        }

//        private ConnectionStatus _isOneDriveConnected = ConnectionStatus.Unconnected;
//        public ConnectionStatus IsOneDriveConnected
//        {
//            get { _lock.EnterReadLock(); try { return _isOneDriveConnected; } finally { _lock.ExitReadLock(); } }
//            set { _lock.EnterWriteLock(); try { _isOneDriveConnected = value; } finally { _lock.ExitWriteLock(); } }
//        }

//        private ConnectionStatus _isDropboxConnected = ConnectionStatus.Unconnected;
//        public ConnectionStatus IsDropboxConnected
//        {
//            get { _lock.EnterReadLock(); try { return _isDropboxConnected; } finally { _lock.ExitReadLock(); } }
//            set { _lock.EnterWriteLock(); try { _isDropboxConnected = value; } finally { _lock.ExitWriteLock(); } }
//        }

//        private ConnectionStatus _ispDriveConnected = ConnectionStatus.Unconnected;
//        public ConnectionStatus IspDriveConnected
//        {
//            get { _lock.EnterReadLock(); try { return _ispDriveConnected; } finally { _lock.ExitReadLock(); } }
//            set { _lock.EnterWriteLock(); try { _ispDriveConnected = value; } finally { _lock.ExitWriteLock(); } }
//        }

//        private string _currentOperation = "Idle";
//        public string CurrentOperation
//        {
//            get { _lock.EnterReadLock(); try { return _currentOperation; } finally { _lock.ExitReadLock(); } }
//            set { _lock.EnterWriteLock(); try { _currentOperation = value; } finally { _lock.ExitWriteLock(); } }
//        }

//        private string _lastSyncedFile = "None";
//        public string LastSyncedFile
//        {
//            get { _lock.EnterReadLock(); try { return _lastSyncedFile; } finally { _lock.ExitReadLock(); } }
//            set { _lock.EnterWriteLock(); try { _lastSyncedFile = value; } finally { _lock.ExitWriteLock(); } }
//        }

//        private int _activeUploadQueueCount = 0;
//        public int ActiveUploadQueueCount
//        {
//            get { _lock.EnterReadLock(); try { return _activeUploadQueueCount; } finally { _lock.ExitReadLock(); } }
//            set { _lock.EnterWriteLock(); try { _activeUploadQueueCount = value; } finally { _lock.ExitWriteLock(); } }
//        }

//        private readonly List<string> _activeWatchedPaths = new();
//        public List<string> ActiveWatchedPaths
//        {
//            get { _lock.EnterReadLock(); try { return new List<string>(_activeWatchedPaths); } finally { _lock.ExitReadLock(); } }
//        }

//        // =========== EVENTS ===========
//        public event Action<string>? OnNewFolderAdded;

//        /// <summary>
//        /// Thread-safe notification that a new folder was added.
//        /// Only raises the event if the folder wasn't already being tracked.
//        /// </summary>
//        public void NotifyNewFolderAdded(string path)
//        {
//            _lock.EnterWriteLock();
//            try
//            {
//                if (!_activeWatchedPaths.Contains(path))
//                {
//                    _activeWatchedPaths.Add(path);
//                    OnNewFolderAdded?.Invoke(path);
//                }
//            }
//            finally
//            {
//                _lock.ExitWriteLock();
//            }
//        }

//        /// <summary>
//        /// Gets a snapshot of current watched paths (thread-safe copy).
//        /// </summary>
//        public List<string> GetWatchedPaths()
//        {
//            _lock.EnterReadLock();
//            try
//            {
//                return new List<string>(_activeWatchedPaths);
//            }
//            finally
//            {
//                _lock.ExitReadLock();
//            }
//        }

//        /// <summary>
//        /// Thread-safe check: should the sync engine proceed?
//        /// </summary>
//        public bool IsSafeToSync()
//        {
//            _lock.EnterReadLock();
//            try
//            {
//                return !_isPaused && !IsOnMeteredConnection() && !IsHeavyProcessRunning();
//            }
//            finally
//            {
//                _lock.ExitReadLock();
//            }
//        }

//        private bool IsHeavyProcessRunning()
//        {
//            string[] heavyApps = { "Zoom", "Teams", "csgo", "Cyberpunk2077" };
//            var activeProcesses = Process.GetProcesses();
//            return activeProcesses.Any(p => heavyApps.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase));
//        }

//        private bool IsOnMeteredConnection()
//        {
//            return NetworkInterface.GetAllNetworkInterfaces()
//                .Any(ni => ni.OperationalStatus == OperationalStatus.Up &&
//                           ni.NetworkInterfaceType == NetworkInterfaceType.Wwan); // Fixed typo: Wman → Wwan
//        }

//        /// <summary>
//        /// Cleanup when the Singleton is disposed (on app shutdown).
//        /// </summary>
//        ~BackupEngineState()
//        {
//            _lock?.Dispose();
//        }
//    }
//}