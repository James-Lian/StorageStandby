import * as React from "react"
import { fetchAllWatchedFolders } from "@/api/data"
import type { WatchedFolder } from "@/types/data"

// type for snapshot
export type WatchedFoldersSnapshot = {
    folders: WatchedFolder[]
    isLoading: boolean
    error: Error | null
    lastRefreshedAt: number | null
}

// listeners and snapshot declaration
const listeners = new Set<() => void>()
let snapshot: WatchedFoldersSnapshot = { folders: [], isLoading: false, error: null, lastRefreshedAt: null }

// for each listener, call callback function
const notifyListeners = () => 
    listeners.forEach((listener) => listener())

// replace object with new reference for reference equality checks, and then notify listeners
const setSnapshot = (updater: (current: WatchedFoldersSnapshot) => WatchedFoldersSnapshot) => {
    snapshot = updater(snapshot)
    notifyListeners()
}

// refresh snapshot with backend fetch
export const refreshWatchedFolders = async () => {
    setSnapshot((current) => ({ ...current, isLoading: true, error: null }))
    try {
        const folders = await fetchAllWatchedFolders()
        setSnapshot((current) => ({ ...current, folders, isLoading: false, lastRefreshedAt: Date.now() }))
    } catch (error) {
        setSnapshot((current) => ({
            ...current,
            isLoading: false,
            error: error instanceof Error ? error : new Error("Failed to fetch watched folders"),
        }))
    }
}

// subscribe to listener set, and return cleanup function
const subscribe = (listener: () => void) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
}
const getSnapshot = () => snapshot

export const useWatchedFolders = () =>
    React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot)