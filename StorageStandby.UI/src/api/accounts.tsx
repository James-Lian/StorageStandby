import { PROVIDER_METADATA, type Providers } from "@/types/providers";
import { logError, notifySuccess } from "@/lib/notifications";
import { readErrorDetails, logProviderProblemDetailsError } from "@/lib/read-errors-utils";

export interface ConnectedAccount {
  id: string;
  status: number;
  email?: string;
  name?: string;
}

export async function fetchConnectedAccounts(
    provider: Providers, 
    callbackFn?: (data: ConnectedAccount[]) => void
): Promise<ConnectedAccount[]> {
    try {
        console.log("fetchConnectedAccounts: Start function call.")
        const response = await fetch(`http://storagestandby.local/api/auth/${PROVIDER_METADATA[provider].providerStr}/accounts`);
        
        if (!response.ok) {
            const details = await readErrorDetails(response)
            logProviderProblemDetailsError({
                fallbackTitle: "Response invalid; Failed to fetch accounts",
                providerDisplayName: PROVIDER_METADATA[provider].displayName,
                responseStatus: response.status,
                details,
            })
            throw new Error("fetchConnectedAccounts: Failed to fetch accounts.");
        }

        console.log("fetchConnectedAccounts: Response ok.")
    
        const data = await response.json();
        console.log("fetchConnectedAccounts: Data json'd.")

        if (callbackFn) {
            callbackFn(data);
        }
        return data;
    }
    catch (error) {
        if (error instanceof Error && error.message.startsWith("fetchConnectedAccounts:")) {
            throw error
        }

        logError({
            title: "fetchConnectedAccounts: Failed to fetch accounts",
            summary: `Provider: ${PROVIDER_METADATA[provider].providerName}`,
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

export async function startOAuthFlow(
    provider: Providers,
    // callbackFn?: (error?: Error) => void
) {     
    try {
        console.log("startOAuthFlow: Start function call.")
        const response = await fetch(`http://storagestandby.local/api/auth/${PROVIDER_METADATA[provider].providerStr}/start`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
            }
        });
    
        if (!response.ok) {
            const details = await readErrorDetails(response)
            logProviderProblemDetailsError({
                fallbackTitle: "Response invalid; Failed to start OAuth",
                providerDisplayName: PROVIDER_METADATA[provider].providerName,
                responseStatus: response.status,
                details,
            })
            throw new Error("startOAuthFlow: Failed to start OAuth ")
        }
        console.log("startOAuthFlow: Response ok.")
        notifySuccess({
            title: `${PROVIDER_METADATA[provider].providerName} authorization success!`
        })
    }
    catch (error) {
        if (error instanceof Error && error.message.startsWith("startOAuthFlow:")) {
            throw error
        }

        logError({
            title: "startOAuthFlow: Failed to start OAuth",
            summary: `Provider: ${PROVIDER_METADATA[provider].providerName}`,
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

export async function revokeOAuthFlow(
    provider: Providers, 
    accountId: string,
    // callbackFn?: (error?: Error) => void
) {
    try {
        console.log("revokeOAuthFlow: Start function call.")
        const response = await fetch(`http://storagestandby.local/api/auth/${PROVIDER_METADATA[provider].providerStr}/revoke`, 
        {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
            },
            body: JSON.stringify({
                accountId: accountId
            })
        });
    
        if (!response.ok) {
            const details = await readErrorDetails(response)
            logProviderProblemDetailsError({
                fallbackTitle: "Retrieved response; Failed to revoke OAuth",
                providerDisplayName: PROVIDER_METADATA[provider].providerName,
                responseStatus: response.status,
                details,
            })
            throw new Error("revokeOAuthFlow: Failed to revoke OAuth ")
        }
        console.log("revokeOAuthFlow: Response ok.")
        notifySuccess({
            title: `${PROVIDER_METADATA[provider].providerName} authorization successfully revoked.`
        })
    } catch (error) {
        if (error instanceof Error && error.message.startsWith("revokeOAuthFlow:")) {
            throw error
        }

        logError({
            title: "Failed to revoke OAuth",
            summary: `Provider: ${PROVIDER_METADATA[provider].providerName}, AccoundId: ${accountId}`,
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
