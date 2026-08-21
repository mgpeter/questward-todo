import { useAuth0 } from '@auth0/auth0-react'
import { useQueryClient } from '@tanstack/react-query'
import { LogOut } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useRef, useState } from 'react'
import { useIsMobile, usePrefersReducedMotion } from '../lib/useMediaQuery'
import { Sheet } from './Sheet'
import { SoundToggle } from './SoundToggle'
import { ThemeToggle } from './ThemeToggle'

export function AccountMenu() {
  const isMobile = useIsMobile()
  const { user, logout } = useAuth0()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    // The sheet brings its own Escape handling and closes on a tap outside, so this is the
    // popover's business only. Left armed on mobile it would fight the sheet for Escape.
    if (!open || isMobile) return

    const onPointerDown = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) setOpen(false)
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false)
    }

    document.addEventListener('mousedown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)

    return () => {
      document.removeEventListener('mousedown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open, isMobile])

  const signOut = () => {
    // Without this the next person to sign in on this browser sees the previous user's
    // cached tasks until every query refetches.
    queryClient.clear()

    void logout({ logoutParams: { returnTo: window.location.origin } })
  }

  const label = user?.name || user?.email || 'Account'

  const trigger = (
    <button
      type="button"
      onClick={() => setOpen((current) => !current)}
      aria-haspopup={isMobile ? 'dialog' : 'menu'}
      aria-expanded={open}
      aria-label={`Account: ${label}`}
      data-testid="account-menu"
      className={`grid place-items-center overflow-hidden rounded-full border border-line bg-surface-sunk transition hover:border-line-strong ${
        isMobile ? 'h-9 w-9' : 'h-8 w-8'
      }`}
    >
      {user?.picture ? (
        <img src={user.picture} alt="" className="h-full w-full object-cover" />
      ) : (
        <span className="text-[11px] font-medium text-ink-muted">
          {label.slice(0, 1).toUpperCase()}
        </span>
      )}
    </button>
  )

  // Everything the compact header had to give up lives behind the avatar: the theme
  // switch, the sound button and the sign-out that was already here. A 56px header cannot
  // carry three 32px controls and a progress rail, and these are the three nobody touches
  // in a given session.
  if (isMobile) {
    return (
      <>
        {trigger}
        <AccountSheet
          open={open}
          onClose={() => setOpen(false)}
          user={{ name: user?.name, email: user?.email, picture: user?.picture, label }}
          onSignOut={signOut}
        />
      </>
    )
  }

  return (
    <div ref={containerRef} className="relative">
      {trigger}

      <AnimatePresence>
        {open && (
          <motion.div
            role="menu"
            initial={{ opacity: 0, y: -6, scale: 0.97 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: -6, scale: 0.97 }}
            transition={{ duration: 0.14 }}
            className="panel absolute right-0 z-40 mt-2 w-56 overflow-hidden rounded-xl"
          >
            <div className="border-b border-line px-3.5 py-3">
              <p className="truncate text-[13px] font-medium">{user?.name ?? 'Signed in'}</p>
              {user?.email && <p className="truncate text-[11.5px] text-ink-faint">{user.email}</p>}
            </div>

            <button
              type="button"
              role="menuitem"
              onClick={signOut}
              data-testid="sign-out"
              className="flex w-full items-center gap-2 px-3.5 py-2.5 text-left text-[13px] text-ink-muted transition hover:bg-surface-sunk hover:text-ink"
            >
              <LogOut size={13} />
              Sign out
            </button>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}

function AccountSheet({
  open,
  onClose,
  user,
  onSignOut,
}: {
  open: boolean
  onClose: () => void
  user: { name?: string; email?: string; picture?: string; label: string }
  onSignOut: () => void
}) {
  const reduced = usePrefersReducedMotion()

  return (
    <Sheet open={open} onClose={onClose} title="Display and account" testId="account-sheet">
      <div className="flex items-center gap-3 border-b border-line pt-1 pb-4">
        <span className="grid h-11 w-11 shrink-0 place-items-center overflow-hidden rounded-full border border-line bg-surface-sunk">
          {user.picture ? (
            <img src={user.picture} alt="" className="h-full w-full object-cover" />
          ) : (
            <span className="text-[15px] text-ink-muted">{user.label.slice(0, 1).toUpperCase()}</span>
          )}
        </span>
        <div className="min-w-0">
          <p className="truncate text-[14.5px] font-medium">{user.name ?? 'Signed in'}</p>
          {user.email && <p className="mt-0.5 truncate text-[12px] text-ink-faint">{user.email}</p>}
        </div>
      </div>

      <p className="mt-4 mb-2.5 text-[10px] tracking-[0.18em] text-ink-faint uppercase">Theme</p>
      <ThemeToggle variant="stacked" />

      <div className="mt-4">
        <SoundToggle variant="row" />

        {/*
          Read-only, and deliberately so. The browser owns this setting and an app switch
          that appeared to control it would be lying; saying which way it is currently read
          explains why the animations are doing what they are doing.
        */}
        <div className="flex items-center gap-3 border-t border-line py-3.5">
          <span className="min-w-0 flex-1">
            <span className="block text-[14.5px]">Reduced motion</span>
            <span className="mt-0.5 block text-[11.5px] text-ink-faint">
              Following your system setting
            </span>
          </span>
          <span
            className="tabular shrink-0 text-[11.5px] text-ink-faint"
            data-testid="reduced-motion-state"
            data-reduced={reduced ? 'on' : 'off'}
          >
            {reduced ? 'On' : 'Off'}
          </span>
        </div>

        <button
          type="button"
          onClick={onSignOut}
          data-testid="sign-out"
          className="flex w-full items-center gap-2.5 border-t border-line py-4 text-left text-[14.5px] text-rose"
        >
          <LogOut size={16} />
          Sign out
        </button>
      </div>
    </Sheet>
  )
}
