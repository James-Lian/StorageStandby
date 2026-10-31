import { fetchOpenExplorer } from "@/api/data"
import type { WatchedFolder } from "@/types/data"
import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"
import { notifyError } from "./notifications"

export function cn(...inputs: ClassValue[]) {
    return twMerge(clsx(inputs))
}

export const getFolderName = (folder: WatchedFolder) =>
    folder.localPath?.split(/[\\/]/).filter(Boolean).pop() ?? "Unnamed folder"

function parseBackendUtcDate(value: string) {
    const hasTimezone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value)
    const normalizedValue = hasTimezone ? value : `${value}Z`
    return new Date(normalizedValue)
}

export const formatLocalDateTime = (value: string | null | undefined) =>
    value
        ? new Intl.DateTimeFormat(undefined, {
            dateStyle: "medium",
            timeStyle: "short",
        }).format(parseBackendUtcDate(value))
        : "Never"

export const formatLocalDate = (value: string | null | undefined) =>
    value
        ? new Intl.DateTimeFormat(undefined, {
            dateStyle: "medium",
        }).format(parseBackendUtcDate(value))
        : "Unknown date"

export const handleOpenExplorer = (path: string | null) => {
    if (path) {
        fetchOpenExplorer(path);
    }
    else {
        notifyError({
            title: "Path is null",
            details: "Cannot open explorer; path variable is null"
        })
    }
}