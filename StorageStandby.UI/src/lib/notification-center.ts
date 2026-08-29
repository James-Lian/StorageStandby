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

const setSnapshot = (updater: (current: NotificationSnapshot) => NotificationSnapshot) => {
    snapshot = updater(snapshot)
    notifyListeners()
}

export const createNotification = (input: CreateNotificationInput): AppNotification => {
    const notification: AppNotification = {
        id: crypto.randomUUID(),
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
