import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

const originalFetch = window.fetch.bind(window)
const mappedApiOrigin = 'http://storagestandby-api.local'
const virtualUiOrigin = 'http://storagestandby.local'

window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
  const requestUrl = input instanceof Request ? input.url : input.toString()

  if (requestUrl.startsWith(`${virtualUiOrigin}/api/`)) {
    const apiUrl = `${mappedApiOrigin}${requestUrl.slice(virtualUiOrigin.length)}`
    if (input instanceof Request) {
      return originalFetch(new Request(apiUrl, input), init)
    }
    return originalFetch(apiUrl, init)
  }

  return originalFetch(input, init)
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
