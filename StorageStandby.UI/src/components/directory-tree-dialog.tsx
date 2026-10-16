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
import type { DirectoryNode } from "@/types/data"
import { cn } from "@/lib/utils"

interface DirectoryTreeDialogProps {
    open: boolean
    onOpenChange: (open: boolean) => void
    folderPath: string | null
    ignoreRules: string
}

function normalizePath(value: string) {
    return value.replace(/\\/g, "/").replace(/\/+$/, "").toLowerCase()
}

function escapeRegex(value: string) {
    return value.replace(/[.+^${}()|[\]\\]/g, "\\$&")
}

function globToRegex(glob: string) {
    let pattern = ""
    for (let index = 0; index < glob.length; index += 1) {
        const character = glob[index]
        if (character === "*" && glob[index + 1] === "*") {
            pattern += ".*"
            index += 1
        } else if (character === "*") {
            pattern += "[^/]*"
        } else if (character === "?") {
            pattern += "[^/]"
        } else {
            pattern += escapeRegex(character)
        }
    }
    return new RegExp(`^${pattern}$`, "i")
}

function isIgnoredPath(fullPath: string, basePath: string, rules: string) {
    const normalizedFullPath = normalizePath(fullPath)
    const normalizedBasePath = normalizePath(basePath)
    const relativePath = normalizedFullPath.startsWith(`${normalizedBasePath}/`)
        ? normalizedFullPath.slice(normalizedBasePath.length + 1)
        : normalizedFullPath

    return rules
        .split(";")
        .map((rule) => rule.trim())
        .filter(Boolean)
        .some((rule) => {
            const normalizedRawRule = rule.replace(/\\/g, "/").toLowerCase()
            const matchesDescendants = normalizedRawRule.endsWith("/")
            const ruleWithoutSlash = normalizedRawRule.replace(/\/+$/, "")
            const candidates = ruleWithoutSlash.includes(":/") || ruleWithoutSlash.startsWith("/")
                ? [normalizedFullPath]
                : [relativePath, normalizedFullPath]
            const regex = globToRegex(ruleWithoutSlash)
            return candidates.some((candidate) =>
                regex.test(candidate)
                || matchesDescendants && candidate.startsWith(`${ruleWithoutSlash}/`),
            )
        })
}

function TreeNode({ node, basePath, ignoreRules }: {
    node: DirectoryNode
    basePath: string
    ignoreRules: string
}) {
    const ignored = isIgnoredPath(node.fullPath, basePath, ignoreRules)
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
                                basePath={basePath}
                                ignoreRules={ignoreRules}
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
    const [isLoading, setIsLoading] = useState(open && Boolean(folderPath))
    const [error, setError] = useState<string | null>(null)

    useEffect(() => {
        if (!open || !folderPath) return

        let cancelled = false
        void fetchDirectoryTree(folderPath)
            .then((result) => {
                if (!cancelled) {
                    setTree(result)
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
    }, [open, folderPath])

    const root = useMemo(() => tree && folderPath ? (
        <TreeNode node={tree} basePath={folderPath} ignoreRules={ignoreRules} />
    ) : null, [tree, folderPath, ignoreRules])

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
