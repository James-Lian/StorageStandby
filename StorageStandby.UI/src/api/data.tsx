import { PROVIDER_METADATA, type Providers } from "@/types/providers";
import { logError } from "@/lib/notifications";
import { logApiProblemDetailsError, readErrorDetails } from "@/lib/read-errors-utils";

// ---------------------------------------------------------------------------
// WATCHED FOLDERS
// ---------------------------------------------------------------------------

export async function fetchAllWatchedFolders() {
    try {
        const response = await fetch(`http://storagestandby.local/api/folders`);

        if (!response.ok) {
            const details = await readErrorDetails(response);
            logApiProblemDetailsError({
                fallbackTitle: "Response invalid; Failed to fetch all watched folders",
                details
            })
            throw new Error("fetchAllWatchedFolders: Failed to fetch all watched folders");
        }

        const data = await response.json();

        return data;
    }
    catch (error) {
        if (error instanceof Error && error.message.startsWith("fetchAllWatchedFolders:")) {
            throw error
        }

        logError({
            title: "fetchAllWatchedFolders: Failed to fetch all watched folders",
            details: error instanceof Error
                ? {
                    name: error.name,
                    message: error.message,
                    stack: error.stack,
                    cause: error.cause,
                }
                : error,
        })

        throw error
    }
}

export async function fetchWatchedFolder(localPath: string) {
    try {
        const response = await fetch(`http://storagestandby.local/api/folders/${localPath}`);

        if (!response.ok) {
            const details = await readErrorDetails(response);
            logApiProblemDetailsError({
                fallbackTitle: "Response invalid; Failed to fetch all watched folders",
                details
            })
            throw new Error("fetchAllWatchedFolders: Failed to fetch all watched folders");
        }

        const data = await response.json();

        return data;
    }
    catch (error) {
        if (error instanceof Error && error.message.startsWith("fetchAllWatchedFolders:")) {
            throw error
        }

        logError({
            title: "fetchAllWatchedFolders: Failed to fetch all watched folders",
            details: error instanceof Error
                ? {
                    name: error.name,
                    message: error.message,
                    stack: error.stack,
                    cause: error.cause,
                }
                : error,
        })

        throw error
    }
}

export async function fetchFolderIgnoreRules() {

}

export async function editFolderIgnoreRules(localPath: string, newIgnoreRules: string) {

}

// ---------------------------------------------------------------------------
// SETTINGS
// ---------------------------------------------------------------------------

export async function fetchGlobalIgnoreRules() {

}

export async function editGlocalIgnoreRules() {

}