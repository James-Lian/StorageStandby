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

import { Folder, LayoutDashboard, Link as LucideLink, RefreshCw, ChartLine, Info, LetterText, FileQuestion, Pause, Play } from "lucide-react"
import { Link } from 'react-router-dom'
import { useConnectedAccounts } from "@/lib/connected-accounts"

function convertBytesToWrittenForm(amtBytes: number) {
    if (amtBytes > 1000000000) {
        return String(Math.round(amtBytes/1000000000 * 10) / 10) + "GB";
    } else if (amtBytes > 1000000) {
        return String(Math.round(amtBytes/1000000 * 10) / 10) + "MB";
    } else if (amtBytes > 1000) {
        return String(Math.round(amtBytes/1000 * 10) / 10) + "KB";
    } else {
        return String(Math.round(amtBytes) + " B")
    }
}

// feature: spinner

export function AppSidebar() {
    const { counts } = useConnectedAccounts()
    const googleDriveAccounts = counts[0]
    const oneDriveAccounts = counts[1]
    const dropboxAccounts = counts[2]

    let syncActive: boolean = false;

    let amtSynced: number = 0;

    return (
            <Sidebar
                collapsible="icon"
            >
                <SidebarHeader />
                <SidebarContent>
                    <SidebarGroup>
                        <SidebarGroupLabel>Home</SidebarGroupLabel>
                        <SidebarMenu>
                            <SidebarMenuItem>
                                <SidebarMenuButton asChild>
                                    <Link to="/">
                                        <LayoutDashboard /> <span>Dashboard</span>
                                    </Link>
                                </SidebarMenuButton>
                            </SidebarMenuItem>
                            <SidebarMenuItem>
                                <SidebarMenuButton asChild>
                                    <Link to="/folders">
                                        <Folder /><span>Folders & files</span>
                                    </Link>
                                </SidebarMenuButton>
                                <SidebarMenuBadge>{convertBytesToWrittenForm(amtSynced)} synced</SidebarMenuBadge>
                            </SidebarMenuItem>
                            <SidebarMenuItem>
                                <SidebarMenuButton asChild>
                                    <Link to="/sync">
                                        <RefreshCw /> <span>Sync status</span>
                                    </Link>
                                </SidebarMenuButton>
                                <SidebarMenuBadge>Paused</SidebarMenuBadge>
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
                                                <span>Google Drive</span><span className="flex-1"/><span>({googleDriveAccounts})</span>
                                            </Link>
                                        </SidebarMenuSubButton>
                                    </SidebarMenuSubItem>
                                    <SidebarMenuSubItem>
                                        <SidebarMenuSubButton asChild>
                                            <Link to="/link?provider=1" className="flex w-full">
                                                <span>OneDrive</span><span className="flex-1"/><span>({oneDriveAccounts})</span>
                                            </Link>
                                        </SidebarMenuSubButton>
                                    </SidebarMenuSubItem>
                                    <SidebarMenuSubItem>
                                        <SidebarMenuSubButton asChild>
                                            <Link to="/link?provider=2" className="flex w-full">
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
                                    <a>
                                        <FileQuestion/> <span>How to use</span>
                                    </a>
                                </SidebarMenuButton>
                                <SidebarMenuButton asChild>
                                    <a>
                                        <Info /><span>Info</span>
                                    </a>
                                </SidebarMenuButton>
                            </SidebarMenuItem>
                        </SidebarMenu>
                    </SidebarGroup>
                </SidebarContent>
                <SidebarFooter />
            </Sidebar>
    )
}