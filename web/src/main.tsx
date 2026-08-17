import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import { GameFeedProvider } from './game/GameFeed'
import { ThemeProvider } from './theme/ThemeProvider'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // A single-user local app: no need to re-hit the API on every window focus.
      refetchOnWindowFocus: false,
      staleTime: 5_000,
      retry: 1,
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <GameFeedProvider>
          <App />
        </GameFeedProvider>
      </ThemeProvider>
    </QueryClientProvider>
  </StrictMode>,
)
