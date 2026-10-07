namespace StorageStandby.Backend.Models
{
    public enum UntrackedPathStatus
    {
        Untracked = 0,
        Resolved = 1
    }

    public class UntrackedPath
    {
        public long Id { get; set; }
        public long WatchedFolderId { get; set; }
        public string LocalPath { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public UntrackedPathStatus Status { get; set; } = UntrackedPathStatus.Untracked;
    }
}