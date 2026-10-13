import * as React from "react"
import { fetchSyncEvents } from "@/api/data"
import type { SyncEvent } from "@/types/data"

export type SyncEventsSnapshot = {
    events: SyncEvent[]
    isLoading: boolean
    error: Error | null
    lastRefreshedAt: number | null
}

const listeners = new Set<() => void>()
let snapshot: SyncEventsSnapshot = { events: [], isLoading: false, error: null, lastRefreshedAt: null }
const notifyListeners = () => listeners.forEach((listener) => listener())
const setSnapshot = (updater: (current: SyncEventsSnapshot) => SyncEventsSnapshot) => {
    snapshot = updater(snapshot)
    notifyListeners()
}

export const refreshSyncEvents = async () => {
    setSnapshot((current) => ({ ...current, isLoading: true, error: null }))
    try {
        const events = await fetchSyncEvents()
        setSnapshot((current) => ({ ...current, events, isLoading: false, lastRefreshedAt: Date.now() }))
    } catch (error) {
        setSnapshot((current) => ({
            ...current,
            isLoading: false,
            error: error instanceof Error ? error : new Error("Failed to fetch sync events"),
        }))
    }
}

const subscribe = (listener: () => void) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
}
const getSnapshot = () => snapshot

export const useSyncEvents = () =>
    React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot)
