import React, { useEffect, useState, type Provider } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Pause, Play, RefreshCw, ShieldAlert, Zap } from "lucide-react"
import { PROVIDERS, type Providers } from "@/types/providers"
import { IgnoreRulesDialog } from "@/components/ignore-rules-dialog"

// API
import { fetchAllWatchedFolders, fetchWatchedFolder } from "@/api/data"

export function Sync() {
    // Feature: sync scheduling option (automatic or manual)
    // see what's in sync queue

    // Sync vs Timeline
    // Sync: active governance and settings

    const [isPaused, setIsPaused] = useState(false)
    const [preferredProvider, setPreferredProvider] = useState<string>("auto")
    const [ignoreRules, setIgnoreRules] = useState<string>("*.tmp;node_modules/;.git/")
    const [isIgnoreRulesDialogOpen, setIsIgnoreRulesDialogOpen] = useState(false)

    const [providerDestinationByFolder, setProviderDestinationByFolder] = React.useState<Record<string, [Providers, string]>>({
        "example": [PROVIDERS.Google, "accountid"]
    });

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
                <div className="flex items-center gap-2">
                    {isPaused ? (
                        <Button variant="default" onClick={() => setIsPaused(false)}>
                            <Play className="mr-2 size-4" /> Resume Sync
                        </Button>
                    ) : (
                        <Button variant="outline" onClick={() => setIsPaused(true)}>
                            <Pause className="mr-2 size-4" /> Pause Engine
                        </Button>
                    )}
                    <Button variant="secondary">
                        <RefreshCw className="mr-2 size-4" /> Sync All Now
                    </Button>
                </div>
            </div>

            {/* Status & Global Rules Section */}
            <div className="grid gap-4 md:grid-cols-2">
                {/* Global Provider Settings */}
                <div className="rounded-2xl border border-border bg-background p-4 space-y-3">
                    <div>
                        <h2 className="text-lg font-semibold">Global Preferred Provider</h2>
                        <p className="text-sm text-muted-foreground">
                            Default destination for new backup targets unless explicitly overridden.
                        </p>
                    </div>
                    <Select value={preferredProvider} onValueChange={setPreferredProvider}>
                        <SelectTrigger>
                            <SelectValue placeholder="Select Provider" />
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value="auto">Auto (Storage Optimized)</SelectItem>
                            <SelectItem value="google">Google Drive</SelectItem>
                            <SelectItem value="microsoft">OneDrive</SelectItem>
                            <SelectItem value="dropbox">Dropbox</SelectItem>
                        </SelectContent>
                    </Select>
                </div>

                {/* Global Ignore Rules */}
                <div className="rounded-2xl border border-border bg-background p-4 space-y-3">
                    <div>
                        <h2 className="text-lg font-semibold">Global Ignore Rules</h2>
                        <p className="text-sm text-muted-foreground">
                            Semicolon-delimited patterns skipped across all watched directories.
                        </p>
                    </div>
                    <div className="flex flex-row">
                        <div className="flex-1 overflow-x-auto">
                            <Input 
                                value={ignoreRules} 
                                readOnly 
                                placeholder="Your ignore rules appear here (semicolon-delimited)." 
                            />
                        </div>
                        <Button 
                            className="ml-2"
                            onClick={() => setIsIgnoreRulesDialogOpen(true)}
                        >
                                Edit
                        </Button>
                    </div>
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
                            <TableHead>Folder Path</TableHead>
                            <TableHead>Priority</TableHead>
                            <TableHead>Target Cloud</TableHead>
                            <TableHead>Next Sync</TableHead>
                            <TableHead className="text-right">Actions</TableHead>
                        </TableRow>
                    </TableHeader>
                    <TableBody>
                        <TableRow>
                            <TableCell className="font-medium">C:/Users/james/Projects</TableCell>
                            <TableCell><Badge variant="default">Critical</Badge></TableCell>
                            <TableCell>
                                <Select value={"auto"}>
                                    <SelectTrigger>
                                        <SelectValue placeholder="Select Provider" />
                                    </SelectTrigger>
                                    <SelectContent>
                                        <SelectItem value="auto">Auto</SelectItem>
                                        <SelectItem value="google">Google Drive</SelectItem>
                                        <SelectItem value="microsoft">OneDrive</SelectItem>
                                        <SelectItem value="dropbox">Dropbox</SelectItem>
                                    </SelectContent>
                                </Select>
                            </TableCell>
                            <TableCell>Daily</TableCell>
                            <TableCell className="text-right">
                                <Button variant="ghost" size="sm">Sync Now</Button>
                            </TableCell>
                        </TableRow>
                    </TableBody>
                </Table>
            </div>

            <IgnoreRulesDialog
                open={isIgnoreRulesDialogOpen}
                onOpenChange={setIsIgnoreRulesDialogOpen}
                rules={ignoreRules}
                onRulesChange={setIgnoreRules}
            />
        </div>
    )
}