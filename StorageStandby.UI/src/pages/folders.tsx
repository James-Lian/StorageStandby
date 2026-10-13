import { ArrowDownUp, Ellipsis, ArrowUp, ArrowDown, Grid, List, Plus, Trash2, Upload, CircleCheck, CircleX } from "lucide-react"
import { useEffect, useMemo, useState } from "react"
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

export function Folders() {
	const { folders, isLoading, error } = useWatchedFolders()
	const [mode, setMode] = useState<"list" | "grid">("list")
	const [search, setSearch] = useState("")
	
	const [selectedFolder, setSelectedFolder] = useState<WatchedFolder | null>(null)
	const [selectedRows, setSelectedRows] = useState<Array<number>>([])
	const [sortMethod, setSortMethod] = useState<string>("path");

	useEffect(() => {
		void refreshWatchedFolders()
	}, [])

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
	const handleAddFolder = () => {
		
	};

	return (
		<div className="space-y-6 p-4">
			<div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
				<div className="space-y-2">
					<h1 className="text-2xl font-semibold">Backup folders</h1>
					<p className="text-sm text-muted-foreground">
						Manage folders and files selected for backup. Switch between a folder list and a selected folder contents grid.
					</p>
				</div>

				<div className="flex flex-col gap-3 sm:flex-row sm:items-center">
					<div className="flex items-center gap-2">
						<Button variant={mode === "list" ? "default" : "outline"} size="sm" onClick={() => setMode("list")}>
							<List className="size-4" /> List
						</Button>
						<Button variant={mode === "grid" ? "default" : "outline"} size="sm" onClick={() => setMode("grid")}>
							<Grid className="size-4" /> Grid
						</Button>
					</div>

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
						<ButtonGroup>
							<Button variant={mode === "list" ? "default" : "outline"} size="sm" onClick={() => setMode("list")}>List</Button>
							<Button variant={mode === "grid" ? "default" : "outline"} size="sm" onClick={() => setMode("grid")}>Grid</Button>
						</ButtonGroup>
					</div>
				</div>

				{mode === "list" ? (
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
											<ButtonGroup>
												<Button variant="outline" size="sm">
													<ArrowUp className="size-4" />
												</Button>
												<Button variant="outline" size="sm">
													<ArrowDown className="size-4" />
												</Button>
											</ButtonGroup>
											<Button variant="outline" size="sm">
												<Trash2 className="size-4" /> Remove
											</Button>
											<Button variant="outline" size="sm">
												<Upload className="size-4" /> Upload files
											</Button>
										</div>
										<div className="flex items-center gap-2">
											<div className="rounded-full bg-muted px-3 py-1 text-xs font-medium uppercase tracking-[0.2em] text-muted-foreground">
												{mode === "list" ? "List view" : "Grid view"}
											</div>
										</div>
									</div>
								</TableCell>
							</TableRow>
						</TableFooter>
					</Table>
				) : (
					<div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
						{selectedFolder ? (
							<div className="rounded-2xl border border-border bg-background p-4">
								<div className="mb-4 flex items-center justify-between gap-4">
									<div>
										<p className="text-sm text-muted-foreground">Contents of</p>
										<h3 className="text-lg font-semibold">{getFolderName(selectedFolder)}</h3>
									</div>
									<Badge variant={getFolderStatus(selectedFolder) === "Synced" ? "default" : getFolderStatus(selectedFolder) === "Pending" ? "secondary" : "destructive"}>
										{getFolderStatus(selectedFolder)}
									</Badge>
								</div>
								<div className="grid gap-3">
									<div className="rounded-2xl border border-border bg-muted/50 p-4">
										<p className="text-sm text-muted-foreground">Local path</p>
										<p className="font-mono text-sm">{selectedFolder.localPath ?? "No local path"}</p>
										<p className="mt-3 text-sm text-muted-foreground">Cloud assignments</p>
										<p>{selectedFolder.assignedClouds.length}</p>
										<p className="mt-3 text-sm text-muted-foreground">Ignore rules</p>
										<p>{selectedFolder.ignoreRules || "None"}</p>
										<p className="mt-3 text-sm text-muted-foreground">Last synced</p>
										<p>{formatDate(selectedFolder.lastSync)}</p>
										</div>
								</div>
							</div>
						) : (
							<div className="rounded-2xl border border-border bg-background p-4">
								<p className="text-sm text-muted-foreground">Select a folder from the list to view its contents.</p>
							</div>
						)}
					</div>
				)}
			</div>
		</div>
	)
}