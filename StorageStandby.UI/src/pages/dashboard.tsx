import { Activity, ArrowRight, CheckCircle2, Cloud, CloudUpload, FolderPlus, ShieldCheck, Zap } from "lucide-react"
import { Link } from "react-router-dom"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
    ChartContainer,
    ChartLegend,
    ChartLegendContent,
    ChartTooltip,
    ChartTooltipContent,
} from "@/components/ui/chart"
import { Marker, MarkerContent, MarkerIcon } from "@/components/ui/marker"
import {
    Bar,
    BarChart,
    CartesianGrid,
    PolarAngleAxis,
    RadialBar,
    RadialBarChart,
    XAxis,
    YAxis,
} from "recharts"
import { useEffect, useState } from "react"

const weeklySyncData = [
    { day: "Mon", google: 24, onedrive: 10, dropbox: 7 },
    { day: "Tue", google: 18, onedrive: 16, dropbox: 9 },
    { day: "Wed", google: 31, onedrive: 13, dropbox: 6 },
    { day: "Thu", google: 22, onedrive: 19, dropbox: 8 },
    { day: "Fri", google: 29, onedrive: 14, dropbox: 11 },
    { day: "Sat", google: 27, onedrive: 17, dropbox: 9 },
    { day: "Sun", google: 34, onedrive: 15, dropbox: 12 },
]

const storageUsage = [
    { name: "Google Drive", used: 74, quota: 100, cost: "$9.90", color: "#2563eb" },
    { name: "OneDrive", used: 58, quota: 100, cost: "$6.30", color: "#0f766e" },
    { name: "Dropbox", used: 81, quota: 100, cost: "$12.40", color: "#7c3aed" },
]

const timelineEvents = [
    { title: "Nightly sync completed", detail: "3.2 GB moved to Google Drive", time: "8 min ago", icon: <CloudUpload className="size-4" />, tone: "text-emerald-600" },
    { title: "Backup set created", detail: "Project backups refreshed from 48 files", time: "24 min ago", icon: <FolderPlus className="size-4" />, tone: "text-sky-600" },
    { title: "Storage warning cleared", detail: "Quota usage normalized after cleanup", time: "1 hr ago", icon: <ShieldCheck className="size-4" />, tone: "text-violet-600" },
    { title: "Manual upload queued", detail: "4 new files ready to sync", time: "2 hrs ago", icon: <Zap className="size-4" />, tone: "text-amber-600" },
]

const chartConfig = {
    google: { label: "Google Drive", color: "#2563eb" },
    onedrive: { label: "OneDrive", color: "#0f766e" },
    dropbox: { label: "Dropbox", color: "#7c3aed" },
}

interface Status {
    syncedFiles: number,
    syncedFolders: number,
    gbSynced: number, // in bytes
    addedFiles: number,
    addedFolders: number,
    pendingSync: number, // 0, 1, 2
}

export function Dashboard() {

    const [status, setStatus] = useState<Status | null>(null);

    let syncedFiles: number = 128
    let syncedFolders: number = 10
    let addedFiles: number = 24
    let addedFolders: number = 3
    let pendingSync: number = 6

    // empty dependency array runs once on component mount
    useEffect(() => {
        async function fetchStatus() {
            const response = await fetch("http://storagestandby.local");
            
            const data = await response.json();

            setStatus(data);
        }
    }, [])

    return (
        <div className="space-y-6 p-4">
            <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
                <div className="space-y-2">
                    <div className="inline-flex items-center gap-2 rounded-full border border-border bg-background px-3 py-1 text-xs font-medium uppercase tracking-[0.2em] text-muted-foreground">
                        <Activity className="size-3.5" /> StorageStandby overview
                    </div>
                    <div>
                        <h1 className="text-2xl font-semibold">Your backup command center</h1>
                        <p className="text-sm text-muted-foreground">
                            Keep an eye on sync health, storage usage, and recent backup actions from one place.
                        </p>
                    </div>
                </div>
                <div className="flex flex-wrap gap-2">
                    <Button asChild variant="outline" size="sm">
                        <Link to="/sync">Run sync</Link>
                    </Button>
                    <Button asChild size="sm">
                        <Link to="/folders">Manage backups</Link>
                    </Button>
                </div>
            </div>

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                <div className="rounded-2xl border border-border bg-background p-4">
                    <p className="text-sm text-muted-foreground">Added today</p>
                    <div className="mt-2 flex items-end justify-between">
                        <div>
                            <p className="text-2xl font-semibold">{addedFiles} files</p>
                            <p className="text-sm text-muted-foreground">{addedFolders} folders</p>
                        </div>
                        <FolderPlus className="size-5 text-primary" />
                    </div>
                </div>
                <div className="rounded-2xl border border-border bg-background p-4">
                    <p className="text-sm text-muted-foreground">Synced today</p>
                    <div className="mt-2 flex items-end justify-between">
                        <div>
                            <p className="text-2xl font-semibold">{syncedFiles} files</p>
                            <p className="text-sm text-muted-foreground">{syncedFolders} folders</p>
                        </div>
                        <CheckCircle2 className="size-5 text-emerald-600" />
                    </div>
                </div>
                <div className="rounded-2xl border border-border bg-background p-4">
                    <p className="text-sm text-muted-foreground">Pending items</p>
                    <div className="mt-2 flex items-end justify-between">
                        <div>
                            <p className="text-2xl font-semibold">{pendingSync}</p>
                            <p className="text-sm text-muted-foreground">needs attention</p>
                        </div>
                        <Zap className="size-5 text-amber-600" />
                    </div>
                </div>
                <div className="rounded-2xl border border-border bg-background p-4">
                    <p className="text-sm text-muted-foreground">Sync health</p>
                    <div className="mt-2 flex items-end justify-between">
                        <div>
                            <p className="text-2xl font-semibold">Healthy</p>
                            <p className="text-sm text-muted-foreground">Last run 8 min ago</p>
                        </div>
                        <ShieldCheck className="size-5 text-sky-600" />
                    </div>
                </div>
            </div>

            <div className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
                <div className="rounded-2xl border border-border bg-background p-4">
                    <div className="mb-4 flex items-center justify-between">
                        <div>
                            <h2 className="text-lg font-semibold">Weekly sync activity</h2>
                            <p className="text-sm text-muted-foreground">Files and folders synced across connected clouds.</p>
                        </div>
                        <Badge variant="outline">Last 7 days</Badge>
                    </div>

                    <ChartContainer config={chartConfig} className="h-65 w-full">
                        <BarChart data={weeklySyncData}>
                            <CartesianGrid vertical={false} strokeDasharray="3 3" />
                            <XAxis dataKey="day" tickLine={false} axisLine={false} tickMargin={8} />
                            <YAxis tickLine={false} axisLine={false} />
                            <ChartTooltip cursor={false} content={<ChartTooltipContent />} />
                            <ChartLegend content={<ChartLegendContent />} />
                            <Bar dataKey="google" stackId="sync" fill="#2563eb" radius={[4, 4, 0, 0]} />
                            <Bar dataKey="onedrive" stackId="sync" fill="#0f766e" radius={[4, 4, 0, 0]} />
                            <Bar dataKey="dropbox" stackId="sync" fill="#7c3aed" radius={[4, 4, 0, 0]} />
                        </BarChart>
                    </ChartContainer>
                </div>

                <div className="rounded-2xl border border-border bg-background p-4">
                    <div className="mb-4 flex items-center justify-between">
                        <div>
                            <h2 className="text-lg font-semibold">Storage status</h2>
                            <p className="text-sm text-muted-foreground">Available quota and estimated monthly cost.</p>
                        </div>
                        <Badge variant="secondary">Live snapshot</Badge>
                    </div>

                    <div className="space-y-3">
                        {storageUsage.map((item) => (
                            <div key={item.name} className="rounded-xl border border-border bg-muted/40 p-3">
                                <div className="mb-2 flex items-center justify-between">
                                    <div className="flex items-center gap-2">
                                        <Cloud className="size-4" style={{ color: item.color }} />
                                        <span className="text-sm font-medium">{item.name}</span>
                                    </div>
                                    <span className="text-sm font-semibold">{item.cost}</span>
                                </div>
                                <div className="h-2 rounded-full bg-background">
                                    <div className="h-2 rounded-full" style={{ width: `${item.used}%`, backgroundColor: item.color }} />
                                </div>
                                <div className="mt-2 flex items-center justify-between text-xs text-muted-foreground">
                                    <span>{item.used}% used</span>
                                    <span>{100 - item.used} GB free</span>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            </div>

            <div className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
                <div className="rounded-2xl border border-border bg-background p-4">
                    <div className="mb-4 flex items-center justify-between">
                        <div>
                            <h2 className="text-lg font-semibold">Cloud storage usage</h2>
                            <p className="text-sm text-muted-foreground">Quick radial view of usage against each plan.</p>
                        </div>
                        <Badge variant="outline">Quota</Badge>
                    </div>

                    <div className="grid gap-3 sm:grid-cols-3">
                        {storageUsage.map((item) => (
                            <div key={`${item.name}-radial`} className="rounded-xl border border-border bg-muted/30 p-3">
                                <ChartContainer config={{ [item.name.toLowerCase().replace(/\s+/g, "")]: { label: item.name, color: item.color } }} className="h-32 w-full">
                                    <RadialBarChart innerRadius="70%" outerRadius="100%" data={[{ name: item.name, value: item.used, fill: item.color }] as Array<{ name: string; value: number; fill: string }>} startAngle={180} endAngle={0}>
                                        <PolarAngleAxis type="number" domain={[0, 100]} tick={false} />
                                        <RadialBar background dataKey="value" cornerRadius={999} />
                                        <ChartTooltip cursor={false} content={<ChartTooltipContent hideLabel />} />
                                    </RadialBarChart>
                                </ChartContainer>
                                <div className="-mt-3 text-center">
                                    <p className="text-sm font-semibold">{item.used}%</p>
                                    <p className="text-xs text-muted-foreground">{item.name}</p>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>

                <div className="rounded-2xl border border-border bg-background p-4">
                    <div className="mb-4 flex items-center justify-between">
                        <div>
                            <h2 className="text-lg font-semibold">Recent backups & actions</h2>
                            <p className="text-sm text-muted-foreground">A timeline-style feed for the latest app activity.</p>
                        </div>
                        <Button asChild variant="ghost" size="sm">
                            <Link to="/timeline">Open timeline <ArrowRight className="size-4" /></Link>
                        </Button>
                    </div>

                    <div className="max-h-80 space-y-3 overflow-y-auto pr-1">
                        {timelineEvents.map((event, index) => (
                            <Marker key={`${event.title}-${index}`} variant="border" className="pb-3">
                                <MarkerIcon className={event.tone}>{event.icon}</MarkerIcon>
                                <MarkerContent className="flex flex-col gap-1">
                                    <div className="flex items-center justify-between gap-3">
                                        <span className="font-medium text-foreground">{event.title}</span>
                                        <span className="text-xs text-muted-foreground">{event.time}</span>
                                    </div>
                                    <span className="text-sm text-muted-foreground">{event.detail}</span>
                                </MarkerContent>
                            </Marker>
                        ))}
                    </div>
                </div>
            </div>
        </div>
    )
}
