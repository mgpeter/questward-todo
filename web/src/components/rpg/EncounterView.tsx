import { Swords } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import type { ReactNode } from 'react'
import type { AttackResult, CharacterSheet, Encounter, InventoryItem } from '../../lib/rpg'
import { play } from '../../lib/sound'
import { useAbility, useAttack, useConsumeItem, useFlee } from '../../lib/rpgQueries'
import { ConsumableTray } from './ConsumableTray'
import { DiceRoll } from './DiceRoll'
import { PhaseBanner, PhaseChip } from './PhaseBanner'
import { StatusEffectStrip } from './StatusEffectStrip'

/**
 * A round's cues, in the order the log reads.
 *
 * Loot subsumes gold rather than sounding beside it: the drop cue already contains the coin
 * cue, so playing both would only make the coins twice as loud. The synth drops a repeat of
 * the same cue inside 40 ms, which is what keeps a round with two hits in it to one click.
 */
function announce(result: AttackResult) {
  for (const roll of result.rolls) {
    if (roll.kind !== 'attack') continue

    if (roll.outcome === 'critical') play('critical')
    else if (roll.outcome === 'hit') play('hit')
    else if (roll.outcome === 'miss' || roll.outcome === 'fumble') play('miss')
  }

  if (result.encounter.status === 'won') play('kill', 0.12)
  if (result.encounter.status === 'lost') play('defeat', 0.12)

  if (result.loot || result.clearReward) play('drop', 0.35)
  else if (result.goldAwarded > 0) play('coin', 0.35)
}

/**
 * One fight, wherever it is being fought.
 *
 * A dungeon room and a tavern brawl are the same encounter row resolved through the same
 * routes, so they are the same screen. The caller supplies the framing above it and the
 * sentence under the withdraw button, which are the only two things that differ.
 */
export function EncounterView({
  encounter,
  sheet,
  inventory,
  onFinished,
  banner,
  fleeLabel = 'Withdraw',
  fleeNote,
}: {
  encounter: Encounter
  sheet: CharacterSheet
  inventory: InventoryItem[]
  onFinished: (result: AttackResult) => void
  banner?: ReactNode
  fleeLabel?: string
  fleeNote?: string
}) {
  const attack = useAttack()
  const ability = useAbility()
  const consume = useConsumeItem()
  const flee = useFlee()

  const busy = attack.isPending || ability.isPending || consume.isPending
  const over = encounter.status !== 'active'

  // One place deciding a round is over, so an ability or a potion ending a fight shows the
  // same summary a plain attack does.
  const finish = (result: AttackResult) => {
    if (result.encounter.status !== 'active') {
      onFinished(result)
    }
  }

  // The swing sounds on the click rather than on the reply, so the button feels connected
  // to the arm even when the round trip is slow.
  const resolve = (result: AttackResult) => {
    announce(result)
    finish(result)
  }

  const swing = () => {
    play('attack')
    attack.mutate(encounter.id, { onSuccess: resolve })
  }

  const invoke = (abilityKey: string) => {
    play('attack')
    ability.mutate({ encounterId: encounter.id, abilityKey }, { onSuccess: resolve })
  }

  const drink = (item: InventoryItem) => {
    play('drop', 0.3)
    consume.mutate({ encounterId: encounter.id, itemId: item.id }, { onSuccess: resolve })
  }

  const monsterPercent = Math.max(
    0,
    Math.round((encounter.monsterHitPoints / Math.max(1, encounter.monsterMaxHitPoints)) * 100),
  )
  const playerPercent = Math.max(
    0,
    Math.round((sheet.currentHitPoints / Math.max(1, sheet.maxHitPoints)) * 100),
  )

  // Only the newest round animates; the rest of the log is history.
  const latestRound = encounter.log.length > 0 ? encounter.log[encounter.log.length - 1].round : 0

  // Refusals from the round-resolving actions are worth a sentence. "You have none left" is
  // reachable by clicking a tray that is one refetch stale, and a silent no-op there reads
  // as the button being broken.
  const refusal = (consume.error ?? attack.error ?? ability.error) as Error | null

  return (
    <div className="space-y-4" data-testid="encounter" data-status={encounter.status}>
      {banner}

      <section className="panel rounded-2xl p-5">
        <header className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
          <h2 className="min-w-0 font-display text-2xl">{encounter.monsterName}</h2>

          <div className="flex flex-wrap items-center gap-2">
            <PhaseChip encounter={encounter} />
            <span className="tabular whitespace-nowrap text-[11px] text-ink-faint">
              Round {encounter.round}
            </span>
          </div>
        </header>

        <PhaseBanner encounter={encounter} />

        <div className="mt-4 space-y-3">
          <div>
            <HealthBar
              label={encounter.monsterName}
              current={encounter.monsterHitPoints}
              max={encounter.monsterMaxHitPoints}
              percent={monsterPercent}
              tone="rose"
              testId="monster-health"
            />
            <StatusEffectStrip encounter={encounter} target="monster" testId="monster-effects" />
          </div>

          <div>
            <HealthBar
              label="You"
              current={sheet.currentHitPoints}
              max={sheet.maxHitPoints}
              percent={playerPercent}
              tone="teal"
              testId="player-health"
            />
            <StatusEffectStrip encounter={encounter} target="player" testId="player-effects" />
          </div>
        </div>

        {sheet.classAbilities.length > 0 && (
          <div className="mt-4 flex flex-wrap gap-2" data-testid="ability-bar">
            {sheet.classAbilities.map((entry) => (
              <button
                key={entry.key}
                type="button"
                onClick={() => invoke(entry.key)}
                disabled={entry.remaining <= 0 || busy || over}
                title={entry.description}
                data-testid={`ability-${entry.key}`}
                data-remaining={entry.remaining}
                className="flex-1 basis-36 rounded-lg border border-gold/40 bg-gold/8 px-3 py-2 text-[12px] font-medium whitespace-nowrap text-gold transition hover:bg-gold/15 disabled:border-line disabled:bg-transparent disabled:text-ink-faint"
              >
                {entry.name}
                <span className="tabular ml-1.5 text-[10.5px] opacity-70">
                  {entry.remaining}/{entry.usesPerEncounter}
                </span>
              </button>
            ))}
          </div>
        )}

        <ConsumableTray
          items={inventory}
          onUse={drink}
          disabled={busy || over}
          pendingId={consume.isPending ? (consume.variables?.itemId ?? null) : null}
        />

        {refusal && (
          <p role="alert" className="mt-3 text-[12px] text-rose" data-testid="round-error">
            {refusal.message}
          </p>
        )}

        <div className="mt-3 flex flex-wrap gap-2">
          <button
            type="button"
            onClick={swing}
            disabled={busy || over}
            data-testid="attack"
            className="inline-flex flex-1 basis-40 items-center justify-center gap-1.5 rounded-lg bg-ink px-4 py-2.5 text-sm font-medium text-canvas transition hover:opacity-90 disabled:opacity-30"
          >
            <Swords size={14} />
            Attack
          </button>

          <button
            type="button"
            onClick={() => {
              play('flee')
              flee.mutate(encounter.id)
            }}
            disabled={flee.isPending || over}
            data-testid="flee"
            className="rounded-lg border border-line px-4 py-2.5 text-sm whitespace-nowrap text-ink-muted transition hover:border-line-strong disabled:opacity-30"
          >
            {fleeLabel}
          </button>
        </div>

        {/* Walking out of a room walks you out of the dungeon, which is a thing to be told
            before the click rather than after it. */}
        {fleeNote && (
          <p className="mt-1.5 text-[11px] text-ink-faint" data-testid="flee-note">
            {fleeNote}
          </p>
        )}
      </section>

      <section className="panel rounded-2xl p-5">
        <h3 className="mb-2 text-[11px] font-medium tracking-[0.16em] uppercase text-ink-faint">
          Combat log
        </h3>

        <div className="max-h-96 divide-y divide-line overflow-y-auto" data-testid="combat-log">
          <AnimatePresence initial={false}>
            {encounter.log.map((roll, index) => (
              <DiceRoll
                key={`${roll.round}-${index}`}
                roll={roll}
                index={roll.round === latestRound ? index % 4 : 0}
              />
            ))}
          </AnimatePresence>
        </div>
      </section>
    </div>
  )
}

export function HealthBar({
  label,
  current,
  max,
  percent,
  tone,
  testId,
}: {
  label: string
  current: number
  max: number
  percent: number
  tone: 'rose' | 'teal'
  testId: string
}) {
  return (
    <div data-testid={testId} data-percent={percent}>
      <div className="mb-1 flex items-baseline justify-between gap-2 text-[11px]">
        <span className="min-w-0 text-ink-muted">{label}</span>
        <span className="tabular shrink-0 whitespace-nowrap text-ink-faint">
          {current} / {max}
        </span>
      </div>

      <div className="h-2 overflow-hidden rounded-full bg-surface-sunk ring-1 ring-line/70 ring-inset">
        <motion.div
          className="h-full rounded-full"
          style={{ backgroundColor: tone === 'rose' ? 'var(--rose)' : 'var(--teal)' }}
          initial={false}
          animate={{ width: `${percent}%` }}
          transition={{ type: 'spring', stiffness: 160, damping: 22 }}
        />
      </div>
    </div>
  )
}
