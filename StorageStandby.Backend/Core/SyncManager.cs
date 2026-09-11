using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;

namespace StorageStandby.Backend.Core
{
    public class SyncManager
    {
        readonly IServiceScopeFactory _scopeFactory;
        public SyncManager (
            IServiceScopeFactory scopeFactory
        ) {
            _scopeFactory = scopeFactory;
        }
    
        // ----------------------------------------------------------------------------------------------------
        // Local-Adjacent Methods
        // TODO: I don't like this CreateWatchedFolderResult enum and will probably replace it
        public enum CreateWatchedFolderResult
        {
            Success,
            Failure,
            AlreadyExists
        }
        public void CreateWatchedFolder()
        {
            
        }

        public void DeleteWatchedFolder()
        {
            
        }

        // ----------------------------------------------------------------------------------------------------
        // Sync & Queue Methods

        // see which queue actions have previous dependencies
        public void QueueDependencyChecker()
        {
            
        }
    }
}