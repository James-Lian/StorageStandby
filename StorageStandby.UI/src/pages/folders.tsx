import { ArrowDownUp, Ellipsis, ArrowUp, ChevronDown, Folder, FolderPlus, Grid, List, Plus, Trash2, Upload, ArrowDown } from "lucide-react"
import { useMemo, useState } from "react"
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
	TableCaption,
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

const initialFolders = [
	{
		id: "folder-1",
		name: "Project backups",
		syncStatus: "Synced",
		lastSynced: "2026-07-02",
		size: "12.1 GB",
		files: 48,
		folders: 4,
		tag: "Stable",
		path: "C:/Users/james/temp"
	},
	{
		id: "folder-2",
		name: "Work documents",
		syncStatus: "Pending",
		lastSynced: "2026-07-01",
		size: "4.9 GB",
		files: 12,
		folders: 2,
		tag: "Priority",
		path: "C:/Users/james/temp"
	},
	{
		id: "folder-3",
		name: "Photos archive",
		syncStatus: "Error",
		lastSynced: "2026-06-30",
		size: "23.7 GB",
		files: 324,
		folders: 16,
		tag: "Media",
		path: "C:/Users/james/temp"
	},
]

const folderContents = [
	{
		id: "item-1",
		name: "Design specs.pdf",
		type: "File",
		size: "1.2 MB",
		status: "Synced",
		icon: "file",
	},
	{
		id: "item-2",
		name: "Assets",
		type: "Folder",
		size: "10.4 GB",
		status: "Pending",
		icon: "folder",
	},
	{
		id: "item-3",
		name: "Meeting notes.docx",
		type: "File",
		size: "220 KB",
		status: "Synced",
		icon: "file",
	},
	{
		id: "item-4",
		name: "Screenshots",
		type: "Folder",
		size: "3.6 GB",
		status: "Error",
		icon: "folder",
	},
]

// TODO: file timeline/time travel previews
// one-click, creates a version control
// Synced? column (Y, N, Syncing...) + add spinner to the name column
// stored locations column: (local, Google Drive, OneCloud (pending), etc.)

export function Folders() {
	const [mode, setMode] = useState<"list" | "grid">("list")
	const [search, setSearch] = useState("")
	
	// placeholder code, remove later
	const [selectedFolder, setSelectedFolder] = useState(initialFolders[0])
	const [selectedRows, setSelectedRows] = useState<Array<string>>([])
	const [sortMethod, setSortMethod] = useState<string>("path");

    const appendToSelectedRows = (elem: string) => {
        setSelectedRows((selectedRows) => [...selectedRows, elem])
    }
    const removeFromSelectedRows = (elem: string) => {
        setSelectedRows((selectedRows) => selectedRows.filter((item) => item !== elem))
    }

	const filteredFolders = useMemo(() => {
		if (!search) return initialFolders
		return initialFolders.filter((folder) =>
			folder.name.toLowerCase().includes(search.toLowerCase())
		)
	}, [search]);

	const totalFiles = filteredFolders.reduce((acc, item) => acc + item.files, 0);

	const tagOptions = ["Stable", "Priority", "Media"];

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
					<p className="text-sm text-muted-foreground">Files</p>
					<p className="text-xl font-semibold">{totalFiles}</p>
				</div>
			</div>

			<div className="space-y-6">
				<div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
					<div>
						<h2 className="text-lg font-semibold">Top-level folders</h2>
						<p className="text-sm text-muted-foreground">{filteredFolders.length} folders, {totalFiles} files across current filter.</p>
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
								<TableHead>Size</TableHead>
								<TableHead>Labels</TableHead>
								<TableHead className="text-right"></TableHead>
							</TableRow>
						</TableHeader>
						<TableBody>
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
										<Tooltip>
										<TooltipTrigger asChild>
											<Button variant="link" className="cursor-pointer">
													{folder.name}
												</Button>
											</TooltipTrigger>
											<TooltipContent>
												{folder.path}
											</TooltipContent>
										</Tooltip>
									</TableCell>
									<TableCell>
										<Badge variant={folder.syncStatus === "Synced" ? "default" : folder.syncStatus === "Pending" ? "secondary" : "destructive"}>
											{folder.syncStatus}
										</Badge>
									</TableCell>
									<TableCell>{folder.lastSynced}</TableCell>
									<TableCell>{folder.size}</TableCell>
									<TableCell>
										<Badge variant="outline">{folder.tag}</Badge>
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
											<Button variant="default" size="sm">
												<Plus className="size-4" /> Add folders
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
										<h3 className="text-lg font-semibold">{selectedFolder.name}</h3>
									</div>
									<Badge variant={selectedFolder.syncStatus === "Synced" ? "default" : selectedFolder.syncStatus === "Pending" ? "secondary" : "destructive"}>
										{selectedFolder.syncStatus}
									</Badge>
								</div>
								<div className="grid gap-3">
									{folderContents.map((item) => (
										<div key={item.id} className="rounded-2xl border border-border bg-muted/50 p-4">
											<div className="mb-3 flex items-center justify-between gap-2">
												<div className="flex items-center gap-3">
													<div className="inline-flex h-10 w-10 items-center justify-center rounded-2xl bg-background text-primary">
														{item.icon === "folder" ? <Folder className="size-5" /> : <FolderPlus className="size-5" />}
													</div>
													<div>
														<p className="font-semibold">{item.name}</p>
														<p className="text-xs text-muted-foreground">{item.type}</p>
													</div>
												</div>
												<Badge variant={item.status === "Synced" ? "default" : item.status === "Pending" ? "secondary" : "destructive"}>
													{item.status}
												</Badge>
											</div>
											<div className="flex items-center justify-between text-sm text-muted-foreground">
												<span>{item.size}</span>
												<DropdownMenu>
													<DropdownMenuTrigger asChild>
														<Button variant="outline" size="sm">
															Actions <ChevronDown className="size-4" />
														</Button>
													</DropdownMenuTrigger>
													<DropdownMenuPortal>
														<DropdownMenuContent>
															<DropdownMenuItem>Open</DropdownMenuItem>
															<DropdownMenuItem>Move</DropdownMenuItem>
															<DropdownMenuItem variant="destructive">Remove</DropdownMenuItem>
														</DropdownMenuContent>
													</DropdownMenuPortal>
												</DropdownMenu>
											</div>
										</div>
									))}
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