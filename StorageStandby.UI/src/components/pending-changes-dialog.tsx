import { fetchPendingSyncItems } from "@/api/data"
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogTitle,
} from "@/components/ui/dialog"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Check } from "lucide-react"
import { useEffect, useState } from "react"

export interface PendingChangesDialogProps {
    // boolean state variable
    open: boolean
    // notifies parent when dialog wants to open/close
    onOpenChange: (open: boolean) => void
    watchedFolderId: number
    folderPath?: string
}

export function PendingChangesDialog({
    open, 
    onOpenChange,
    watchedFolderId,
    folderPath
} : PendingChangesDialogProps) {
    const [pendingItems, setPendingItems] = useState<Awaited<ReturnType<typeof fetchPendingSyncItems>>>([])

    const applicablePendingItems = pendingItems.filter((item) => item.watchedFolderId === watchedFolderId)

    useEffect(() => {
        if (!open) return

        void fetchPendingSyncItems().then(setPendingItems).catch(console.error)

        const refreshTimer = window.setInterval(() => {
            void fetchPendingSyncItems().then(setPendingItems).catch(console.error)
        }, 5000);

        return () => window.clearInterval(refreshTimer)
    }, [open])

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="max-h-[85vh] sm:max-w-2xl">
                <DialogTitle>Pending Changes</DialogTitle>
                <DialogDescription className="truncate">
                    {folderPath ?? `Folder ${watchedFolderId}`}
                </DialogDescription>
                <div className="max-h-[60vh] min-h-40 overflow-auto rounded-md border border-border p-2">
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>Name</TableHead>
                                <TableHead>Original path</TableHead>
                                <TableHead className="text-center">Deleted</TableHead>
                                <TableHead className="text-center">Moved</TableHead>
                                <TableHead className="text-center">Renamed</TableHead>
                                <TableHead className="text-center">Changed</TableHead>
                                <TableHead className="text-center">Created</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            {applicablePendingItems.length === 0 ? (
                                <TableRow>
                                    <TableCell colSpan={7} className="py-6 text-center text-muted-foreground">
                                        No pending changes.
                                    </TableCell>
                                </TableRow>
                            ) : applicablePendingItems.map((item) => (
                                <TableRow
                                    key={item.id}
                                    className={item.deleted
                                        ? "bg-red-50 text-red-700 hover:bg-red-100 dark:bg-red-950/30 dark:text-red-300"
                                        : undefined}
                                >
                                    <TableCell className="max-w-80 truncate" title={item.localPath}>
                                        {item.localPath.split("\\").pop()}
                                    </TableCell>
                                    <TableCell className="max-w-80 truncate" title={item.originalLocalPath ?? undefined}>
                                        {item.originalLocalPath ?? ""}
                                    </TableCell>
                                    <TableCell className="text-center">{item.deleted && <Check className="mx-auto size-4" />}</TableCell>
                                    <TableCell className="text-center">{item.moved && <Check className="mx-auto size-4" />}</TableCell>
                                    <TableCell className="text-center">{item.renamed && <Check className="mx-auto size-4" />}</TableCell>
                                    <TableCell className="text-center">{item.changed && <Check className="mx-auto size-4" />}</TableCell>
                                    <TableCell className="text-center">{item.created && <Check className="mx-auto size-4" />}</TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                </div>
            </DialogContent>
        </Dialog>
    )
}