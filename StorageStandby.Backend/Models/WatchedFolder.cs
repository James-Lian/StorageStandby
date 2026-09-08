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
        InvalidPath = 3,
        Other = 4 // catch-all for unexpected issues
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
    public class WatchedFolder
    {
        public long Id { get; set; }
        public string? LocalPath { get; set; } = string.Empty; // validity checker - the user could have moved it

        // ---------------------------------------------------------------
        // Cloud/Provider Information
        public List<WatchedFolderCloudMetadata> AssignedClouds { get; set; } = new();

        // ---------------------------------------------------------------
        // Folder/File specific settings
        public string IgnoreRules { get; set; } = string.Empty; // semicolon-delimited
        public bool IsZipCompressionEnabled { get; set; } = false;
        public WatchedFolderPriority Priority { get; set; } = WatchedFolderPriority.Default;

        public bool TrackSnapshots { get; set; } = false;
        public bool KeepSnapshotsInSameAccount { get; set; } = true;
        public string SnapshotUrls { get; set; } = string.Empty; // semicolon delimited list of URLs to snapshot locations (if any)

        public Providers? PreferredProvider { get; set; } = null;
        public ProblematicFolderType? Problem { get; set; } // Configuration problem - NOT SYNC EVENT PROBLEM

        // ---------------------------------------------------------------
        // Sync state information
        public DateTime? LastSync { get; set; }
        public DateTime DateAdded { get; set; }
        public SyncFrequency? CustomSyncFrequency { get; set; } = null;
    }

    public class WatchedFolderCloudMetadata
    {
        public long Id { get; set; }
        public Providers Provider { get; set; }
        public string AccountId { get; set; }
        public string RemoteFolderId { get; set; }

        // Foreign key to link back to the WatchedFolder in EF Core
        // Fully Defined Relationship - object reference + id
        public long WatchedFolderId { get; set; }
        public WatchedFolder WatchedFolder { get; set; }
        // maybe: saving a config file in the cloud as well??
        public string ConfigFileId { get; set; }
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

        public string LocalPath { get; set; }
        public string CloudFolderId { get; set; }

        public FolderSyncAction SyncAction { get; set; }

        public DateTime LastSyncedToCloud { get; set; }
    }
}
