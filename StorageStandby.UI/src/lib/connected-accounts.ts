import * as React from "react"
import { PROVIDERS, type Providers } from "@/types/providers"
import { fetchConnectedAccounts, type ConnectedAccount } from "@/api/accounts"

// DEV: store in React is a centralized data container outside of React
// DEV: value returned by a store via getSnapshot in useSyncExternalStore OR React's immutable view of state at a specific point in time

// describes complete piece of state shared by every component that uses useConnectedAccounts
// runtime state is held in snapshot variable; ensuring one sole source of truth and preventing components from making their own individual reqs
type ConnectedAccountsSnapshot = {
    accountsByProvider: Record<Providers, ConnectedAccount[]>
    isLoading: boolean
    lastRefreshedAt: number | null
}

// React components subscribe to this set through React.useSyncExternalStore() and the subscribe() function
// the Set avoids duplicate subscription, and makes removing a listener simple
const listeners = new Set<() => void>()

// store starts with an empty array
let snapshot: ConnectedAccountsSnapshot = {
    accountsByProvider: {
        [PROVIDERS.Google]: [],
        [PROVIDERS.Microsoft]: [],
        [PROVIDERS.Dropbox]: [],
    },
    isLoading: false,
    lastRefreshedAt: null,
}

// tell every subscribed React component that the external store changed
const notifyListeners = () => {
    for (const listener of listeners) {
        listener()
    }
}

// all store updates use this helper
// updater receives current snapshot, returns the next one, and then subscribers are notified so React can read new value and re-render
const setSnapshot = (updater: (current: ConnectedAccountsSnapshot) => ConnectedAccountsSnapshot) => {
    snapshot = updater(snapshot) // this line creates a new object, which tells React something has changed because it compares w/ reference equality
    notifyListeners()
}

// fetch the account lists for all supported providers, publish them as one update
// Promise.all starts three requests concurrently and only replaces store after all three have completed
export const refreshConnectedAccounts = async () => {
    // tell UI a refresh is in progress
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
        // clear loading flag so UI is not stuck loading
        setSnapshot((current) => ({ ...current, isLoading: false }))
    }
}

// register component's callback and return cleanup function required by useSyncExternalStore when that component unmounts
const subscribe = (listener: () => void) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
}

// useSyncExternalStore calls this to read the current store value during rendering
// (for other applications, it would also use this for server-side rendering)
const getSnapshot = () => snapshot

export function useConnectedAccounts() {
    const state = React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot)

    // derive convenient per-provider/total counts from account lists
    // useMemo avoids recalculating them when a render happens for an unrelated reason, and only recalculates when state.accountsByProvider changes
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