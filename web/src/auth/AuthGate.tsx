import { useAuth0 } from '@auth0/auth0-react'
import { motion } from 'motion/react'
import type { ReactNode } from 'react'
import { ThemeToggle } from '../components/ThemeToggle'

/**
 * Renders the sign-in screen until Auth0 reports an authenticated session.
 * Everything behind this point can assume a signed-in user.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const { isLoading, isAuthenticated, error, loginWithRedirect } = useAuth0()

  if (isLoading) {
    return <Splash />
  }

  if (isAuthenticated) {
    return <>{children}</>
  }

  return (
    <div className="relative z-10 grid min-h-dvh place-items-center px-4">
      <div className="absolute top-4 right-4">
        <ThemeToggle />
      </div>

      <motion.div
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.35, ease: [0.16, 1, 0.3, 1] }}
        className="panel w-full max-w-sm rounded-2xl px-8 py-10 text-center"
      >
        <span
          aria-hidden="true"
          className="mx-auto grid h-12 w-12 rotate-45 place-items-center rounded-[12px] border border-gold/50 bg-linear-to-br from-gold/25 to-transparent"
        >
          <span className="-rotate-45 text-lg leading-none text-gold">&#9670;</span>
        </span>

        <h1 className="mt-5 font-display text-3xl leading-tight">
          Quest<span className="text-gold">ward</span>
        </h1>
        <p className="mt-2 text-[13px] leading-snug text-ink-muted">
          A todo list that pays you in experience points. Sign in to pick up where your
          character left off.
        </p>

        {error && (
          <p role="alert" className="mt-4 rounded-lg border border-rose/35 bg-rose/8 px-3 py-2 text-[12px] text-rose">
            {error.message}
          </p>
        )}

        <button
          type="button"
          onClick={() => void loginWithRedirect()}
          data-testid="sign-in"
          className="mt-6 w-full rounded-lg bg-ink px-4 py-2.5 text-sm font-medium text-canvas transition hover:opacity-90"
        >
          Sign in
        </button>

        <p className="mt-4 text-[11px] leading-snug text-ink-faint">
          Authentication is handled by Auth0, so signing in needs an internet connection.
          Your tasks and XP never leave this server.
        </p>
      </motion.div>
    </div>
  )
}

export function Splash({ label = 'Loading' }: { label?: string }) {
  return (
    <div className="relative z-10 grid min-h-dvh place-items-center">
      <div className="flex flex-col items-center gap-3" role="status" aria-live="polite">
        <span className="h-6 w-6 animate-spin rounded-full border-2 border-line border-t-gold" />
        <span className="text-[12px] text-ink-faint">{label}</span>
      </div>
    </div>
  )
}
