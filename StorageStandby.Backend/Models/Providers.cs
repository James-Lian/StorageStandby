using System.Collections.Frozen;

namespace StorageStandby.Backend.Models
{
    public enum Providers
    {
        None = -1,
        Google = 0,
        Microsoft = 1,
        Dropbox = 2,
        pDrive = 3
    }
    public enum ConnectionStatus
    {
        Unconnected = 0,
        Connected = 1,
        Reauthenticate = 2
    }

    public class ProviderAccountCloud
    {
        public Providers Provider { get; set; }
        public string AccountId { get; set; } = string.Empty;
    }

    public static class ProviderMetadata
    {
        public static readonly FrozenDictionary<Providers, string> ProviderNames = new Dictionary<Providers, string>
        {
            { Providers.Google, "Google" },
            { Providers.Microsoft, "Microsoft" },
            { Providers.Dropbox, "Dropbox" },
            { Providers.pDrive, "pDrive" }
        }.ToFrozenDictionary();

        public static readonly FrozenDictionary<Providers, string> TokenEndpoints = new Dictionary<Providers, string>
        {
            { Providers.Google, "https://oauth2.googleapis.com/token" },
            { Providers.Microsoft, "https://login.microsoftonline.com/common/oauth2/v2.0/token" },
            { Providers.Dropbox, "" },
            { Providers.pDrive, "" }

        }.ToFrozenDictionary();

        public static readonly FrozenDictionary<Providers, string> TokenRevocationEndpoints = new Dictionary<Providers, string>
        {
            { Providers.Google, "https://oauth2.googleapis.com/revoke" },
            { Providers.Microsoft, "" },
            { Providers.Dropbox, "" },
            { Providers.pDrive, "" }
        }.ToFrozenDictionary();

    }
}
