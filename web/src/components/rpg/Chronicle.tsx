import {
  ChevronDown,
  DoorOpen,
  FileSignature,
  Flag,
  Footprints,
  ScrollText,
  Skull,
  Sparkles,
  Star,
  Swords,
  type LucideIcon,
} from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import { formatDate } from '../../lib/format'
import type { ChronicleEntry as Entry } from '../../lib/rpg'
import { useChronicle } from '../../lib/rpgQueries'
import { DiceRoll } from './DiceRoll'

/**
 * The server names an icon, this decides what it looks like.
 *
 * The same arrangement avatars and monsters already use: the key crosses the wire and the
 * drawing stays here, so changing a picture never touches a row or a response.
 */
const ICONS: Record<string, LucideIcon> = {
  fight: Swords,
  defeat: Skull,
  flight: Footprints,
  quest: ScrollText,
  contract: FileSignature,
  banner: Flag,
  dungeon: DoorOpen,
  level: Star,
  ascend: Sparkles,
}

const TONE: Record<string, string> = {
  fight: 'text-gold',
  defeat: 'text-rose',
  banner: 'text-gold',
  level: 'text-gold',
  ascend: 'text-gold',
}

export function Chronicle() {
  const chronicle = useChronicle()

  if (chronicle.isLoading) {
    return <div className="panel h-72 animate-pulse rounded-2xl opacity-60" />
  }

  const pages = chronicle.data?.pages ?? []
  const summary = pages[0]?.summary
  const entries = pages.flatMap((page) => page.entries)

  return (
    <div className="space-y-4" data-testid="chronicle">
      <header>
        <h2 className="font-display text-2xl">Chronicle</h2>
        <p className="mt-0.5 text-[13px] text-ink-muted">
          Everything that has happened, newest first.
        </p>
      </header>

      {summary && summary.fought > 0 && (
        <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-4">
          <Stat label="Fought" value={summary.fought} />
          <Stat label="Won" value={summary.won} tone="text-gold" />
          <Stat label="Lost" value={summary.lost} tone={summary.lost > 0 ? 'text-rose' : undefined} />
          <Stat label="Gold earned" value={summary.goldEarned} tone="text-gold" />
        </div>
      )}

      {summary?.mostFoughtMonster && (
        <p className="text-[12px] text-ink-muted">
          Most fought: <span className="text-ink">{summary.mostFoughtMonster}</span>{' '}
          <span className="tabular text-ink-faint">({summary.mostFoughtCount})</span>
        </p>
      )}

      {entries.length === 0 ? (
        <div className="panel rounded-2xl px-6 py-12 text-center">
          <p className="font-display text-lg">No stories yet</p>
          <p className="mt-1 text-[13px] text-ink-muted">
            Take a contract, claim a quest or finish a fight, and it will be written down here.
          </p>
        </div>
      ) : (
        <>
          <ul className="space-y-2">
            {entries.map((entry, index) => (
              <Row key={entry.id} entry={entry} previous={entries[index - 1]} />
            ))}
          </ul>

          {chronicle.hasNextPage && (
            <button
              type="button"
              onClick={() => void chronicle.fetchNextPage()}
              disabled={chronicle.isFetchingNextPage}
              data-testid="chronicle-more"
              className="w-full rounded-xl border border-line py-2.5 text-[13px] text-ink-muted transition hover:bg-surface-sunk disabled:opacity-60"
            >
              {chronicle.isFetchingNextPage ? 'Reading back...' : 'Earlier entries'}
            </button>
          )}
        </>
      )}
    </div>
  )
}

function Stat({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return (
    <div className="panel rounded-xl px-3.5 py-3">
      <p className={`tabular text-[20px] leading-none font-medium ${tone ?? ''}`}>
        {value.toLocaleString()}
      </p>
      <p className="mt-1.5 text-[9.5px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        {label}
      </p>
    </div>
  )
}

/**
 * One line, and the divider that may belong above it.
 *
 * The feed runs newest first, so the era on an entry only ever drops as you read down. Where it
 * drops, an ascension happened, and the line immediately below the divider is the ascension's
 * own entry: it carries the era it ended, which is what puts it on the correct side.
 */
function Row({ entry, previous }: { entry: Entry; previous?: Entry }) {
  const dividesEras = previous !== undefined && previous.era > entry.era

  return (
    <>
      {dividesEras && (
        <li aria-hidden="true" className="flex items-center gap-3 pt-3 pb-1">
          <span className="h-px flex-1 bg-line" />
          <span className="text-[9.5px] font-medium uppercase tracking-[0.18em] text-ink-faint">
            Ascension {previous.era}
          </span>
          <span className="h-px flex-1 bg-line" />
        </li>
      )}

      <Line entry={entry} />
    </>
  )
}

function Line({ entry }: { entry: Entry }) {
  const [open, setOpen] = useState(false)

  const Icon = ICONS[entry.icon] ?? Swords
  const encounter = entry.encounter
  const expandable = encounter !== null && encounter.log.length > 0

  const body = (
    <>
      <Icon size={14} className={`shrink-0 ${TONE[entry.icon] ?? 'text-ink-faint'}`} />

      <span className="min-w-0 flex-1">
        <span className="block text-[14px]">{entry.title}</span>
        <span className="tabular block text-[11px] text-ink-faint">
          {formatDate(entry.occurredAt)}
          {entry.detail ? ` · ${entry.detail}` : ''}
        </span>
      </span>
    </>
  )

  // A line with no fight behind it is not a button. Every entry used to expand because every
  // entry was a fight; a quest claim that opened to nothing would be a control that does nothing.
  if (!expandable) {
    return (
      <li className="panel rounded-xl px-4 py-3" data-testid="chronicle-entry">
        <div className="flex items-center gap-3">{body}</div>
      </li>
    )
  }

  return (
    <li className="panel overflow-hidden rounded-xl" data-testid="chronicle-entry">
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        aria-expanded={open}
        className="flex w-full items-center gap-3 px-4 py-3 text-left transition hover:bg-surface-sunk"
      >
        {body}

        <ChevronDown
          size={14}
          className={`shrink-0 text-ink-faint transition-transform ${open ? 'rotate-180' : ''}`}
        />
      </button>

      <AnimatePresence initial={false}>
        {open && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.2 }}
            className="overflow-hidden border-t border-line"
          >
            <div className="max-h-80 divide-y divide-line overflow-y-auto px-4 py-2">
              {encounter.log.map((roll, index) => (
                <DiceRoll key={index} roll={roll} index={0} />
              ))}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </li>
  )
}
