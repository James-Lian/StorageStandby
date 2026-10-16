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