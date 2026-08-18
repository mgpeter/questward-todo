import { Flame } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useRef, useState } from 'react'
import type { Encounter } from '../../lib/rpg'
import { play } from '../../lib/sound'

/** How long the moment holds before the fight goes back to being a fight. */
const HOLD_MS = 5200

/**
 * A boss crossing a threshold, announced.
 *
 * The server already narrates the change in the combat log and the new effect already
 * appears on the strip, but both are quiet: a log line scrolls and a chip fades in beside
 * four others. A phase is the one moment in a fight where the rules change under the
 * player, so it gets a banner, a cue and a name.
 *
 * Fires on the phase going up and never on mount, so resuming a fight that is already in
 * its second phase does not re-announce something that happened before the reload.
 */
export function PhaseBanner({ encounter }: { encounter: Encounter }) {
  const [moment, setMoment] = useState<{ name: string; phase: number } | null>(null)

  // Keyed by encounter as well as phase: a new fight starts back at zero, and comparing
  // against the previous fight's phase would either miss the first threshold or invent one.
  const seen = useRef({ id: encounter.id, phase: encounter.phase })

  useEffect(() => {
    const previous = seen.current
    seen.current = { id: encounter.id, phase: encounter.phase }

    if (previous.id !== encounter.id || encounter.phase <= previous.phase) return

    setMoment({ name: encounter.phaseName ?? 'It changes', phase: encounter.phase })
    play('critical')

    const timer = window.setTimeout(() => setMoment(null), HOLD_MS)
    return () => window.clearTimeout(timer)
  }, [encounter.id, encounter.phase, encounter.phaseName])

  return (
    <AnimatePresence>
      {moment && (
        <motion.div
          initial={{ opacity: 0, y: -8, scaleY: 0.6 }}
          animate={{ opacity: 1, y: 0, scaleY: 1 }}
          exit={{ opacity: 0, y: -6 }}
          transition={{ type: 'spring', stiffness: 260, damping: 22 }}
          className="relative mt-3 overflow-hidden rounded-xl border border-rose/40 bg-rose/8 px-3.5 py-2.5"
          data-testid="phase-banner"
          data-phase={moment.phase}
          role="status"
        >
          <motion.div
            aria-hidden="true"
            className="absolute inset-y-0 w-1/3 bg-linear-to-r from-transparent via-rose/20 to-transparent"
            initial={{ x: '-120%' }}
            animate={{ x: '340%' }}
            transition={{ duration: 1.1, ease: 'easeOut' }}
          />

          <p className="flex items-center gap-1.5 text-[9.5px] font-medium uppercase tracking-[0.18em] text-rose">
            <Flame size={12} className="shrink-0" />
            Phase {moment.phase}
          </p>

          {/* min-w-0 rather than a truncation: a phase name is short and the player needs
              all of it, so a narrow column wraps between words instead of clipping. */}
          <p className="mt-0.5 min-w-0 font-display text-[17px] leading-tight text-ink">
            {moment.name}
          </p>

          <p className="mt-0.5 text-[11.5px] text-ink-muted">
            {encounter.monsterName} fights differently from here.
          </p>
        </motion.div>
      )}
    </AnimatePresence>
  )
}

/**
 * The standing reminder that the banner's moment has passed but its consequences have not.
 * Null-safe on the name, because a phase retired from the catalog reads as no name rather
 * than as a stale one.
 */
export function PhaseChip({ encounter }: { encounter: Encounter }) {
  if (encounter.phase <= 0) return null

  return (
    <span
      className="flex items-center gap-1 whitespace-nowrap rounded-full border border-rose/40 bg-rose/10 px-2 py-0.5 text-[10.5px] font-medium text-rose"
      data-testid="phase-chip"
      data-phase={encounter.phase}
    >
      <Flame size={11} className="shrink-0" />
      {encounter.phaseName ?? `Phase ${encounter.phase}`}
    </span>
  )
}
