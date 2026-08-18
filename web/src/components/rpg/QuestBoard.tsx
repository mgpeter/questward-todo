import { Check, Coins, Lock, Scroll } from 'lucide-react'
import { motion } from 'motion/react'
import { useState } from 'react'
import type { Quest } from '../../lib/rpg'
import { useClaimQuest, useQuests } from '../../lib/rpgQueries'
import { play } from '../../lib/sound'

type Filter = 'all' | 'ready' | 'active' | 'claimed' | 'locked'

/** Pure, so the tab counts and the visible list cannot disagree. */
function matches(quest: Quest, filter: Filter): boolean {
  switch (filter) {
    case 'ready':
      return quest.isComplete && !quest.isClaimed
    case 'active':
      return !quest.isComplete && !quest.isClaimed && !quest.isLocked
    case 'claimed':
      return quest.isClaimed
    case 'locked':
      return quest.isLocked && !quest.isClaimed
    default:
      return true
  }
}

const FILTERS: { key: Filter; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'ready', label: 'Ready' },
  { key: 'active', label: 'In progress' },
  { key: 'locked', label: 'Locked' },
  { key: 'claimed', label: 'Claimed' },
]

export function QuestBoard() {
  const quests = useQuests()
  const claim = useClaimQuest()
  const [filter, setFilter] = useState<Filter>('all')

  if (quests.isLoading) {
    return <div className="panel h-72 animate-pulse rounded-2xl opacity-60" />
  }

  const all = quests.data ?? []

  const shown = all.filter((quest) => matches(quest, filter))
  const claimed = all.filter((q) => q.isClaimed).length
  const ready = all.filter((q) => q.isComplete && !q.isClaimed).length

  return (
    <div data-testid="quest-board">
      <header className="mb-4">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="font-display text-2xl">Quest Log</h2>
          <p className="tabular text-[12px] text-ink-faint">
            <span className="text-gold">{claimed}</span> / {all.length} claimed
            {ready > 0 && <span className="ml-2 text-teal">{ready} ready</span>}
          </p>
        </div>
        <p className="mt-0.5 text-[13px] text-ink-muted">
          Short goals that pay in gold and gear. Never in experience: that is what the task list
          is for.
        </p>
      </header>

      <div className="mb-4 flex flex-wrap items-center gap-0.5 rounded-lg border border-line bg-surface-sunk p-0.5">
        {FILTERS.map((entry) => {
          const count = all.filter((q) => matches(q, entry.key)).length

          return (
            <button
              key={entry.key}
              type="button"
              aria-pressed={filter === entry.key}
              onClick={() => setFilter(entry.key)}
              data-testid={`quest-filter-${entry.key}`}
              className={`flex-1 rounded-md px-2.5 py-1.5 text-[11.5px] font-medium transition ${
                filter === entry.key
                  ? 'bg-surface text-ink shadow-[0_1px_2px_rgb(0_0_0/0.1)]'
                  : 'text-ink-faint hover:text-ink-muted'
              }`}
            >
              {entry.label}
              <span className="tabular ml-1.5 text-[10px] opacity-60">{count}</span>
            </button>
          )
        })}
      </div>

      <ul className="grid gap-2.5 lg:grid-cols-2">
        {shown.map((quest, index) => (
          <motion.li
            key={quest.key}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: Math.min(index * 0.03, 0.25) }}
            data-testid="quest"
            data-key={quest.key}
            data-complete={quest.isComplete}
            data-claimed={quest.isClaimed}
            data-locked={quest.isLocked}
            className={`panel rounded-xl p-4 ${
              quest.isClaimed || quest.isLocked
                ? 'opacity-60'
                : quest.isComplete
                  ? 'border-gold/40'
                  : ''
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

              {quest.isClaimed ? (
                <span className="flex shrink-0 items-center gap-1 text-[10px] font-medium uppercase tracking-[0.14em] text-teal">
                  <Check size={11} /> Done
                </span>
              ) : quest.isLocked ? (
                <span className="flex shrink-0 items-center gap-1 text-[10px] font-medium uppercase tracking-[0.14em] text-ink-faint">
                  <Lock size={11} /> Level {quest.minimumLevel}
                </span>
              ) : null}
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

              {!quest.isClaimed && !quest.isLocked && (
                <button
                  type="button"
                  onClick={() =>
                    // A reward that includes an item gets the drop cue, which already
                    // contains the coin cue, so the gold is never announced twice.
                    claim.mutate(quest.key, {
                      onSuccess: (result) => play(result.item ? 'drop' : 'coin'),
                    })
                  }
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
