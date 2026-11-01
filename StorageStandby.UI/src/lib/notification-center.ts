import * as React from "react"

export type NotificationLevel = "success" | "info" | "warning" | "error"
export type NotificationCategory = "notification" | "error"
export type NotificationDismissReason = "manual" | "timeout" | "programmatic"

export type AppNotification = {
    id: string
    title: string
    summary?: string
    details?: unknown
    statusCode?: number | string
    level: NotificationLevel
    category: NotificationCategory
    createdAt: number
    dismissedAt?: number
    dismissReason?: NotificationDismissReason
}

type CreateNotificationInput = {
    title: string
    summary?: string
    details?: unknown
    statusCode?: number | string
    level: NotificationLevel
    category: NotificationCategory
}

type NotificationSnapshot = {
    notifications: AppNotification[]
}

const listeners = new Set<() => void>()
let snapshot: NotificationSnapshot = { notifications: [] }

const notifyListeners = () => {
    for (const listener of listeners) {
        listener()
    }
}

const createNotificationId = () => {
    if (typeof crypto.randomUUID === "function") {
        return crypto.randomUUID()
    }

    const bytes = new Uint8Array(16)
    crypto.getRandomValues(bytes)
    bytes[6] = (bytes[6] & 0x0f) | 0x40
    bytes[8] = (bytes[8] & 0x3f) | 0x80

    const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("")
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

const setSnapshot = (updater: (current: NotificationSnapshot) => NotificationSnapshot) => {
    snapshot = updater(snapshot)
    notifyListeners()
}

export const createNotification = (input: CreateNotificationInput): AppNotification => {
    const notification: AppNotification = {
        id: createNotificationId(),
        createdAt: Date.now(),
        title: input.title,
        summary: input.summary,
        details: input.details,
        statusCode: input.statusCode,
        level: input.level,
        category: input.category,
    }

    setSnapshot((current) => ({
        notifications: [notification, ...current.notifications].slice(0, 200),
    }))

    return notification
}

export const dismissNotification = (id: string, reason: NotificationDismissReason = "programmatic") => {
    setSnapshot((current) => ({
        notifications: current.notifications.map((entry) => {
            if (entry.id !== id || entry.dismissedAt !== undefined) return entry
            return {
                ...entry,
                dismissedAt: Date.now(),
                dismissReason: reason,
            }
        }),
    }))
}

export const dismissAllNotifications = (reason: NotificationDismissReason = "programmatic") => {
    setSnapshot((current) => ({
        notifications: current.notifications.map((entry) => {
            return {
                ...entry,
                dismissedAt: Date.now(),
                dismissReason: reason,
            }
        })
    }))
}

export const clearNotification = (id: string) => {
    setSnapshot((current) => ({
        notifications: current.notifications.filter((entry) => entry.id !== id),
    }))
}

export const clearDismissedNotifications = () => {
    setSnapshot((current) => ({
        notifications: current.notifications.filter((entry) => entry.dismissedAt === undefined),
    }))
}

export const clearAllNotifications = () => {
    setSnapshot(() => ({
        notifications: [],
    }))
}

const subscribe = (listener: () => void) => {
    listeners.add(listener)
    return () => listeners.delete(listener)
}

const getSnapshot = () => snapshot

export function useNotificationCenter() {
    const state = React.useSyncExternalStore(subscribe, getSnapshot, getSnapshot)

    const activeCount = React.useMemo(
        () => state.notifications.filter((entry) => entry.dismissedAt === undefined).length,
        [state.notifications]
    )

    return {
        notifications: state.notifications,
        activeCount,
    }
}
