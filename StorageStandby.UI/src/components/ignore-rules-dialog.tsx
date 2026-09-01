"use client"

// Reusable "Ignore Rules" editor dialog.
//
// Provides two ways to edit a semicolon-delimited ignore rules string:
//   1. A user-friendly UI table (live validity, reorder, delete, add).
//   2. A raw semicolon-delimited textarea for power users.
//
// Usage (modular / reused across pages):
//   <IgnoreRulesDialog
//     open={open}
//     onOpenChange={setOpen}
//     rules={ignoreRules}
//     onRulesChange={setIgnoreRules}   // called with the new ";"-joined string on Apply
//     basePath="C:/Users/james/Projects" // optional; omit for GLOBAL ignore rules
//   />
//
// When `basePath` is provided (folder-specific rules), a label at the top shows the
// folder's full path and each relative table row shows a tooltip with its full path.
// That affordance does not apply when `basePath` is omitted (global ignore rules).

import { useState } from "react"
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from "@/components/ui/dialog"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Textarea } from "@/components/ui/textarea"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from "@/components/ui/table"
import { ArrowDown, ArrowUp, CircleCheck, FolderCheck, Trash2, TriangleAlert } from "lucide-react"
import { cn } from "@/lib/utils"
import { joinPath, splitRules, isValidRulePath } from "@/lib/ignore-rules-utils"

export interface IgnoreRulesDialogProps {
    /** Controlled open state. */
    open: boolean
    /** Notify the parent when the dialog wants to open/close. */
    onOpenChange: (open: boolean) => void
    /** Current semicolon-delimited ignore rules (the source of truth). */
    rules: string
    /** Called with the new semicolon-delimited string when the user clicks Apply. */
    onRulesChange: (rules: string) => void
    /**
     * Optional folder path the rules belong to. When provided, the table paths are
     * treated as relative to this folder (full-path label + tooltips are shown).
     * Omit for global ignore rules.
     */
    basePath?: string
    /** Dialog title. Defaults to "Ignore Rules". */
    title?: string
    /** Optional description shown under the title. */
    description?: string
}

export function IgnoreRulesDialog({
    open,
    onOpenChange,
    rules,
    onRulesChange,
    basePath,
    title = "Ignore Rules",
    description,
}: IgnoreRulesDialogProps) {
    // Local working copy of the rules (list view). Committed to the caller on Apply.
    const [draft, setDraft] = useState<string[]>([])
    const [newRule, setNewRule] = useState<string>("")
    const [activeTab, setActiveTab] = useState<string>("ui")

    // "Adjust state while rendering" (https://react.dev/reference/react/useState#storing-information-from-previous-renders):
    // remember the props we've already synced into the draft, and when they change we reset the working
    // copy during render instead of inside an effect. This avoids calling setState() synchronously within
    // an effect (which would cause a cascading re-render).
    const [lastSynced, setLastSynced] = useState({ open, rules })

    if (lastSynced.open !== open || lastSynced.rules !== rules) {
        setLastSynced({ open, rules })
        if (open) {
            setDraft(splitRules(rules))
            setNewRule("")
            setActiveTab("ui")
        }
    }

    function moveRule(index: number, dir: -1 | 1) {
        const target = index + dir
        if (target < 0 || target >= draft.length) return
        setDraft((prev) => {
            const next = [...prev]
            const [moved] = next.splice(index, 1)
            next.splice(target, 0, moved)
            return next
        })
    }

    function deleteRule(index: number) {
        setDraft((prev) => prev.filter((_, i) => i !== index))
    }

    function addRule() {
        const value = newRule.trim()
        if (!value) return
        setDraft((prev) => [...prev, value])
        setNewRule("")
    }

    function applyChanges() {
        onRulesChange(draft.join(";"))
        onOpenChange(false)
    }

    const isFolderSpecific = Boolean(basePath && basePath.trim().length > 0)
    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="sm:max-w-lg">
                <DialogHeader>
                    <DialogTitle>{title}</DialogTitle>
                    <DialogDescription>
                        {description ??
                            (isFolderSpecific
                                ? "Ordered ignore rules for this folder. Relative paths are valid directly; glob patterns are allowed."
                                : "Global semicolon-delimited patterns skipped across all watched directories.")}
                    </DialogDescription>
                </DialogHeader>

                <Tabs value={activeTab} onValueChange={setActiveTab}>
                    <TabsList>
                        <TabsTrigger value="ui">Visual editor</TabsTrigger>
                        <TabsTrigger value="raw">Raw rules</TabsTrigger>
                    </TabsList>

                    <TabsContent value="ui" className="space-y-3">
                        {isFolderSpecific && (
                            <div className="flex items-center gap-2 rounded-md border border-border bg-muted/50 px-3 py-2 text-sm">
                                <FolderCheck className="size-4 shrink-0 text-muted-foreground" />
                                <span className="truncate font-medium">{basePath}</span>
                                <span className="ml-auto shrink-0 text-xs text-muted-foreground">Paths are relative</span>
                            </div>
                        )}

                        <div className="max-h-64 overflow-y-auto rounded-lg border border-border">
                            <Table>
                                <TableHeader>
                                    <TableRow>
                                        <TableHead>Path / Rule</TableHead>
                                        <TableHead className="text-center">Path</TableHead>
                                        <TableHead className="w-[130px]" />
                                    </TableRow>
                                </TableHeader>
                                <TableBody>
                                    {draft.length === 0 ? (
                                        <TableRow>
                                            <TableCell colSpan={3} className="py-6 text-center text-muted-foreground">No rules yet — add one below.</TableCell>
                                        </TableRow>
                                    ) : (
                                        draft.map((rule, index) => {
                                            const valid = isValidRulePath(rule)
                                            return (
                                                <TableRow key={`${index}-${rule}`}>
                                                    <TableCell className="font-medium">
                                                        <Tooltip>
                                                            <TooltipTrigger asChild>
                                                                <button
                                                                    type="button"
                                                                    className="cursor-help font-mono text-xs underline-offset-2 hover:underline"
                                                                >
                                                                    {rule}
                                                                </button>
                                                            </TooltipTrigger>
                                                            <TooltipContent side="top">
                                                                {isFolderSpecific && basePath
                                                                    ? `Full path: ${joinPath(basePath, rule)}`
                                                                    : `Pattern: ${rule}`}
                                                            </TooltipContent>
                                                        </Tooltip>
                                                    </TableCell>
                                                    <TableCell className="text-center">
                                                        {valid ? (
                                                            <CircleCheck className="mx-auto size-4 text-emerald-600" aria-label="Valid path" />
                                                        ) : (
                                                            <TriangleAlert className="mx-auto size-4 text-amber-600" aria-label="Invalid path" />
                                                        )}
                                                    </TableCell>
                                                    <TableCell>
                                                        <div className="flex items-center justify-end gap-1">
                                                            <Button type="button" variant="ghost" size="icon-sm" disabled={index === 0} onClick={() => moveRule(index, -1)} aria-label="Move up">
                                                                <ArrowUp className="size-3.5" />
                                                            </Button>
                                                            <Button type="button" variant="ghost" size="icon-sm" disabled={index === draft.length - 1} onClick={() => moveRule(index, 1)} aria-label="Move down">
                                                                <ArrowDown className="size-3.5" />
                                                            </Button>
                                                            <Button type="button" variant="ghost" size="icon-sm" className="text-destructive hover:text-destructive" onClick={() => deleteRule(index)} aria-label="Delete">
                                                                <Trash2 className="size-3.5" />
                                                            </Button>
                                                        </div>
                                                    </TableCell>
                                                </TableRow>
                                            )
                                        })
                                    )}
                                </TableBody>
                            </Table>
                        </div>

                        <div className="flex items-center gap-2">
                            <Input
                                value={newRule}
                                onChange={(e) => setNewRule(e.target.value)}
                                onKeyDown={(e) => {
                                    if (e.key === "Enter") {
                                        e.preventDefault()
                                        addRule()
                                    }
                                }}
                                placeholder="Add a new path or pattern… (Enter to add)"
                                className="flex-1"
                            />
                            <Button type="button" variant="default" size="icon" onClick={addRule} disabled={!newRule.trim()} aria-label="Add rule">
                                <ArrowUp className={cn("size-4 shrink-0")} />
                            </Button>
                        </div>
                    </TabsContent>

                    <TabsContent value="raw">
                        <Textarea
                            className="min-h-40 font-mono text-xs"
                            value={draft.join(";")}
                            onChange={(e) => setDraft(splitRules(e.target.value))}
                            placeholder="*.tmp;node_modules/;.git/"
                        />
                        <p className="mt-1 text-xs text-muted-foreground">Semicolon-delimited ignore rules — one per entry.</p>
                    </TabsContent>
                </Tabs>

                <DialogFooter>
                    <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
                        Cancel
                    </Button>
                    {activeTab == "ui" ? 
                        <Button type="button" onClick={applyChanges}>
                            Ok
                        </Button>
                    : 
                        <Button type="button" onClick={applyChanges}>
                            Apply
                        </Button>
                    }
                </DialogFooter>
            </DialogContent>
        </Dialog>
    )
}

export default IgnoreRulesDialog