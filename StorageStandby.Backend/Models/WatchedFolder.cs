using System.ComponentModel.DataAnnotations.Schema;

namespace StorageStandby.Backend.Models
{
    /// A static class that holds reference values for watched folders.
    public static class WatchedFolderReference
    {
        public static readonly long maximumFolderSize = 15_000_000_000;
        public static readonly long maximumFileCount = 50_000;
    }
    public enum ProblematicFolderType
    {
        None = 0,
        TooLarge = 1,
        TooManyFiles = 2,
        PathNotFound = 3,
        Other = 4,
        NestedWatchedFolder = 5
    }

    // describes order of sync-ing
    public enum WatchedFolderPriority
    {
        Low = 0,
        Default = 1,
        Important = 2,
        Critical = 3
    }

    // only the roots are selected by the user + ignore rules
    // TODO: FileSystemWatcher events don't fire for root folder renames OR deletions (need to watch IT'S parent instead)
    public class WatchedFolder
    {
        public long Id { get; set; }
        public string? LocalPath { get; set; } = string.Empty; // validity checker - the user could have moved it

        // ---------------------------------------------------------------
        // Cloud/Provider Information
        public List<WatchedFolderCloudMetadata> AssignedClouds { get; private set; } = []; // <-- clouds where information has actually been uploaded
        [NotMapped]
        public List<ProviderAccountCloud> UserAssignedClouds { get; private set; } = [];

        // ---------------------------------------------------------------
        // Folder/File specific settings
        public string IgnoreRules { get; set; } = string.Empty; // semicolon-delimited

        public bool TrackSnapshots { get; set; } = false;
        public string SnapshotUrls { get; set; } = string.Empty; // semicolon delimited list of URLs to snapshot locations (if any)

        public ProblematicFolderType Problem { get; set; } = ProblematicFolderType.None; // Configuration problem - NOT SYNC EVENT PROBLEM

        // ---------------------------------------------------------------
        // Sync state information
        public DateTime? LastSync { get; set; }
        public DateTime DateAdded { get; set; }
        [NotMapped]
        public SyncFrequency? CustomSyncFrequency { get; set; } = null;

        // User assignments
        public void AddUserCloudAssignment(Providers provider, string accountId)
        {
            UserAssignedClouds.Add(new ProviderAccountCloud
            {
                Provider=provider,
                AccountId=accountId
            });
        }

        public void RemoveUserCloudAssignment(ProviderAccountCloud cloud)
        {
            UserAssignedClouds.RemoveAll(i =>
                i.Provider == cloud.Provider &&
                i.AccountId == cloud.AccountId);
            AssignedClouds.RemoveAll(i => 
                i.Provider == cloud.Provider && 
                i.AccountId == cloud.AccountId);
        }

        // tie the local folder with a cloud location
        public void AddAssignedCloud(Providers provider, string accountId, string remoteFolderId, string? configFileId = null)
        {

            // TODO: Do I need cloud creation events??
            AssignedClouds.Add(new WatchedFolderCloudMetadata
            {
                Provider = provider,
                AccountId = accountId,
                RemoteFolderId = remoteFolderId,
                WatchedFolderId = Id,
                ConfigFileId = configFileId
            });
        }
    }

    // Ties online clouds with local folders
    public class WatchedFolderCloudMetadata
    {
        public long Id { get; set; }
        public Providers Provider { get; set; }
        public string AccountId { get; set; } = string.Empty;

        public string RemoteFolderId { get; set; } = string.Empty;

        // Foreign key to link back to the WatchedFolder in EF Core
        // Fully Defined Relationship - object reference + id
        public long WatchedFolderId { get; set; }
        // maybe: saving a config file in the cloud as well??
        public string? ConfigFileId { get; set; }

        public bool StoreZip { get; set; } = false;
        public CloudAssignmentState State { get; set; } = CloudAssignmentState.PendingBootstrap;
        public string? LastError { get; set; }
    }

    public enum CloudAssignmentState
    {
        PendingBootstrap = 0,
        Bootstrapping = 1,
        Active = 2,
        Failed = 3,
        Removing = 4,
        Unlinked = 5
    }

    public enum FileSyncAction
    {
        Created,
        Deleted,
        Renamed,
        Changed,
        Moved
    }

    public class FileSyncRecord
    {
        // -----------------------------------------------------------
        // Identifiers
        public long Id { get; set; }
        public long WatchedFolderId { get; set; }

        // -----------------------------------------------------------
        // Props

        // validity checks cause file could be changed
        public string LocalPath { get; set; } = string.Empty;
        // validity checks cause file could be changed/deleted
        public string CloudId { get; set; } = string.Empty;

        public DateTime LastModifiedLocal { get; set; }
        public DateTime LastSyncedToCloud { get; set; }
    }

    public enum FolderSyncAction
    {
        Rename = 0,
        Delete = 1,
        Move = 2
    }
    public class FolderSyncRecord
    {
        public long Id { get; set; } 
        public long WatchedFolderId { get; set; }

        public string LocalPath { get; set; } = string.Empty;
        public string CloudFolderId { get; set; } = string.Empty;

        public FolderSyncAction SyncAction { get; set; }

        public DateTime LastSyncedToCloud { get; set; }
    }
}
