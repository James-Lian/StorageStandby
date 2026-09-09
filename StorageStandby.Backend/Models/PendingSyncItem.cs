namespace StorageStandby.Backend.Models
{
    // UI (future feature): 
    // - item selection screen to select any items that the system didn't think were changed (idk, e.g. if the user wants to rename them)
    // |--> staging changes, essentially
    // |--> harder to implement. As of now, with the queue system, folder changes can't be ignored. 

    public class PendingSyncItem
    {
        // -----------------------------------------------------------------
        // Identifiers
        public long Id { get; set; }
        public long WatchedFolderId { get; set; }
        public string LocalPath { get; set; } = string.Empty;
        
        // -----------------------------------------------------------------
        // Folder actions
        // - Folder deletion --> all PendingSyncItems with the associated folder are deleted
        // - Folder move --> all PendingSyncItems with the associated folder are moved
        // - Folder rename 
        // - Folder creation --> all PendingSyncItems with the associated folder are created
        public bool IsFolder { get; set; } = false;
        
        // -----------------------------------------------------------------
        // EventTypes - cascading (stop at first true prop)
        // TODO: Check parent folder deletion. If deleted, fuck that. 
        public bool Deleted { get; set; } = false; // first check - if deleted, no need to check other eventtypes (validity checks to see if it was created in a previous sync)
        public bool Moved { get; set; } = false; // second check - if not created before, create at new location
        public string? OriginalLocalPath { get; set; } = null;
        public bool Renamed { get; set; } = false; // third check - rename file (create file if not created yet)
        public bool Changed { get; set; } = false; // fourth check - upload final file (create file if not created yet)
        public bool Created { get; set; } = false; // fifth check - upload initial file (only if all previous checks are false)

        // A PendingSyncItem with a Deleted prop is instantly considered "complete" or terminal
        // All subsequent edits to an item with the same LocalPath needs to have a new PendingSyncItem created
        public bool IsTerminal()
        {
            return Deleted;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Unlock Logic: 
    // bool unlocked = await WaitForFileUnlockAsync(filePath, maxTimeoutMs: 5000, stoppingToken);
    // // Ensure file isn't currently locked by a process (e.g. Word, Photoshop)
    // if (!unlocked)
    // {
    //     // TODO: re-add to queue?? Maybe??
    //     _logger.LogWarning("File was locked by another process. Deferring: {Path}", filePath);
    //     return;
    // }
}