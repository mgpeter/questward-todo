import { AnimatePresence, motion } from 'motion/react'
import { useGameFeed } from '../game/GameFeed'

/** Fixed layer so the rising numbers are never clipped by a scroll container. */
export function XpFloatLayer() {
  const { floats } = useGameFeed()

  return (
    <div className="pointer-events-none fixed inset-0 z-50" aria-hidden="true">
      <AnimatePresence>
        {floats.map((float) => (
          <motion.span
            key={float.id}
            data-testid="xp-float"
            initial={{ opacity: 0, y: 0, scale: 0.8 }}
            animate={{ opacity: 1, y: -46, scale: 1 }}
            exit={{ opacity: 0, y: -72, scale: 0.9 }}
            transition={{ duration: 1.1, ease: [0.16, 1, 0.3, 1] }}
            className="tabular absolute text-[15px] font-semibold"
            style={{
              left: float.x,
              top: float.y,
              color: float.amount >= 0 ? 'var(--gold)' : 'var(--ink-faint)',
              textShadow: float.amount >= 0 ? '0 0 14px var(--gold-glow)' : 'none',
            }}
          >
            {float.amount >= 0 ? `+${float.amount}` : float.amount} XP
          </motion.span>
        ))}
      </AnimatePresence>
    </div>
  )
}
