import { motion } from 'motion/react'
import type { Character } from '../lib/api'

interface XpRailProps {
  character: Character
}

/**
 * The persistent progress strip in the header. Deliberately the only place in the
 * header that carries gold, so the eye goes straight to it after a completion.
 */
export function XpRail({ character }: XpRailProps) {
  const percent = Math.min(
    100,
    Math.round((character.xpIntoLevel / Math.max(1, character.xpForNextLevel)) * 100),
  )

  return (
    <div className="flex min-w-0 flex-1 items-center gap-3" data-testid="xp-rail">
      <LevelBadge level={character.level} />

      <div className="min-w-0 flex-1">
        <div className="mb-1.5 flex items-baseline justify-between gap-3">
          <span className="truncate font-display text-[13px] leading-none text-ink-muted">
            {character.title}
          </span>
          <span className="tabular shrink-0 text-[11px] leading-none text-ink-faint">
            <span data-testid="xp-into-level" className="text-ink">
              {character.xpIntoLevel}
            </span>
            <span className="mx-0.5 opacity-60">/</span>
            <span data-testid="xp-for-level">{character.xpForNextLevel}</span>
            <span className="ml-1 tracking-wider">XP</span>
          </span>
        </div>

        <div
          className="relative h-[7px] overflow-hidden rounded-full bg-surface-sunk ring-1 ring-line/70 ring-inset"
          role="progressbar"
          aria-valuenow={percent}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-label={`Level ${character.level} progress`}
          data-testid="xp-bar"
          data-percent={percent}
        >
          <motion.div
            className="h-full rounded-full bg-linear-to-r from-gold to-gold-bright"
            style={{ boxShadow: '0 0 12px var(--gold-glow)' }}
            initial={false}
            animate={{ width: `${percent}%` }}
            transition={{ type: 'spring', stiffness: 140, damping: 22, mass: 0.6 }}
          />
        </div>
      </div>

      <span className="tabular hidden shrink-0 text-[11px] text-ink-faint sm:block">
        <span data-testid="total-xp" className="text-ink-muted">
          {character.totalXp.toLocaleString()}
        </span>{' '}
        total
      </span>
    </div>
  )
}

function LevelBadge({ level }: { level: number }) {
  return (
    <div className="relative shrink-0" data-testid="level-badge" data-level={level}>
      <div className="grid h-10 w-10 place-items-center rounded-full border border-gold/45 bg-linear-to-b from-gold/18 to-transparent">
        <span className="tabular text-[15px] font-semibold leading-none text-gold">{level}</span>
      </div>
      <span className="absolute -bottom-1 left-1/2 -translate-x-1/2 rounded-full bg-canvas px-1 text-[8px] font-medium uppercase tracking-[0.14em] text-ink-faint">
        lvl
      </span>
    </div>
  )
}
