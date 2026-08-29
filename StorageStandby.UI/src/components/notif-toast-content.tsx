import * as React from "react"
import { Check, ChevronDown, Copy } from "lucide-react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible"
import type { AppNotification, NotificationDismissReason } from "@/lib/notification-center"
import { stringifyDetails } from "@/lib/notification-utils"

function useHoverPauseDismissTimer({
    durationMs,
    onTimeout,
}: {
    durationMs: number
    onTimeout: () => void
}) {
    const [remainingMs, setRemainingMs] = React.useState(durationMs)
    const [isPaused, setIsPaused] = React.useState(false)
    const hasTimedOutRef = React.useRef(false)

    React.useEffect(() => {
        if (isPaused || hasTimedOutRef.current) return

        let previousTick = window.performance.now()
        const intervalId = window.setInterval(() => {
            const now = window.performance.now()
            const elapsed = now - previousTick
            previousTick = now

            setRemainingMs((current) => {
                const next = Math.max(0, current - elapsed)
                if (next <= 0 && !hasTimedOutRef.current) {
                    hasTimedOutRef.current = true
                    onTimeout()
                }
                return next
            })
        }, 100)

        return () => {
            window.clearInterval(intervalId)
        }
    }, [isPaused, onTimeout])

    return {
        remainingMs,
        isPaused,
        setIsPaused,
    }
}

export function NotificationToastContent({
    toastId,
    notification,
    durationMs,
    onDismiss,
}: {
    toastId: string | number
    notification: AppNotification
    durationMs: number
    onDismiss: (reason: NotificationDismissReason) => void
}) {
    const [isExpanded, setIsExpanded] = React.useState(false)
    const [isCopied, setIsCopied] = React.useState(false)
    const detailText = stringifyDetails(notification.details)
    const hasDetails = detailText.length > 0
    const handleTimeout = React.useCallback(() => {
        onDismiss("timeout")
        toast.dismiss(toastId)
    }, [onDismiss, toastId])

    const { remainingMs, setIsPaused } = useHoverPauseDismissTimer({
        durationMs,
        onTimeout: handleTimeout,
    })

    const handleCopyDetails = async () => {
        try {
            await navigator.clipboard.writeText(detailText)
            setIsCopied(true)
            window.setTimeout(() => setIsCopied(false), 1600)
        } catch {
            setIsCopied(false)
        }
    }

    return (
        <div
            className="w-full space-y-3 rounded-lg border border-border bg-background p-4 text-sm shadow-sm"
            onMouseEnter={() => setIsPaused(true)}
            onMouseLeave={() => setIsPaused(false)}
        >
            <div className="space-y-1">
                <p className="font-semibold text-foreground">
                    {notification.title}
                    {notification.statusCode !== undefined ? ` (HTTP ${notification.statusCode})` : ""}
                </p>
                {notification.summary ? <p className="text-xs text-muted-foreground">{notification.summary}</p> : null}
            </div>

            {hasDetails ? (
                <Collapsible open={isExpanded} onOpenChange={setIsExpanded}>
                    <CollapsibleTrigger className="flex w-full cursor-pointer items-center justify-between border-t border-border pt-3 text-left text-xs font-medium text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2">
                        <span>{isExpanded ? "Hide details" : "Show details"}</span>
                        <ChevronDown className={`size-4 transition-transform ${isExpanded ? "rotate-180" : "rotate-0"}`} />
                    </CollapsibleTrigger>
                    <CollapsibleContent className="mt-2">
                        <div className="relative min-w-0 pt-2">
                            <Button
                                type="button"
                                variant="ghost"
                                size="icon-xs"
                                aria-label={isCopied ? "Details copied" : "Copy details"}
                                title={isCopied ? "Details copied" : "Copy details"}
                                onClick={handleCopyDetails}
                                className="absolute top-3 right-5 z-10 border border-border/60 bg-background/75 text-muted-foreground shadow-sm backdrop-blur-sm hover:bg-background/90 hover:text-foreground"
                            >
                                {isCopied ? <Check /> : <Copy />}
                            </Button>
                            <pre className="max-h-44 min-w-0 overflow-auto rounded-md bg-muted p-2 text-[11px] leading-relaxed whitespace-pre-wrap break-all">
                                {detailText}
                            </pre>
                        </div>
                    </CollapsibleContent>
                </Collapsible>
            ) : null}

            <div className="space-y-2">
                <div className="h-1 w-full overflow-hidden rounded-full bg-muted">
                    <div
                        className="h-full bg-primary transition-[width] duration-100 ease-linear"
                        style={{ width: `${Math.max(0, (remainingMs / durationMs) * 100)}%` }}
                    />
                </div>
                <div className="flex items-center justify-end">
                    {/* <p className="text-[11px] text-muted-foreground">
                        {isPaused ? "Auto-dismiss paused" : `Auto-dismiss in ${Math.ceil(remainingMs / 1000)}s`}
                    </p> */}
                    <Button
                        className=""
                        variant="ghost"
                        size="sm"
                        onClick={() => {
                            onDismiss("manual")
                            toast.dismiss(toastId)
                        }}
                    >
                        Dismiss
                    </Button>
                </div>
            </div>
        </div>
    )
}
