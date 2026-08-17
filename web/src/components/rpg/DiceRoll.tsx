import { motion } from 'motion/react'
import type { CombatRoll } from '../../lib/rpg'

const OUTCOME_LABEL: Record<string, string> = {
  hit: 'Hit',
  critical: 'Critical hit',
  miss: 'Miss',
  fumble: 'Fumble',
}

/**
 * One roll, shown as arithmetic.
 *
 * The breakdown is the point of the whole feature: seeing `d20: 14 +3 DEX +2 prof = 19 vs
 * AC 15` makes a miss read as bad luck rather than an arbitrary verdict from the server.
 */
export function DiceRoll({ roll, index }: { roll: CombatRoll; index: number }) {
  const isPlayer = roll.actor === 'player'

  if (roll.kind === 'note') {
    return (
      <motion.p
        initial={{ opacity: 0, x: isPlayer ? -6 : 6 }}
        animate={{ opacity: 1, x: 0 }}
        transition={{ delay: index * 0.12 }}
        className="py-1 text-[12px] italic text-ink-muted"
      >
        {roll.text}
      </motion.p>
    )
  }

  const accent =
    roll.outcome === 'critical'
      ? 'text-gold'
      : roll.outcome === 'fumble'
        ? 'text-rose'
        : isPlayer
          ? 'text-ink'
          : 'text-ink-muted'

  return (
    <motion.div
      initial={{ opacity: 0, y: 6 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: index * 0.12, type: 'spring', stiffness: 320, damping: 26 }}
      className={`flex flex-wrap items-center gap-1.5 py-1.5 ${isPlayer ? '' : 'pl-4'}`}
      data-testid="dice-roll"
      data-outcome={roll.outcome}
    >
      {roll.dice.map((die, dieIndex) => (
        <Die key={dieIndex} sides={die.sides} value={die.value} kept={die.kept} roll={roll} />
      ))}

      {roll.modifiers.map((modifier) => (
        <span
          key={modifier.label}
          className="tabular rounded-md border border-line bg-surface-sunk px-1.5 py-0.5 text-[10.5px] text-ink-muted"
        >
          {modifier.value >= 0 ? '+' : ''}
          {modifier.value} {modifier.label}
        </span>
      ))}

      <span className="tabular text-[11px] text-ink-faint">
        = <span className={`font-medium ${accent}`}>{roll.total}</span>
        {roll.target !== null && <> vs {roll.target}</>}
      </span>

      {roll.outcome !== 'none' && (
        <span className={`text-[10px] font-medium uppercase tracking-[0.14em] ${accent}`}>
          {OUTCOME_LABEL[roll.outcome] ?? roll.outcome}
        </span>
      )}

      <span className="w-full text-[11.5px] text-ink-muted">{roll.text}</span>
    </motion.div>
  )
}

function Die({
  sides,
  value,
  kept,
  roll,
}: {
  sides: number
  value: number
  kept: boolean
  roll: CombatRoll
}) {
  const critical = sides === 20 && value === 20
  const fumble = sides === 20 && value === 1

  return (
    <motion.span
      initial={{ rotate: -25, scale: 0.7 }}
      animate={{ rotate: 0, scale: 1 }}
      transition={{ type: 'spring', stiffness: 300, damping: 14 }}
      title={kept ? `d${sides} rolled ${value}` : `d${sides} rolled ${value}, discarded`}
      className={`tabular grid h-7 w-7 place-items-center rounded-md border text-[12px] font-medium ${
        !kept
          ? 'border-line text-ink-faint line-through opacity-50'
          : critical
            ? 'border-gold/60 bg-gold/15 text-gold'
            : fumble
              ? 'border-rose/50 bg-rose/10 text-rose'
              : 'border-line-strong bg-surface text-ink'
      }`}
      aria-label={`d${sides} rolled ${value}${kept ? '' : ', discarded'}`}
      data-die={sides}
      data-value={value}
      data-critical={critical && roll.critical}
    >
      {value}
    </motion.span>
  )
}
