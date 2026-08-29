import * as React from "react"
import { Bell, Check, CircleCheck, Copy, Info, OctagonX, TriangleAlert } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { ScrollArea } from "@/components/ui/scroll-area"
import {
    Drawer,
    DrawerClose,
    DrawerContent,
    DrawerDescription,
    DrawerFooter,
    DrawerHeader,
    DrawerTitle,
    DrawerTrigger,
} from "@/components/ui/drawer"
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible"
import { ChevronDown } from "lucide-react"
import { toast } from "sonner"
import {
    clearAllNotifications,
    clearNotification,
    dismissNotification,
    type NotificationLevel,
    useNotificationCenter,
} from "@/lib/notification-center"
import { stringifyDetails } from "@/lib/notification-utils"

const levelIcon: Record<NotificationLevel, React.ReactNode> = {
    success: <CircleCheck className="size-4 text-emerald-600" />,
    info: <Info className="size-4 text-sky-600" />,
    warning: <TriangleAlert className="size-4 text-amber-600" />,
    error: <OctagonX className="size-4 text-red-600" />,
}

const formatTimestamp = (timestamp: number) =>
    new Intl.DateTimeFormat(undefined, {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
        month: "short",
        day: "2-digit",
    }).format(timestamp)

function NotificationDetails({ details }: { details: unknown }) {
    const [isExpanded, setIsExpanded] = React.useState(false)
    const [isCopied, setIsCopied] = React.useState(false)
    const detailText = stringifyDetails(details)
    if (!detailText) return null

    const handleCopy = async () => {
        try {
            await navigator.clipboard.writeText(detailText)
            setIsCopied(true)
            window.setTimeout(() => setIsCopied(false), 1600)
        } catch {
            setIsCopied(false)
        }
    }

    return (
        <Collapsible open={isExpanded} onOpenChange={setIsExpanded}>
            <CollapsibleTrigger className="flex w-full items-center justify-between border-t border-border pt-2 text-left text-xs text-muted-foreground hover:text-foreground">
                <span>{isExpanded ? "Hide details" : "Show details"}</span>
                <ChevronDown className={`size-4 transition-transform ${isExpanded ? "rotate-180" : "rotate-0"}`} />
            </CollapsibleTrigger>
            <CollapsibleContent className="pt-2">
                <div className="relative min-w-0 pt-2">
                    <Button
                        type="button"
                        variant="ghost"
                        size="icon-xs"
                        aria-label={isCopied ? "Details copied" : "Copy details"}
                        title={isCopied ? "Details copied" : "Copy details"}
                        onClick={handleCopy}
                        className="absolute top-3 right-2 z-10 border border-border/60 bg-background/75 text-muted-foreground shadow-sm backdrop-blur-sm hover:bg-background/90 hover:text-foreground"
                    >
                        {isCopied ? <Check /> : <Copy />}
                    </Button>
                    <pre className="max-h-48 min-w-0 overflow-auto rounded-md bg-muted p-2 text-[11px] leading-relaxed whitespace-pre-wrap break-all">
                        {detailText}
                    </pre>
                </div>
            </CollapsibleContent>
        </Collapsible>
    )
}

export function NotificationCenter() {
    const [open, setOpen] = React.useState(false)
    const { notifications, activeCount } = useNotificationCenter()
    const handleClearAll = React.useCallback(() => {
        toast.dismiss()
        clearAllNotifications()
    }, [])

    return (
        <>
            <div className="fixed inset-x-0 bottom-0 z-30 border-t border-border bg-background/95 backdrop-blur supports-backdrop-filter:backdrop-blur-sm">
                <div className="mx-auto flex h-14 w-full items-center justify-end px-4">
                    <Drawer open={open} onOpenChange={setOpen} direction="right">
                        <DrawerTrigger asChild>
                            <Button variant="outline" className="relative">
                                <Bell className="size-4" />
                                Notifications
                                {activeCount > 0 ? (
                                    <span className="absolute -top-2 -right-2 inline-flex min-w-5 items-center justify-center rounded-full bg-primary px-1 text-[11px] font-semibold text-primary-foreground">
                                        {activeCount}
                                    </span>
                                ) : null}
                            </Button>
                        </DrawerTrigger>

                        <DrawerContent className="w-[min(96vw,32rem)] sm:max-w-120">
                            <DrawerHeader className="border-b border-border">
                                <DrawerTitle>Notifications</DrawerTitle>
                                <DrawerDescription>Recent app notifications and errors.</DrawerDescription>
                            </DrawerHeader>

                            <div className="flex items-center justify-end px-4 py-3">
                                <Button variant="ghost" size="sm" onClick={handleClearAll}>
                                    Clear all
                                </Button>
                            </div>

                            <ScrollArea className="h-[calc(100dvh-11rem)] px-4 pb-4">
                                <div className="space-y-2 pr-2">
                                    {notifications.length === 0 ? (
                                        <div className="rounded-lg border border-dashed border-border p-4 text-sm text-muted-foreground">
                                            No notifications yet.
                                        </div>
                                    ) : null}

                                    {notifications.map((item) => {
                                        const isActive = item.dismissedAt === undefined
                                        return (
                                            <article key={item.id} className="space-y-2 rounded-lg border border-border bg-card p-3">
                                                <div className="flex items-start justify-between gap-2">
                                                    <div className="flex min-w-0 gap-2">
                                                        <span className="mt-0.5">{levelIcon[item.level]}</span>
                                                        <div className="min-w-0">
                                                            <p className="truncate text-sm font-semibold text-foreground">
                                                                {item.title}
                                                                {item.statusCode !== undefined ? ` (HTTP ${item.statusCode})` : ""}
                                                            </p>
                                                            {item.summary ? (
                                                                <p className="text-xs text-muted-foreground">{item.summary}</p>
                                                            ) : null}
                                                        </div>
                                                    </div>
                                                    <Badge variant={isActive ? "secondary" : "outline"}>
                                                        {isActive ? "Active" : "Dismissed"}
                                                    </Badge>
                                                </div>

                                                <NotificationDetails details={item.details} />

                                                <div className="flex items-center justify-between gap-2 text-[11px] text-muted-foreground">
                                                    <span className="min-w-0 truncate">{formatTimestamp(item.createdAt)}</span>
                                                    <div className="flex shrink-0 items-center gap-1">
                                                        {isActive ? (
                                                            <Button
                                                                variant="ghost"
                                                                size="sm"
                                                                onClick={() => {
                                                                    dismissNotification(item.id, "manual")
                                                                    toast.dismiss(item.id)
                                                                }}
                                                            >
                                                                Dismiss
                                                            </Button>
                                                        ) : (
                                                            <span className="pr-1">{item.dismissReason === "timeout" ? "Auto-dismissed" : "Dismissed"}</span>
                                                        )}
                                                        <Button
                                                            variant="ghost"
                                                            size="sm"
                                                            onClick={() => {
                                                                toast.dismiss(item.id)
                                                                clearNotification(item.id)
                                                            }}
                                                        >
                                                            Clear
                                                        </Button>
                                                    </div>
                                                </div>
                                            </article>
                                        )
                                    })}
                                </div>
                            </ScrollArea>

                            <DrawerFooter className="border-t border-border">
                                <DrawerClose asChild>
                                    <Button variant="outline">Close</Button>
                                </DrawerClose>
                            </DrawerFooter>
                        </DrawerContent>
                    </Drawer>
                </div>
            </div>
        </>
    )
}
