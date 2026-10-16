import { useEffect, useMemo, useState } from "react"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Pause, Play, RefreshCw } from "lucide-react"
import { cn } from "@/lib/utils"
import {
    pauseCurrentSync,
    fetchPendingSyncItems,
    setSyncPaused,
    triggerSync,
} from "@/api/data"
import { refreshSyncEvents, useSyncEvents } from "@/lib/sync-events"
import { refreshWatchedFolders, useWatchedFolders } from "@/lib/watched-folders"
import { getProviderDisplayName, type Providers } from "@/types/providers"
import type { SyncEvent } from "@/types/data"

type QueueRow = {
    id: string
    folderId: number
    folderPath: string | null | undefined
    provider: Providers
    accountId: string
    pendingCount: number
    nextSyncAt: string | null
}

export function Sync() {

    // Feature: sync scheduling option (automatic or manual)
    // see what's in sync queue

    // Sync vs Timeline
    // Sync: active governance and settings

    const [isPaused, setIsPaused] = useState(false)
    const [pendingItems, setPendingItems] = useState<Awaited<ReturnType<typeof fetchPendingSyncItems>>>([])
    const { events, error: eventsError } = useSyncEvents()
    const { folders } = useWatchedFolders()

    useEffect(() => {
        void refreshSyncEvents()
        void refreshWatchedFolders()
        void fetchPendingSyncItems().then(setPendingItems).catch(console.error)

        const refreshTimer = window.setInterval(() => {
            void refreshSyncEvents()
            void fetchPendingSyncItems().then(setPendingItems).catch(console.error)
        }, 2000)

        return () => window.clearInterval(refreshTimer)
    }, [])

    useEffect(() => {
        console.log("Pending sync items:", pendingItems)
    }, [pendingItems])

    const queueRows = useMemo<QueueRow[]>(() => {
        const foldersById = new Map(folders.map((folder) => [folder.id, folder]))
        const pendingByFolder = new Map<number, number>()
        for (const item of pendingItems) {
            pendingByFolder.set(item.watchedFolderId, (pendingByFolder.get(item.watchedFolderId) ?? 0) + 1)
        }

        const rows: QueueRow[] = []
        for (const [folderId, pendingCount] of pendingByFolder) {
            const folder = foldersById.get(folderId)
            if (!folder) continue

            const destinations = [
                ...folder.assignedClouds
                    .filter((cloud) => cloud.state !== 4 && cloud.state !== 5)
                    .map((cloud) => ({ provider: cloud.provider, accountId: cloud.accountId })),
                ...folder.userAssignedClouds.map((cloud) => ({ provider: cloud.provider, accountId: cloud.accountId })),
            ]
            const uniqueDestinations = new Map(
                destinations.map((destination) => [
                    `${destination.provider}:${destination.accountId.toLowerCase()}`,
                    destination,
                ])
            )

            for (const destination of uniqueDestinations.values()) {
                rows.push({
                    id: `${folderId}:${destination.provider}:${destination.accountId}`,
                    folderId,
                    folderPath: folder.localPath,
                    provider: destination.provider,
                    accountId: destination.accountId,
                    pendingCount,
                    nextSyncAt: folder.nextSyncAt,
                })
            }
        }

        return rows.sort((left, right) =>
            (left.nextSyncAt ?? "").localeCompare(right.nextSyncAt ?? ""))
    }, [folders, pendingItems])

    const currentRows = useMemo(() => {
        const foldersById = new Map(folders.map((folder) => [folder.id, folder]))
        return events
            .filter((event) => event.completionType === 1)
            .map((event) => ({ event, folder: foldersById.get(event.watchedFolderId) }))
    }, [events, folders])

    const refreshQueue = async () => {
        await refreshSyncEvents()
        const refreshedPendingItems = await fetchPendingSyncItems()
        setPendingItems(refreshedPendingItems)
    }

    const handleEventAction = async (action: () => Promise<void>) => {
        try {
            await action()
            await refreshQueue()
        } catch (error) {
            console.error(error)
        }
    }

    const getFolderLabel = (event: SyncEvent, localPath: string | null | undefined) =>
        localPath ?? `Folder ${event.watchedFolderId}`

    return (
        <div className="space-y-6 p-4">
            {/* Header / Global Action Bar */}
            <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                <div>
                    <h1 className="text-2xl font-semibold">Sync Engine & Governance</h1>
                    <p className="text-sm text-muted-foreground">
                        Manage engine state, active queues, global routing, and path ignore patterns.
                    </p>
                </div>
                <Badge variant={isPaused ? "destructive" : "default"}>
                    {isPaused ? "Engine Paused" : "Engine Running"}
                </Badge>
            </div>

            {/* Automatic Sync Control Bar */}
            <div
                className={cn(
                    "flex flex-col gap-4 rounded-2xl border p-4 md:flex-row md:items-center md:justify-between",
                    isPaused
                        ? "border-rose-500/40 bg-rose-500/10"
                        : "border-sky-500/40 bg-linear-to-r from-sky-500/10 via-indigo-500/10 to-violet-500/10"
                )}
            >
                <div className="flex items-center gap-3">
                    <div
                        className={cn(
                            "flex size-10 shrink-0 items-center justify-center rounded-xl",
                            isPaused
                                ? "bg-rose-500/15 text-rose-600"
                                : "bg-linear-to-br from-sky-500/20 to-indigo-500/20 text-indigo-600"
                        )}
                    >
                        {isPaused ? <Pause className="size-5" /> : <Play className="size-5" />}
                    </div>
                    <div>
                        <div className="flex items-center gap-2">
                            <h2 className="text-lg font-semibold">
                                {isPaused ? "Automatic syncs are OFF" : "Automatic syncs are ON"}
                            </h2>
                            {/* <Badge variant={isPaused ? "destructive" : "default"}>
                                {isPaused ? "Paused" : "Running"}
                            </Badge> */}
                        </div>
                        <p className="text-sm text-muted-foreground">
                            {isPaused
                                ? "File changes are still being tracked."
                                : "See below for pending sync operations."}
                        </p>
                    </div>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                    <Button
                        size="lg"
                        className="bg-linear-to-r from-sky-500 to-indigo-500 text-white shadow-md hover:from-sky-600 hover:to-indigo-600"
                        onClick={() => void handleEventAction(triggerSync)}
                    >
                        <RefreshCw className="mr-2 size-4" /> Sync All Now
                    </Button>
                    {isPaused ? (
                        <Button
                            size="lg"
                            className="bg-indigo-600 text-white shadow-md hover:bg-indigo-700"
                            onClick={() => void handleEventAction(async () => {
                                await setSyncPaused(false)
                                setIsPaused(false)
                            })}
                        >
                            <Play className="mr-2 size-4" /> Resume
                        </Button>
                    ) : (
                        <Button
                            size="lg"
                            className="bg-red-600/75 text-white shadow-md hover:bg-red-700/75"
                            onClick={() => void handleEventAction(async () => {
                                await setSyncPaused(true)
                                setIsPaused(true)
                            })}
                        >
                            <Pause className="mr-2 size-4" /> Pause
                        </Button>
                    )}
                </div>
            </div>

            {/* Current jobs */}
            <div className="rounded-2xl border border-border bg-background p-4 space-y-4">
                <div className="flex items-center justify-between">
                    <div>
                        <h2 className="text-lg font-semibold">Current Jobs</h2>
                        <p className="text-sm text-muted-foreground">Provider operations currently in progress.</p>
                    </div>
                    <Badge variant="outline">{currentRows.length} Active</Badge>
                </div>

                {currentRows.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No sync jobs are currently running.</p>
                ) : (
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>Folder Path</TableHead>
                                <TableHead>Destination</TableHead>
                                <TableHead>Account</TableHead>
                                <TableHead>Changes Tracked</TableHead>
                                {/* Number of changes tracked */}
                                <TableHead className="text-right">Actions</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            {currentRows.map(({ event, folder }) => (
                                <TableRow key={event.id}>
                                    <TableCell className="font-medium">{getFolderLabel(event, folder?.localPath)}</TableCell>
                                    <TableCell>{getProviderDisplayName(event.provider as Providers)}</TableCell>
                                    <TableCell>{event.accountId}</TableCell>
                                    <TableCell className="text-right">
                                        <Button
                                            variant="outline"
                                            size="sm"
                                            onClick={() => void handleEventAction(async () => {
                                                await pauseCurrentSync()
                                            })}
                                        >
                                            <Pause className="mr-2 size-4" /> Pause Current Job
                                        </Button>
                                    </TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                )}
            </div>

            {/* Upcoming & queued events */}
            <div className="rounded-2xl border border-border bg-background p-4 space-y-4">
                <div className="flex items-center justify-between">
                    <div>
                        <h2 className="text-lg font-semibold">Upcoming Sync Queue</h2>
                        <p className="text-sm text-muted-foreground">Pending synchronization operations ordered by priority.</p>
                    </div>
                    <Badge variant="outline">{queueRows.length} Queued</Badge>
                </div>

                <Table>
                    <TableHeader>
                        <TableRow>
                            <TableHead>Folder Path</TableHead>
                            <TableHead>Destination</TableHead>
                            <TableHead>Account</TableHead>
                            <TableHead>Pending Changes</TableHead>
                            <TableHead>Next Sync</TableHead>
                            <TableHead className="text-right">Actions</TableHead>
                        </TableRow>
                    </TableHeader>
                    <TableBody>
                        {queueRows.map((row) => (
                        <TableRow key={row.id}>
                            <TableCell className="font-medium">{row.folderPath ?? `Folder ${row.folderId}`}</TableCell>
                            <TableCell>{getProviderDisplayName(row.provider)}</TableCell>
                            <TableCell>{row.accountId}</TableCell>
                            <TableCell>{row.pendingCount}</TableCell>
                            <TableCell>{row.nextSyncAt ? new Date(row.nextSyncAt).toLocaleString() : "Due now"}</TableCell>
                            <TableCell className="text-right">
                                <Button variant="ghost" size="sm" onClick={() => void handleEventAction(triggerSync)}>
                                    Sync Now
                                </Button>
                            </TableCell>
                        </TableRow>
                        ))}
                    </TableBody>
                </Table>

                {eventsError && <p className="text-sm text-destructive">Unable to load current jobs: {eventsError.message}</p>}
            </div>

        </div>
    )
}