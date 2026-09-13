using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;

namespace StorageStandby.Backend.Core
{
    public class SyncManagerService
    {
        readonly IServiceScopeFactory _scopeFactory;
        public SyncManagerService (
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

        public void ExecuteSyncQueue()
        {
            // 1. Build list of altered WatchedFolders

            // 2. Iterate through them: choose a provider and an account, perform upload
            // |--> 1 SyncEvent per folder-provider-account combo
            // |--> SyncEvent created with: WatchedFolderId, Providers, AccountId
            
        }

        // 1. see which queue actions have previous dependencies - allow for user to stage certain changes
        // 2. remove useless dependencies that don't change anything
        public void QueueDependencyChecker()
        {
            
        }
    }
}