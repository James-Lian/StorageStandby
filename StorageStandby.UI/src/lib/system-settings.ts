import * as React from "react"
import { fetchSystemSettings } from "@/api/data"
import type { SystemSettings } from "@/types/data"

export type SystemSettingsSnapshot = {
    settings: SystemSettings | null
    isLoading: boolean
    error: Error | null
    lastRefreshedAt: number | null
}

const listeners = new Set<() => void>()
let snapshot: SystemSettingsSnapshot = { settings: null, isLoading: false, error: null, lastRefreshedAt: null }
const notifyListeners = () => listeners.forEach((listener) => listener())
const setSnapshot = (updater: (current: SystemSettingsSnapshot) => SystemSettingsSnapshot) => {
    snapshot = updater(snapshot)
    notifyListeners()
}

export const refreshSystemSettings = async () => {
    setSnapshot((current) => ({ ...current, isLoading: true, error: null }))
    try {
        const settings = await fetchSystemSettings()
        setSnapshot((current) => ({ ...current, settings, isLoading: false, lastRefreshedAt: Date.now() }))
    } catch (error) {
        setSnapshot((current) => ({
            ...current,
            isLoading: false,
            error: error instanceof Error ? error : new Error("Failed to fetch system settings"),
        }))
    }
}

const subscribe = (listener: () => void) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
}
const getSnapshot = () => snapshot

export const useSystemSettings = () =>
    React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot)
