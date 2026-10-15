import { logError } from "@/lib/notifications";
import { logApiProblemDetailsError, readErrorDetails } from "@/lib/read-errors-utils";
import { ErrorHandlingWrapper } from "./errorhandling";
import type { SyncEvent, SystemSettings, WatchedFolder } from "@/types/data";

// ---------------------------------------------------------------------------
// WATCHED FOLDERS
// ---------------------------------------------------------------------------

export async function fetchAllWatchedFolders(fName: string = "fetchAllWatchedFolders"): Promise<WatchedFolder[]> {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch(`http://storagestandby.local/api/folders`);

        if (!response.ok) {
            const responseFailTitle = `${fName}: Response invalid`
            const details = await readErrorDetails(response);
            logApiProblemDetailsError({
                fallbackTitle: responseFailTitle,
                details
            })
            throw new Error(responseFailTitle);
        }

        const data = await response.json();

        return data;
    })
}

export async function fetchWatchedFolder(localPath: string, fName: string = "fetchWatchedFolder"): Promise<WatchedFolder> {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch(`http://storagestandby.local/api/folders/${localPath}`);

        if (!response.ok) {
            const details = await readErrorDetails(response);
            const responseFailTitle = `${fName}: response invalid`;
            logApiProblemDetailsError({
                fallbackTitle: responseFailTitle,
                details
            })
            throw new Error(responseFailTitle);
        }

        const data = await response.json();

        return data;
    })
}

export async function addFolderCloud(
    folderId: number,
    cloud: { provider: number; accountId: string },
    fName: string = "addFolderCloud",
): Promise<void> {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch(`http://storagestandby.local/api/folders/${folderId}/clouds`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(cloud),
        });
        if (!response.ok) {
            const details = await readErrorDetails(response);
            logApiProblemDetailsError({ fallbackTitle: `${fName}: response invalid`, details });
            throw new Error(`${fName}: Failed to request cloud assignment`);
        }
    });
}

export async function removeFolderCloud(
    folderId: number,
    cloud: { provider: number; accountId: string },
    fName: string = "removeFolderCloud",
): Promise<void> {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch(
            `http://storagestandby.local/api/folders/${folderId}/clouds/${cloud.provider}/${encodeURIComponent(cloud.accountId)}`,
            { method: "DELETE" },
        );
        if (!response.ok) {
            const details = await readErrorDetails(response);
            logApiProblemDetailsError({ fallbackTitle: `${fName}: response invalid`, details });
            throw new Error(`${fName}: Failed to remove cloud assignment`);
        }
    });
}

export async function resyncFolder(
    folderId: number,
    fName: string = "resyncFolder"
): Promise<void> {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch(
            `http://storagestandby.local/api/folders/${folderId}/resync`,
            { method: "POST" }
        )

        if (!response.ok) {
            const details = await readErrorDetails(response);
            logApiProblemDetailsError({ fallbackTitle: `${fName}: response invalid`, details });
            throw new Error(`${fName}: Failed to resync folder`);
        }
    })
}

export async function reconcileFolderIgnoreRules(
    folderId: number, 
    oldIgnoreRules: string,
    newIgnoreRules: string,
    fName: string = "reconcileFolderIgnoreRules"
): Promise<void> {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch(
            `http://storagestandby.local/api/folders/${folderId}/ignore-rules/reconcile`,
            { 
                method: "POST",
                headers: {"Content-Type": "application/json"},
                body: JSON.stringify({OldIgnoreRules: oldIgnoreRules, NewIgnoreRules: newIgnoreRules})
            }
        )

        if (!response.ok) {
            const details = await readErrorDetails(response);
            logApiProblemDetailsError({ fallbackTitle: `${fName}: response invalid`, details });
            throw new Error(`${fName}: fail`);
        }
    })
}

// ---------------------------------------------------------------------------
// SETTINGS
// ---------------------------------------------------------------------------


export async function fetchSystemSettings(fName: string = "fetchSystemSettings"): Promise<SystemSettings> {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch(`http://storagestandby.local/api/settings`);
        if (!response.ok) {
            const responseFailTitle = `${fName}: response invalid`;
            const details = await readErrorDetails(response);
            logApiProblemDetailsError({ fallbackTitle: responseFailTitle, details });
            throw new Error(responseFailTitle);
        }
        return await response.json() as SystemSettings;
    });
}

export async function fetchSyncEvents(fName: string = "fetchSyncEvents"): Promise<SyncEvent[]> {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch(`http://storagestandby.local/api/syncevents`);
        if (!response.ok) {
            const details = await readErrorDetails(response);
            logApiProblemDetailsError({ fallbackTitle: `${fName}: Response invalid`, details });
            throw new Error(`${fName}: Failed to fetch sync events`);
        }
        return await response.json() as SyncEvent[];
    });
}

// ---------------------------------------------------------------------------
// FILESYSTEM
// ---------------------------------------------------------------------------

export async function fetchOpenExplorer(folderPath: string, fName: string = "fetchOpenExplorer"): Promise<void> {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch(`http://storagestandby.local/api/fileSystem/open-explorer`, {
            method: "POST",
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({ localPath: folderPath }),
        });

        if (!response.ok) {
            const details = await readErrorDetails(response);
            const responseFailTitle = `${fName}: response invalid`;
            logApiProblemDetailsError({
                fallbackTitle: responseFailTitle,
                details
            })
            throw new Error(responseFailTitle);
        }

    })
}