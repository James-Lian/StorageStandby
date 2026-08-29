using System.Text.Json;

namespace StorageStandby.Backend.Models
{
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
        public DateTime? PauseSyncUntil { get; set; } = null; // starts up and checks this value
        public List<SyncEvent> SyncEvents { get; set; } = new List<SyncEvent> { }; // might not be all... ?

        // ----------------------------------------------------------
        // API Settings
    }
}
