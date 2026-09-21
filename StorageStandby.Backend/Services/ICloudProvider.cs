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
        Task<SyncResult> ExecuteSyncQueueAsync(
            SyncEvent syncEvent,
            string accountId,
            IReadOnlyList<PendingSyncItem> queue,
            CancellationToken cancellationToken = default);
        Task<List<ConnectedAccountDto>> GetConnectedAccounts();
        Task<StorageQuotaDto> GetRemainingStorageQuotaAsync(
            string accountId, 
            CancellationToken cancellationToken = default);
        Task<AuthResult> StartOAuthAsync();
    }
    internal static class SyncEventPathHelper
    {
        // Adds a local path to a semicolon-delimited event path list.
        public static string AppendPath(string current, string path)
        {
            return string.IsNullOrEmpty(current)
                ? path
                : $"{current};{path}";
        }

        // Removes a local path from a semicolon-delimited event path list.
        public static string RemovePath(string current, string path)
        {
            if (string.IsNullOrEmpty(current)) return string.Empty;
            
            return string.Join(";", current
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
