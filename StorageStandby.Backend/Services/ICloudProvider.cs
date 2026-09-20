using Microsoft.AspNetCore.DataProtection;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using System.Threading.Tasks;


namespace StorageStandby.Backend.Services
{
    public class AuthResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }
    public sealed class SyncResult
    {
        public SyncEventType Status { get; init; }
        public int Succeeded { get; init; }
        public int Failed { get; init; }
        public int Unfinished { get; init; }
    }

    public sealed class ConnectedAccountDto
    {
        public string Id { get; init; } = string.Empty;
        public ConnectionStatus Status { get; init; }
        public string? Email { get; init; }
        public string? Name { get; init; }
    }
    public sealed class StorageQuotaDto
    {
        public long TotalBytes { get; init; }
        public long UsedBytes { get; init; }
        public long RemainingBytes => TotalBytes - UsedBytes;
    }
    public interface ICloudProvider
    {
        string ProviderName { get; }
        Task<SyncResult> ExecuteSyncQueueAsync();
        Task ExecuteItemAsync();
        Task<List<ConnectedAccountDto>> GetConnectedAccounts();
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
