import { useAuth0 } from '@auth0/auth0-react'
import { useQueryClient } from '@tanstack/react-query'
import { LogOut } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useRef, useState } from 'react'

export function AccountMenu() {
  const { user, logout } = useAuth0()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return

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
  }, [open])

  const signOut = () => {
    // Without this the next person to sign in on this browser sees the previous user's
    // cached tasks until every query refetches.
    queryClient.clear()

    void logout({ logoutParams: { returnTo: window.location.origin } })
  }

  const label = user?.name || user?.email || 'Account'

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={`Account: ${label}`}
        data-testid="account-menu"
        className="grid h-8 w-8 place-items-center overflow-hidden rounded-full border border-line bg-surface-sunk transition hover:border-line-strong"
      >
        {user?.picture ? (
          <img src={user.picture} alt="" className="h-full w-full object-cover" />
        ) : (
          <span className="text-[11px] font-medium text-ink-muted">
            {label.slice(0, 1).toUpperCase()}
          </span>
        )}
      </button>

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
              {user?.email && (
                <p className="truncate text-[11.5px] text-ink-faint">{user.email}</p>
              )}
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
