import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible"
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import { Separator } from "@/components/ui/separator"
import { Box, Folder, HardDrive, Plus } from "lucide-react"
import React, { useEffect, useMemo, useRef, type RefObject, type ElementType } from "react"
import { useLocation } from "react-router-dom"
import { PROVIDERS, PROVIDER_METADATA } from "@/types/providers"
import type { Providers } from "@/types/providers"

import * as Accounts from "@/api/accounts"
import { type ConnectedAccount } from "@/api/accounts"
import { notifyInfo } from "@/lib/notifications"
import { CONNECTED_STATUS, getConnectedStatusName } from "@/types/connected-status"
import { Badge } from "@/components/ui/badge"
import { refreshConnectedAccounts, useConnectedAccounts } from "@/lib/connected-accounts"

export function Link() {
	const googleDriveRef = useRef<HTMLDivElement | null>(null)
	const oneDriveRef = useRef<HTMLDivElement | null>(null)
	const dropboxRef = useRef<HTMLDivElement | null>(null)

	const [googleDriveOpen, setGoogleDriveOpen] = React.useState(false)
	const [oneDriveOpen, setOneDriveOpen] = React.useState(false)
	const [dropboxOpen, setDropboxOpen] = React.useState(false)
	const [selectedAccountsByProvider, setSelectedAccountsByProvider] = React.useState<Record<Providers, string[]>>({
		[PROVIDERS.Google]: [],
		[PROVIDERS.Microsoft]: [],
		[PROVIDERS.Dropbox]: [],
	})
	const { accountsByProvider } = useConnectedAccounts()

	type Provider = {
		key: Providers
		name: string
		description: string
		icon: ElementType
		ref: RefObject<HTMLDivElement | null>
		open: boolean
		setOpen: React.Dispatch<React.SetStateAction<boolean>>
	}

	const providers = useMemo<Provider[]>(
		() => [
			{
				key: PROVIDERS.Google,
				name: PROVIDER_METADATA[PROVIDERS.Google].displayName,
				description: PROVIDER_METADATA[PROVIDERS.Google].description,
				icon: Folder,
				ref: googleDriveRef,
				open: googleDriveOpen,
				setOpen: setGoogleDriveOpen,
			},
			{
				key: PROVIDERS.Microsoft,
				name: PROVIDER_METADATA[PROVIDERS.Microsoft].displayName,
				description: PROVIDER_METADATA[PROVIDERS.Microsoft].description,
				icon: HardDrive,
				ref: oneDriveRef,
				open: oneDriveOpen,
				setOpen: setOneDriveOpen,
			},
			{
				key: PROVIDERS.Dropbox,
				name: PROVIDER_METADATA[PROVIDERS.Dropbox].displayName,
				description: PROVIDER_METADATA[PROVIDERS.Dropbox].description,
				icon: Box,
				ref: dropboxRef,
				open: dropboxOpen,
				setOpen: setDropboxOpen,
			},
		],
		[googleDriveOpen, oneDriveOpen, dropboxOpen]
	)

	const location = useLocation()
	const params = new URLSearchParams(location.search)
	const providerFromQuery = params.get("provider")

	const scrollElemToView = (targetRef: RefObject<HTMLElement | null>) => {
		targetRef.current?.scrollIntoView({ behavior: "smooth" })
	}

	const renderAccountCellValue = (value?: string | null) => {
		const trimmed = value?.trim()
		return trimmed ? trimmed : "—"
	}

	const addAccount = async (provider: Providers) => {
		await Accounts.startOAuthFlow(provider)
		await refreshConnectedAccounts()
	}

	const removeAccount = async (provider: Providers, accountId: string) => {
		setSelectedAccountsByProvider((current) => ({
			...current,
			[provider]: (current[provider] ?? []).filter((selectedId) => selectedId !== accountId),
		}))

		await Accounts.revokeOAuthFlow(provider, accountId)
		await refreshConnectedAccounts()
	}

	const toggleAccountSelection = (providerKey: Providers, accountId: string) => {
		setSelectedAccountsByProvider((current) => {
			const selected = current[providerKey] ?? []
			return {
				...current,
				[providerKey]: selected.includes(accountId)
					? selected.filter((id) => id !== accountId)
					: [...selected, accountId],
			}
		})
	}

	const toggleSelectAll = (providerKey: Providers, providerAccounts: ConnectedAccount[]) => {
		setSelectedAccountsByProvider((current) => {
			const selected = current[providerKey] ?? []
			const allSelected = providerAccounts.length > 0 && selected.length === providerAccounts.length
			return {
				...current,
				[providerKey]: allSelected ? [] : providerAccounts.map((account) => account.id),
			}
		})
	}

	const removeSelectedAccounts = async (providerKey: Providers) => {
		const selectedIds = selectedAccountsByProvider[providerKey] ?? []
		if (selectedIds.length === 0) return

		setSelectedAccountsByProvider((current) => ({
			...current,
			[providerKey]: [],
		}))

		await Promise.all(selectedIds.map((id) => Accounts.revokeOAuthFlow(providerKey, id)))
		await refreshConnectedAccounts()
	}

	useEffect(() => {
		if (!providerFromQuery) return

		const providerKey = parseInt(providerFromQuery, 10)
		const matched = providers.find((s) => s.key === providerKey)
		if (matched) {
			scrollElemToView(matched.ref)
			matched.setOpen(true)
		}
	}, [providerFromQuery, providers])

	useEffect(() => {
		// Pull the latest accounts into the shared store whenever this page is visited.
		void refreshConnectedAccounts()
	}, [])

	return (
		<div className="space-y-6 p-4">
			<div className="space-y-2">
				<h1 className="text-2xl font-semibold">Link cloud providers</h1>
				<p className="max-w-2xl text-sm text-muted-fg">
					Connect one or more cloud storage providers to keep files synced and available across your workspace.
				</p>
			</div>

			<div className="space-y-4">
				{providers.map((provider) => {
					const Icon = provider.icon
					const accounts = accountsByProvider[provider.key] ?? []
					const selectedForProvider = selectedAccountsByProvider[provider.key] ?? []
					const isAllSelected = accounts.length > 0 && selectedForProvider.length === accounts.length

					return (
						<div key={provider.name}>
							<Collapsible open={provider.open} ref={provider.ref} className="overflow-hidden rounded-2xl border border-border shadow-sm">
								<CollapsibleTrigger
									onClick={() => provider.setOpen(!provider.open)}
									className="flex w-full cursor-pointer items-center justify-between gap-4 px-4 py-3 text-left text-sm font-semibold transition-colors hover:bg-muted"
								>
									<span className="inline-flex items-center gap-3">
										<Icon className="size-5 text-primary" />
										{provider.name}
									</span>
									<span className="rounded-full bg-muted px-3 py-1 text-[0.75rem] font-medium text-muted-foreground">
										{accounts.length > 0 ? `${accounts.length} connected` : "Not connected"}
									</span>
								</CollapsibleTrigger>
								<CollapsibleContent className="border-t border-border px-4 pb-4 pt-3">
									<div className="flex flex-col gap-4">
										<p className="text-sm leading-6 text-muted-foreground">{provider.description}</p>

										<div className="space-y-3">
											<div className="flex flex-col items-center justify-between gap-3">
												<Separator />
												<div className="self-start text-sm font-medium text-foreground">Connected accounts</div>
											</div>

											<div className="overflow-hidden rounded-md border border-border">
												<Table className="text-sm">
													<TableHeader>
														<TableRow>
															<TableHead className="w-12">
																<Checkbox
																	checked={isAllSelected}
																	onCheckedChange={() => toggleSelectAll(provider.key, accounts)}
																/>
															</TableHead>
															<TableHead className="w-16 max-w-16">ID</TableHead>
															<TableHead>Email</TableHead>
															<TableHead>Name</TableHead>
															<TableHead>Status</TableHead>
															<TableHead className="w-28 text-right">Action</TableHead>
														</TableRow>
													</TableHeader>
													<TableBody>
														{accounts.length === 0 ? (
															<TableRow>
																<TableCell colSpan={5} className="text-center py-6">
																	<p className="text-sm text-muted-foreground">No accounts added.</p>
																</TableCell>
															</TableRow>
														) : (
															accounts.map((account) => {
																const shortId = account.id.length > 8 ? `${account.id.slice(0, 8)}...` : account.id
																const isSelected = selectedForProvider.includes(account.id)
																return (
																	<TableRow key={account.id} className={isSelected ? "bg-muted/50" : ""}>
																		<TableCell className="w-12 align-middle">
																			<Checkbox
																				checked={isSelected}
																				onCheckedChange={() => toggleAccountSelection(provider.key, account.id)}
																			/>
																		</TableCell>
																		<TableCell className="w-16 max-w-16 align-middle">
																			<Tooltip>
																				<TooltipTrigger asChild>
																					<span className="block max-w-32 cursor-help truncate font-mono text-[11px] text-foreground/80">
																						{shortId}
																					</span>
																				</TooltipTrigger>
																				<TooltipContent side="top" className="max-w-[24rem] break-all font-mono text-[11px]">
																					{account.id}
																				</TooltipContent>
																			</Tooltip>
																		</TableCell>
																		<TableCell>
																			<Tooltip>
																				<TooltipTrigger asChild>
																					<span className="truncate">
																						{renderAccountCellValue(account.email)}
																					</span>
																				</TooltipTrigger>
																				<TooltipContent side="top" className="max-w-[24rem] break-all font-mono text-[11px]">
																					{renderAccountCellValue(account.email)}
																				</TooltipContent>
																			</Tooltip>
																		</TableCell>
																		<TableCell>
																			<Tooltip>
																				<TooltipTrigger asChild>
																					<span className="truncate">
																						{renderAccountCellValue(account.name)}
																					</span>
																				</TooltipTrigger>
																				<TooltipContent side="top" className="max-w-[24rem] break-all font-mono text-[11px]">
																					{renderAccountCellValue(account.name)}
																				</TooltipContent>
																			</Tooltip>
																		</TableCell>
																		<TableCell>
																			<Badge variant={
																				account.status == CONNECTED_STATUS.Connected ? "default" 
																				: account.status == CONNECTED_STATUS.Unconnected ? "secondary" 
																				: "destructive"
																			}>
																				{getConnectedStatusName(account.status)}
																			</Badge>
																		</TableCell>
																		<TableCell className="w-28 text-right">
																			<Button
																				variant="ghost"
																				size="sm"
																				className="h-7 px-2 text-destructive hover:bg-destructive/10 hover:text-destructive"
																				onClick={() => removeAccount(provider.key, account.id)}
																			>
																				Remove
																			</Button>
																		</TableCell>
																	</TableRow>
																)
															})
														)}
													</TableBody>
													<TableFooter>
														<TableRow>
															<TableCell colSpan={6} className="p-0">
																<div className="flex items-center justify-between gap-3 px-3 py-2">
																	<div className="flex items-center gap-2">
																		<span className="text-xs text-muted-foreground">{isAllSelected ? "All selected" : selectedForProvider.length.toString() + " selected"}</span>
																	</div>
																	<div className="flex items-center gap-2">
																		<Button
																			variant="outline"
																			size="sm"
																			disabled={selectedForProvider.length === 0}
																			onClick={() => removeSelectedAccounts(provider.key)}
																			className="disabled:cursor-not-allowed disabled:opacity-50 text-destructive hover:bg-destructive/10 hover:text-destructive"
																		>
																			{isAllSelected ? "Remove all" : selectedForProvider.length === 0 ? "Remove" : "Remove " + selectedForProvider.length.toString()}
																		</Button>
																		<Button onClick={() => {
																			addAccount(provider.key)
																			notifyInfo({ 
																				title: "Authorization required", 
																				summary: `Open your browser to sign in to ${PROVIDER_METADATA[provider.key]["providerName"]}`
																			})
																			}
																		} 
																			variant="secondary" 
																			size="sm" 
																			className="cursor-pointer"
																		>
																			{"Add account"}
																			<Plus className="size-4" />
																		</Button>
																	</div>
																</div>
															</TableCell>
														</TableRow>
													</TableFooter>
												</Table>
											</div>
										</div>
									</div>
								</CollapsibleContent>
							</Collapsible>
						</div>
					)
				})}
			</div>

			<div className="rounded-2xl border border-border bg-background p-4 text-sm text-muted-foreground">
				<p className="font-semibold">Suggested UI improvements</p>
				<ul className="mt-3 list-disc space-y-2 pl-5">
					<li>Show provider connection status badges like “Connected” or “Pending”.</li>
					<li>Add a sync toggle per provider and a last-sync timestamp.</li>
					<li>Display available storage and recent activity for each provider.</li>
				</ul>
			</div>
		</div>
	)
}
