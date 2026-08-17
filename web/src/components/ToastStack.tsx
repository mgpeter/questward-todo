import { X } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useGameFeed } from '../game/GameFeed'

export function ToastStack() {
  const { toasts, dismissToast } = useGameFeed()

  return (
    <div
      className="pointer-events-none fixed right-4 bottom-4 z-40 flex w-[280px] flex-col gap-2"
      role="status"
      aria-live="polite"
    >
      <AnimatePresence initial={false}>
        {toasts.map((toast) => (
          <motion.div
            key={toast.id}
            layout
            initial={{ opacity: 0, x: 40, scale: 0.95 }}
            animate={{ opacity: 1, x: 0, scale: 1 }}
            exit={{ opacity: 0, x: 40, scale: 0.95 }}
            transition={{ type: 'spring', stiffness: 380, damping: 32 }}
            data-testid="achievement-toast"
            data-achievement={toast.achievement.key}
            className="panel pointer-events-auto flex items-start gap-3 rounded-xl border-gold/40 p-3"
          >
            <span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-gold/12 text-lg ring-1 ring-gold/25">
              {toast.achievement.icon}
            </span>

            <div className="min-w-0 flex-1">
              <p className="text-[9px] font-medium uppercase tracking-[0.18em] text-gold">
                Badge earned
              </p>
              <p className="mt-0.5 font-display text-[15px] leading-tight">
                {toast.achievement.name}
              </p>
              <p className="mt-0.5 text-[11.5px] leading-snug text-ink-muted">
                {toast.achievement.description}
              </p>
            </div>

            <button
              type="button"
              onClick={() => dismissToast(toast.id)}
              aria-label="Dismiss"
              className="shrink-0 rounded p-0.5 text-ink-faint transition hover:text-ink"
            >
              <X size={13} />
            </button>
          </motion.div>
        ))}
      </AnimatePresence>
    </div>
  )
}
