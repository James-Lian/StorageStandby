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
        public int Multiplier { get; set; } = 1;
        public List<int> Identifiers { get; set; } = []; // Day numbers for weekly/monthly schedules.
        // TODO: for month, if one of the identifiers is greater than the maximum number of days in the month (e.g. 31 in february), check for that
        public SyncIncrements SyncIncrements { get; set; } = SyncIncrements.Weekly;
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
        public bool AutomaticSyncs { get; set; } = true;

        // ----------------------------------------------------------
        // API Settings
        
    }
}
