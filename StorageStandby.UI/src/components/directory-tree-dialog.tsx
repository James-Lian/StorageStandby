import { useEffect, useMemo, useState } from "react"
import { ChevronRight, File, Folder, Loader2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
} from "@/components/ui/dialog"
import {
    Collapsible,
    CollapsibleContent,
    CollapsibleTrigger,
} from "@/components/ui/collapsible"
import { fetchDirectoryTree } from "@/api/data"
import { fetchIgnoredPaths } from "@/api/utils"
import type { DirectoryNode } from "@/types/data"
import { cn } from "@/lib/utils"

interface DirectoryTreeDialogProps {
    open: boolean
    onOpenChange: (open: boolean) => void
    folderPath: string | null
    ignoreRules: string
}

function flattenTreePaths(node: DirectoryNode): string[] {
    return [
        node.fullPath,
        ...node.children.flatMap((child) => child ? flattenTreePaths(child) : []),
    ]
}

function TreeNode({ node, ignoredPaths, basePath }: {
    node: DirectoryNode
    ignoredPaths: Set<string>
    basePath: string
}) {
    const ignored = ignoredPaths.has(node.fullPath)
    const children = node.children.filter((child): child is DirectoryNode => child !== null)

    if (!node.isDirectory) {
        return (
            <div className={cn("flex items-center gap-2 rounded-md px-2 py-1 text-sm", ignored && "text-rose-600")}>
                <File className="size-4 shrink-0" />
                <span className="truncate" title={node.fullPath}>{node.name}</span>
            </div>
        )
    }

    return (
        <Collapsible defaultOpen={node.fullPath === basePath}>
            <CollapsibleTrigger asChild>
                <Button
                    variant="ghost"
                    size="sm"
                    className={cn("group w-full justify-start gap-2", ignored && "text-rose-600")}
                >
                    <ChevronRight className="size-4 shrink-0 transition-transform group-data-[state=open]:rotate-90" />
                    <Folder className="size-4 shrink-0" />
                    <span className="truncate" title={node.fullPath}>{node.name}</span>
                </Button>
            </CollapsibleTrigger>
            {children.length > 0 && (
                <CollapsibleContent>
                    <div className="ml-4 border-l border-border pl-2">
                        {children.map((child) => (
                            <TreeNode
                                key={child.fullPath}
                                node={child}
                                ignoredPaths={ignoredPaths}
                                basePath={basePath}
                            />
                        ))}
                    </div>
                </CollapsibleContent>
            )}
        </Collapsible>
    )
}

export function DirectoryTreeDialog({
    open,
    onOpenChange,
    folderPath,
    ignoreRules,
}: DirectoryTreeDialogProps) {
    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="max-h-[85vh] sm:max-w-2xl">
                <DialogHeader>
                    <DialogTitle>Folder contents</DialogTitle>
                    <DialogDescription className="truncate" title={folderPath ?? undefined}>
                        {folderPath ?? "No folder selected"}
                    </DialogDescription>
                </DialogHeader>
                <DirectoryTreeContent
                    key={`${open}-${folderPath ?? ""}`}
                    open={open}
                    folderPath={folderPath}
                    ignoreRules={ignoreRules}
                />
                <p className="text-xs text-muted-foreground">Ignored paths are shown in red. Set ignored paths with ignore rules.</p>
            </DialogContent>
        </Dialog>
    )
}

function DirectoryTreeContent({
    open,
    folderPath,
    ignoreRules,
}: Pick<DirectoryTreeDialogProps, "open" | "folderPath" | "ignoreRules">) {
    const [tree, setTree] = useState<DirectoryNode | null>(null)
    const [ignoredPaths, setIgnoredPaths] = useState<Set<string>>(new Set())
    const [isLoading, setIsLoading] = useState(open && Boolean(folderPath))
    const [error, setError] = useState<string | null>(null)

    useEffect(() => {
        if (!open || !folderPath) return

        let cancelled = false
        void fetchDirectoryTree(folderPath)
            .then(async (result) => {
                if (!cancelled) {
                    setTree(result)
                    const paths = result ? flattenTreePaths(result) : []
                    const matches = await fetchIgnoredPaths(folderPath, paths, ignoreRules)
                    setIgnoredPaths(new Set(paths.filter((_, index) => matches[index])))
                    setIsLoading(false)
                }
            })
            .catch((reason: unknown) => {
                if (!cancelled) {
                    setError(reason instanceof Error ? reason.message : "Unable to load directory tree.")
                    setIsLoading(false)
                }
            })

        return () => {
            cancelled = true
        }
    }, [open, folderPath, ignoreRules])

    const root = useMemo(() => tree && folderPath ? (
        <TreeNode node={tree} ignoredPaths={ignoredPaths} basePath={folderPath} />
    ) : null, [tree, folderPath, ignoredPaths])

    return (
        <div className="max-h-[60vh] min-h-40 overflow-auto rounded-md border border-border p-2">
            {isLoading && (
                <div className="flex items-center gap-2 p-3 text-sm text-muted-foreground">
                    <Loader2 className="size-4 animate-spin" /> Loading folder contents...
                </div>
            )}
            {error && <p className="p-3 text-sm text-destructive">{error}</p>}
            {!isLoading && !error && root}
        </div>
    )
}
