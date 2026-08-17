import { Check, Coins, Scroll } from 'lucide-react'
import { motion } from 'motion/react'
import { useClaimQuest, useQuests } from '../../lib/rpgQueries'

export function QuestBoard() {
  const quests = useQuests()
  const claim = useClaimQuest()

  if (quests.isLoading) {
    return <div className="panel h-72 animate-pulse rounded-2xl opacity-60" />
  }

  return (
    <div data-testid="quest-board">
      <header className="mb-4">
        <h2 className="font-display text-2xl">Quest Board</h2>
        <p className="mt-0.5 text-[13px] text-ink-muted">
          Short goals that pay in gold and gear. Never in experience: that is what the task list
          is for.
        </p>
      </header>

      <ul className="grid gap-2.5 lg:grid-cols-2">
        {quests.data?.map((quest, index) => (
          <motion.li
            key={quest.key}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: Math.min(index * 0.03, 0.25) }}
            data-testid="quest"
            data-key={quest.key}
            data-complete={quest.isComplete}
            data-claimed={quest.isClaimed}
            className={`panel rounded-xl p-4 ${
              quest.isClaimed ? 'opacity-60' : quest.isComplete ? 'border-gold/40' : ''
            }`}
          >
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <h3 className="flex items-center gap-1.5 font-display text-[16px]">
                  <Scroll size={13} className="text-ink-faint" />
                  {quest.name}
                </h3>
                <p className="mt-1 text-[12px] leading-snug text-ink-muted">{quest.description}</p>
              </div>

              {quest.isClaimed && (
                <span className="flex shrink-0 items-center gap-1 text-[10px] font-medium uppercase tracking-[0.14em] text-teal">
                  <Check size={11} /> Done
                </span>
              )}
            </div>

            <ul className="mt-3 space-y-1.5">
              {quest.objectives.map((objective) => (
                <li key={objective.id} className="flex items-center gap-2">
                  <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-surface-sunk">
                    <div
                      className="h-full rounded-full transition-all"
                      style={{
                        width: `${Math.min(100, (objective.current / Math.max(1, objective.required)) * 100)}%`,
                        backgroundColor: objective.isComplete ? 'var(--teal)' : 'var(--gold)',
                      }}
                    />
                  </div>
                  <span className="tabular w-24 shrink-0 text-right text-[10.5px] text-ink-faint">
                    {objective.current}/{objective.required}
                  </span>
                </li>
              ))}
            </ul>

            <p className="mt-2 text-[11px] text-ink-faint">
              {quest.objectives.map((o) => o.description).join(' · ')}
            </p>

            <div className="mt-3 flex items-center justify-between gap-2">
              <p className="tabular flex items-center gap-1.5 text-[11.5px] text-gold">
                <Coins size={12} />
                {quest.rewardGold}
                {quest.rewardItemName && (
                  <span className="text-ink-muted">and {quest.rewardItemName}</span>
                )}
              </p>

              {!quest.isClaimed && (
                <button
                  type="button"
                  onClick={() => claim.mutate(quest.key)}
                  disabled={!quest.isComplete || claim.isPending}
                  data-testid={`claim-${quest.key}`}
                  className="rounded-lg bg-ink px-3 py-1.5 text-[11.5px] font-medium text-canvas transition hover:opacity-90 disabled:opacity-25"
                >
                  Claim
                </button>
              )}
            </div>
          </motion.li>
        ))}
      </ul>
    </div>
  )
}
