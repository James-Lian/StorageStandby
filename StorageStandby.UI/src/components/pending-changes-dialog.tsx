import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from "@/components/ui/dialog"
import { Collapsible } from "radix-ui"

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
    folderPath,
} : PendingChangesDialogProps) {
    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="max-h-[85vh] sm:max-w-2xl">
                <DialogTitle>Pending Changes</DialogTitle>
                <DialogDescription className="truncate">
                    {folderPath ?? `Folder ${watchedFolderId}`}
                </DialogDescription>
            </DialogContent>
        </Dialog>
    )
}