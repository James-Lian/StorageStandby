import { ArrowDownUp, Ellipsis, Plus, Trash2, CircleCheck, CircleDashed } from "lucide-react"
import { useCallback, useEffect, useMemo, useState } from "react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
	Combobox,
	ComboboxContent,
	ComboboxEmpty,
	ComboboxItem,
	ComboboxList,
	ComboboxTrigger,
	ComboboxValue,
} from "@/components/ui/combobox"
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
import { PROVIDER_METADATA, type Providers } from "@/types/providers"
import { useConnectedAccounts } from "@/lib/connected-accounts"
import { ConfirmationModal } from "@/components/confirmation-dialog"
import { IgnoreRulesDialog } from "@/components/ignore-rules-dialog"
import { addFolderCloud, fetchOpenExplorer, reconcileFolderIgnoreRules, removeFolderCloud, resyncFolder } from "@/api/data"
import type { ConnectedAccount } from "@/api/accounts"

import { getFolderName, handleOpenExplorer } from "@/lib/utils"

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

// TODO: redo
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
	// --------------------------------------
	// Stores
	const { accountsByProvider, counts } = useConnectedAccounts()

	// --------------------------------------
	// Component variables
	const { folders, isLoading, error } = useWatchedFolders()
	const [search, setSearch] = useState("")
	
	// stores ids of WatchedFolders
	const [selectedRows, setSelectedRows] = useState<Array<number>>([])
	const [sortMethod, setSortMethod] = useState<string>("path");

	// removing folders confirmation dialog
	const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false)
	const [pendingDeleteIds, setPendingDeleteIds] = useState<number[]>([])
	const [ignoreRulesFolder, setIgnoreRulesFolder] = useState<WatchedFolder | null>(null)

	// listens for WebView2 message from native folder picker in useEffect, e.g. {"type": "folder-selected", "path": "C:\example"}
	// useCallback memoizes the function
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

	// 1. Refresh WatchedFolders from backend
	// 2. Setup event listener for message from WebView2 folder picker
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
		const matchingFolders = folders.filter((folder) =>
			getFolderName(folder).toLowerCase().includes(normalizedSearch) ||
			folder.localPath?.toLowerCase().includes(normalizedSearch)
		)

		return [...matchingFolders].sort((left, right) => {
			switch (sortMethod) {
				case "alphabetical":
					return getFolderName(left).localeCompare(getFolderName(right))
				case "syncStatus":
					return getFolderStatus(left).localeCompare(getFolderStatus(right))
				case "dateLastSynced":
					return (right.lastSync ? new Date(right.lastSync).getTime() : 0) -
						(left.lastSync ? new Date(left.lastSync).getTime() : 0)
				case "path":
				default:
					return (left.localPath ?? "").localeCompare(right.localPath ?? "")
			}
		})
	}, [folders, search, sortMethod]);

	const foldersNeedingAttention = filteredFolders.filter(
		(folder) => folder.problem !== PROBLEMATIC_FOLDER_TYPE.None,
	).length

	const tagOptions = ["Healthy", "Path not found", "Too large", "Too many files", "Issue"];

	// ---------------------------
	// Adding/Removing Folders
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

	const handleRemoveFolders = (ids: number[]) => {
		if (ids.length === 0) return

		setPendingDeleteIds(ids)
		setIsDeleteDialogOpen(true)
	}

	const handleRemoveFolder = (id: number) => {
		setPendingDeleteIds([id])
		setIsDeleteDialogOpen(true)
	}

	const handleIgnoreRulesChange = async (newIgnoreRules: string) => {
		if (!ignoreRulesFolder) return

		try {
			await reconcileFolderIgnoreRules(
				ignoreRulesFolder.id,
				ignoreRulesFolder.ignoreRules,
				newIgnoreRules,
			)
			await refreshWatchedFolders()
			notifySuccess({
				title: "Ignore rules updated",
				summary: ignoreRulesFolder.localPath ?? "Folder rules saved",
			})
		} catch (error) {
			notifyError({
				title: "Unable to update ignore rules",
				summary: error instanceof Error ? error.message : "An unknown error occurred.",
			})
			throw error
		}
	}

	const confirmRemoval = async () => {
		try {
			await Promise.all(
				pendingDeleteIds.map(async (id) => {
					const response = await fetch(`http://storagestandby.local/api/folders/${id}`, {
						method: "DELETE",
					})

					if (!response.ok) {
						const details = await readErrorDetails(response)
						throw new Error(
							details.problem?.detail ??
							details.problem?.title ??
							`Folder could not be deleted (HTTP ${response.status}).`
						)
					}
				})
			)

			setSelectedRows([])
			setPendingDeleteIds([])
			setIsDeleteDialogOpen(false)
			await refreshWatchedFolders()
		} catch (error) {
			notifyError({
				title: "Unable to delete folder(s)",
				summary: error instanceof Error ? error.message : "An unknown error occurred.",
			})
		}
	}

	// -------------------------------
	// Folder display information

	const inFolderAssignedClouds = (folder: WatchedFolder, account: ConnectedAccount, provider: Providers) => {
		if (folder.assignedClouds.find(cloud => cloud.accountId == account.id && cloud.provider == provider)) {
			return true
		}
		return false
	}

	const inUserAssignedClouds = (folder: WatchedFolder, account: ConnectedAccount, provider: Providers) => {
		if (folder.userAssignedClouds.find(cloud => cloud.accountId == account.id && cloud.provider == provider)) {
			return true
		}
		return false
	}

	// joining provider and accountid to create a unique identifier for combobox
	const getCloudKey = (cloud: { provider: number; accountId: string }) =>
    	`${cloud.provider}:${cloud.accountId}`

	// parsing that unique identifier to retrieve provider and accountId
	const parseCloudKey = (value: string) => {
		const separatorIndex = value.indexOf(":")

		return {
			provider: Number(value.slice(0, separatorIndex)),
			accountId: value.slice(separatorIndex + 1),
		}
	}

	const handleCloudSelectionChange = async (
		folder: WatchedFolder,
		nextValues: string[],
	) => {
		const previousValues = [
			...folder.assignedClouds.map(getCloudKey),
        	...folder.userAssignedClouds.map(getCloudKey),
		]

		const previous = new Set(previousValues)
		const next = new Set(nextValues)

		const selected = nextValues.filter((value) => !previous.has(value))
		const deselected = previousValues.filter((value) => !next.has(value))

		try {
			await Promise.all(selected.map((value) =>
				addFolderCloud(folder.id, parseCloudKey(value)),
			))
			await Promise.all(deselected.map((value) =>
				removeFolderCloud(folder.id, parseCloudKey(value)),
			))
			await refreshWatchedFolders()
			notifySuccess({
				title: "Cloud assignments updated",
				summary: "New assignments will be synchronized automatically.",
			})
		} catch (error) {
			notifyError({
				title: "Failed to update cloud assignments",
				summary: error instanceof Error ? error.message : String(error),
			})
			await refreshWatchedFolders()
		}
	}

	return (
		<div className="space-y-6 p-4">
			<IgnoreRulesDialog
				open={ignoreRulesFolder !== null}
				onOpenChange={(open) => {
					if (!open) setIgnoreRulesFolder(null)
				}}
				rules={ignoreRulesFolder?.ignoreRules ?? ""}
				onRulesChange={handleIgnoreRulesChange}
				basePath={ignoreRulesFolder?.localPath ?? undefined}
				title="Folder ignore rules"
			/>
			<ConfirmationModal
				isOpen={isDeleteDialogOpen}
				onClose={() => {
					setIsDeleteDialogOpen(false)
					setPendingDeleteIds([])
				}}
				onConfirm={confirmRemoval}
				title={
					pendingDeleteIds.length === 1
						? "Delete 1 folder?"
						: `Delete ${pendingDeleteIds.length} folders?`
				}
				description={
					<>
						<span>Deleting this folder removes its local tracking history.</span>
						<span className="mt-1 block">
							(Synced cloud files will not be deleted. It is recommended to move them out of the application folder immediately.)
						</span>
					</>
				}
				isDestructive
			/>
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

			<div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
				<div className="rounded-2xl border border-border bg-background p-4">
					<p className="text-sm text-muted-foreground">Folders</p>
					<p className="text-xl font-semibold">{filteredFolders.length}</p>
				</div>
				<div className="rounded-2xl border border-border bg-background p-4">
					<p className="text-sm text-muted-foreground">Needs attention</p>
					<p className="text-xl font-semibold">{foldersNeedingAttention}</p>
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
						<Select 
							value={sortMethod} 
							onValueChange={(method) => {
								setSortMethod(method)
							}}
						>
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
							<TableHead>Ignore Rules</TableHead>
							{/* <TableHead>Track Snapshots</TableHead> */}
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
												<Button 
													variant="link" 
													className="cursor-pointer"
													onClick={(event: React.MouseEvent<HTMLButtonElement>) => {
														// check if Control key was held down
														if (event.ctrlKey) {
															handleOpenExplorer(folder.localPath);
														} else {
															
														}
													}}
												>
													{getFolderName(folder)}
												</Button>
											</TooltipTrigger>
											<TooltipContent>
												{folder.localPath ?? "No local path"}
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
								<TableCell>
									<Combobox
										multiple
										value={[
											...folder.assignedClouds.map(getCloudKey), 
											...folder.userAssignedClouds.map(getCloudKey)
										]}
										onValueChange={(value) => {handleCloudSelectionChange(folder, value)}}
									>
										<ComboboxTrigger aria-label={`${folder.assignedClouds.length + folder.userAssignedClouds.length} assigned cloud accounts`}>
											<ComboboxValue>{folder.assignedClouds.length + folder.userAssignedClouds.length}</ComboboxValue>
										</ComboboxTrigger>
										<ComboboxContent>
											<ComboboxList>
												{counts[0] + counts[1] + counts[2] === 0 ? (
													<ComboboxEmpty>No cloud accounts available</ComboboxEmpty>
												) : (
													Object.entries(accountsByProvider).flatMap(([provKey, accounts]) => {
														const prov: Providers = Number(provKey) as Providers
														const Icon = PROVIDER_METADATA[prov].icon 
														
														return accounts.map((account) => (
															<ComboboxItem
																key={`${prov}:${account.id}`}
																value={`${prov}:${account.id}`}
															>
																<Icon />
																<span className={inFolderAssignedClouds(folder, account, prov) ? "min-w-0 truncate text-blue-600" : "min-w-0 truncate"}>
																	{account.email ?? "Unknown account"}
																</span>
																{
																	inFolderAssignedClouds(folder, account, prov) ? <CircleCheck /> 
																	: inUserAssignedClouds(folder, account, prov) ? <CircleDashed />
																	: <></>
																}
															</ComboboxItem>
														))
													})
												)}
											</ComboboxList>
										</ComboboxContent>
									</Combobox>
								</TableCell>
								<TableCell>
									<Button
										variant={"ghost"}
										onClick={() => {
												setIgnoreRulesFolder(folder)
										}}
									>Open ({folder.ignoreRules.split(";").filter(rule => rule !== "").length} {folder.ignoreRules.split(";").filter(rule => rule !== "").length == 1 ? "rule" : "rules"})</Button>
								</TableCell>
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
											<DropdownMenuContent align="end">
												<DropdownMenuItem
													onClick={() => {handleOpenExplorer(folder.localPath)}}
												>
													Open explorer
												</DropdownMenuItem>
												<DropdownMenuItem
													onClick={() => {resyncFolder(folder.id)}}
												>
													Resync folder contents
													{/* "Resync" */}
												</DropdownMenuItem>
												<DropdownMenuItem 
													variant="destructive"
													onClick={() => {handleRemoveFolder(folder.id)}}
												>
													Delete
												</DropdownMenuItem>
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
										<Button 
											variant="outline" 
											size="sm"
											onClick={() => handleRemoveFolders(selectedRows)}
										>
											<Trash2 className="size-4" /> Remove {selectedRows.length == 1 ? selectedRows.length + " folder" : selectedRows.length > 1 ? selectedRows.length + " folders": ""}
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