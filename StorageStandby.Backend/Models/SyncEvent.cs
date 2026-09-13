using System.Text.Json;

namespace StorageStandby.Backend.Models
{

    public enum SyncEventType
    {
        Upcoming = 0,
        InProgress = 1,
        Interrupted = 2,
        Succeeded = 3,
        Unfinished = 4,
        Other = 5,
    }

    // One SyncEvent per WatchedFolder, per sync operation. This is a record of what happened during that sync.
    public class SyncEvent
    {
        // ----------------------------------------------------------
        // Sync Event identifiers
        public long Id { get; set; }
        public long WatchedFolderId { get; set; }
        public DateTime? CompletedTimestamp { get; set; } = null;
        public SyncEventType CompletionType { get; set; } = SyncEventType.Upcoming;

        // ----------------------------------------------------------
        // Sync Event information
        public Providers Provider { get; set; }
        public string AccountId { get; set; }
        public string SyncedItems { get; set; } = string.Empty; // semicolon delimited - files
        public List<FailedItemsDetails> FailedItems { get; set; } = [];
        public string UnfinishedItems { get; set; } = string.Empty;
    }

    public class FailedItemsDetails
    {
        public string FailedItem { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }
}
