import type { Providers } from "./providers"

export type WatchedFolder = {
    id: number
    localPath: string | null
    assignedClouds: WatchedFolderCloudMetadata[]
    userAssignedClouds: ProviderAccountCloud[]
    ignoreRules: string
    trackSnapshots: boolean
    snapshotUrls: string
    problem: number
    lastSync: string | null
    dateAdded: string
}

export type WatchedFolderCloudMetadata = {
    id: number
    provider: Providers
    accountId: string
    remoteFolderId: string
    watchedFolderId: number
    configFileId: string | null
    state: number
    lastError: string | null
}

export type ProviderAccountCloud = {
    provider: number
    accountId: string
}

export type SyncEvent = {
    id: number
    watchedFolderId: number
    completedTimestamp: string | null
    completionType: number
    provider: number
    accountId: string
    syncedItems: string
    syncedItemIds: string
    failedItems: FailedItemDetails[]
    unfinishedItems: string
    unfinishedItemIds: string
}

export type FailedItemDetails = {
    failedItem: string
    details: string
}

export type SystemSettings = {
    id: number
    isSyncPaused: boolean
    pauseSyncUntil: string | null
    syncEvents: SyncEvent[]
    automaticSyncs: boolean
    automaticSyncIntervalMinutes: number
}
