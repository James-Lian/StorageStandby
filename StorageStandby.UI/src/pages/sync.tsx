import { useEffect, useState } from "react"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Checkbox } from "@/components/ui/checkbox"
import { cn } from "@/lib/utils"
import { Pause, Play, RefreshCw, X } from "lucide-react"

export function Sync() {

    // Feature: sync scheduling option (automatic or manual)
    // see what's in sync queue

    // Sync vs Timeline
    // Sync: active governance and settings

    const [isPaused, setIsPaused] = useState(false)

    const [selectedRows, setSelectedRows] = useState<string[]>([])
    const [queueRows, setQueueRows] = useState([
        { id: "projects", folder: "C:/Users/james/Projects", priority: "Critical", nextSync: "Daily" },
    ])

    useEffect(() => {

    }, [])

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
                    >
                        <RefreshCw className="mr-2 size-4" /> Sync All Now
                    </Button>
                    {isPaused ? (
                        <Button
                            size="lg"
                            className="bg-indigo-600 text-white shadow-md hover:bg-indigo-700"
                            onClick={() => setIsPaused(false)}
                        >
                            <Play className="mr-2 size-4" /> Resume
                        </Button>
                    ) : (
                        <Button
                            size="lg"
                            className="bg-red-600/75 text-white shadow-md hover:bg-red-700/75"
                            onClick={() => setIsPaused(true)}
                        >
                            <Pause className="mr-2 size-4" /> Pause
                        </Button>
                    )}
                </div>
            </div>

            {/* Active & Queue Section */}
            <div className="rounded-2xl border border-border bg-background p-4 space-y-4">
                <div className="flex items-center justify-between">
                    <div>
                        <h2 className="text-lg font-semibold">Upcoming Sync Queue</h2>
                        <p className="text-sm text-muted-foreground">Pending synchronization operations ordered by priority.</p>
                    </div>
                    <Badge variant="outline">3 Queued</Badge>
                </div>

                <Table>
                    <TableHeader>
                        <TableRow>
                            <TableHead className="w-12">
                                <Checkbox
                                    checked={queueRows.length > 0 && selectedRows.length === queueRows.length}
                                    onCheckedChange={() => {
                                        if (selectedRows.length === queueRows.length) {
                                            setSelectedRows([])
                                        } else {
                                            setSelectedRows(queueRows.map((row) => row.id))
                                        }
                                    }}
                                    aria-label="Select all rows"
                                />
                            </TableHead>
                            <TableHead>Folder Path</TableHead>
                            <TableHead>Priority</TableHead>
                            <TableHead>Target Cloud</TableHead>
                            <TableHead>Next Sync</TableHead>
                            <TableHead className="text-right">Actions</TableHead>
                            <TableHead className="w-12"></TableHead>
                        </TableRow>
                    </TableHeader>
                    <TableBody>
                        {queueRows.map((row) => (
                        <TableRow key={row.id} className={cn(selectedRows.includes(row.id) && "bg-muted")}>
                            <TableCell>
                                <Checkbox
                                    checked={selectedRows.includes(row.id)}
                                    onCheckedChange={() => {
                                        setSelectedRows((prev) =>
                                            prev.includes(row.id)
                                                ? prev.filter((id) => id !== row.id)
                                                : [...prev, row.id]
                                        )
                                    }}
                                    aria-label={`Select ${row.folder}`}
                                />
                            </TableCell>
                            <TableCell className="font-medium">{row.folder}</TableCell>
                            <TableCell><Badge variant="default">{row.priority}</Badge></TableCell>
                            <TableCell>
                                <Select value={"none"}>
                                    <SelectTrigger>
                                        <SelectValue placeholder="Select Provider" />
                                    </SelectTrigger>
                                    <SelectContent>
                                        <SelectItem value="none">None (auto storage-optimization)</SelectItem>
                                        <SelectItem value="google">Google Drive</SelectItem>
                                        <SelectItem value="microsoft">OneDrive</SelectItem>
                                        <SelectItem value="dropbox">Dropbox</SelectItem>
                                    </SelectContent>
                                </Select>
                            </TableCell>
                            <TableCell>{row.nextSync}</TableCell>
                            <TableCell className="text-right">
                                <Button variant="ghost" size="sm">Sync Now</Button>
                            </TableCell>
                            <TableCell className="text-right">
                                <Button
                                    variant="ghost"
                                    size="icon-sm"
                                    aria-label={`Remove ${row.folder}`}
                                    className="text-destructive hover:bg-destructive/10 hover:text-destructive"
                                    onClick={() => setQueueRows((prev) => prev.filter((r) => r.id !== row.id))}
                                >
                                    <X className="size-4" />
                                </Button>
                            </TableCell>
                        </TableRow>
                        ))}
                    </TableBody>
                </Table>
            </div>

        </div>
    )
}