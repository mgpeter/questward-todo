import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MotionConfig } from 'motion/react'
import App from './App'
import { AuthBootstrap } from './auth/AuthBootstrap'
import { GameFeedProvider } from './game/GameFeed'
import { NavigationProvider } from './game/Navigation'
import { ThemeProvider } from './theme/ThemeProvider'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // A personal task list: no need to re-hit the API on every window focus.
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
        {/*
          The reduced-motion block in index.css clamps CSS durations, which motion's own
          springs never pass through: the level-up medallion still sprang in and the tab
          underline still slid for someone who had asked for neither. "user" drops transform
          and layout animations while leaving fades alone, so a sheet cross-fades into place
          instead of sliding, rather than nothing happening at all.
        */}
        <MotionConfig reducedMotion="user">
          <AuthBootstrap>
            <GameFeedProvider>
              <NavigationProvider>
                <App />
              </NavigationProvider>
            </GameFeedProvider>
          </AuthBootstrap>
        </MotionConfig>
      </ThemeProvider>
    </QueryClientProvider>
  </StrictMode>,
)
