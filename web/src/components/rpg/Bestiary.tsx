import { Coins, Eye, HelpCircle, Skull, Timer } from 'lucide-react'
import { motion } from 'motion/react'
import { useState } from 'react'
import { formatDate } from '../../lib/format'
import type { BestiaryEntry } from '../../lib/rpg'
import { useBestiary } from '../../lib/rpgQueries'

type Filter = 'all' | 'met' | 'slain' | 'unmet'

/** Pure, so the tab counts and the visible list cannot disagree. */
function matches(entry: BestiaryEntry, filter: Filter): boolean {
  switch (filter) {
    case 'met':
      return entry.isDiscovered
    case 'slain':
      return entry.isSlain
    case 'unmet':
      return !entry.isDiscovered
    default:
      return true
  }
}

const FILTERS: { key: Filter; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'met', label: 'Met' },
  { key: 'slain', label: 'Slain' },
  { key: 'unmet', label: 'Unmet' },
]

/**
 * The codex, including the rows nobody has met yet.
 *
 * Hiding an unmet monster would make the panel a record of the past. Showing it as a
 * silhouette makes it a list of things to go and find, which is the whole point of keeping
 * the description back until the first sighting.
 */
export function Bestiary() {
  const bestiary = useBestiary()
  const [filter, setFilter] = useState<Filter>('all')

  if (bestiary.isLoading) {
    return <div className="panel h-72 animate-pulse rounded-2xl opacity-60" />
  }

  if (bestiary.isError || !bestiary.data) {
    return (
      <p role="alert" className="panel rounded-xl p-4 text-[13px] text-rose">
        Could not open the bestiary: {(bestiary.error as Error)?.message ?? 'unknown error'}
      </p>
    )
  }

  const { entries, discovered, slain, total } = bestiary.data
  const shown = entries.filter((entry) => matches(entry, filter))
  const goldTaken = entries.reduce((sum, entry) => sum + entry.goldTaken, 0)
  const percent = Math.round((discovered / Math.max(1, total)) * 100)

  return (
    <div className="space-y-4" data-testid="bestiary" data-discovered={discovered}>
      <header>
        <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
          <h2 className="font-display text-2xl">Bestiary</h2>
          <p className="tabular text-[12px] text-ink-faint">
            <span className="text-gold">{discovered}</span> / {total} met
            {slain > 0 && <span className="ml-2 text-teal">{slain} slain</span>}
          </p>
        </div>
        <p className="mt-0.5 text-[13px] text-ink-muted">
          Everything in the valley, met or not. A description is the reward for the first
          sighting.
        </p>
      </header>

      <div className="h-1.5 overflow-hidden rounded-full bg-surface-sunk ring-1 ring-line/70 ring-inset">
        <motion.div
          className="h-full rounded-full bg-gold"
          initial={false}
          animate={{ width: `${percent}%` }}
          transition={{ type: 'spring', stiffness: 160, damping: 22 }}
        />
      </div>

      <div className="grid grid-cols-3 gap-2.5">
        <Stat label="Met" value={`${discovered}/${total}`} />
        <Stat label="Slain" value={`${slain}`} tone="text-teal" />
        {/* One word each: at 320px these three cards are 90px wide and a two-word label
            takes a second line the numbers do not need. */}
        <Stat label="Gold" value={goldTaken.toLocaleString()} tone="text-gold" />
      </div>

      <div className="flex flex-wrap items-center gap-0.5 rounded-lg border border-line bg-surface-sunk p-0.5">
        {FILTERS.map((entry) => (
          <button
            key={entry.key}
            type="button"
            aria-pressed={filter === entry.key}
            onClick={() => setFilter(entry.key)}
            data-testid={`bestiary-filter-${entry.key}`}
            className={`flex-1 rounded-md px-2.5 py-1.5 text-[11.5px] font-medium transition ${
              filter === entry.key
                ? 'bg-surface text-ink shadow-[0_1px_2px_rgb(0_0_0/0.1)]'
                : 'text-ink-faint hover:text-ink-muted'
            }`}
          >
            {entry.label}
            <span className="tabular ml-1.5 text-[10px] opacity-60">
              {entries.filter((row) => matches(row, entry.key)).length}
            </span>
          </button>
        ))}
      </div>

      <ul className="grid gap-2.5 sm:grid-cols-2">
        {shown.map((entry, index) => (
          <Card key={entry.key} entry={entry} index={index} />
        ))}
      </ul>
    </div>
  )
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="panel rounded-xl px-3 py-2.5">
      <p className={`tabular text-[17px] leading-none font-medium ${tone ?? ''}`}>{value}</p>
      <p className="mt-1.5 text-[9.5px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        {label}
      </p>
    </div>
  )
}

function Card({ entry, index }: { entry: BestiaryEntry; index: number }) {
  return (
    <motion.li
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: Math.min(index * 0.02, 0.2) }}
      data-testid="bestiary-entry"
      data-monster={entry.key}
      data-discovered={entry.isDiscovered}
      data-slain={entry.isSlain}
      className={`panel flex flex-col rounded-xl p-4 ${
        entry.isDiscovered ? '' : 'border-dashed border-line-strong/60 bg-transparent shadow-none'
      }`}
    >
      <div className="flex items-start justify-between gap-2">
        <h3
          className={`min-w-0 font-display text-[16px] leading-tight ${
            entry.isDiscovered ? '' : 'text-ink-faint'
          }`}
        >
          {entry.name}
        </h3>
        <span className="tabular shrink-0 pt-0.5 text-[10.5px] text-ink-faint">
          Level {entry.level}
        </span>
      </div>

      {entry.isDiscovered ? (
        <>
          <p className="mt-1 flex-1 text-[12px] leading-snug text-ink-muted">{entry.blurb}</p>

          <dl className="tabular mt-3 flex flex-wrap gap-x-3 gap-y-1 text-[10.5px] text-ink-faint">
            <Fact icon={<Eye size={11} />} term="Sightings" text={`${entry.encounters} seen`} />
            <Fact
              icon={<Skull size={11} />}
              term="Kills"
              text={`${entry.kills} slain`}
              tone={entry.kills > 0 ? 'text-teal' : undefined}
            />
            {entry.bestRound > 0 && (
              <Fact
                icon={<Timer size={11} />}
                term="Best kill"
                text={`best in ${entry.bestRound}`}
              />
            )}
            {entry.goldTaken > 0 && (
              <Fact
                icon={<Coins size={11} />}
                term="Gold taken"
                text={`${entry.goldTaken} gold`}
                tone="text-gold"
              />
            )}
          </dl>

          {entry.firstSeenAt && (
            <p className="mt-2 text-[10.5px] text-ink-faint">
              First met {formatDate(entry.firstSeenAt)}
              {entry.lastSeenAt && entry.lastSeenAt !== entry.firstSeenAt && (
                <> &middot; last {formatDate(entry.lastSeenAt)}</>
              )}
            </p>
          )}
        </>
      ) : (
        <div className="mt-1 flex flex-1 items-center gap-2.5">
          <span
            aria-hidden="true"
            className="grid h-8 w-8 shrink-0 place-items-center rounded-lg border border-dashed border-line-strong/60 text-ink-faint"
          >
            <HelpCircle size={14} />
          </span>
          <p className="min-w-0 text-[12px] leading-snug text-ink-faint">
            Never met. Find it in the tavern and the entry writes itself.
          </p>
        </div>
      )}
    </motion.li>
  )
}

/** The icon carries the meaning visually, so the term is there only for a screen reader. */
function Fact({
  icon,
  term,
  text,
  tone,
}: {
  icon: React.ReactNode
  term: string
  text: string
  tone?: string
}) {
  return (
    <div className={`flex items-center gap-1 whitespace-nowrap ${tone ?? ''}`}>
      {icon}
      <dt className="sr-only">{term}</dt>
      <dd>{text}</dd>
    </div>
  )
}
