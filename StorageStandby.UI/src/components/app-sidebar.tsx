import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuItem,
  SidebarMenuButton,
  SidebarMenuBadge,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
  SidebarMenuSub,
} from "@/components/ui/sidebar"

import { Folder, LayoutDashboard, Link as LucideLink, RefreshCw, ChartLine, Info, FileQuestion } from "lucide-react"
import { Link } from 'react-router-dom'
import { useConnectedAccounts } from "@/lib/connected-accounts"
import { useWatchedFolders } from "@/lib/watched-folders";
import { PROVIDER_METADATA, PROVIDERS } from "@/types/providers";
import { Button } from "./ui/button";
import { notifyInfo } from "@/lib/notifications";
import { openInDefaultBrowser } from "@/api/utils";
import { getSyncDisplayState, refreshPauseState, usePauseState } from "@/lib/pause-state"
import { useEffect } from "react"

// feature: spinner

export function AppSidebar() {
    const { counts } = useConnectedAccounts()
    const { folders } = useWatchedFolders();
    const pauseState = usePauseState()
    const syncDisplayState = getSyncDisplayState(pauseState)

    useEffect(() => {
        void refreshPauseState()
        const refreshTimer = window.setInterval(() => void refreshPauseState(), 5000)
        return () => window.clearInterval(refreshTimer)
    }, [])
    const googleDriveAccounts = counts[0]
    const oneDriveAccounts = counts[1]
    const dropboxAccounts = counts[2]

    return (
            <Sidebar
                collapsible="icon"
                className="z-1000"
            >
                <SidebarHeader />
                <SidebarContent>
                    <SidebarGroup>
                        <SidebarGroupLabel>Home</SidebarGroupLabel>
                        <SidebarMenu>
                            {/* <SidebarMenuItem>
                                <SidebarMenuButton asChild>
                                    <Link to="/">
                                        <LayoutDashboard /> <span>Dashboard</span>
                                    </Link>
                                </SidebarMenuButton>
                            </SidebarMenuItem> */}
                            <SidebarMenuItem>
                                <SidebarMenuButton asChild>
                                    <Link to="/folders">
                                        <Folder /><span>Folders & files</span>
                                    </Link>
                                </SidebarMenuButton>
                                <SidebarMenuBadge>{folders.length} {folders.length === 1 ? "folder" : "folders"}</SidebarMenuBadge>
                            </SidebarMenuItem>
                            <SidebarMenuItem>
                                <SidebarMenuButton asChild>
                                    <Link to="/sync">
                                        <RefreshCw /> <span>Sync status</span>
                                    </Link>
                                </SidebarMenuButton>
                                <SidebarMenuBadge>{syncDisplayState}</SidebarMenuBadge>
                            </SidebarMenuItem>
                            <SidebarMenuItem>
                                <SidebarMenuButton asChild>
                                    <Link to="/timeline">
                                        <ChartLine /> <span>Timeline</span>
                                    </Link>
                                </SidebarMenuButton>
                            </SidebarMenuItem>
                            <SidebarMenuItem>
                                <SidebarMenuButton asChild>
                                    <Link to="/link">
                                        <LucideLink /><span>Link services</span>
                                    </Link>
                                </SidebarMenuButton>
                                <SidebarMenuBadge>{googleDriveAccounts + oneDriveAccounts + dropboxAccounts}</SidebarMenuBadge>
                                <SidebarMenuSub>
                                    <SidebarMenuSubItem>
                                        <SidebarMenuSubButton asChild>
                                            <Link to="/link?provider=0" className="flex w-full">
                                                {(() => {
                                                    const Icon = PROVIDER_METADATA[PROVIDERS.Google]["icon"]
                                                    return <Icon/>
                                                })()}
                                                <span>Google Drive</span><span className="flex-1"/><span>({googleDriveAccounts})</span>
                                            </Link>
                                        </SidebarMenuSubButton>
                                    </SidebarMenuSubItem>
                                    <SidebarMenuSubItem>
                                        <SidebarMenuSubButton asChild>
                                            <Link to="/link?provider=1" className="flex w-full">
                                                {(() => {
                                                    const Icon = PROVIDER_METADATA[PROVIDERS.Microsoft]["icon"]
                                                    return <Icon/>
                                                })()}
                                                <span>OneDrive</span><span className="flex-1"/><span>({oneDriveAccounts})</span>
                                            </Link>
                                        </SidebarMenuSubButton>
                                    </SidebarMenuSubItem>
                                    <SidebarMenuSubItem>
                                        <SidebarMenuSubButton asChild>
                                            <Link to="/link?provider=2" className="flex w-full">
                                                {(() => {
                                                    const Icon = PROVIDER_METADATA[PROVIDERS.Dropbox]["icon"]
                                                    return <Icon/>
                                                })()}
                                                <span>Dropbox</span><span className="flex-1"/><span>({dropboxAccounts})</span>
                                            </Link>
                                        </SidebarMenuSubButton>
                                    </SidebarMenuSubItem>
                                </SidebarMenuSub>
                            </SidebarMenuItem>
                        </SidebarMenu>
                    </SidebarGroup>
                    <SidebarGroup>
                        <SidebarGroupLabel>About</SidebarGroupLabel>
                        <SidebarMenu>
                            <SidebarMenuItem>
                                <SidebarMenuButton asChild>
                                    <Button
                                        variant={"link"}
                                        className="items-center justify-start cursor-pointer"
                                        onClick={() => {
                                            openInDefaultBrowser("https://github.com/James-Lian/StorageStandby/blob/main/HOWTOUSE.md")
                                            notifyInfo({ 
                                                title: "URL opened", 
                                                summary: `Open your browser to view the Guide`
                                            })
                                        }}
                                    >
                                        <Info /><span>How to use</span>
                                    </Button>
                                </SidebarMenuButton>
                                <SidebarMenuButton asChild>
                                    <Button
                                        variant={"link"}
                                        className="items-center justify-start cursor-pointer"
                                        onClick={() => {
                                            openInDefaultBrowser("https://github.com/James-Lian/StorageStandby")
                                            notifyInfo({ 
                                                title: "URL opened", 
                                                summary: `Open your browser to view the GitHub`
                                            })
                                        }}
                                    >
                                        <Info /><span>Info</span>
                                    </Button>
                                </SidebarMenuButton>
                            </SidebarMenuItem>
                        </SidebarMenu>
                    </SidebarGroup>
                </SidebarContent>
                <SidebarFooter />
            </Sidebar>
    )
}