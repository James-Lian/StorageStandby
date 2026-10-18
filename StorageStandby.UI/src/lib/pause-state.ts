import * as React from "react"
import { fetchSyncStatus, setSyncPause } from "@/api/data"

export type SyncDisplayState = "Paused" | "Standby" | "Running"

export type PauseStateSnapshot = {
    isPaused: boolean
    isUploading: boolean
    isLoading: boolean
    error: Error | null
    lastRefreshedAt: number | null
}

const listeners = new Set<() => void>()
let snapshot: PauseStateSnapshot = {
    isPaused: false,
    isUploading: false,
    isLoading: false,
    error: null,
    lastRefreshedAt: null,
}

const notifyListeners = () => listeners.forEach((listener) => listener())
const setSnapshot = (updater: (current: PauseStateSnapshot) => PauseStateSnapshot) => {
    snapshot = updater(snapshot)
    notifyListeners()
}

export const refreshPauseState = async () => {
    setSnapshot((current) => ({ ...current, isLoading: true, error: null }))
    try {
        const status = await fetchSyncStatus()
        setSnapshot((current) => ({
            ...current,
            isPaused: status.isPaused,
            isUploading: status.isUploading,
            isLoading: false,
            lastRefreshedAt: Date.now(),
        }))
    } catch (error) {
        setSnapshot((current) => ({
            ...current,
            isLoading: false,
            error: error instanceof Error ? error : new Error("Failed to fetch pause state"),
        }))
    }
}

export const updatePauseState = async (paused: boolean) => {
    setSnapshot((current) => ({ ...current, isLoading: true, error: null }))
    try {
        await setSyncPause(paused)
        await refreshPauseState()
    } catch (error) {
        setSnapshot((current) => ({
            ...current,
            isLoading: false,
            error: error instanceof Error ? error : new Error("Failed to update pause state"),
        }))
        throw error
    }
}

const subscribe = (listener: () => void) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
}
const getSnapshot = () => snapshot

export const usePauseState = () =>
    React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot)

export const getSyncDisplayState = (state: Pick<PauseStateSnapshot, "isPaused" | "isUploading">): SyncDisplayState =>
    state.isPaused ? "Paused" : state.isUploading ? "Running" : "Standby"