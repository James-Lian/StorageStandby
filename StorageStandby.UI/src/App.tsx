import './App.css'
import { useEffect, useRef } from 'react'
import { SidebarProvider, SidebarTrigger } from './components/ui/sidebar'
import { AppSidebar } from './components/app-sidebar'
import { HashRouter, Routes, Route } from 'react-router-dom'
import { Dashboard } from './pages/dashboard'
import { Folders } from './pages/folders'
import { Link } from './pages/link'
import { Sync } from './pages/sync'
import { Timeline } from './pages/timeline'
import { TooltipProvider } from './components/ui/tooltip'
import { Toaster } from './components/ui/sonner'
import { logError } from './lib/notifications'
import { NotificationCenter } from './components/notification-center'
import { refreshConnectedAccounts } from './lib/connected-accounts'


// Interesting shadcn/ui features
// alert
// alert dialog
// attachment + alert: file/folder upload: https://ui.shadcn.com/docs/components/radix/attachment
// spinner: api callback
// badge: folder labelling
// card: UI grouping
// chart: individual file analytics + timeline
// context menu: for file selector
// marker: timeline feature https://ui.shadcn.com/docs/components/radix/marker
// menubar: idk somewhere feature-heavy maybe
// scroll area
// separator
// sheet: side dialog
// sonner (toast component): for actions
// tabs: https://ui.shadcn.com/docs/components/radix/tabs
// toggle/toggle group: https://ui.shadcn.com/docs/components/radix/toggle
// tooltip/hover card

// TODO: System tray icon
// Windows toast notification system

function App() {
	const hasLoggedStartupTest = useRef(false)

	useEffect(() => {
		if (hasLoggedStartupTest.current) return
		hasLoggedStartupTest.current = true

		logError({
			title: 'Startup test error',
			statusCode: 500,
			summary: 'This is a test error emitted when the app starts.',
			details: { source: 'startup-test' },
		})

		void refreshConnectedAccounts()
	}, [])

	return (
		<TooltipProvider>
			<SidebarProvider>
				<HashRouter>
					<div className="flex min-h-screen pb-16">
						<AppSidebar />
						<main className="flex-1 p-4 pb-24">
							<SidebarTrigger />
							<Routes>
								<Route path="/" element={<Dashboard />} />
								<Route path="/folders" element={<Folders />} />
								<Route path="/Sync" element={<Sync />} />
								<Route path="/Link" element={<Link />} />
								<Route path="/Timeline" element={<Timeline />} />
							</Routes>
						</main>
					</div>
				</HashRouter>
				<NotificationCenter />
				<Toaster position="bottom-right" visibleToasts={5} expand closeButton offset={{ bottom: 72, right: 16 }} />
			</SidebarProvider>
		</TooltipProvider>
	)
}

export default App
