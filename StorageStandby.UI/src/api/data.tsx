import { PROVIDER_METADATA, type Providers } from "@/types/providers";
import { logError } from "@/lib/notifications";
import { logApiProblemDetailsError, readErrorDetails } from "@/lib/read-errors-utils";
import { ErrorHandlingWrapper } from "./errorhandling";

// ---------------------------------------------------------------------------
// WATCHED FOLDERS
// ---------------------------------------------------------------------------

export async function fetchAllWatchedFolders(fName: string = "fetchAllWatchedFolders") {
    ErrorHandlingWrapper(fName, async () => {
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

export async function fetchWatchedFolder(localPath: string, fName: string = "fetchWatchedFolder") {
    ErrorHandlingWrapper(fName, async () => {
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
    })
}

// export async function fetchWatchedFolderProp() {
//     try {
//         const response = await fetch(`http://storagestandby.local/api/folders`);

//         if (!response.ok) {
//             const details = await readErrorDetails(response);
//             logApiProblemDetailsError({
//                 fallbackTitle: "Response invalid; Failed to fetch all watched folders",
//                 details
//             })
//             throw new Error("fetchAllWatchedFolders: Failed to fetch all watched folders");
//         }

//         const data = await response.json();

//         return data;        
//     } catch (error) {
//         if (error instanceof Error && error.message.startsWith("fetchAllWatchedFolders:")) {
//             throw error
//         }

//         logError({
//             title: "fetchAllWatchedFolders: Failed to fetch all watched folders",
//             details: error instanceof Error
//                 ? {
//                     name: error.name,
//                     message: error.message,
//                     stack: error.stack,
//                     cause: error.cause,
//                 }
//                 : error,
//         })

//         throw error
//     }
// }

export async function fetchFolderIgnoreRules() {
    try {
        
    }
    catch (error) {
        if (error instanceof Error && error.message.startsWith("fetchFolderIgnoreRules:")) {
            throw error
        }

        logError({
            title: "fetchFolderIgnoreRules: Failed to fetch all watched folders",
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

export async function editFolderIgnoreRules(localPath: string, newIgnoreRules: string) {

}

// ---------------------------------------------------------------------------
// SETTINGS
// ---------------------------------------------------------------------------

export async function fetchGlobalIgnoreRules() {

}

export async function editGlocalIgnoreRules() {

}