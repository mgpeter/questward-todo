import { ScrollText, Swords, Zap } from 'lucide-react'
import { motion } from 'motion/react'
import type { Task } from '../../lib/api'
import { useNavigation } from '../../game/Navigation'
import { bountyLabel, bountyTier, describeAge, isReadyToFight, type HuntContract } from '../../lib/rpg'
import {
  useAcceptHunt,
  useActiveHunt,
  useHuntOffer,
  useSheet,
  useTaskContract,
} from '../../lib/rpgQueries'
import { play } from '../../lib/sound'
import { ArchetypeIcon, BountyChip, FactionChip, RewardFloorChip } from './HuntChrome'

/**
 * What a task is worth as a contract, on the task itself.
 *
 * Three things can be true of a task here, and the seal says which: it could be written up, it
 * has been written up and is waiting on the work, or the work is done and there is a creature
 * to go and collect from. Nothing here subtracts, warns, or counts down. The one number it
 * shows is a multiplier at or above 1.
 *
 * The offer half is only drawn on a task that is actually overdue, and that restraint is the
 * point rather than a saving. DEC-013 says an overdue task is a bounty and never a debuff, so
 * the backlog is where the treasure is: a fresh task can carry a contract too, but it pays 1x
 * and no item, and dressing every card on the board in gold would leave the three-week-old
 * chore looking exactly like the one created this morning.
 */
export function TaskHuntSeal({
  task,
  variant = 'full',
}: {
  task: Task
  /**
   * `strip` is the mobile card's footer band: the creature, what it pays, and one button.
   * Everything else the offer carries - its age, its faction, the reward floor - moves to
   * the detail sheet, which draws the `full` form.
   */
  variant?: 'full' | 'strip'
}) {
  const contract = useTaskContract(task.id)
  const offer = useHuntOffer(task.id)
  const active = useActiveHunt()
  const sheet = useSheet()
  const accept = useAcceptHunt()
  const { goTo } = useNavigation()

  const live =
    active.data && active.data.taskId === task.id && active.data.encounter.status === 'active'
      ? active.data
      : null

  if (live) {
    return (
      <div
        className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1.5 rounded-lg border border-gold/50 bg-gold/10 px-2 py-1.5"
        data-testid="task-hunt-live"
      >
        <p className="flex min-w-0 flex-1 items-center gap-1.5 text-[11px] text-gold">
          <ArchetypeIcon archetypeKey={live.archetypeKey} size={12} className="shrink-0" />
          <span className="min-w-0 break-words">{live.monsterName} is up</span>
        </p>

        <button
          type="button"
          onClick={() => goTo('adventure', 'hunts')}
          data-testid="task-hunt-goto"
          className="shrink-0 rounded-md border border-gold/50 px-2 py-1 text-[10.5px] font-medium whitespace-nowrap text-gold transition hover:bg-gold/15"
        >
          To the fight
        </button>
      </div>
    )
  }

  // A contract already taken beats an offer, because the same task cannot have both: the board
  // stops offering a task the moment one is written on it.
  if (contract.data) {
    return <TakenSeal contract={contract.data} stamina={sheet.data?.stamina ?? 0} />
  }

  // No offer means the task cannot carry a contract right now: it is a subtask, it is done, or
  // one has already been taken and closed on it this period. All three are silent rather than
  // explained, because a card is not the place to teach the gate.
  if (!offer.data || offer.data.daysOverdue <= 0) return null

  const quoted = offer.data
  const legend = bountyTier(quoted.daysOverdue) === 'legend'
  const busy = accept.isPending && accept.variables === task.id

  if (variant === 'strip') {
    return (
      <div
        data-testid="task-hunt-strip"
        data-archetype={quoted.archetypeKey}
        data-days-overdue={quoted.daysOverdue}
        className={`flex items-center gap-2.5 border-t px-3.5 py-2.5 ${
          legend ? 'border-gold/45 bg-gold/14' : 'border-gold/30 bg-gold/9'
        }`}
        // Full bleed, so the glow that would be sheared off inside the card's padding is
        // simply the strip's own background here.
        style={legend ? { boxShadow: 'inset 0 0 12px -4px var(--gold-glow)' } : undefined}
      >
        <div className="min-w-0 flex-1">
          <p className="flex items-center gap-1.5 text-[12px] font-medium text-gold">
            <ArchetypeIcon archetypeKey={quoted.archetypeKey} size={12} className="shrink-0" />
            <span className="truncate">{quoted.monsterName}</span>
            <span
              className="tabular shrink-0 font-normal text-ink-muted"
              data-bounty={quoted.bountyPercent}
            >
              {bountyLabel(quoted.bountyPercent)}
            </span>
          </p>
          <p className="mt-0.5 truncate text-[10.5px] text-ink-muted">
            {describeAge(quoted.daysOverdue)}
          </p>
        </div>

        <button
          type="button"
          onClick={() => {
            play('coin')
            accept.mutate(task.id)
          }}
          disabled={busy}
          data-testid="task-hunt-take"
          className="min-h-11 shrink-0 rounded-lg bg-ink px-3 py-2.5 text-[11.5px] font-medium text-canvas transition disabled:opacity-30"
        >
          Take it
        </button>
      </div>
    )
  }

  return (
    <motion.div
      layout
      initial={{ opacity: 0, y: -4 }}
      animate={{ opacity: 1, y: 0 }}
      data-testid="task-hunt-seal"
      data-archetype={quoted.archetypeKey}
      data-days-overdue={quoted.daysOverdue}
      className={`mt-2 rounded-lg border px-2 py-1.5 ${
        legend ? 'border-gold/60 bg-gold/14' : 'border-gold/35 bg-gold/7'
      }`}
      // The oldest contracts glow. Nothing else on a task card does, which is what makes
      // the thing you have been avoiding the most attractive object on the screen.
      // Kept inside the card's own padding: the card clips its overflow, and a wider glow
      // would be sheared off at the edge rather than fading out.
      style={legend ? { boxShadow: '0 0 0 1px var(--gold-glow), 0 0 12px -4px var(--gold-glow)' } : undefined}
    >
      <p className="flex items-center gap-1.5 text-[11px] font-medium text-gold">
        <ArchetypeIcon archetypeKey={quoted.archetypeKey} size={12} className="shrink-0" />
        <span className="min-w-0 break-words">{quoted.monsterName}</span>
      </p>

      <p className="mt-0.5 text-[10px] text-ink-muted">
        {describeAge(quoted.daysOverdue)}
        {quoted.factionTitle && `, and they call you ${quoted.factionTitle}`}
      </p>

      <div className="mt-1.5 flex flex-wrap items-center gap-1">
        <BountyChip
          bountyPercent={quoted.bountyPercent}
          daysOverdue={quoted.daysOverdue}
          testId="task-hunt-bounty"
        />
        {quoted.paysContractReward && <RewardFloorChip floor={quoted.rewardFloor} />}
        {quoted.factionName && (
          <FactionChip
            name={quoted.factionName}
            title={quoted.factionTitle}
            standing={quoted.standing}
          />
        )}
      </div>

      {/* No stamina beside this button and no cost under it, because there is none. Taking a
          contract is free; what it buys is the right to collect once the task is done. */}
      <button
        type="button"
        onClick={() => {
          play('coin')
          accept.mutate(task.id)
        }}
        disabled={busy}
        data-testid="task-hunt-start"
        className="mt-1.5 flex w-full flex-wrap items-center justify-center gap-x-1.5 rounded-md bg-ink px-2 py-1.5 text-[11px] font-medium text-canvas transition hover:opacity-90 disabled:opacity-30"
      >
        <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
          <ScrollText size={11} className="shrink-0" />
          Take the contract
        </span>
        <span className="whitespace-nowrap opacity-70">free</span>
      </button>

      {accept.isError && accept.variables === task.id && (
        <p role="alert" className="mt-1 text-[10px] leading-snug text-rose" data-testid="task-hunt-error">
          {(accept.error as Error).message}
        </p>
      )}
    </motion.div>
  )
}

/**
 * A contract already standing on this task: waiting on the work, or waiting to be collected.
 *
 * The waiting form deliberately carries no button. There is nothing to press until the task is
 * ticked off, and offering one would be offering the thing DEC-013 removed: a way to cash in a
 * chore by fighting instead of by doing it.
 */
function TakenSeal({ contract, stamina }: { contract: HuntContract; stamina: number }) {
  const { goTo } = useNavigation()
  const ready = isReadyToFight(contract)

  return (
    <motion.div
      layout
      initial={{ opacity: 0, y: -4 }}
      animate={{ opacity: 1, y: 0 }}
      data-testid="task-hunt-taken"
      data-status={contract.status}
      data-archetype={contract.archetypeKey}
      className={`mt-2 rounded-lg border px-2 py-1.5 ${
        ready ? 'border-gold/60 bg-gold/14' : 'border-line bg-surface-sunk'
      }`}
      style={ready ? { boxShadow: '0 0 0 1px var(--gold-glow), 0 0 12px -4px var(--gold-glow)' } : undefined}
    >
      <p
        className={`flex items-center gap-1.5 text-[11px] font-medium ${
          ready ? 'text-gold' : 'text-ink-muted'
        }`}
      >
        <ArchetypeIcon archetypeKey={contract.archetypeKey} size={12} className="shrink-0" />
        <span className="min-w-0 break-words">{contract.monsterName}</span>
      </p>

      <p className="mt-0.5 text-[10px] leading-snug text-ink-muted">
        {ready
          ? 'The work is done. The bounty is waiting to be collected.'
          : 'Under contract. Finish this and the bounty comes due.'}
      </p>

      <div className="mt-1.5 flex flex-wrap items-center gap-1">
        <BountyChip
          bountyPercent={contract.bountyPercent}
          daysOverdue={contract.daysOverdue}
          testId="task-hunt-bounty"
        />
        {contract.paysContractReward && <RewardFloorChip floor={contract.rewardFloor} />}
      </div>

      {ready && (
        <button
          type="button"
          onClick={() => goTo('adventure', 'hunts')}
          data-testid="task-hunt-collect"
          className="tabular mt-1.5 flex w-full flex-wrap items-center justify-center gap-x-1.5 rounded-md bg-ink px-2 py-1.5 text-[11px] font-medium text-canvas transition hover:opacity-90"
        >
          <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
            <Swords size={11} className="shrink-0" />
            Collect the bounty
          </span>
          <span
            className={`inline-flex items-center gap-0.5 whitespace-nowrap ${
              stamina >= contract.staminaCost ? 'opacity-70' : 'opacity-40'
            }`}
          >
            <Zap size={9} className="shrink-0" />
            {contract.staminaCost}
          </span>
        </button>
      )}
    </motion.div>
  )
}
