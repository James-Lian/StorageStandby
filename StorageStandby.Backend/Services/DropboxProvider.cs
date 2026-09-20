using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StorageStandby.Backend.Core;
using StorageStandby.Backend.Data;
using StorageStandby.Backend.Models;
using StorageStandby.Backend.Services;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace StorageStandby.Backend.Services
{
    public class DropboxProvider
    {
        private readonly AppDbContext _db;
        private readonly BackupEngineState _state;
        private readonly IDataProtectionProvider _dataProtector;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly TokenManager _tokenManager;
        public string ProviderName => "Dropbox";


        public DropboxProvider(
            AppDbContext db,
            BackupEngineState state,
            IDataProtectionProvider dataProtector,
            HttpClient httpClient,
            IConfiguration configuration,
            TokenManager tokenManager
        )
        {
            _db = db;
            _state = state;
            _dataProtector = dataProtector;
            _httpClient = httpClient;
            _configuration = configuration;
            _tokenManager = tokenManager;
        }

        
        // ----------------------------------------------------------
        // REST API
        // ----------------------------------------------------------

        // Gets the remaining storage quota for the authenticated Drive account.
        public async Task<StorageQuotaDto> GetRemainingStorageQuotaAsync(
            string accountId,
            CancellationToken cancellationToken = default)
        {
            // ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

            // string accessToken = await _tokenManager.GetValidAccessTokenAsync(
            //     Providers.Google,
            //     accountId);
            // using var driveService = BuildDriveClient(accessToken);
            // var about = await driveService.About.Get().ExecuteAsync(cancellationToken);

            // long limit = about.StorageQuota?.Limit ?? 0;
            // long usage = about.StorageQuota?.Usage ?? 0;
            // return new StorageQuotaDto{
            //     TotalBytes = limit,
            //     UsedBytes = usage,
            // };
        }

        // ----------------------------------------------------------
        // USERS/AUTH/SIGN-IN
        // ----------------------------------------------------------
        public async Task<List<ConnectedAccountDto>> GetConnectedAccounts()
        {
            return await _db.CloudTokens
                .Where(p => p.ProviderName == Providers.Google)
                .Select(account => new ConnectedAccountDto
                {
                    Id = account.AccountId,
                    Status = account.Status,
                    Email = string.IsNullOrEmpty(account.Email) ? null : account.Email,
                    Name = string.IsNullOrEmpty(account.AccountName) ? null : account.AccountName,
                })
                .ToListAsync();
        }

        public async Task<AuthResult> StartOAuthAsync()
        {
            // string clientId = _configuration["DropboxOAuth:ClientId"] 
            //     ?? throw new InvalidOperationException("Dropbox Client ID not configured.");
            // string clientSecret = _configuration["DropboxOAuth:ClientSecret"]
            //     ?? throw new InvalidOperationException("Dropbox Client Secret not configured.");
        
            // int port = GetAvailablePort(5431);
            // string redirectUri = $"http://localhost:{port}/callback";

            // // PCKE binds the authorization code to thiss authorization request
            // string codeVerifier = CreateCodeVerifier();
            // string codeChallenge = CreateCodeChallenge(codeVerifier);
            // string state = CreateCodeVerifier();

            // string scope = Uri.EscapeDataString();
        }

    }
}
