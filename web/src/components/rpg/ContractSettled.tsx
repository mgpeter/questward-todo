import { Swords, X, Zap } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useRef } from 'react'
import { useGameFeed } from '../../game/GameFeed'
import { useNavigation } from '../../game/Navigation'
import { bountyIsCapped, bountyLabel, pluralDays } from '../../lib/rpg'
import { play } from '../../lib/sound'
import { ArchetypeIcon, BountyChip, RewardFloorChip } from './HuntChrome'

const LIFETIME_MS = 11_000

/**
 * The moment a contract comes due, announced where the work was finished.
 *
 * Finishing the task is the only thing that discharges a contract, and it happens on the task
 * screen, two tabs away from anything that renders one. Without this the creature the player
 * has just earned the right to fight would be waiting on a board they are not looking at.
 *
 * Nothing is paid here, and the copy says so plainly. The purse is what winning the fight pays,
 * and the fight still costs the one stamina every fight costs; a toast that announced gold would
 * be describing the old shape, where a bounty could be collected on a task that was never done.
 *
 * Anchored bottom left because the badge toasts own the bottom right, and under the level-up
 * overlay's layer because a level is the bigger moment and only tasks can grant one.
 */
export function ContractSettled() {
  const { contract, dismissContract } = useGameFeed()
  const { goTo } = useNavigation()

  // Held in a ref and kept out of the effect's dependencies on purpose. The effect below plays
  // a cue and arms a timer, and both must happen once per contract: keyed on a callback
  // identity instead, it re-ran whenever anything else in the game feed moved, replaying the
  // cue and pushing the dismissal further out each time.
  const dismiss = useRef(dismissContract)
  dismiss.current = dismissContract

  const contractId = contract?.id ?? null

  useEffect(() => {
    if (!contractId) return

    // The kill cue, because that is what a discharge is: the thing goes down, and it went down
    // to a finished task rather than to a sword. No coins, because none have been paid.
    play('kill', 0.05)

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') dismiss.current()
    }

    const timer = window.setTimeout(() => dismiss.current(), LIFETIME_MS)
    window.addEventListener('keydown', onKey)

    return () => {
      window.clearTimeout(timer)
      window.removeEventListener('keydown', onKey)
    }
  }, [contractId])

  return (
    <div
      className="pointer-events-none fixed bottom-4 left-4 z-50 w-[min(21rem,calc(100vw-2rem))]"
      role="status"
      aria-live="polite"
    >
      <AnimatePresence>
        {contract && (
          <motion.div
            key={contract.id}
            initial={{ opacity: 0, x: -40, scale: 0.96 }}
            animate={{ opacity: 1, x: 0, scale: 1 }}
            exit={{ opacity: 0, x: -40, scale: 0.96 }}
            transition={{ type: 'spring', stiffness: 340, damping: 30 }}
            data-testid="contract-settled"
            data-contract={contract.id}
            data-status={contract.status}
            className="panel pointer-events-auto rounded-xl border-gold/50 p-3.5"
            style={{ boxShadow: '0 0 26px -10px var(--gold-glow), var(--shadow-lift)' }}
          >
            <div className="flex items-start gap-2">
              <div className="min-w-0 flex-1">
                <p className="text-[9px] font-medium tracking-[0.2em] uppercase text-gold">
                  Contract discharged
                </p>

                <p className="mt-1 flex items-center gap-1.5 font-display text-[16px] leading-tight">
                  <ArchetypeIcon
                    archetypeKey={contract.archetypeKey}
                    className="shrink-0 text-gold"
                  />
                  <span className="min-w-0 break-words">{contract.monsterName}</span>
                </p>

                <p className="mt-0.5 text-[11.5px] leading-snug text-ink-muted">
                  {contract.taskTitle
                    ? `"${contract.taskTitle}" is done, so the thing it stood for can be hunted.`
                    : 'The work is done, so the thing it stood for can be hunted.'}
                </p>
              </div>

              <button
                type="button"
                onClick={dismissContract}
                aria-label="Dismiss"
                data-testid="contract-dismiss"
                className="shrink-0 rounded p-0.5 text-ink-faint transition hover:text-ink"
              >
                <X size={13} />
              </button>
            </div>

            <div className="mt-2.5 flex flex-wrap items-center gap-1.5">
              <BountyChip
                bountyPercent={contract.bountyPercent}
                daysOverdue={contract.daysOverdue}
                testId="contract-bounty"
              />
              {contract.paysContractReward && <RewardFloorChip floor={contract.rewardFloor} />}
            </div>

            {contract.daysOverdue > 0 && (
              <p className="mt-2 text-[10.5px] leading-snug text-ink-faint" data-testid="contract-age">
                {contract.daysOverdue} {pluralDays(contract.daysOverdue)} of waiting is worth{' '}
                <span className="tabular">{bountyLabel(contract.bountyPercent)}</span>
                {bountyIsCapped(contract.bountyPercent)
                  ? ' on the purse. That is the ceiling: more waiting would have been worth nothing more.'
                  : ' on the purse.'}
              </p>
            )}

            <button
              type="button"
              onClick={() => {
                dismissContract()
                goTo('adventure', 'hunts')
              }}
              data-testid="contract-to-the-fight"
              className="tabular mt-3 flex w-full flex-wrap items-center justify-center gap-x-1.5 rounded-lg bg-ink px-3 py-2 text-xs font-medium text-canvas transition hover:opacity-90"
            >
              <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
                <Swords size={12} className="shrink-0" />
                Collect it
              </span>
              <span className="inline-flex items-center gap-0.5 whitespace-nowrap opacity-70">
                <Zap size={10} className="shrink-0" />
                {contract.staminaCost}
              </span>
            </button>

            {/* The rule the whole design rests on, said in the one place a player might
                otherwise conclude that finishing the task had already paid the purse. */}
            <p className="mt-2.5 border-t border-line pt-2 text-[10px] leading-snug text-ink-faint">
              The XP came from finishing the task. The gold is still in the creature, and it
              keeps.
            </p>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}
