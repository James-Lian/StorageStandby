import type { Providers } from "./providers"

export type WatchedFolder = {
    id: number
    localPath: string | null
    assignedClouds: WatchedFolderCloudMetadata[]
    userAssignedClouds: WatchedFolderUserCloudAssignment[]
    userCloudRemovalAssignments: WatchedFolderUserCloudRemovalAssignment[]
    ignoreRules: string
    trackSnapshots: boolean
    snapshotUrls: string
    problem: number
    lastSync: string | null
    nextSyncAt: string | null
    dateAdded: string
    syncFrequency: SyncFrequency
    syncsToSkip: number
}

export const SyncIncrements = {
    Inactive: 0,
    Daily: 1,
    Weekly: 2,
    Monthly: 3,
} as const

export type SyncFrequency = {
    multiplier: number
    identifiers: number[]
    syncIncrements: number
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

export type WatchedFolderUserCloudAssignment = {
    id: number
    watchedFolderId: number
    provider: Providers
    accountId: string
}

export type WatchedFolderUserCloudRemovalAssignment = {
    id: number
    watchedFolderId: number
    provider: Providers
    accountId: string
}

export type SyncEvent = {
    id: number
    watchedFolderId: number
    scheduledTimestamp: string | null
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

export type PendingSyncItem = {
    id: number
    watchedFolderId: number
    localPath: string
    isFolder: boolean
    deleted: boolean
    moved: boolean
    originalLocalPath: string | null
    renamed: boolean
    changed: boolean
    created: boolean
}

export type DirectoryNode = {
    name: string
    fullPath: string
    isDirectory: boolean
    children: (DirectoryNode | null)[]
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
}
