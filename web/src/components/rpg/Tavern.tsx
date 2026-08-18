import { MapPin, Swords, Zap } from 'lucide-react'
import { useState } from 'react'
import type { AttackResult, CharacterSheet, InventoryItem } from '../../lib/rpg'
import { nextRoom } from '../../lib/rpg'
import {
  useActiveDungeonRun,
  useActiveEncounter,
  useActiveHunt,
  useDismissEncounter,
  useMonsters,
  useStartEncounter,
} from '../../lib/rpgQueries'
import { RoomBanner } from './Dungeons'
import { EncounterResult } from './EncounterResult'
import { EncounterView } from './EncounterView'
import { HuntBanner } from './HuntChrome'

export function Tavern({
  sheet,
  inventory,
}: {
  sheet: CharacterSheet
  inventory: InventoryItem[]
}) {
  const encounter = useActiveEncounter()
  const run = useActiveDungeonRun()
  const hunt = useActiveHunt()
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

  const active = encounter.data && encounter.data.status === 'active' ? encounter.data : null

  // A dungeon room is the one active encounter, so it surfaces here too. It is framed as
  // what it is rather than shown bare: a fight the player started two screens away, with a
  // withdraw button that ends a whole run, is worth labelling.
  const room = run.data?.encounter?.id === active?.id ? (run.data ?? null) : null

  // A contract is the one active encounter too, so it surfaces here on the same terms. The
  // banner is what tells the player which of their own tasks is swinging at them; without
  // it the tavern would present a creature named after a catalog they never chose.
  const contract = hunt.data?.encounterId === active?.id ? (hunt.data ?? null) : null

  if (active) {
    return (
      <EncounterView
        encounter={active}
        sheet={sheet}
        inventory={inventory}
        onFinished={setOutcome}
        fleeLabel={room ? 'Leave the dungeon' : contract ? 'Tear up the contract' : 'Withdraw'}
        fleeNote={
          room
            ? 'Walking out of a room walks you out of the run. Rooms already cleared stay cleared.'
            : contract
              ? 'Walking away costs you the stamina and the purse. The task is untouched, and it can be written up again next time round.'
              : undefined
        }
        banner={
          room ? (
            <RoomBanner run={room} room={nextRoom(room)} />
          ) : contract ? (
            <HuntBanner hunt={contract} />
          ) : undefined
        }
      />
    )
  }

  return <MonsterList sheet={sheet} />
}

function MonsterList({ sheet }: { sheet: CharacterSheet }) {
  const monsters = useMonsters()
  const run = useActiveDungeonRun()
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

        <p className="tabular flex items-center gap-1.5 whitespace-nowrap text-[12px] text-ink-muted">
          <Zap size={13} className={canFight ? 'text-teal' : 'text-ink-faint'} />
          <span className={canFight ? 'text-teal' : 'text-ink-faint'}>{sheet.stamina}</span> stamina
        </p>
      </header>

      {/* One fight at a time, and a run in progress is holding the slot. Saying so beats
          letting the player pick a monster and receive a 409 they have no way to read. */}
      {run.data && (
        <p
          className="panel flex flex-wrap items-center gap-x-2 gap-y-1 rounded-xl border-gold/30 px-4 py-3 text-[12.5px] text-ink-muted"
          data-testid="run-in-progress"
        >
          <MapPin size={13} className="shrink-0 text-gold" />
          <span className="min-w-0">
            You are partway into {run.data.name}. Finish or abandon it in Dungeons before
            picking a fight here.
          </span>
        </p>
      )}

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
              <h3 className="min-w-0 font-display text-[17px]">{monster.name}</h3>
              <span className="tabular shrink-0 whitespace-nowrap text-[10.5px] text-ink-faint">
                Level {monster.level}
              </span>
            </div>

            <p className="mt-1 flex-1 text-[12px] leading-snug text-ink-muted">{monster.blurb}</p>

            <div className="tabular mt-3 flex flex-wrap gap-2 text-[10.5px] text-ink-faint">
              <span>AC {monster.armourClass}</span>
              <span className="whitespace-nowrap">{monster.maxHitPoints} HP</span>
              <span>{monster.damage}</span>
              <span className="whitespace-nowrap text-gold">
                {monster.minGold}-{monster.maxGold} gold
              </span>
            </div>

            <button
              type="button"
              onClick={() => start.mutate(monster.key)}
              disabled={!canFight || start.isPending || Boolean(run.data)}
              data-testid={`fight-${monster.key}`}
              className="mt-3 inline-flex items-center justify-center gap-1.5 rounded-lg bg-ink px-3 py-2 text-xs font-medium whitespace-nowrap text-canvas transition hover:opacity-90 disabled:opacity-30"
            >
              <Swords size={13} className="shrink-0" />
              Fight ({monster.staminaCost} stamina)
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
