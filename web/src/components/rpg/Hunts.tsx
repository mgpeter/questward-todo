import { CircleCheck, Flag, ScrollText, Swords, Trash2, Zap } from 'lucide-react'
import { motion } from 'motion/react'
import { useState } from 'react'
import { difficultyMeta } from '../../lib/difficulty'
import {
  bountyTier,
  describeAge,
  isReadyToFight,
  standingLabel,
  standingRung,
  STANDING_RUNGS,
  type AttackResult,
  type CharacterSheet,
  type FactionStanding,
  type HuntContract,
  type HuntOffer,
  type InventoryItem,
} from '../../lib/rpg'
import {
  useAbandonHunt,
  useAcceptHunt,
  useActiveEncounter,
  useActiveHunt,
  useDismissEncounter,
  useDismissHunt,
  useFightHunt,
  useHuntBoard,
} from '../../lib/rpgQueries'
import { play } from '../../lib/sound'
import { EncounterResult } from './EncounterResult'
import { EncounterView } from './EncounterView'
import {
  ArchetypeIcon,
  BountyChip,
  ContractStats,
  HuntBanner,
  OfferStats,
  RewardFloorChip,
} from './HuntChrome'

/**
 * How many offers the board shows before it stops.
 *
 * A board, not a second copy of the task list. The list is already on the screen next door;
 * what this is for is picking which chore to promise to finish, and forty rows of
 * nothing-much buries the one Ancient Bulwark that is the point.
 *
 * The cap lives here rather than on the server, and that is the fix rather than a detail: the
 * server used to trim the list to twenty and the task cards read their own contract out of
 * that same trimmed list, so a player with twenty-one overdue tasks had five of them silently
 * lose the seal, the button and every route to a contract. A display cap belongs on the
 * display.
 */
const BOARD_LIMIT = 20

/**
 * The contract board, the contracts taken, and the fight one of them bought.
 *
 * Three steps, and the panel is laid out as them. Accepting is free and writes a promise;
 * finishing the task discharges it; fighting it costs the one stamina every fight costs. There
 * is no button anywhere here that turns an unfinished task into gold, which is the whole point:
 * DEC-013 makes the backlog the treasure, and the treasure is behind doing the work.
 */
export function Hunts({
  sheet,
  inventory,
}: {
  sheet: CharacterSheet
  inventory: InventoryItem[]
}) {
  const hunt = useActiveHunt()
  const encounter = useActiveEncounter()
  const dismissHunt = useDismissHunt()
  const dismissEncounter = useDismissEncounter()

  // Held here rather than in the query cache, matching the tavern and the dungeons: an outcome
  // belongs to the player's attention and stays put until it has been read.
  //
  // The banner rides along with it because the killing blow invalidates the live hunt, so by
  // the time this card renders there is nothing left to ask which banner paid for it.
  const [outcome, setOutcome] = useState<{ result: AttackResult; banner: string | null } | null>(
    null,
  )

  if (outcome) {
    return (
      <EncounterResult
        result={outcome.result}
        againLabel="Back to the board"
        // The banner's guaranteed item, not a dungeon's. One wire slot carries both and an
        // encounter says nothing about which it was, so the panel that knows names it.
        clearRewardCaption={outcome.banner ?? 'Contract reward'}
        onDismiss={() => {
          setOutcome(null)
          dismissEncounter()
          dismissHunt()
        }}
        onFightAgain={() => {
          setOutcome(null)
          dismissEncounter()
          dismissHunt()
        }}
      />
    )
  }

  if (hunt.isLoading) {
    return <div className="panel h-64 animate-pulse rounded-2xl opacity-60" />
  }

  const live = hunt.data && hunt.data.encounter.status === 'active' ? hunt.data : null

  // The two reads describe the same row, so the hunt's own copy is only trusted for the
  // framing. What is fought is whatever the encounter cache holds, which is the same object
  // the tavern would fight and the same one an attack updates.
  const open =
    live && encounter.data && encounter.data.id === live.encounterId ? encounter.data : null

  if (live && open) {
    return (
      <EncounterView
        encounter={open}
        sheet={sheet}
        inventory={inventory}
        onFinished={(result) => setOutcome({ result, banner: live.factionName })}
        fleeLabel="Let it go"
        fleeNote="Walking away costs you the stamina and the purse. The task stays done, and the contract is spent."
        banner={<HuntBanner hunt={live} />}
      />
    )
  }

  return <ContractBoard sheet={sheet} />
}

function ContractBoard({ sheet }: { sheet: CharacterSheet }) {
  const board = useHuntBoard()
  const encounter = useActiveEncounter()

  // One fight at a time. A brawl already standing in the tavern refuses every contract fight
  // here, so it is said before the click rather than delivered as a 409 after it. Accepting is
  // untouched by it: a promise is not a fight.
  const busyElsewhere = encounter.data?.status === 'active'

  if (board.isLoading) {
    return <div className="panel h-64 animate-pulse rounded-2xl opacity-60" />
  }

  const offers = board.data?.offers ?? []
  const contracts = board.data?.contracts ?? []
  const factions = board.data?.factions ?? []
  const shown = offers.slice(0, BOARD_LIMIT)

  return (
    <div className="space-y-4" data-testid="hunts">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <div className="min-w-0">
          <h2 className="font-display text-2xl">The Contract Board</h2>
          <p className="mt-0.5 text-[13px] text-ink-muted">
            Your own backlog, written up as bounties. Taking one is free. Finishing the task is
            what makes it payable, and the longer it had waited, the more it pays.
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

      <Banners factions={factions} offers={offers} />

      <TakenContracts
        contracts={contracts}
        stamina={sheet.stamina}
        blocked={busyElsewhere}
      />

      {offers.length === 0 ? (
        <p
          className="panel rounded-xl px-4 py-8 text-center text-[13px] text-ink-muted"
          data-testid="hunts-empty"
        >
          Nothing is up for contract. Every open task either carries one already or has not
          been written up yet.
        </p>
      ) : (
        <>
          <p className="text-[11.5px] text-ink-faint" data-testid="hunt-order">
            Oldest first, which is the same as richest first. Taking one costs nothing at all;
            the fight it earns costs a stamina, once the task itself is done.
          </p>

          <ul className="grid gap-2.5 lg:grid-cols-2">
            {shown.map((offer) => (
              <OfferCard key={offer.taskId} offer={offer} />
            ))}
          </ul>

          {offers.length > shown.length && (
            <p className="text-[11.5px] text-ink-faint" data-testid="hunt-more">
              {offers.length - shown.length} more could be written up. They are on your task
              list, each with its own seal.
            </p>
          )}
        </>
      )}
    </div>
  )
}

/**
 * The contracts already taken: what is promised, and what is owed.
 *
 * Two states, and the difference between them is the whole feature. An accepted contract shows
 * what finishing the task will unlock and offers no fight; a discharged one is the only thing
 * on this screen with a fight button on it.
 */
function TakenContracts({
  contracts,
  stamina,
  blocked,
}: {
  contracts: HuntContract[]
  stamina: number
  /** A fight already standing, which would refuse this one. */
  blocked: boolean
}) {
  if (contracts.length === 0) return null

  const ready = contracts.filter(isReadyToFight).length

  return (
    <section className="space-y-2.5" data-testid="taken-contracts">
      <header className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <h3 className="flex items-center gap-1.5 text-[11px] font-medium tracking-[0.16em] uppercase text-ink-faint">
          <ScrollText size={12} />
          Contracts taken
        </h3>
        <p className="text-[11px] text-ink-faint" data-testid="contracts-ready">
          {ready === 0
            ? 'Finish the task behind one and it becomes payable.'
            : `${ready} ready to collect on.`}
        </p>
      </header>

      {blocked && ready > 0 && (
        <p
          className="panel flex flex-wrap items-center gap-x-2 gap-y-1 rounded-xl border-gold/30 px-4 py-3 text-[12.5px] text-ink-muted"
          data-testid="hunt-blocked"
        >
          <Swords size={13} className="shrink-0 text-gold" />
          <span className="min-w-0">
            You are already in a fight. Finish it in the Tavern before collecting on one of
            these. The contract keeps.
          </span>
        </p>
      )}

      <ul className="grid gap-2.5 lg:grid-cols-2">
        {contracts.map((contract) => (
          <ContractCard
            key={contract.id}
            contract={contract}
            stamina={stamina}
            blocked={blocked}
          />
        ))}
      </ul>
    </section>
  )
}

function ContractCard({
  contract,
  stamina,
  blocked,
}: {
  contract: HuntContract
  stamina: number
  blocked: boolean
}) {
  const fight = useFightHunt()
  const abandon = useAbandonHunt()
  const ready = isReadyToFight(contract)
  const legend = bountyTier(contract.daysOverdue) === 'legend'
  const affordable = stamina >= contract.staminaCost
  const busy = fight.isPending && fight.variables === contract.id

  return (
    <motion.li
      layout
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      className={`panel flex flex-col rounded-xl p-4 ${
        ready ? 'border-gold/55' : legend ? 'border-gold/35' : ''
      }`}
      // A discharged contract glows, and only it does. The thing you have earned is the
      // brightest object on the screen, which is DEC-013 said in light.
      style={ready ? { boxShadow: '0 0 24px -10px var(--gold-glow), var(--shadow-card)' } : undefined}
      data-testid="hunt-contract"
      data-contract={contract.id}
      data-status={contract.status}
      data-task={contract.taskId ?? ''}
      data-days-overdue={contract.daysOverdue}
    >
      <div className="flex items-baseline justify-between gap-2">
        <h4 className="flex min-w-0 items-center gap-1.5 font-display text-[17px]">
          <ArchetypeIcon archetypeKey={contract.archetypeKey} size={14} className="shrink-0 text-gold" />
          <span className="min-w-0 break-words">{contract.monsterName}</span>
        </h4>
        <span className="tabular shrink-0 text-[10.5px] whitespace-nowrap text-ink-faint">
          Level {contract.level}
        </span>
      </div>

      <p
        className="mt-2 flex flex-wrap items-baseline gap-x-2 gap-y-1 border-t border-line pt-2 text-[12px]"
        data-testid="contract-task"
      >
        <span className="min-w-0 break-words text-ink">
          {contract.taskTitle}
        </span>
        {contract.taskId === null && (
          <span className="shrink-0 text-[10px] whitespace-nowrap text-ink-faint">
            task deleted
          </span>
        )}
      </p>

      <ContractStats contract={contract} />

      <div className="mt-2 flex flex-wrap items-center gap-1.5">
        <BountyChip bountyPercent={contract.bountyPercent} daysOverdue={contract.daysOverdue} />
        <span
          className={`rounded-full border px-2 py-0.5 text-[10px] whitespace-nowrap ${
            contract.daysOverdue > 0 ? 'border-gold/35 text-gold' : 'border-line text-ink-faint'
          }`}
          data-testid="contract-age"
        >
          {describeAge(contract.daysOverdue)}
        </span>
        {contract.paysContractReward && <RewardFloorChip floor={contract.rewardFloor} />}
        {contract.factionName && (
          <span
            className="inline-flex min-w-0 items-center gap-1 rounded-full border border-line bg-surface-sunk px-2 py-0.5 text-[10px] text-ink-muted"
            data-testid="contract-faction"
            data-faction={contract.factionKey}
            title={contract.factionTitle ? `${contract.factionName}: ${contract.factionTitle}` : contract.factionName ?? undefined}
          >
            <Flag size={9} className="shrink-0" />
            {/* The banner first. This read the title alone, so a correctly tagged contract
                announced itself as "Unentered" and the tag rule looked broken. */}
            <span className="truncate">{contract.factionName}</span>
            {contract.factionTitle && (
              <span className="hidden shrink-0 text-ink-faint sm:inline">{contract.factionTitle}</span>
            )}
          </span>
        )}
      </div>

      <div className="flex-1" />

      {ready ? (
        <button
          type="button"
          onClick={() => {
            play('attack')
            fight.mutate(contract.id)
          }}
          disabled={busy || blocked || !affordable}
          data-testid={`fight-${contract.id}`}
          className="tabular mt-3 flex flex-wrap items-center justify-center gap-x-1.5 gap-y-0.5 rounded-lg bg-ink px-3 py-2 text-xs font-medium text-canvas transition hover:opacity-90 disabled:opacity-30"
        >
          <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
            <Swords size={13} className="shrink-0" />
            Collect the bounty
          </span>
          <span className="inline-flex items-center gap-0.5 whitespace-nowrap opacity-70">
            <Zap size={11} className="shrink-0" />
            {contract.staminaCost}
          </span>
        </button>
      ) : (
        // No fight button at all while the work is outstanding, rather than a disabled one:
        // there is nothing to enable. The purse is behind the task, and the card says which
        // task.
        <p
          className="mt-3 flex items-start gap-1.5 rounded-lg border border-line bg-surface-sunk px-3 py-2 text-[11.5px] leading-snug text-ink-muted"
          data-testid="contract-waiting"
        >
          <CircleCheck size={13} className="mt-0.5 shrink-0 text-ink-faint" />
          <span className="min-w-0">
            Finish the task and this becomes payable. Nothing here can be fought until it is.
          </span>
        </p>
      )}

      {ready && !affordable && (
        <p className="mt-1.5 text-[11px] leading-snug text-ink-faint" data-testid="contract-no-stamina">
          Not enough stamina. The contract keeps: finish something else and come back to it.
        </p>
      )}

      <button
        type="button"
        onClick={() => abandon.mutate(contract.id)}
        disabled={abandon.isPending && abandon.variables === contract.id}
        data-testid={`abandon-${contract.id}`}
        className="mt-1.5 inline-flex items-center justify-center gap-1 self-center rounded-md px-2 py-1 text-[10.5px] whitespace-nowrap text-ink-faint transition hover:text-ink disabled:opacity-40"
      >
        <Trash2 size={10} className="shrink-0" />
        Tear it up
      </button>

      {fight.isError && fight.variables === contract.id && (
        <p role="alert" className="mt-1.5 text-[11.5px] text-rose" data-testid="contract-error">
          {(fight.error as Error).message}
        </p>
      )}
    </motion.li>
  )
}

/**
 * Standing with the five banners, derived from the tags on the player's own tasks.
 *
 * Every banner is shown, flown or not, for the same reason the bestiary shows monsters that
 * have never been met: the empty rows are what there is to aim at. Standing counts contracts
 * won, never contracts taken, and buys exactly one mechanical thing, which is the floor a
 * contract reward cannot roll below.
 */
function Banners({ factions, offers }: { factions: FactionStanding[]; offers: HuntOffer[] }) {
  if (factions.length === 0) return null

  const openUnder = (key: string) => offers.filter((offer) => offer.factionKey === key).length

  return (
    <section className="panel rounded-2xl p-4" data-testid="factions">
      <header className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <h3 className="flex items-center gap-1.5 text-[11px] font-medium tracking-[0.16em] uppercase text-ink-faint">
          <Flag size={12} />
          Banners
        </h3>
        <p className="text-[11px] text-ink-faint">
          Mustered from the tags on your tasks. Standing is won contracts, and it buys a better
          floor on what they hand over.
        </p>
      </header>

      <ul className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
        {factions.map((faction) => {
          const open = openUnder(faction.key)
          const known = faction.wonHunts > 0

          return (
            <li
              key={faction.key}
              className={`rounded-xl border px-3 py-2.5 ${
                known ? 'border-gold/35 bg-gold/6' : 'border-line bg-surface-sunk'
              }`}
              data-testid="faction"
              data-faction={faction.key}
              data-standing={faction.standing}
              data-won={faction.wonHunts}
            >
              <div className="flex items-baseline justify-between gap-2">
                <p className="min-w-0 truncate font-display text-[15px]">{faction.name}</p>
                <span className="tabular shrink-0 text-[10px] whitespace-nowrap text-ink-faint">
                  {faction.wonHunts} won
                </span>
              </div>

              <p className="mt-0.5 text-[11px] leading-snug text-ink-muted">{faction.blurb}</p>

              {/* The words themselves. This panel said "mustered from your tags" without ever
                  saying which, and they were nowhere else on screen either - so twenty words
                  decided whether a contract paid an item and none of them were readable. */}
              <p className="mt-1.5 text-[10.5px] leading-snug text-ink-faint" data-testid="faction-aliases">
                {faction.aliases.length > 0 ? (
                  faction.aliases.map((alias, index) => (
                    <span key={alias}>
                      {index > 0 && <span className="opacity-50"> &middot; </span>}
                      <span className="text-ink-muted">{alias}</span>
                    </span>
                  ))
                ) : (
                  <span className="italic">any other tag</span>
                )}
              </p>

              <StandingLadder faction={faction} />

              <div className="mt-2 flex flex-wrap items-center gap-1.5">
                <span
                  className={`rounded-full border px-2 py-0.5 text-[10px] ${
                    known ? 'border-gold/40 bg-gold/10 text-gold' : 'border-line text-ink-faint'
                  }`}
                  data-testid="faction-title"
                >
                  {faction.title}
                </span>
                <RewardFloorChip floor={faction.rewardFloor} testId="faction-floor" />
                {open > 0 && (
                  <span
                    className="tabular rounded-full border border-line px-2 py-0.5 text-[10px] whitespace-nowrap text-ink-muted"
                    data-testid="faction-open"
                  >
                    {open} up
                  </span>
                )}
              </div>
            </li>
          )
        })}
      </ul>
    </section>
  )
}

/** Four rungs, because standing has a top. A hunter with 200 wins is as Sworn as one with 40. */
function StandingLadder({ faction }: { faction: FactionStanding }) {
  const rung = standingRung(faction.standing)

  return (
    <div
      className="mt-2 flex items-center gap-1.5"
      title={`${standingLabel(faction.standing)} with ${faction.name}`}
    >
      <div className="flex flex-1 gap-0.5" aria-hidden="true">
        {Array.from({ length: STANDING_RUNGS }, (_, index) => (
          <span
            key={index}
            className={`h-1 flex-1 rounded-full ${index < rung ? 'bg-gold' : 'bg-line'}`}
          />
        ))}
      </div>
      <span className="shrink-0 text-[9.5px] tracking-[0.14em] uppercase text-ink-faint">
        {standingLabel(faction.standing)}
      </span>
    </div>
  )
}

function OfferCard({ offer }: { offer: HuntOffer }) {
  const accept = useAcceptHunt()
  const meta = difficultyMeta(offer.difficulty)
  const legend = bountyTier(offer.daysOverdue) === 'legend'
  const busy = accept.isPending && accept.variables === offer.taskId

  return (
    <motion.li
      layout
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      className={`${meta.tierClass} panel flex flex-col rounded-xl p-4 ${
        legend ? 'border-gold/50' : ''
      }`}
      // The oldest contracts glow, and only they do. The most avoided thing on the list is
      // the most attractive object on the screen, which is DEC-013 said in light.
      style={legend ? { boxShadow: '0 0 24px -10px var(--gold-glow), var(--shadow-card)' } : undefined}
      data-testid="hunt-offer"
      data-task={offer.taskId}
      data-archetype={offer.archetypeKey}
      data-days-overdue={offer.daysOverdue}
    >
      <div className="flex items-baseline justify-between gap-2">
        <h3 className="flex min-w-0 items-center gap-1.5 font-display text-[17px]">
          <ArchetypeIcon archetypeKey={offer.archetypeKey} size={14} className="shrink-0 text-gold" />
          <span className="min-w-0 break-words">{offer.monsterName}</span>
        </h3>
        <span className="tabular shrink-0 text-[10.5px] whitespace-nowrap text-ink-faint">
          Level {offer.level}
        </span>
      </div>

      <p className="mt-1 text-[12px] leading-snug text-ink-muted">{offer.blurb}</p>

      {/* The task's own words, in the one place the hunt lets them appear. The creature's
          name, the combat log and the chronicle stay catalog text, which is what keeps a
          fight readable to anyone and a task title private to its owner. */}
      <p
        className="mt-2 flex flex-wrap items-baseline gap-x-2 gap-y-1 border-t border-line pt-2 text-[12px]"
        data-testid="offer-task"
      >
        <span className="min-w-0 break-words text-ink">{offer.title}</span>
        <span className="tier-chip shrink-0 rounded-full px-2 py-0.5 text-[10px] font-medium">
          {meta.label}
        </span>
        {offer.subtasks > 0 && (
          <span className="tabular shrink-0 text-[10px] whitespace-nowrap text-ink-faint">
            {offer.subtasks} {offer.subtasks === 1 ? 'part' : 'parts'}
          </span>
        )}
      </p>

      <OfferStats offer={offer} />

      <div className="mt-2 flex flex-wrap items-center gap-1.5">
        <BountyChip bountyPercent={offer.bountyPercent} daysOverdue={offer.daysOverdue} />
        <span
          className={`rounded-full border px-2 py-0.5 text-[10px] whitespace-nowrap ${
            offer.daysOverdue > 0 ? 'border-gold/35 text-gold' : 'border-line text-ink-faint'
          }`}
          data-testid="offer-age"
        >
          {describeAge(offer.daysOverdue)}
        </span>
        {offer.paysContractReward && <RewardFloorChip floor={offer.rewardFloor} />}
        {offer.factionName && (
          <span
            className="inline-flex min-w-0 items-center gap-1 rounded-full border border-line bg-surface-sunk px-2 py-0.5 text-[10px] text-ink-muted"
            data-testid="offer-faction"
            data-faction={offer.factionKey}
            title={offer.factionTitle ? `${offer.factionName}: ${offer.factionTitle}` : offer.factionName ?? undefined}
          >
            <Flag size={9} className="shrink-0" />
            {/* The banner first. This read the title alone, so a correctly tagged contract
                announced itself as "Unentered" and the tag rule looked broken. */}
            <span className="truncate">{offer.factionName}</span>
            {offer.factionTitle && (
              <span className="hidden shrink-0 text-ink-faint sm:inline">{offer.factionTitle}</span>
            )}
          </span>
        )}
      </div>

      <div className="flex-1" />

      {/* No stamina on this button and no cost beside it, because there is none. Charging to
          take a contract would be a toll for having a backlog, and DEC-013 replaced every
          such toll with a bounty. */}
      <button
        type="button"
        onClick={() => {
          play('coin')
          accept.mutate(offer.taskId)
        }}
        disabled={busy}
        data-testid={`hunt-${offer.taskId}`}
        className="mt-3 flex flex-wrap items-center justify-center gap-x-1.5 gap-y-0.5 rounded-lg bg-ink px-3 py-2 text-xs font-medium text-canvas transition hover:opacity-90 disabled:opacity-30"
      >
        <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
          <ScrollText size={13} className="shrink-0" />
          Take the contract
        </span>
        <span className="whitespace-nowrap opacity-70">free</span>
      </button>

      <p className="mt-1.5 text-[11px] leading-snug text-ink-faint" data-testid="offer-terms">
        {offer.daysOverdue > 0
          ? 'Costs nothing to take. Finishing the task is what makes it payable, and the fight after that costs one stamina.'
          : 'Nothing is owed on this one yet, so it would pay plain gold and no item. It is worth more the longer it sits, and it only pays once it is finished.'}
      </p>

      {accept.isError && accept.variables === offer.taskId && (
        <p role="alert" className="mt-1.5 text-[11.5px] text-rose" data-testid="offer-error">
          {(accept.error as Error).message}
        </p>
      )}
    </motion.li>
  )
}
