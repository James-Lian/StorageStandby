// plain old JS alternative to enums
export const FOLDER_SYNC_STATUS = {
    Synced: 0,
    Pending: 1,
    Issue: 2,
} as const // makes every property readonly

export type FolderSyncStatus = typeof FOLDER_SYNC_STATUS[keyof typeof FOLDER_SYNC_STATUS]