using System.Text.Json;

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
        public Providers? GlobalPreferredProvider { get; set; } = null;

        // ----------------------------------------------------------
        // Local Settings
        public string GlobalIgnoreRules { get; set; } = "*.tmp;node_modules/"; // semicolon-delimited

        // ----------------------------------------------------------
        // Sync Settings
        public bool IsSyncPaused { get; set; } = false;
        public DateTime? PauseSyncUntil { get; set; } = null; // null: indefinite pause; otherwise, resume after this time
        public List<SyncEvent> SyncEvents { get; set; } = new List<SyncEvent> { }; // might not be all... ?

        public SyncFrequency SyncFrequency { get; set; } = new SyncFrequency();

        // ----------------------------------------------------------
        // API Settings
        
    }
}
