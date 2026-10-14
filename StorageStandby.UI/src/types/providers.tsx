import { Box, Folder, HardDrive, type LucideIcon } from "lucide-react"

// plain old JS alternative to enums
export const PROVIDERS = {
    Google: 0,
    Microsoft: 1,
    Dropbox: 2,
} as const // makes every property readonly

export type Providers = typeof PROVIDERS[keyof typeof PROVIDERS]

// typeof PROVIDERS: Gets the TypeScript type representation of the object shape.
// keyof typeof PROVIDERS: Extracts the keys of that object shape as a union of strings: "Google" | "Microsoft" | "Dropbox".
// typeof PROVIDERS[...]: Indexes into the object type using those keys. This extracts the actual values tied to those keys.

export type ProviderMetadata = {
    providerStr: string
    providerName: string
    displayName: string
    description: string
    icon: LucideIcon
}

export const PROVIDER_METADATA: Record<Providers, ProviderMetadata> = {
    [PROVIDERS.Google]: {
        providerStr: "google",
        providerName: "Google",
        displayName: "Google Drive",
        description: "Connect Google Drive to sync your documents, spreadsheets, and shared folders.",
        icon: Folder,
    },
    [PROVIDERS.Microsoft]: {
        providerStr: "microsoft",
        providerName: "Microsoft",
        displayName: "OneDrive",
        description: "Connect OneDrive to keep your files available across desktop and cloud.",
        icon: HardDrive
    },
    [PROVIDERS.Dropbox]: {
        providerStr: "dropbox",
        providerName: "Dropbox",
        displayName: "Dropbox",
        description: "Connect Dropbox to access your shared files and folder backups.",
        icon: Box
    },
}

export const PROVIDER_ORDER: Providers[] = [PROVIDERS.Google, PROVIDERS.Microsoft, PROVIDERS.Dropbox]

export const getProviderDisplayName = (provider: Providers) => PROVIDER_METADATA[provider].displayName
export const getProviderDescription = (provider: Providers) => PROVIDER_METADATA[provider].description
