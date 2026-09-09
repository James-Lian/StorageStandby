using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;

namespace StorageStandby.Backend.Core
{
    public class SyncManager
    {
        IServiceScopeFactory _scopeFactory;
        AppDbContext _db;
        public SyncManager (
            IServiceScopeFactory scopeFactory,
            AppDbContext db
        ) {
            _scopeFactory = scopeFactory;
            _db = db;
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

        public long? GetWatchedFolderIdFromPath(string path) {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var folder = db.WatchedFolders.FirstOrDefault(f => f.LocalPath == path);
            if (folder is not null)
            {
                return folder.Id;
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

        public WatchedFolder GetWatchedFolderFromId(long id)
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

        // ----------------------------------------------------------------------------------------------------
        // Sync & Queue Methods

    }
}