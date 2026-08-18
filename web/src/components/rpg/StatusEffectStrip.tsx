import { Flame, HeartPulse, ShieldPlus, Skull, TrendingDown } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import {
  effectDetail,
  effectExplain,
  effectLabel,
  effectRemaining,
  effectsOn,
  favoursPlayer,
  isLasting,
  type EffectKindName,
  type EffectTargetName,
  type Encounter,
  type StatusEffect,
} from '../../lib/rpg'

const ICONS: Record<EffectKindName, typeof Flame> = {
  weakened: TrendingDown,
  empowered: Flame,
  guarded: ShieldPlus,
  poisoned: Skull,
  regenerating: HeartPulse,
}

/**
 * What is riding one combatant, with how long is left on each.
 *
 * The colour carries one meaning and only one: teal is in the player's favour, rose is
 * against them. Poison on the monster and poison on the player are the same mechanic, so
 * colouring by kind would teach the player nothing and colouring by bearer would teach them
 * something false.
 *
 * Renders nothing when there is nothing in force, so the health bars stay quiet in the
 * common case rather than carrying a permanent empty rail.
 */
export function StatusEffectStrip({
  encounter,
  target,
  testId,
}: {
  encounter: Encounter
  target: EffectTargetName
  testId: string
}) {
  const effects = effectsOn(encounter, target)

  if (effects.length === 0) return null

  return (
    <ul
      className="mt-1.5 flex flex-wrap gap-1"
      data-testid={testId}
      data-count={effects.length}
      aria-label={target === 'player' ? 'Effects on you' : 'Effects on your opponent'}
    >
      <AnimatePresence initial={false}>
        {effects.map((effect) => (
          <EffectChip key={`${effect.kind}-${effect.source}`} effect={effect} />
        ))}
      </AnimatePresence>
    </ul>
  )
}

function EffectChip({ effect }: { effect: StatusEffect }) {
  const Icon = ICONS[effect.kind] ?? Flame
  const good = favoursPlayer(effect)
  const detail = effectDetail(effect)
  const label = effectLabel(effect.kind)

  return (
    <motion.li
      layout
      initial={{ opacity: 0, scale: 0.85 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.85 }}
      transition={{ type: 'spring', stiffness: 340, damping: 26 }}
      // whitespace-nowrap on the chip and wrap on the list: a narrow column breaks between
      // effects, never through "Regenerating".
      className={`flex items-center gap-1 whitespace-nowrap rounded-full border px-2 py-0.5 text-[10.5px] ${
        good ? 'border-teal/40 bg-teal/10 text-teal' : 'border-rose/40 bg-rose/10 text-rose'
      }`}
      title={`${label}. ${effectExplain(effect)}. ${
        isLasting(effect)
          ? 'Lasts the fight'
          : `${effect.rounds} more ${effect.rounds === 1 ? 'application' : 'applications'}`
      }.`}
      data-testid="status-effect"
      data-kind={effect.kind}
      data-target={effect.target}
      data-rounds={effect.rounds}
      data-favourable={good}
    >
      <Icon size={11} className="shrink-0" />
      <span className="font-medium">{label}</span>
      {detail && <span className="tabular opacity-80">{detail}</span>}
      <span className="tabular opacity-70">{effectRemaining(effect)}</span>
    </motion.li>
  )
}
