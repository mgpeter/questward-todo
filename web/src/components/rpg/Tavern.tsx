import { Swords, Zap } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import type { AttackResult, CharacterSheet, Encounter } from '../../lib/rpg'
import { play } from '../../lib/sound'
import {
  useAbility,
  useActiveEncounter,
  useAttack,
  useDismissEncounter,
  useFlee,
  useMonsters,
  useStartEncounter,
} from '../../lib/rpgQueries'
import { DiceRoll } from './DiceRoll'
import { EncounterResult } from './EncounterResult'

export function Tavern({ sheet }: { sheet: CharacterSheet }) {
  const encounter = useActiveEncounter()
  const dismiss = useDismissEncounter()

  // Held here rather than in the query cache: the outcome belongs to the player's
  // attention, not to the server, and it stays put until they have read it.
  const [outcome, setOutcome] = useState<AttackResult | null>(null)

  if (outcome) {
    return (
      <EncounterResult
        result={outcome}
        onDismiss={() => {
          setOutcome(null)
          dismiss()
        }}
        onFightAgain={() => {
          setOutcome(null)
          dismiss()
        }}
      />
    )
  }

  if (encounter.isLoading) {
    return <div className="panel h-64 animate-pulse rounded-2xl opacity-60" />
  }

  return encounter.data && encounter.data.status === 'active' ? (
    <EncounterView encounter={encounter.data} sheet={sheet} onFinished={setOutcome} />
  ) : (
    <MonsterList sheet={sheet} />
  )
}

function MonsterList({ sheet }: { sheet: CharacterSheet }) {
  const monsters = useMonsters()
  const start = useStartEncounter()

  const canFight = sheet.stamina > 0

  return (
    <div className="space-y-4" data-testid="tavern">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <h2 className="font-display text-2xl">The Tavern</h2>
          <p className="mt-0.5 text-[13px] text-ink-muted">
            Trouble worth your time, sized to your level.
          </p>
        </div>

        <p className="tabular flex items-center gap-1.5 text-[12px] text-ink-muted">
          <Zap size={13} className={canFight ? 'text-teal' : 'text-ink-faint'} />
          <span className={canFight ? 'text-teal' : 'text-ink-faint'}>{sheet.stamina}</span> stamina
        </p>
      </header>

      {!canFight && (
        <p
          className="panel rounded-xl border-gold/30 px-4 py-3 text-[12.5px] text-ink-muted"
          data-testid="no-stamina"
        >
          You are out of stamina. Complete a task to earn more: Easy grants 1, Epic grants 5.
        </p>
      )}

      {start.isError && (
        <p role="alert" className="panel rounded-xl px-4 py-3 text-[12.5px] text-rose">
          {(start.error as Error).message}
        </p>
      )}

      <ul className="grid gap-2.5 sm:grid-cols-2">
        {monsters.data?.map((monster) => (
          <li
            key={monster.key}
            className="panel flex flex-col rounded-xl p-4"
            data-testid="monster"
            data-monster={monster.key}
          >
            <div className="flex items-baseline justify-between gap-2">
              <h3 className="font-display text-[17px]">{monster.name}</h3>
              <span className="tabular text-[10.5px] text-ink-faint">Level {monster.level}</span>
            </div>

            <p className="mt-1 flex-1 text-[12px] leading-snug text-ink-muted">{monster.blurb}</p>

            <div className="tabular mt-3 flex flex-wrap gap-2 text-[10.5px] text-ink-faint">
              <span>AC {monster.armourClass}</span>
              <span>{monster.maxHitPoints} HP</span>
              <span>{monster.damage}</span>
              <span className="text-gold">
                {monster.minGold}-{monster.maxGold} gold
              </span>
            </div>

            <button
              type="button"
              onClick={() => start.mutate(monster.key)}
              disabled={!canFight || start.isPending}
              data-testid={`fight-${monster.key}`}
              className="mt-3 inline-flex items-center justify-center gap-1.5 rounded-lg bg-ink px-3 py-2 text-xs font-medium text-canvas transition hover:opacity-90 disabled:opacity-30"
            >
              <Swords size={13} />
              Fight ({monster.staminaCost} stamina)
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}

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

  if (result.loot) play('drop', 0.35)
  else if (result.goldAwarded > 0) play('coin', 0.35)
}

function EncounterView({
  encounter,
  sheet,
  onFinished,
}: {
  encounter: Encounter
  sheet: CharacterSheet
  onFinished: (result: AttackResult) => void
}) {
  const attack = useAttack()
  const ability = useAbility()
  const flee = useFlee()

  const busy = attack.isPending || ability.isPending

  // One place deciding a round is over, so an ability ending a fight shows the same
  // summary a plain attack does.
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

  return (
    <div className="space-y-4" data-testid="encounter" data-status={encounter.status}>
      <section className="panel rounded-2xl p-5">
        <header className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="font-display text-2xl">{encounter.monsterName}</h2>
          <span className="tabular text-[11px] text-ink-faint">Round {encounter.round}</span>
        </header>

        <div className="mt-4 space-y-3">
          <HealthBar
            label={encounter.monsterName}
            current={encounter.monsterHitPoints}
            max={encounter.monsterMaxHitPoints}
            percent={monsterPercent}
            tone="rose"
            testId="monster-health"
          />
          <HealthBar
            label="You"
            current={sheet.currentHitPoints}
            max={sheet.maxHitPoints}
            percent={playerPercent}
            tone="teal"
            testId="player-health"
          />
        </div>

        {sheet.classAbilities.length > 0 && (
          <div className="mt-4 flex flex-wrap gap-2" data-testid="ability-bar">
            {sheet.classAbilities.map((ability) => (
              <button
                key={ability.key}
                type="button"
                onClick={() => invoke(ability.key)}
                disabled={
                  ability.remaining <= 0 || busy || encounter.status !== 'active'
                }
                title={ability.description}
                data-testid={`ability-${ability.key}`}
                data-remaining={ability.remaining}
                className="flex-1 rounded-lg border border-gold/40 bg-gold/8 px-3 py-2 text-[12px] font-medium text-gold transition hover:bg-gold/15 disabled:border-line disabled:bg-transparent disabled:text-ink-faint"
              >
                {ability.name}
                <span className="tabular ml-1.5 text-[10.5px] opacity-70">
                  {ability.remaining}/{ability.usesPerEncounter}
                </span>
              </button>
            ))}
          </div>
        )}

        <div className="mt-2 flex gap-2">
          <button
            type="button"
            onClick={swing}
            disabled={busy || encounter.status !== 'active'}
            data-testid="attack"
            className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-lg bg-ink px-4 py-2.5 text-sm font-medium text-canvas transition hover:opacity-90 disabled:opacity-30"
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
            disabled={flee.isPending || encounter.status !== 'active'}
            data-testid="flee"
            className="rounded-lg border border-line px-4 py-2.5 text-sm text-ink-muted transition hover:border-line-strong disabled:opacity-30"
          >
            Withdraw
          </button>
        </div>
      </section>

      <section className="panel rounded-2xl p-5">
        <h3 className="mb-2 text-[11px] font-medium uppercase tracking-[0.16em] text-ink-faint">
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

function HealthBar({
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
      <div className="mb-1 flex items-baseline justify-between text-[11px]">
        <span className="text-ink-muted">{label}</span>
        <span className="tabular text-ink-faint">
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
