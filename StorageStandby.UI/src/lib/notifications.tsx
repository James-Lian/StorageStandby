import { toast } from "sonner"
import { NotificationToastContent } from "@/components/notif-toast-content"
import {
    createNotification,
    dismissNotification,
    type AppNotification,
    type NotificationCategory,
    type NotificationLevel,
} from "@/lib/notification-center"

type StatusCode = number | string

export type ErrorNotificationInput = {
    title: string
    statusCode?: StatusCode
    summary?: string
    details?: unknown
    duration?: number
}

type BasicNotificationInput = {
    title: string
    summary?: string
    details?: unknown
    duration?: number
}

const showNotificationToast = ({
    level,
    category,
    title,
    summary,
    details,
    statusCode,
    durationMs,
}: {
    level: NotificationLevel
    category: NotificationCategory
    title: string
    summary?: string
    details?: unknown
    statusCode?: StatusCode
    durationMs: number
}) => {
    const notification: AppNotification = createNotification({
        level,
        category,
        title,
        summary,
        details,
        statusCode,
    })

    toast.custom(
        (toastId) => (
            <NotificationToastContent
                toastId={toastId}
                notification={notification}
                durationMs={durationMs}
                onDismiss={(reason) => {
                    dismissNotification(notification.id, reason)
                    toast.dismiss(notification.id)
                }}
            />
        ),
        {
            id: notification.id,
            duration: Infinity,
            onDismiss: () => dismissNotification(notification.id),
        }
    )

    return notification.id
}

export const notifySuccess = ({ title, summary, details, duration = 3000 }: BasicNotificationInput) =>
    showNotificationToast({
        level: "success",
        category: "notification",
        title,
        summary,
        details,
        durationMs: duration,
    })

export const notifyInfo = ({ title, summary, details, duration = 3000 }: BasicNotificationInput) =>
    showNotificationToast({
        level: "info",
        category: "notification",
        title,
        summary,
        details,
        durationMs: duration,
    })

export const notifyWarning = ({ title, summary, details, duration = 3000 }: BasicNotificationInput) =>
    showNotificationToast({
        level: "warning",
        category: "notification",
        title,
        summary,
        details,
        durationMs: duration,
    })

export const notifyError = ({ title, summary, details, duration = 5000 }: BasicNotificationInput) =>
    showNotificationToast({
        level: "error",
        category: "notification",
        title,
        summary,
        details,
        durationMs: duration,
    })

export const logError = ({ title, statusCode, summary, details, duration = 5000 }: ErrorNotificationInput) =>
    showNotificationToast({
        level: "error",
        category: "error",
        title,
        summary,
        details,
        statusCode,
        durationMs: duration,
    })
