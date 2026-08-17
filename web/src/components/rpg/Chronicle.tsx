import { ChevronDown, Coins, Swords } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import { formatDate } from '../../lib/format'
import type { Encounter } from '../../lib/rpg'
import { useChronicle } from '../../lib/rpgQueries'
import { DiceRoll } from './DiceRoll'

const STATUS_TONE: Record<string, string> = {
  won: 'text-gold',
  lost: 'text-rose',
  fled: 'text-ink-faint',
}

export function Chronicle() {
  const chronicle = useChronicle()

  if (chronicle.isLoading) {
    return <div className="panel h-72 animate-pulse rounded-2xl opacity-60" />
  }

  const summary = chronicle.data?.summary
  const encounters = chronicle.data?.encounters ?? []

  return (
    <div className="space-y-4" data-testid="chronicle">
      <header>
        <h2 className="font-display text-2xl">Chronicle</h2>
        <p className="mt-0.5 text-[13px] text-ink-muted">
          Every fight you have finished, roll by roll.
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

      {encounters.length === 0 ? (
        <div className="panel rounded-2xl px-6 py-12 text-center">
          <p className="font-display text-lg">No stories yet</p>
          <p className="mt-1 text-[13px] text-ink-muted">
            Finish a fight and it will be written down here.
          </p>
        </div>
      ) : (
        <ul className="space-y-2">
          {encounters.map((encounter) => (
            <ChronicleEntry key={encounter.id} encounter={encounter} />
          ))}
        </ul>
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

function ChronicleEntry({ encounter }: { encounter: Encounter }) {
  const [open, setOpen] = useState(false)

  return (
    <li className="panel overflow-hidden rounded-xl" data-testid="chronicle-entry">
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        aria-expanded={open}
        className="flex w-full items-center gap-3 px-4 py-3 text-left transition hover:bg-surface-sunk"
      >
        <Swords size={14} className="shrink-0 text-ink-faint" />

        <span className="min-w-0 flex-1">
          <span className="block text-[14px]">{encounter.monsterName}</span>
          <span className="tabular block text-[11px] text-ink-faint">
            {formatDate(encounter.startedAt)} · {encounter.round}{' '}
            {encounter.round === 1 ? 'round' : 'rounds'}
          </span>
        </span>

        {encounter.goldAwarded > 0 && (
          <span className="tabular flex shrink-0 items-center gap-1 text-[11.5px] text-gold">
            <Coins size={11} />
            {encounter.goldAwarded}
          </span>
        )}

        <span
          className={`shrink-0 text-[10px] font-medium uppercase tracking-[0.14em] ${
            STATUS_TONE[encounter.status] ?? 'text-ink-faint'
          }`}
        >
          {encounter.status}
        </span>

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
