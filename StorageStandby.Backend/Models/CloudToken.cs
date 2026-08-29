// Models: simple C# classes that represent "things" in our app
// Each class becomes a table in SQL, and every property in the class becomes a Column

using Microsoft.EntityFrameworkCore;

namespace StorageStandby.Backend.Models
{
    // EF core: uses composite primary key on (ProviderName, AccountId)
    public class CloudToken
    {
        // EF core attributes; add unique index on (ProviderName, AccountId) to prevent accidental duplicates
        //[Index(nameof(ProviderName), nameof(AccountId), IsUnique = true)]
        // DEV: removed --> composite primary key was created instead in AppDbContext in OnModelCreating

        // e.g., "GoogleDrive", "OneDrive"
        public Providers ProviderName { get; set; } = Providers.None;

        // The token encrypted via Windows DPAPI before being saved
        public string EncryptedRefreshToken { get; set; } = string.Empty;

        // Tracks if the user needs to log in again (e.g., password changed)
        public ConnectionStatus Status { get; set; } = ConnectionStatus.Unconnected;

        public string AccountId { get; set; } = string.Empty;
        public string? Email { get; set; } = string.Empty;
        public string? AccountName { get; set; } = string.Empty;

        public DateTime LastUpdated { get; set; }
    }
}
