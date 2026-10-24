import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from "@/components/ui/dialog"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { SyncIncrements, type SyncFrequency } from "@/types/data"
import { Minus, Plus } from "lucide-react"
import { useState } from "react"
import { updateWatchedFolderSyncFrequency } from "@/api/data"

export interface SyncFrequencyDialogProps {
    open: boolean
    onOpenChange: (open: boolean) => void
    watchedFolderId: number
    syncIncrements: 0 | 1 | 2 | 3
    onSyncIncrementsChange: (syncIncrements: 0 | 1 | 2 | 3) => void | Promise<void>
    multiplier: number
    onMultiplierChange: (multiplier: number) => void | Promise<void>
    identifiers: number[]
    onIdentifiersChange: (identifiers: number[]) => void | Promise<void>
    title?: string
}

const weekdays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]

function MultiplierControl({ value, onChange }: { value: number; onChange: (value: number) => void }) {
    return (
        <div className="flex items-center gap-2">
            <Button type="button" variant="outline" size="icon" onClick={() => onChange(Math.max(1, value - 1))} disabled={value <= 1} aria-label="Decrease multiplier">
                <Minus />
            </Button>
            <Input
                type="number"
                min={1}
                value={value}
                onChange={(event) => {
                    const nextValue = Number(event.target.value)
                    onChange(Number.isFinite(nextValue) ? Math.max(1, nextValue) : 1)
                }}
                className="w-20 text-center"
                aria-label="Sync frequency multiplier"
            />
            <Button type="button" variant="outline" size="icon" onClick={() => onChange(value + 1)} aria-label="Increase multiplier">
                <Plus />
            </Button>
        </div>
    )
}

export function SyncFrequencyDialog({
    open,
    onOpenChange,
    watchedFolderId,
    syncIncrements,
    onSyncIncrementsChange,
    multiplier,
    onMultiplierChange,
    identifiers,
    onIdentifiersChange,
    title = "Set the frequency of automatic syncs",
}: SyncFrequencyDialogProps) {
    const [activeTab, setActiveTab] = useState(String(syncIncrements))
    const [draftMultiplier, setDraftMultiplier] = useState(Math.max(1, multiplier))
    const [draftIdentifiers, setDraftIdentifiers] = useState(identifiers)
    const [isApplying, setIsApplying] = useState(false)
    const [lastSynced, setLastSynced] = useState({ open, syncIncrements, multiplier, identifiers })

    if (
        lastSynced.open !== open
        || lastSynced.syncIncrements !== syncIncrements
        || lastSynced.multiplier !== multiplier
        || lastSynced.identifiers !== identifiers
    ) {
        setLastSynced({ open, syncIncrements, multiplier, identifiers })
        if (open) {
            setActiveTab(String(syncIncrements))
            setDraftMultiplier(Math.max(1, multiplier))
            setDraftIdentifiers(identifiers)
        }
    }

    const activeIncrement = Number(activeTab) as 0 | 1 | 2 | 3
    const needsIdentifiers = activeIncrement === SyncIncrements.Weekly || activeIncrement === SyncIncrements.Monthly
    const selectedIdentifiers = activeIncrement === SyncIncrements.Weekly
        ? draftIdentifiers.filter((identifier) => identifier >= 1 && identifier <= 7)
        : activeIncrement === SyncIncrements.Monthly
            ? draftIdentifiers.filter((identifier) => identifier >= 1 && identifier <= 31)
            : []
    const canConfirm = !needsIdentifiers || selectedIdentifiers.length > 0

    const toggleIdentifier = (identifier: number, checked: boolean) => {
        setDraftIdentifiers((current) => {
            const next = new Set(current)
            if (checked) next.add(identifier)
            else next.delete(identifier)
            return [...next].sort((left, right) => left - right)
        })
    }

    const handleConfirm = async () => {
        if (!canConfirm) return

        const nextFrequency: SyncFrequency = {
            multiplier: draftMultiplier,
            identifiers: selectedIdentifiers,
            syncIncrements: activeIncrement,
        }

        setIsApplying(true)
        try {
            await updateWatchedFolderSyncFrequency(watchedFolderId, nextFrequency)
            await onSyncIncrementsChange(activeIncrement)
            await onMultiplierChange(nextFrequency.multiplier)
            await onIdentifiersChange(nextFrequency.identifiers)
            onOpenChange(false)
        } finally {
            setIsApplying(false)
        }
    }

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="sm:max-w-2xl">
                <DialogHeader>
                    <DialogTitle>{title}</DialogTitle>
                    <DialogDescription>
                        Choose between daily, weekly, monthly, or inactive settings.
                    </DialogDescription>
                </DialogHeader>

                <Tabs value={activeTab} onValueChange={setActiveTab}>
                    <TabsList>
                        <TabsTrigger value={String(SyncIncrements.Daily)}>Daily</TabsTrigger>
                        <TabsTrigger value={String(SyncIncrements.Weekly)}>Weekly</TabsTrigger>
                        <TabsTrigger value={String(SyncIncrements.Monthly)}>Monthly</TabsTrigger>
                        <TabsTrigger value={String(SyncIncrements.Inactive)}>Inactive</TabsTrigger>
                    </TabsList>

                    <TabsContent value={String(SyncIncrements.Daily)} className="space-y-3">
                        <p className="text-sm text-muted-foreground">Sync every {draftMultiplier} day{draftMultiplier === 1 ? "" : "s"}.</p>
                        <MultiplierControl value={draftMultiplier} onChange={setDraftMultiplier} />
                    </TabsContent>

                    <TabsContent value={String(SyncIncrements.Weekly)} className="space-y-3">
                        <MultiplierControl value={draftMultiplier} onChange={setDraftMultiplier} />
                        <p className="text-sm text-muted-foreground">Sync every {draftMultiplier} week{draftMultiplier === 1 ? "" : "s"} on:</p>
                        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                            {weekdays.map((day, index) => {
                                const identifier = index + 1
                                return (
                                    <label key={day} className="flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm">
                                        <Checkbox checked={draftIdentifiers.includes(identifier)} onCheckedChange={(checked) => toggleIdentifier(identifier, checked === true)} />
                                        {day}
                                    </label>
                                )
                            })}
                        </div>
                    </TabsContent>

                    <TabsContent value={String(SyncIncrements.Monthly)} className="space-y-3">
                        <MultiplierControl value={draftMultiplier} onChange={setDraftMultiplier} />
                        <p className="text-sm text-muted-foreground">Sync every {draftMultiplier} month{draftMultiplier === 1 ? "" : "s"} on:</p>
                        <div className="grid grid-cols-5 gap-2 sm:grid-cols-7">
                            {Array.from({ length: 31 }, (_, index) => {
                                const identifier = index + 1
                                return (
                                    <label key={identifier} className="flex items-center justify-center gap-1 rounded-md border border-border px-2 py-2 text-sm">
                                        <Checkbox checked={draftIdentifiers.includes(identifier)} onCheckedChange={(checked) => toggleIdentifier(identifier, checked === true)} />
                                        {identifier}
                                    </label>
                                )
                            })}
                        </div>
                    </TabsContent>

                    <TabsContent value={String(SyncIncrements.Inactive)}>
                        Automatic syncs for this folder will not occur.
                    </TabsContent>
                </Tabs>

                <DialogFooter>
                    <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
                        Cancel
                    </Button>
                    <Button type="button" onClick={() => void handleConfirm()} disabled={!canConfirm || isApplying}>
                        {isApplying ? "Saving..." : "Confirm"}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    )
}

export default SyncFrequencyDialog