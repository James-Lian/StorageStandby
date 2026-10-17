import { useEffect, useMemo, useState } from "react"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Ellipsis, Pause, Play, RefreshCw } from "lucide-react"
import { cn, getFolderName, handleOpenExplorer } from "@/lib/utils"
import {
    pauseCurrentSync,
    fetchPendingSyncItems,
    fetchSystemSettings,
    removeFolderCloud,
    setSyncPaused,
    triggerCloudAssignmentSync,
    triggerSync,
} from "@/api/data"
import { refreshSyncEvents, useSyncEvents } from "@/lib/sync-events"
import { refreshWatchedFolders, useWatchedFolders } from "@/lib/watched-folders"
import { getProviderDisplayName, type Providers } from "@/types/providers"
import type { SyncEvent, WatchedFolder } from "@/types/data"
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuPortal, DropdownMenuTrigger } from "@/components/ui/dropdown-menu"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import { useConnectedAccounts } from "@/lib/connected-accounts"

type QueueRow = {
    id: string
    folder: WatchedFolder
    folderId: number
    folderPath: string | null
    provider: Providers
    accountId: string
    email: string | null
    pendingCount: number
    nextSyncAt: string | null
}

type CloudAssignmentRow = {
    id: string
    folder: WatchedFolder
    provider: Providers
    accountId: string
    type: "new" | "removal"
    lastError: string | null
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

    const { accountsByProvider } = useConnectedAccounts()

    useEffect(() => {
        void refreshSyncEvents()
        void refreshWatchedFolders()
        void fetchPendingSyncItems().then(setPendingItems).catch(console.error)
        void fetchSystemSettings().then((settings) => setIsPaused(settings.isSyncPaused)).catch(console.error)

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
                    .filter((cloud) => cloud.state === 2)
                    .map((cloud) => ({ provider: cloud.provider, accountId: cloud.accountId })),
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
                    folder,
                    folderId,
                    folderPath: folder.localPath,
                    provider: destination.provider,
                    accountId: destination.accountId,
                    email: accountsByProvider[destination.provider].find(acc => acc.id == destination.accountId)?.email ?? null,
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

    const assignmentRows = useMemo<CloudAssignmentRow[]>(() => {
        const rows: CloudAssignmentRow[] = []
        for (const folder of folders) {
            for (const cloud of folder.userAssignedClouds) {
                const key = `${folder.id}:new:${cloud.provider}:${cloud.accountId.toLowerCase()}`
                rows.push({
                    id: key,
                    folder,
                    provider: cloud.provider,
                    accountId: cloud.accountId,
                    type: "new",
                    lastError: null,
                })
            }

            for (const cloud of folder.userCloudRemovalAssignments) {
                const key = `${folder.id}:removal:${cloud.provider}:${cloud.accountId.toLowerCase()}`
                rows.push({
                    id: key,
                    folder,
                    provider: cloud.provider,
                    accountId: cloud.accountId,
                    type: "removal",
                    lastError: null,
                })
            }
        }
        return rows
    }, [folders])

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
                                <TableHead>Folder</TableHead>
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
                                    <TableCell className="font-medium">
                                        <Tooltip>
                                            <TooltipTrigger asChild>
                                                <span>{folder ? getFolderName(folder) : getFolderLabel(event, undefined)}</span>
                                            </TooltipTrigger>
                                            <TooltipContent>
                                                {folder ? (folder.localPath ?? "No local path") : "No local path"}
                                            </TooltipContent>
                                        </Tooltip>
                                    </TableCell>
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

            <div className="rounded-2xl border border-border bg-background p-4 space-y-4">
                <div className="flex items-center justify-between">
                    <div>
                        <h2 className="text-lg font-semibold">Pending Cloud Assignments</h2>
                        <p className="text-sm text-muted-foreground">New cloud assignments and queued remote removals.</p>
                    </div>
                    <Badge variant="outline">{assignmentRows.length} Assigned</Badge>
                </div>

                {assignmentRows.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No pending cloud assignments.</p>
                ) : (
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>Folder</TableHead>
                                <TableHead>Provider</TableHead>
                                <TableHead>Account</TableHead>
                                <TableHead>Type</TableHead>
                                <TableHead>Status</TableHead>
                                <TableHead className="text-right">Actions</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            {assignmentRows.map((row) => {
                                const email = accountsByProvider[row.provider]
                                    ?.find((account) => account.id.toLowerCase() === row.accountId.toLowerCase())
                                    ?.email ?? row.accountId
                                return (
                                    <TableRow key={row.id}>
                                        <TableCell className="font-medium">
                                            <Tooltip>
                                                <TooltipTrigger asChild>
                                                    <span>{getFolderName(row.folder)}</span>
                                                </TooltipTrigger>
                                                <TooltipContent>
                                                    {row.folder.localPath ?? "No local path"}
                                                </TooltipContent>
                                            </Tooltip>
                                        </TableCell>
                                        <TableCell>{getProviderDisplayName(row.provider)}</TableCell>
                                        <TableCell>{email}</TableCell>
                                        <TableCell>
                                            <Badge variant={row.type === "removal" ? "destructive" : "outline"}>
                                                {row.type === "new" ? "New Assignment" : "Removal"}
                                            </Badge>
                                        </TableCell>
                                        <TableCell>
                                            <Badge variant="outline">Pending</Badge>
                                            {row.lastError && <p className="mt-1 text-xs text-destructive">{row.lastError}</p>}
                                        </TableCell>
                                        <TableCell className="text-right">
                                            <DropdownMenu>
                                                <DropdownMenuTrigger asChild>
                                                    <Button variant="ghost" size="sm">
                                                        <Ellipsis className="size-4" />
                                                    </Button>
                                                </DropdownMenuTrigger>
                                                <DropdownMenuPortal>
                                                    <DropdownMenuContent align="end">
                                                        <DropdownMenuItem
                                                            onSelect={() => void handleEventAction(async () => {
                                                                await triggerCloudAssignmentSync(row.folder.id, {
                                                                    provider: row.provider,
                                                                    accountId: row.accountId,
                                                                })
                                                                await refreshWatchedFolders()
                                                            })}
                                                        >
                                                            Execute now
                                                        </DropdownMenuItem>
                                                        <DropdownMenuItem
                                                            onClick={() => void handleEventAction(async () => {
                                                                await removeFolderCloud(row.folder.id, {
                                                                    provider: row.provider,
                                                                    accountId: row.accountId,
                                                                })
                                                                await refreshWatchedFolders()
                                                            })}
                                                        >
                                                            {row.type === "new" ? "Cancel" : "Cancel removal"}
                                                        </DropdownMenuItem>
                                                    </DropdownMenuContent>
                                                </DropdownMenuPortal>
                                            </DropdownMenu>
                                        </TableCell>
                                    </TableRow>
                                )
                            })}
                        </TableBody>
                    </Table>
                )}
            </div>

            {/* Upcoming & queued events */}
            <div className="rounded-2xl border border-border bg-background p-4 space-y-4">
                <div className="flex items-center justify-between">
                    <div>
                        <h2 className="text-lg font-semibold">Upcoming Sync Queue</h2>
                        <p className="text-sm text-muted-foreground">Updating existing remote locations with synchronization operations.</p>
                    </div>
                    <Badge variant="outline">{queueRows.length} Queued</Badge>
                </div>

                <Table>
                    <TableHeader>
                        <TableRow>
                            <TableHead className="pl-4">Folder</TableHead>
                            <TableHead>Destination</TableHead>
                            <TableHead>Account</TableHead>
                            <TableHead>Pending Changes</TableHead>
                            <TableHead>Next Sync</TableHead>
                            <TableHead className="text-right">Actions</TableHead>
                        </TableRow>
                    </TableHeader>
                    <TableBody>
                        {queueRows.length == 0 &&
                            <TableRow>
                                <TableCell colSpan={5} className="text-center py-6">
                                    <p className="text-sm text-muted-foreground">Assign clouds to a folder to see pending sync operations!</p>
                                </TableCell>
                            </TableRow>
                        }
                        {queueRows.map((row) => (
                            <TableRow key={row.id}>
                                <TableCell className="font-medium">
                                    <Tooltip>
                                        <TooltipTrigger asChild>
                                            <Button
                                                variant="link"
                                                className="cursor-pointer"
                                                onClick={(event: React.MouseEvent<HTMLButtonElement>) => {
                                                    // check if Control key was held down
                                                    if (event.ctrlKey) {
                                                        handleOpenExplorer(row.folderPath);
                                                    } else {

                                                    }
                                                }}
                                            >
                                                {getFolderName(row.folder)}
                                            </Button>
                                        </TooltipTrigger>
                                        <TooltipContent>
                                            {row.folderPath ?? "No local path"}
                                        </TooltipContent>
                                    </Tooltip>
                                </TableCell>
                                <TableCell>{getProviderDisplayName(row.provider)}</TableCell>
                                <TableCell>{row.email}</TableCell>
                                <TableCell>{row.pendingCount}</TableCell>
                                <TableCell>{row.nextSyncAt ? new Date(row.nextSyncAt).toLocaleString() : "Due now"}</TableCell>
                                <TableCell className="text-right">
                                    <DropdownMenu>
                                        <DropdownMenuTrigger asChild>
                                            <Button variant="ghost" size="sm">
                                                <Ellipsis className="size-4" />
                                            </Button>
                                        </DropdownMenuTrigger>
                                        <DropdownMenuContent align="end" className="w-72">
                                            <DropdownMenuItem onSelect={() => void handleEventAction(triggerSync)}>
                                                Sync Now
                                            </DropdownMenuItem>
                                            <DropdownMenuItem className="whitespace-normal">
                                                Delete folder's tracked changes (applies to all clouds)
                                            </DropdownMenuItem>
                                        </DropdownMenuContent>
                                    </DropdownMenu>
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