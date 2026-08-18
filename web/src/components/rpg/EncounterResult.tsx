import { Coins, Scroll, Swords } from 'lucide-react'
import { motion } from 'motion/react'
import type { AttackResult } from '../../lib/rpg'

const OUTCOME: Record<string, { title: string; tone: string; blurb: string }> = {
  won: {
    title: 'Victory',
    tone: 'text-gold',
    blurb: 'It goes down and stays down.',
  },
  lost: {
    title: 'Driven off',
    tone: 'text-rose',
    blurb: 'Battered, but breathing. The stamina is spent either way.',
  },
  fled: {
    title: 'Withdrawn',
    tone: 'text-ink-muted',
    blurb: 'Discretion, and the stamina it cost you.',
  },
}

/**
 * Shown after a fight ends, until the player dismisses it.
 *
 * This exists because the result used to be discarded the instant the encounter stopped
 * being active, so the killing blow, the gold and the loot were never visible.
 */
export function EncounterResult({
  result,
  onDismiss,
  onFightAgain,
}: {
  result: AttackResult
  onDismiss: () => void
  onFightAgain: () => void
}) {
  const outcome = OUTCOME[result.encounter.status] ?? OUTCOME.fled
  const won = result.encounter.status === 'won'

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ type: 'spring', stiffness: 260, damping: 24 }}
      className="space-y-4"
      data-testid="encounter-result"
      data-outcome={result.encounter.status}
    >
      <section className="panel relative overflow-hidden rounded-2xl p-6 text-center">
        {won && (
          <div className="absolute inset-x-0 top-0 h-px bg-linear-to-r from-transparent via-gold/70 to-transparent" />
        )}

        <p className={`text-[11px] font-medium uppercase tracking-[0.32em] ${outcome.tone}`}>
          {outcome.title}
        </p>

        <h2 className="mt-2 font-display text-3xl">{result.encounter.monsterName}</h2>
        <p className="mt-1 text-[13px] text-ink-muted">{outcome.blurb}</p>

        <p className="tabular mt-2 text-[11px] text-ink-faint">
          {result.encounter.round} {result.encounter.round === 1 ? 'round' : 'rounds'}
        </p>

        {won && (
          <div className="mt-5 flex flex-wrap justify-center gap-2.5">
            <Reward
              icon={<Coins size={13} />}
              label="Gold"
              value={`+${result.goldAwarded}`}
              tone="text-gold"
              testId="reward-gold"
            />
            {result.loot && (
              <div
                className={`rarity-${result.loot.rarity} tier-chip rounded-xl px-3.5 py-2.5`}
                data-testid="reward-loot"
                data-rarity={result.loot.rarity}
              >
                <p className="text-[9.5px] font-medium uppercase tracking-[0.14em] opacity-80">
                  {result.loot.rarity}
                </p>
                <p className="mt-0.5 text-[14px] font-medium">{result.loot.name}</p>
                {/* The only place a set announces itself at the moment it is found. */}
                {result.loot.setName && (
                  <p className="mt-0.5 text-[10.5px] opacity-80" data-testid="reward-loot-set">
                    {result.loot.setName} set
                  </p>
                )}
              </div>
            )}
          </div>
        )}

        {result.questsAdvanced.length > 0 && (
          <ul className="mt-4 flex flex-wrap justify-center gap-2" data-testid="reward-quests">
            {result.questsAdvanced.map((quest) => (
              <li
                key={quest.key}
                className={`flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] ${
                  quest.justCompleted
                    ? 'border-teal/40 bg-teal/8 text-teal'
                    : 'border-line text-ink-muted'
                }`}
              >
                <Scroll size={11} />
                {quest.name}
                <span className="tabular opacity-70">{quest.progress}</span>
                {quest.justCompleted && <span className="font-medium">ready to claim</span>}
              </li>
            ))}
          </ul>
        )}

        {/* The clearest possible place to teach the rule the whole design rests on. */}
        <p className="mt-5 border-t border-line pt-4 text-[11.5px] text-ink-faint">
          Experience unchanged. Levels come from finishing tasks, never from fighting.
        </p>

        <div className="mt-5 flex justify-center gap-2">
          <button
            type="button"
            onClick={onFightAgain}
            data-testid="fight-again"
            className="inline-flex items-center gap-1.5 rounded-lg bg-ink px-4 py-2 text-xs font-medium text-canvas transition hover:opacity-90"
          >
            <Swords size={13} />
            Find another fight
          </button>
          <button
            type="button"
            onClick={onDismiss}
            data-testid="dismiss-result"
            className="rounded-lg border border-line px-4 py-2 text-xs text-ink-muted transition hover:border-line-strong"
          >
            Done
          </button>
        </div>
      </section>

      <section className="panel rounded-2xl p-5">
        <h3 className="mb-2 text-[11px] font-medium uppercase tracking-[0.16em] text-ink-faint">
          Final round
        </h3>

        <div className="divide-y divide-line">
          {result.rolls.map((roll, index) => (
            <FinalRoll key={index} text={roll.text} total={roll.total} kind={roll.kind} />
          ))}
        </div>
      </section>
    </motion.div>
  )
}

function Reward({
  icon,
  label,
  value,
  tone,
  testId,
}: {
  icon: React.ReactNode
  label: string
  value: string
  tone: string
  testId: string
}) {
  return (
    <div className="rounded-xl border border-line bg-surface-sunk px-3.5 py-2.5" data-testid={testId}>
      <p className="flex items-center gap-1.5 text-[9.5px] font-medium uppercase tracking-[0.14em] text-ink-faint">
        {icon}
        {label}
      </p>
      <p className={`tabular mt-0.5 text-[16px] font-medium ${tone}`}>{value}</p>
    </div>
  )
}

function FinalRoll({ text, total, kind }: { text: string; total: number; kind: string }) {
  return (
    <p className="flex items-baseline justify-between gap-3 py-1.5 text-[12px]">
      <span className={kind === 'note' ? 'italic text-ink-muted' : 'text-ink'}>{text}</span>
      {kind !== 'note' && <span className="tabular shrink-0 text-[11px] text-ink-faint">{total}</span>}
    </p>
  )
}
