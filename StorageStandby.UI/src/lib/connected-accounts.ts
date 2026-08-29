import * as React from "react"
import { PROVIDERS, type Providers } from "@/types/providers"
import { fetchConnectedAccounts, type ConnectedAccount } from "@/api/accounts"

type ConnectedAccountsSnapshot = {
    accountsByProvider: Record<Providers, ConnectedAccount[]>
    isLoading: boolean
    lastRefreshedAt: number | null
}

const listeners = new Set<() => void>()
let snapshot: ConnectedAccountsSnapshot = {
    accountsByProvider: {
        [PROVIDERS.Google]: [],
        [PROVIDERS.Microsoft]: [],
        [PROVIDERS.Dropbox]: [],
    },
    isLoading: false,
    lastRefreshedAt: null,
}

const notifyListeners = () => {
    for (const listener of listeners) {
        listener()
    }
}

const setSnapshot = (updater: (current: ConnectedAccountsSnapshot) => ConnectedAccountsSnapshot) => {
    snapshot = updater(snapshot)
    notifyListeners()
}

export const refreshConnectedAccounts = async () => {
    setSnapshot((current) => ({ ...current, isLoading: true }))

    try {
        const [googleAccounts, microsoftAccounts, dropboxAccounts] = await Promise.all([
            fetchConnectedAccounts(PROVIDERS.Google),
            fetchConnectedAccounts(PROVIDERS.Microsoft),
            fetchConnectedAccounts(PROVIDERS.Dropbox),
        ])

        setSnapshot(() => ({
            accountsByProvider: {
                [PROVIDERS.Google]: googleAccounts,
                [PROVIDERS.Microsoft]: microsoftAccounts,
                [PROVIDERS.Dropbox]: dropboxAccounts,
            },
            isLoading: false,
            lastRefreshedAt: Date.now(),
        }))
    } catch {
        setSnapshot((current) => ({ ...current, isLoading: false }))
    }
}

const subscribe = (listener: () => void) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
}

const getSnapshot = () => snapshot

export function useConnectedAccounts() {
    const state = React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot)

    // useMemo caches result of counts from previous rerenders. If dependency array experiences changes, only then will we recalculate
    const counts = React.useMemo(
        () => ({
            [PROVIDERS.Google]: state.accountsByProvider[PROVIDERS.Google].length,
            [PROVIDERS.Microsoft]: state.accountsByProvider[PROVIDERS.Microsoft].length,
            [PROVIDERS.Dropbox]: state.accountsByProvider[PROVIDERS.Dropbox].length,
            total:
                state.accountsByProvider[PROVIDERS.Google].length +
                state.accountsByProvider[PROVIDERS.Microsoft].length +
                state.accountsByProvider[PROVIDERS.Dropbox].length,
        }),
        [state.accountsByProvider]
    )

    return {
        accountsByProvider: state.accountsByProvider,
        counts,
        isLoading: state.isLoading,
        lastRefreshedAt: state.lastRefreshedAt,
    }
}