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
        public string SyncedItems { get; set; } = string.Empty; // semicolon delimited - files
        public string FailedItems { get; set; } = string.Empty;
        public string UnfinishedItems { get; set; } = string.Empty;
        public Providers Provider { get; set; }
        public string AccountId { get; set; }

        public string MetadataJson { get; set; } = "{}";

        public string? Details { get; set; } = null;

        // ----------------------------------------------------------
        // 
        public void SetMetadata(Dictionary<string, string> metadata)
        {
            MetadataJson = JsonSerializer.Serialize(metadata);
        }

        public Dictionary<string, string>? GetMetadata()
        {
            if (string.IsNullOrWhiteSpace(MetadataJson)) return new Dictionary<string, string>();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(MetadataJson);
        }
    }
}
