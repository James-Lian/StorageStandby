import { logApiProblemDetailsError, readErrorDetails } from "@/lib/read-errors-utils";
import { ErrorHandlingWrapper } from "./errorhandling";

export async function openInDefaultBrowser(url: string, fName: string = "openInDefaultBrowser") {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch(`http://storagestandby.local/api/open-url`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
            },
            body: JSON.stringify({ url }),
        });

        if (!response.ok) {
            const responseFailTitle = `${fName}: Response invalid`
            const details = await readErrorDetails(response);
            logApiProblemDetailsError({
                fallbackTitle: responseFailTitle,
                details
            })
            throw new Error(responseFailTitle);
        }
    })
}

export async function fetchIgnoredPaths(
    basePath: string,
    paths: string[],
    ignoreRules: string,
    fName: string = "fetchIgnoredPaths",
): Promise<boolean[]> {
    return ErrorHandlingWrapper(fName, async () => {
        const response = await fetch("http://storagestandby.local/api/fileSystem/ignored", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ basePath, paths, ignoreRules }),
        })

        if (!response.ok) {
            throw new Error(`${fName}: Failed to match ignore rules`)
        }

        return await response.json() as boolean[]
    })
}