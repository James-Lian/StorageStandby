// plain old JS alternative to enums
export const FOLDER_SYNC_STATUS = {
    Synced: 0,
    Pending: 1,
    Issue: 2,
} as const // makes every property readonly

export type FolderSyncStatus = typeof FOLDER_SYNC_STATUS[keyof typeof FOLDER_SYNC_STATUS]

export const PROBLEMATIC_FOLDER_TYPE = {
    None: 0,
    TooLarge: 1,
    TooManyFiles: 2,
    PathNotFound: 3,
    Other: 4,
    NestedWatchedFolder: 5,
} as const

export type ProblematicFolderType =
    typeof PROBLEMATIC_FOLDER_TYPE[keyof typeof PROBLEMATIC_FOLDER_TYPE]

export const CLOUD_ASSIGNMENT_STATE = {
    PendingBootstrap: 0,
    Bootstrapping: 1,
    Active: 2,
    Failed: 3,
    Removing: 4,
    Unlinked: 5,
} as const

export type CloudAssignmentState =
    typeof CLOUD_ASSIGNMENT_STATE[keyof typeof CLOUD_ASSIGNMENT_STATE]