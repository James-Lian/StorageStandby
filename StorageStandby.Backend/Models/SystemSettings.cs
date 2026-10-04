using System.ComponentModel.DataAnnotations.Schema;

namespace StorageStandby.Backend.Models
{
    public enum SyncIncrements
    {
        Inactive = 0,
        Hourly = 1,
        Daily = 2,
        Weekly = 3,
        Monthly = 4,
        Yearly = 5
    }
    public class SyncFrequency
    {
        // Weekly; Monthly; Yearly; dropdown
        public int Multiplier = 1; 
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
