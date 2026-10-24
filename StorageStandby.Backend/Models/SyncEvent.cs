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
        Paused = 6,
        Skipped = 7,
    }

    // One SyncEvent per WatchedFolder, per sync operation. This is a record of what happened during that sync.
    public class SyncEvent
    {
        // ----------------------------------------------------------
        // Sync Event identifiers
        public long Id { get; set; }
        public long WatchedFolderId { get; set; }
        public DateTime? ScheduledTimestamp { get; set; }
        public DateTime? CompletedTimestamp { get; set; } = null;
        public SyncEventType CompletionType { get; set; } = SyncEventType.Upcoming;

        // ----------------------------------------------------------
        // Sync Event information
        public Providers Provider { get; set; }
        public string AccountId { get; set; } = string.Empty;
        public string SyncedItems { get; set; } = string.Empty; // semicolon delimited - files
        public string SyncedItemIds { get; set; } = string.Empty;
        public List<FailedItemDetails> FailedItems { get; set; } = [];
        public string UnfinishedItems { get; set; } = string.Empty;
        public string UnfinishedItemIds { get; set; } = string.Empty;

        public long GetNextFailedItemId() => FailedItems.Count == 0
            ? 1
            : FailedItems.Max(item => item.Id) + 1;
    }

    public class FailedItemDetails
    {
        public long Id { get; set; }
        public string FailedItem { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }
}
