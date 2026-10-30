namespace StorageStandby.Backend.Models
{
    public enum EventType
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Success = 3
    }

    public enum TimelineEventAction
    {
        SyncStarted = 0,
        SyncCompleted = 1,
        SyncInterrupted = 2,
        SyncSkipped = 3,
        DestinationSkipped = 4,
        ItemUploaded = 5,
        ItemDeleted = 6,
        ItemMoved = 7,
        ItemRenamed = 8,
        ItemFailed = 9,
        CloudAssignmentRequested = 10,
        CloudBootstrapStarted = 11,
        CloudBootstrapCompleted = 12,
        CloudRemovalRequested = 13,
        CloudRemovalCompleted = 14,
        RemoteFolderCreated = 15,
        RemoteFolderReused = 16
    }

    public class TimelineEvent
    {
        public long Id { get; set; }

        // The folder is the required scope for every timeline entry.
        public long WatchedFolderId { get; set; }

        // A timeline entry may describe an assignment action before a SyncEvent exists.
        public long? SyncEventId { get; set; }

        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
        public EventType Type { get; set; } = EventType.Info;
        public TimelineEventAction Action { get; set; }

        // Destination context is copied so history remains useful after an assignment is removed.
        public Providers? Provider { get; set; }
        public string AccountId { get; set; } = string.Empty;

        // Optional item context. These are snapshots rather than required foreign keys because
        // pending items may be removed after successful synchronization.
        public long? PendingSyncItemId { get; set; }
        public string? LocalPath { get; set; }

        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
    }
}
