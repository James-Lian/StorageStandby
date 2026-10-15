using System.ComponentModel.DataAnnotations.Schema;

namespace StorageStandby.Backend.Models
{
    public enum SyncIncrements
    {
        Inactive = 0,
        Daily = 1,
        Weekly = 2,
        Monthly = 3,
    }
    public class SyncFrequency
    {
        // Weekly; Monthly; Yearly; dropdown
        public int Multiplier = 1; 
        public List<int>? Identifiers = []; // e.g. Day 1, 2, 3 of the week/month, doesn't apply for daily
        // TODO: for month, if one of the identifiers is greater than the maximum number of days in the month (e.g. 31 in february), check for that
        public SyncIncrements SyncIncrements = SyncIncrements.Weekly;
    }

    public class SystemSettings
    {
        // Always 1 - guarantees a single-row constraint in SQLite
        public int Id { get; set; } = 1;

        // ----------------------------------------------------------
        // Cloud Settings

        // ----------------------------------------------------------
        // Sync Settings
        public bool IsSyncPaused { get; set; } = false;
        public DateTime? PauseSyncUntil { get; set; } = null; // null: indefinite pause; otherwise, resume after this time
        public List<SyncEvent> SyncEvents { get; set; } = new List<SyncEvent> { }; // might not be all... ?

        [NotMapped]
        public SyncFrequency SyncFrequency { get; set; } = new SyncFrequency();
        public bool AutomaticSyncs { get; set; } = true;
        public int AutomaticSyncIntervalMinutes { get; set; } = 60;

        // ----------------------------------------------------------
        // API Settings
        
    }
}
