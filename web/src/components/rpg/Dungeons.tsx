import { Check, Coins, Crown, DoorOpen, Heart, MapPin, Skull, Swords, Zap } from 'lucide-react'
import { motion } from 'motion/react'
import { useState } from 'react'
import {
  isBossRoom,
  nextRoom,
  roomsAhead,
  type AttackResult,
  type CharacterSheet,
  type Dungeon,
  type DungeonRoom,
  type DungeonRun,
  type InventoryItem,
} from '../../lib/rpg'
import {
  useAbandonDungeonRun,
  useActiveEncounter,
  useDismissEncounter,
  useActiveDungeonRun,
  useDungeons,
  useEnterRoom,
  useStartDungeon,
} from '../../lib/rpgQueries'
import { EncounterResult } from './EncounterResult'
import { EncounterView } from './EncounterView'

/**
 * Dungeon runs: the list of what is open, and the run in progress.
 *
 * The client holds nothing between requests. A reload asks the server what it was doing and
 * gets back the rolled chain, how deep it got and the fight standing open in the current
 * room, which is the whole of resume.
 */
export function Dungeons({
  sheet,
  inventory,
}: {
  sheet: CharacterSheet
  inventory: InventoryItem[]
}) {
  const run = useActiveDungeonRun()
  const encounter = useActiveEncounter()
  const dismiss = useDismissEncounter()

  // Held here rather than in the query cache, matching the tavern: the outcome belongs to
  // the player's attention, not to the server, and it stays put until they have read it.
  const [outcome, setOutcome] = useState<AttackResult | null>(null)

  if (outcome) {
    return (
      <EncounterResult
        result={outcome}
        againLabel="Back to the dungeon"
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

  if (run.isLoading) {
    return <div className="panel h-64 animate-pulse rounded-2xl opacity-60" />
  }

  const active = run.data

  // The two reads describe the same row, so the run's own copy is only trusted for the
  // framing. What is fought is whatever the encounter cache holds, which is the same object
  // the tavern would fight and the same one an attack updates.
  const open =
    active && encounter.data && encounter.data.status === 'active' ? encounter.data : null

  if (active && open) {
    const room = nextRoom(active)

    return (
      <EncounterView
        encounter={open}
        sheet={sheet}
        inventory={inventory}
        onFinished={setOutcome}
        fleeLabel="Leave the dungeon"
        fleeNote="Walking out of a room walks you out of the run. Rooms already cleared stay cleared."
        banner={<RoomBanner run={active} room={room} />}
      />
    )
  }

  return active ? <RunTrack run={active} sheet={sheet} /> : <DungeonList sheet={sheet} />
}

/**
 * Where this fight sits in the run, shown above the fight itself.
 *
 * Exported because the tavern shows the same fight: an open room is the one active
 * encounter, so a player who reaches for the tavern finds it there and needs the same
 * framing to understand why the monster list is gone.
 */
export function RoomBanner({ run, room }: { run: DungeonRun; room: DungeonRoom | null }) {
  const boss = room !== null && isBossRoom(run, room)

  return (
    <div
      className="panel flex flex-wrap items-center gap-x-3 gap-y-1 rounded-xl px-4 py-2.5"
      data-testid="room-banner"
      data-depth={run.depth}
    >
      <p className="flex min-w-0 items-center gap-1.5 text-[12.5px]">
        <MapPin size={13} className="shrink-0 text-ink-faint" />
        <span className="font-display text-[15px]">{run.name}</span>
      </p>

      <p className="tabular whitespace-nowrap text-[11px] text-ink-faint">
        Room {run.depth + 1} of {run.rooms.length}
      </p>

      {boss && (
        <p className="flex items-center gap-1 whitespace-nowrap rounded-full border border-gold/40 bg-gold/10 px-2 py-0.5 text-[10.5px] font-medium text-gold">
          <Crown size={11} className="shrink-0" />
          The last room
        </p>
      )}
    </div>
  )
}

/**
 * A run between rooms: how deep it is, what is ahead, and what it carried out of the last
 * room.
 */
function RunTrack({ run, sheet }: { run: DungeonRun; sheet: CharacterSheet }) {
  const dungeons = useDungeons()
  const enter = useEnterRoom()
  const abandon = useAbandonDungeonRun()

  const room = nextRoom(run)
  const ahead = roomsAhead(run)
  const definition = dungeons.data?.find((d) => d.key === run.dungeonKey) ?? null
  const cost = definition?.staminaPerRoom ?? null
  const canEnter = room !== null && (cost === null || sheet.stamina >= cost)

  return (
    <div className="space-y-4" data-testid="dungeon-run" data-status={run.status}>
      <section className="panel rounded-2xl p-5">
        <header className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
          <h2 className="min-w-0 font-display text-2xl">{run.name}</h2>
          <p className="tabular whitespace-nowrap text-[11px] text-ink-faint">
            {run.depth} of {run.rooms.length} cleared
          </p>
        </header>

        <RoomTrack run={run} />

        {/* What survives a threshold and what does not. Hit points walk through the door;
            an affliction lives on the encounter and dies with the room it was applied in,
            which is a rule the player can only learn by being told. */}
        <div className="mt-4 grid gap-2 sm:grid-cols-3" data-testid="run-carried">
          <Carried
            icon={<Heart size={12} />}
            label="Carried in"
            value={`${sheet.currentHitPoints} / ${sheet.maxHitPoints} HP`}
          />
          <Carried
            icon={<Zap size={12} />}
            label="Stamina"
            value={`${sheet.stamina}${cost === null ? '' : ` (${cost} a room)`}`}
            tone="text-teal"
          />
          <Carried
            icon={<Coins size={12} />}
            label="Won so far"
            value={run.goldAwarded.toLocaleString()}
            tone="text-gold"
          />
        </div>

        <p className="mt-2 text-[11px] leading-snug text-ink-faint" data-testid="carry-rule">
          Wounds follow you between rooms. Afflictions and blessings do not: they belong to
          the fight they were cast in and end with it.
        </p>

        {enter.isError && (
          <p role="alert" className="mt-3 text-[12px] text-rose" data-testid="enter-error">
            {(enter.error as Error).message}
          </p>
        )}

        <div className="mt-4 flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => enter.mutate(run.id)}
            disabled={!canEnter || enter.isPending || abandon.isPending}
            data-testid="enter-room"
            className="inline-flex flex-1 basis-48 items-center justify-center gap-1.5 rounded-lg bg-ink px-4 py-2.5 text-sm font-medium text-canvas transition hover:opacity-90 disabled:opacity-30"
          >
            <DoorOpen size={14} className="shrink-0" />
            <span className="min-w-0">
              {room ? `Open room ${room.index + 1}` : 'Nothing left to open'}
              {room && cost !== null && (
                <span className="tabular ml-1.5 opacity-70">{cost} stamina</span>
              )}
            </span>
          </button>

          <button
            type="button"
            onClick={() => abandon.mutate(run.id)}
            disabled={abandon.isPending}
            data-testid="abandon-run"
            className="rounded-lg border border-line px-4 py-2.5 text-sm whitespace-nowrap text-ink-muted transition hover:border-rose hover:text-rose disabled:opacity-30"
          >
            Abandon
          </button>
        </div>

        {!canEnter && room !== null && (
          <p className="mt-2 text-[11.5px] text-ink-muted" data-testid="run-no-stamina">
            You are out of stamina. The run waits: finish a task and come back to it.
          </p>
        )}

        {ahead.length > 0 && (
          <p className="mt-2 text-[11.5px] text-ink-muted" data-testid="run-ahead">
            {ahead.length} {ahead.length === 1 ? 'room' : 'rooms'} behind this one.
          </p>
        )}
      </section>
    </div>
  )
}

/**
 * The rolled chain, in order.
 *
 * Every chip is nowrap and the list wraps between them, so the narrow column breaks between
 * rooms and never through "Stone Sentinel".
 */
function RoomTrack({ run }: { run: DungeonRun }) {
  return (
    <ol className="mt-4 flex flex-wrap gap-1.5" data-testid="room-track">
      {run.rooms.map((room) => {
        const boss = isBossRoom(run, room)

        const tone =
          room.state === 'cleared'
            ? 'border-teal/40 bg-teal/8 text-teal'
            : room.state === 'current'
              ? 'border-gold/50 bg-gold/10 text-gold'
              : 'border-line text-ink-faint'

        return (
          <motion.li
            key={room.index}
            layout
            initial={{ opacity: 0, y: 4 }}
            animate={{ opacity: 1, y: 0 }}
            className={`flex items-center gap-1.5 whitespace-nowrap rounded-lg border px-2 py-1 text-[11px] ${tone}`}
            data-testid="room"
            data-index={room.index}
            data-state={room.state}
            data-monster={room.monsterKey}
          >
            {room.state === 'cleared' ? (
              <Check size={11} className="shrink-0" />
            ) : boss ? (
              <Crown size={11} className="shrink-0" />
            ) : (
              <Skull size={11} className="shrink-0" />
            )}
            <span className="tabular opacity-60">{room.index + 1}</span>
            <span className="font-medium">{room.monsterName}</span>
          </motion.li>
        )
      })}
    </ol>
  )
}

function Carried({
  icon,
  label,
  value,
  tone = 'text-ink',
}: {
  icon: React.ReactNode
  label: string
  value: string
  tone?: string
}) {
  return (
    <div className="rounded-xl border border-line bg-surface-sunk px-3 py-2">
      <p className="flex items-center gap-1.5 text-[9.5px] font-medium tracking-[0.14em] uppercase text-ink-faint">
        {icon}
        {label}
      </p>
      <p className={`tabular mt-0.5 text-[13px] ${tone}`}>{value}</p>
    </div>
  )
}

function DungeonList({ sheet }: { sheet: CharacterSheet }) {
  const dungeons = useDungeons()
  const encounter = useActiveEncounter()
  const start = useStartDungeon()

  // A run takes the one encounter slot the moment its first room opens, so a fight already
  // standing in the tavern refuses every door here. The symmetric notice lives in the
  // tavern; both exist so the refusal is read before the click rather than as a 409 after.
  const busyElsewhere = encounter.data?.status === 'active'

  if (dungeons.isLoading) {
    return <div className="panel h-64 animate-pulse rounded-2xl opacity-60" />
  }

  const open = dungeons.data ?? []

  return (
    <div className="space-y-4" data-testid="dungeons">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <h2 className="font-display text-2xl">Deep places</h2>
          <p className="mt-0.5 text-[13px] text-ink-muted">
            Several fights and a named thing at the end. One room, one stamina, all the way
            down.
          </p>
        </div>

        <p className="tabular flex items-center gap-1.5 whitespace-nowrap text-[12px] text-ink-muted">
          <Zap size={13} className={sheet.stamina > 0 ? 'text-teal' : 'text-ink-faint'} />
          <span className={sheet.stamina > 0 ? 'text-teal' : 'text-ink-faint'}>
            {sheet.stamina}
          </span>{' '}
          stamina
        </p>
      </header>

      {busyElsewhere && (
        <p
          className="panel flex flex-wrap items-center gap-x-2 gap-y-1 rounded-xl border-gold/30 px-4 py-3 text-[12.5px] text-ink-muted"
          data-testid="fight-in-progress"
        >
          <Swords size={13} className="shrink-0 text-gold" />
          <span className="min-w-0">
            You are already in a fight. Finish it in the Tavern before going down.
          </span>
        </p>
      )}

      {start.isError && (
        <p role="alert" className="panel rounded-xl px-4 py-3 text-[12.5px] text-rose">
          {(start.error as Error).message}
        </p>
      )}

      {open.length === 0 ? (
        <p className="panel rounded-xl px-4 py-8 text-center text-[13px] text-ink-muted" data-testid="dungeons-empty">
          Nothing is open to you yet. The first door unlocks at level 2.
        </p>
      ) : (
        <ul className="grid gap-2.5 sm:grid-cols-2">
          {open.map((dungeon) => (
            <DungeonCard
              key={dungeon.key}
              dungeon={dungeon}
              stamina={sheet.stamina}
              blocked={start.isPending || busyElsewhere}
              onStart={() => start.mutate(dungeon.key)}
            />
          ))}
        </ul>
      )}
    </div>
  )
}

function DungeonCard({
  dungeon,
  stamina,
  blocked,
  onStart,
}: {
  dungeon: Dungeon
  stamina: number
  /** A request in flight, or a fight already standing that would refuse this one. */
  blocked: boolean
  onStart: () => void
}) {
  // Opening a run costs nothing; a room costs one. So the gate is the first room, not the
  // whole run: a run waits indefinitely between rooms and finishing a task refills it.
  //
  // The full price is still shown up front, because the fourth room refusing to open is a
  // worse way to learn what a run costs than the card saying so before the first one.
  const canOpen = stamina >= dungeon.staminaPerRoom
  const canFinish = stamina >= dungeon.totalStaminaCost

  return (
    <motion.li
      layout
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      className={`rarity-${dungeon.rewardFloor} panel flex flex-col rounded-xl p-4`}
      data-testid="dungeon"
      data-dungeon={dungeon.key}
    >
      <div className="flex items-baseline justify-between gap-2">
        <h3 className="min-w-0 font-display text-[17px]">{dungeon.name}</h3>
        <span className="tabular shrink-0 whitespace-nowrap text-[10.5px] text-ink-faint">
          Level {dungeon.level}
        </span>
      </div>

      <p className="mt-1 flex-1 text-[12px] leading-snug text-ink-muted">{dungeon.blurb}</p>

      <div className="tabular mt-3 flex flex-wrap gap-2 text-[10.5px] text-ink-faint">
        <span className="whitespace-nowrap">{dungeon.rooms} rooms</span>
        <span className="flex items-center gap-1 whitespace-nowrap">
          <Crown size={10} className="shrink-0" />
          {dungeon.bossName}
        </span>
        <span className="whitespace-nowrap text-gold">+{dungeon.clearGold} gold</span>
        <span className="tier-chip rounded-full px-2 py-0.5 whitespace-nowrap capitalize">
          {dungeon.rewardFloor} or better
        </span>
      </div>

      <button
        type="button"
        onClick={onStart}
        disabled={!canOpen || blocked}
        data-testid={`descend-${dungeon.key}`}
        className="tabular mt-3 inline-flex items-center justify-center gap-1.5 rounded-lg bg-ink px-3 py-2 text-xs font-medium whitespace-nowrap text-canvas transition hover:opacity-90 disabled:opacity-30"
      >
        <DoorOpen size={13} className="shrink-0" />
        Descend ({dungeon.totalStaminaCost} stamina)
      </button>

      {!canFinish && (
        <p className="mt-1.5 text-[11px] leading-snug text-ink-faint" data-testid="dungeon-short">
          {canOpen
            ? `Enough for the first room. Seeing it through takes ${dungeon.totalStaminaCost}, and the run will wait while you earn it.`
            : 'Not enough stamina to open the first room.'}
        </p>
      )}
    </motion.li>
  )
}
