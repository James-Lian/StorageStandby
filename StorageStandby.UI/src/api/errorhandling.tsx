import { logError } from "@/lib/notifications";

// standard error handling wrapper function for API calls
export async function ErrorHandlingWrapper<T>(fName: string, f: () => Promise<T>): Promise<T> {
    try {
        return await f();
    } 
    catch (error) {
        if (error instanceof Error && error.message.startsWith(fName)) {
            throw error
        }

        logError({
            title: `${fName}: API error`,
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