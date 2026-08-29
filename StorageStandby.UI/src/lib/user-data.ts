import * as React from "react"
import { fetchAllWatchedFolders, fetchWatchedFolder } from "@/api/data"

import type { Providers } from "@/types/providers"

// useSyncExternalStore gotchas:
// 1. __Never mutate snapshot in place.__ `useSyncExternalStore` detects changes by comparing the snapshot *reference*. Always build a new object (or new array) and replace `snapshot` wholesale — mutating and re-notifying returns the same reference, which React treats as "unchanged" (leading to stale renders or infinite loops).
// 2. __`getSnapshot` must return a stable reference when nothing changed.__ That's exactly why `setSnapshot` replaces the whole object instead of patching fields.
// 3. __Don't keep local copies in pages.__ The whole point is one source of truth — the same lesson from the Link refactor.

type UserProfile = {
    test: string
}

type UserDataSnapshot = {
    user: UserProfile | null
    quotas: Record<Providers, string> | null
    isLoading: boolean
    lastRefreshedAt: number | null
}

// React External Store "TRUTHS"
const listeners = new Set<() => void>()
let snapshot: UserDataSnapshot = { user: null, quotas: null, isLoading: false, lastRefreshedAt: null }

const setSnapshot = (updater: (current: UserDataSnapshot) => UserDataSnapshot) => {
    snapshot = updater(snapshot)   // ← ALWAYS replace the whole object, never mutate in place
    for (const l of listeners) l()
}

export const refreshUserData = async () => { /* fetch + setSnapshot in two phases, like connected-accounts */ }
export const updateUserData = (partial) => setSnapshot((s) => ({ ...s, user: { ...s.user, ...partial } }))

const subscribe = (listener: () => void) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
}

const getSnapshot = () => snapshot

export function useUserData() {
    const state = React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot)
    return state;
}
