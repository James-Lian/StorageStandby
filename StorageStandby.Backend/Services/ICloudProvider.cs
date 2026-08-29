using Microsoft.AspNetCore.DataProtection;
using StorageStandby.Backend.Data;
using System.Threading.Tasks;

public class AuthResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

namespace StorageStandby.Backend.Services
{
    public interface ICloudProvider
    {
        string ProviderName { get; }
        Task<long> GetRemainingStorageQuotaAsync();
        Task UploadFileAsync(string localFilePath, string remoteFolderPath);
        Task DeleteFileAsync(string remoteFileId);
        Task StartOAuthAsync(
            AppDbContext db,
            IDataProtectionProvider dataProtector,
            IConfiguration config,
            HttpClient httpClient);
    }
}
