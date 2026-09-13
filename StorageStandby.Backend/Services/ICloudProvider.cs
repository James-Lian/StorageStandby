using Microsoft.AspNetCore.DataProtection;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using System.Threading.Tasks;

public class AuthResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

namespace StorageStandby.Backend.Services
{
    public sealed class SyncResult
    {
        public SyncEventType Status { get; init; }
        public int Succeeded { get; init; }
        public int Failed { get; init; }
        public int Unfinished { get; init; }
    }
    public interface ICloudProvider
    {
        string ProviderName { get; }
        Task<SyncResult> ExecuteSyncQueueAsync();
        Task<long> GetRemainingStorageQuotaAsync();
        Task UploadAsync(string localFilePath, string remoteFolderPath);
        Task DeleteAsync(string remoteFileId);
        Task MoveAsync();
        Task StartOAuthAsync(
            AppDbContext db,
            IDataProtectionProvider dataProtector,
            IConfiguration config,
            HttpClient httpClient);
    }
}
