import { ArrowDownUp, Ellipsis, Plus, Trash2, Upload, CircleCheck, CircleX } from "lucide-react"
import { useCallback, useEffect, useMemo, useState } from "react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ButtonGroup } from "@/components/ui/button-group"
import {
	DropdownMenu,
	DropdownMenuContent,
	DropdownMenuItem,
	DropdownMenuPortal,
	DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Input } from "@/components/ui/input"
import {
	Table,
	TableBody,
	TableCell,
	TableFooter,
	TableHead,
	TableHeader,
	TableRow,
} from "@/components/ui/table"
import { Checkbox } from "@/components/ui/checkbox"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import {
	Select,
	SelectContent,
	SelectGroup,
	SelectItem,
	SelectTrigger,
	SelectValue,
} from "@/components/ui/select"
import { PROBLEMATIC_FOLDER_TYPE } from "@/types/folders"
import type { WatchedFolder } from "@/types/data"
import { refreshWatchedFolders, useWatchedFolders } from "@/lib/watched-folders"
import { notifyError, notifySuccess } from "@/lib/notifications"
import { readErrorDetails } from "@/lib/read-errors-utils"


// TODO: setIsIgnoreRulesOpen(true)
const getFolderName = (folder: WatchedFolder) =>
	folder.localPath?.split(/[\\/]/).filter(Boolean).pop() ?? "Unnamed folder"

const getProblemLabel = (problem: number) => {
	switch (problem) {
		case PROBLEMATIC_FOLDER_TYPE.None:
			return "Healthy"
		case PROBLEMATIC_FOLDER_TYPE.PathNotFound:
			return "Path not found"
		case PROBLEMATIC_FOLDER_TYPE.TooLarge:
			return "Too large"
		case PROBLEMATIC_FOLDER_TYPE.TooManyFiles:
			return "Too many files"
		case PROBLEMATIC_FOLDER_TYPE.NestedWatchedFolder:
			return "Nested folder"
		default:
			return "Issue"
	}
}

const getFolderStatus = (folder: WatchedFolder) =>
	folder.problem === PROBLEMATIC_FOLDER_TYPE.None
		? folder.lastSync ? "Synced" : "Pending"
		: "Issue"

const formatDate = (value: string | null) =>
	value ? new Date(value).toLocaleString() : "Never"

// TODO: file timeline/time travel previews
// one-click, creates a version control
// Synced? column (Y, N, Syncing...) + add spinner to the name column
// stored locations column: (local, Google Drive, OneCloud (pending), etc.)

type FolderPickerMessage =
	| {
			type: "folder-selected"
			path: string
		}
	| {
			type: "folder-picker-cancelled"
		}

type WebViewBridge = {
	postMessage: (message: string) => void
	addEventListener: (type: "message", listener: (event: MessageEvent<unknown>) => void) => void
	removeEventListener: (type: "message", listener: (event: MessageEvent<unknown>) => void) => void
}

declare global {
	interface Window {
		chrome?: {
			webview?: WebViewBridge
		}
		showDirectoryPicker?: () => Promise<FileSystemDirectoryHandle>
	}
}

const isFolderPickerMessage = (value: unknown): value is FolderPickerMessage => {
	if (typeof value !== "object" || value === null || !("type" in value)) {
		return false
	}

	const message = value as { type?: unknown; path?: unknown }
	if (message.type === "folder-picker-cancelled") {
		return true
	}

	return message.type === "folder-selected" && typeof message.path === "string" && message.path.length > 0
}

export function Folders() {
	const { folders, isLoading, error } = useWatchedFolders()
	const [search, setSearch] = useState("")
	
	const [selectedFolder, setSelectedFolder] = useState<WatchedFolder | null>(null)
	const [selectedRows, setSelectedRows] = useState<Array<number>>([])
	const [sortMethod, setSortMethod] = useState<string>("path");

	const addFolder = useCallback(async (path: string) => {
		try {
			const response = await fetch("http://storagestandby.local/api/folders/add", {
				method: "POST",
				headers: {
					"Content-Type": "application/json",
				},
				body: JSON.stringify({ localPath: path }),
			})

			if (!response.ok) {
				const details = await readErrorDetails(response)
				throw new Error(details.problem?.detail ?? details.problem?.title ?? `Folder could not be added (HTTP ${response.status}).`)
			}

			notifySuccess({
				title: "Folder added",
				summary: path,
			})
			await refreshWatchedFolders()
		} catch (error) {
			notifyError({
				title: "Unable to add folder",
				summary: error instanceof Error ? error.message : "An unknown error occurred.",
			})
		}
	}, [])

	useEffect(() => {
		void refreshWatchedFolders()

		const handleFolderPickerMessage = (event: MessageEvent<unknown>) => {
			if (!isFolderPickerMessage(event.data) || event.data.type === "folder-picker-cancelled") {
				return
			}

			void addFolder(event.data.path)
		}

		window.chrome?.webview?.addEventListener("message", handleFolderPickerMessage)

		return () => {
			window.chrome?.webview?.removeEventListener("message", handleFolderPickerMessage)
		}
	}, [addFolder])

    const appendToSelectedRows = (elem: number) => {
        setSelectedRows((selectedRows) => [...selectedRows, elem])
    }
    const removeFromSelectedRows = (elem: number) => {
        setSelectedRows((selectedRows) => selectedRows.filter((item) => item !== elem))
    }

	const filteredFolders = useMemo(() => {
		const normalizedSearch = search.toLowerCase()
		return folders.filter((folder) =>
			getFolderName(folder).toLowerCase().includes(normalizedSearch) ||
			folder.localPath?.toLowerCase().includes(normalizedSearch)
		)
	}, [folders, search]);

	const totalFiles = "Not provided by API";

	const tagOptions = ["Healthy", "Path not found", "Too large", "Too many files", "Issue"];

	// ---------------------------
	// Adding Folders
	const handleAddFolder = async () => {
		const webView = window.chrome?.webview
		if (webView) {
			webView.postMessage("pick-folder")
			return
		}

		if (!window.showDirectoryPicker) {
			notifyError({
				title: "Folder picker unavailable",
				summary: "Open StorageStandby in the desktop app, or use a Chromium-based browser for development.",
			})
			return
		}

		try {
			const directoryHandle = await window.showDirectoryPicker()
			const path = window.prompt(
				`Development fallback: enter the absolute path for "${directoryHandle.name}".`,
				"",
			)?.trim()

			if (!path) {
				return
			}

			if (!/^(?:[A-Za-z]:[\\/]|\\\\)/.test(path)) {
				notifyError({
					title: "Invalid folder path",
					summary: "Enter an absolute Windows path, such as C:\\Users\\you\\Documents.",
				})
				return
			}

			await addFolder(path)
		} catch (error) {
			if (error instanceof DOMException && error.name === "AbortError") {
				return
			}

			notifyError({
				title: "Unable to select folder",
				summary: error instanceof Error ? error.message : "An unknown error occurred.",
			})
		}
	}

	return (
		<div className="space-y-6 p-4">
			<div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
				<div className="space-y-2">
					<h1 className="text-2xl font-semibold">Backup folders</h1>
					<p className="text-sm text-muted-foreground">
						Manage folders and files selected for backup.
					</p>
				</div>

				<div className="flex flex-col gap-3 sm:flex-row sm:items-center">
					<div className="flex items-center gap-2">
						<Input
							list="folder-tags"
							placeholder="Filter folders..."
							value={search}
							onChange={(event) => setSearch(event.target.value)}
							className="w-64"
						/>
						<datalist id="folder-tags">
							{tagOptions.map((option) => (
								<option key={option} value={option} />
							))}
						</datalist>
					</div>
				</div>
			</div>

			<div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
				<div className="rounded-2xl border border-border bg-background p-4">
					<p className="text-sm text-muted-foreground">Folders</p>
					<p className="text-xl font-semibold">{filteredFolders.length}</p>
				</div>
				<div className="rounded-2xl border border-border bg-background p-4">
					<p className="text-sm text-muted-foreground">File count</p>
					<p className="text-xl font-semibold">{totalFiles}</p>
				</div>
			</div>

			<div className="space-y-6">
				{isLoading && (
					<p className="text-sm text-muted-foreground">Loading watched folders...</p>
				)}
				{error && (
					<p className="text-sm text-destructive">Unable to load watched folders: {error.message}</p>
				)}
				<div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
					<div>
						<h2 className="text-lg font-semibold">Top-level folders</h2>
						<p className="text-sm text-muted-foreground">{filteredFolders.length} folders across current filter. File counts are not included in the folder API response.</p>
					</div>
					<div className="flex flex-row gap-3 items-center">
						<Select value={sortMethod} onValueChange={setSortMethod}>
							<SelectTrigger>
								<ArrowDownUp/>
								<SelectValue placeholder="Sort your files" />
							</SelectTrigger>
							<SelectContent>
								<SelectGroup>
									<SelectItem value="path">Sort by path</SelectItem>
									<SelectItem value="alphabetical">Sort alphabetically</SelectItem>
									<SelectItem value="syncStatus">Sort by sync status</SelectItem>
									<SelectItem value="dateLastSynced">Sort by date last synced</SelectItem>
									<SelectItem value="fileSize">Sort by file size</SelectItem>
								</SelectGroup>
							</SelectContent>
						</Select>
					</div>
				</div>

				<Table>
					<TableHeader className="sticky top-0 z-10">
						<TableRow>
							<TableHead>
								<Checkbox 
									checked={selectedRows.length === filteredFolders.length}
									onCheckedChange={() => {
										// all folders are selected
										if (selectedRows.length === filteredFolders.length) {
											setSelectedRows([]); // clearing selected folders
										} else {
											filteredFolders.forEach((item) => {
												if (!selectedRows.includes(item.id)) {
													appendToSelectedRows(item.id);
												}
											});
										}
									}}
									className="border-black"
								/>
							</TableHead>
							<TableHead>Name</TableHead>
							<TableHead>Sync status</TableHead>
							<TableHead>Last synced</TableHead>
							<TableHead>Clouds</TableHead>
							<TableHead>Problem</TableHead>
							<TableHead className="text-right"></TableHead>
						</TableRow>
					</TableHeader>
					<TableBody>
						{filteredFolders.length == 0 && 
							<TableRow>
								<TableCell colSpan={7} className="text-center py-6">
									<p className="text-sm text-muted-foreground">No folders added.</p>
								</TableCell>
							</TableRow>
						}
						{filteredFolders.map((folder) => (
							<TableRow key={folder.id} className={selectedRows.includes(folder.id) ? "bg-muted" : ""}>
								<TableCell>
									<Checkbox 
										checked={selectedRows.includes(folder.id)} 
										onCheckedChange={() => {
											const isSelected = selectedRows.includes(folder.id)
											if (isSelected) {
												removeFromSelectedRows(folder.id)
											} else {
												appendToSelectedRows(folder.id)
											}
										}}
										className="border-black"
									/>
								</TableCell>
								<TableCell>
									<div className="inline-flex w-fit h-fit items-center gap-1">
										<Tooltip>
											<TooltipTrigger asChild>
												<Button variant="link" className="cursor-pointer" onClick={() => {
													setSelectedFolder(folder)
													setMode("grid")
												}}>
													{getFolderName(folder)}
												</Button>
											</TooltipTrigger>
											<TooltipContent>
												{folder.localPath ?? "No local path"}
											</TooltipContent>
										</Tooltip>
										<Tooltip>
											<TooltipTrigger asChild>
												{folder.problem === 0
													? <CircleCheck className="size-4" />
													: <CircleX className="size-4 text-destructive" />}
											</TooltipTrigger>
											<TooltipContent>
												{getProblemLabel(folder.problem)}
											</TooltipContent>
										</Tooltip>
									</div>
								</TableCell>
								<TableCell>
									<Badge variant={getFolderStatus(folder) === "Synced" ? "default" : getFolderStatus(folder) === "Pending" ? "secondary" : "destructive"}>
										{getFolderStatus(folder)}
									</Badge>
								</TableCell>
								<TableCell>
									{formatDate(folder.lastSync)}
								</TableCell>
								<TableCell>{folder.assignedClouds.length}</TableCell>
								<TableCell>
									<Badge variant="outline">{getProblemLabel(folder.problem)}</Badge>
								</TableCell>
								<TableCell className="text-right">
									<DropdownMenu>
										<DropdownMenuTrigger asChild>
											<Button variant="ghost" size="sm">
												<Ellipsis className="size-4" />
											</Button>
										</DropdownMenuTrigger>
										<DropdownMenuPortal>
											<DropdownMenuContent>
												<DropdownMenuItem>Edit folder</DropdownMenuItem>
												<DropdownMenuItem>Rename</DropdownMenuItem>
												<DropdownMenuItem variant="destructive">Delete</DropdownMenuItem>
											</DropdownMenuContent>
										</DropdownMenuPortal>
									</DropdownMenu>
								</TableCell>
							</TableRow>
						))}
					</TableBody>
					<TableFooter>
						<TableRow>
							<TableCell colSpan={7}>
								<div className="flex flex-col gap-4 py-2 md:flex-row md:items-center md:justify-between">
									<div className="flex flex-wrap items-center gap-2">
										<Button 
											variant="default" 
											size="sm"
											onClick={handleAddFolder}
										>
											<Plus className="size-4" /> Add folder
										</Button>
										<Button variant="outline" size="sm">
											<Trash2 className="size-4" /> Remove
										</Button>
									</div>
								</div>
							</TableCell>
						</TableRow>
					</TableFooter>
				</Table>
			</div>
		</div>
	)
}