import * as React from "react"
import { fetchPendingSyncItems } from "@/api/data"
import { type PendingSyncItem } from "@/types/data"

export type PendingSyncItemsSnapshot = {
    items: PendingSyncItem[],
    isLoading: boolean,
    error: Error | null,
    lastRefreshedAt: number | null,
}

const listeners = new Set<() => void>()
let snapshot: PendingSyncItemsSnapshot = {
    items: [],
    isLoading: false,
    error: null,
    lastRefreshedAt: null
}

const notifyListeners = () => listeners.forEach((listener) => listener());
const setSnapshot = (updater: (current: PendingSyncItemsSnapshot) => PendingSyncItemsSnapshot) => {
    snapshot = updater(snapshot)
    notifyListeners()
}

export const refreshPendingSyncItems = async () => {
    setSnapshot((current) => ({
        ...current,
        error: null,
        isLoading: true,
    }))
    try {
        const items = await fetchPendingSyncItems()
        setSnapshot((current) => ({
            ...current,
            isLoading: false,
            items,
            lastRefreshedAt: Date.now(),
        }))
    } catch (error) {
        setSnapshot((current) => ({
            ...current,
            isLoading: false,
            error: error instanceof Error ? error : new Error("Failed to fetch pending sync items"),
            lastRefreshedAt: Date.now(),
        }))
    }

}


const subscribe = (listener: () => void) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
}
const getSnapshot = () => snapshot

export const usePendingSyncItems = () =>
    React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot)