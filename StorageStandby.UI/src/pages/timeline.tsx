import { useEffect, useState } from "react"
import {
    Activity,
    AlertCircle,
    CheckCircle2,
    CircleX,
    Cloud,
    FileUp,
    Loader2,
    RefreshCw,
} from "lucide-react"
import { fetchTimelineEvents } from "@/api/data"
import type { TimelineEvent } from "@/types/data"
import { formatLocalDateTime } from "@/lib/utils"

const providerNames: Record<number, string> = {
    0: "Google Drive",
    1: "OneDrive",
    2: "Dropbox",
}

const actionNames: Record<number, string> = {
    0: "Sync started",
    1: "Sync completed",
    2: "Sync interrupted",
    3: "Sync skipped",
    4: "Destination skipped",
    5: "Item uploaded",
    6: "Item deleted",
    7: "Item moved",
    8: "Item renamed",
    9: "Item failed",
    10: "Cloud assignment requested",
    11: "Cloud bootstrap started",
    12: "Cloud bootstrap completed",
    13: "Cloud removal requested",
    14: "Cloud removal completed",
    15: "Remote folder created",
    16: "Remote folder reused",
}

function EventIcon({ event }: { event: TimelineEvent }) {
    if (event.type === 2) return <CircleX className="size-5" />
    if (event.type === 1) return <AlertCircle className="size-5" />
    if (event.type === 3) return <CheckCircle2 className="size-5" />
    if (event.action === 5) return <FileUp className="size-5" />
    if (event.action >= 10) return <Cloud className="size-5" />
    return <Activity className="size-5" />
}

function eventTone(type: number) {
    if (type === 2) return "border-red-200 bg-red-50 text-red-700"
    if (type === 1) return "border-amber-200 bg-amber-50 text-amber-700"
    if (type === 3) return "border-emerald-200 bg-emerald-50 text-emerald-700"
    return "border-slate-200 bg-slate-50 text-slate-600"
}

export function Timeline() {
    const [events, setEvents] = useState<TimelineEvent[]>([])
    const [isLoading, setIsLoading] = useState(true)
    const [error, setError] = useState<string | null>(null)

    const loadEvents = async () => {
        setIsLoading(true)
        setError(null)
        try {
            setEvents(await fetchTimelineEvents())
        } catch (loadError) {
            setError(loadError instanceof Error ? loadError.message : "Unable to load timeline")
        } finally {
            setIsLoading(false)
        }
    }

    useEffect(() => {
        const timer = window.setTimeout(() => {
            void loadEvents()
        }, 0)
        return () => window.clearTimeout(timer)
    }, [])

    return (
        <div className="space-y-6 p-4">
            <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
                <div>
                    <p className="text-sm font-medium uppercase tracking-[0.18em] text-muted-foreground">Activity history</p>
                    <h1 className="mt-2 text-3xl font-semibold tracking-tight">Timeline</h1>
                    <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                        Follow assignments, bootstrap attempts, uploads, and provider failures as they happen.
                    </p>
                </div>
                <button
                    type="button"
                    onClick={() => void loadEvents()}
                    className="inline-flex items-center gap-2 self-start rounded-md border border-border px-3 py-2 text-sm font-medium hover:bg-muted md:self-auto"
                >
                    <RefreshCw className="size-4" /> Refresh
                </button>
            </div>

            {isLoading ? (
                <div className="flex items-center gap-2 rounded-xl border border-border p-8 text-sm text-muted-foreground">
                    <Loader2 className="size-4 animate-spin" /> Loading timeline...
                </div>
            ) : error ? (
                <div className="rounded-xl border border-red-200 bg-red-50 p-5 text-sm text-red-700">{error}</div>
            ) : events.length === 0 ? (
                <div className="rounded-xl border border-dashed border-border p-10 text-center">
                    <Activity className="mx-auto size-8 text-muted-foreground" />
                    <h2 className="mt-3 font-semibold">No activity yet</h2>
                    <p className="mt-1 text-sm text-muted-foreground">Sync activity will appear here after the next assignment or provider run.</p>
                </div>
            ) : (
                <div className="space-y-3">
                    {events.map((event) => (
                        <article key={event.id} className="rounded-xl border border-border bg-background p-4 shadow-sm">
                            <div className="flex gap-3">
                                <div className={`mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-full border ${eventTone(event.type)}`}>
                                    <EventIcon event={event} />
                                </div>
                                <div className="min-w-0 flex-1">
                                    <div className="flex flex-col gap-1 sm:flex-row sm:items-start sm:justify-between sm:gap-4">
                                        <div>
                                            <h2 className="font-medium">{event.message || actionNames[event.action] || "Timeline event"}</h2>
                                            <p className="text-sm text-muted-foreground">
                                                {actionNames[event.action] || "Activity"}
                                                {event.provider !== null ? ` · ${providerNames[event.provider] ?? `Provider ${event.provider}`}` : ""}
                                                {event.accountId ? ` · ${event.accountId}` : ""}
                                            </p>
                                        </div>
                                        <time className="shrink-0 text-xs text-muted-foreground">{formatLocalDateTime(event.occurredAtUtc)}</time>
                                    </div>
                                    {event.localPath && <p className="mt-3 break-all rounded-md bg-muted px-3 py-2 font-mono text-xs">{event.localPath}</p>}
                                    {event.details && <pre className="mt-3 whitespace-pre-wrap wrap-break-word text-xs text-muted-foreground">{event.details}</pre>}
                                    <p className="mt-3 text-xs text-muted-foreground">Folder #{event.watchedFolderId}{event.syncEventId !== null ? ` · Sync event #${event.syncEventId}` : ""}</p>
                                </div>
                            </div>
                        </article>
                    ))}
                </div>
            )}
        </div>
    )
}