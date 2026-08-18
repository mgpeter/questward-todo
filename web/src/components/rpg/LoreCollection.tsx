import { BookOpen, ChevronDown, Lock } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import type { LoreFragment, LorePlace } from '../../lib/rpg'
import { useLore } from '../../lib/rpgQueries'

type Filter = 'all' | 'found'

/**
 * The lore collection, place by place.
 *
 * Nothing here is stored: a fragment opens because the codex, the level or a claimed quest
 * says so, which is why a locked one can still name itself and say exactly what would open
 * it. A title with a requirement under it is an errand; a blank space is nothing at all.
 */
export function LoreCollection() {
  const lore = useLore()
  const [filter, setFilter] = useState<Filter>('all')
  const [toggled, setToggled] = useState<Record<string, boolean>>({})

  if (lore.isLoading) {
    return <div className="panel h-72 animate-pulse rounded-2xl opacity-60" />
  }

  if (lore.isError || !lore.data) {
    return (
      <p role="alert" className="panel rounded-xl p-4 text-[13px] text-rose">
        Could not open the lore: {(lore.error as Error)?.message ?? 'unknown error'}
      </p>
    )
  }

  const { places, unlocked, total } = lore.data
  const percent = Math.round((unlocked / Math.max(1, total)) * 100)

  // The first place is open on arrival so the panel never reads as an empty list of doors.
  const defaultOpen = places[0]?.key

  return (
    <div className="space-y-4" data-testid="lore" data-unlocked={unlocked}>
      <header>
        <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
          <h2 className="font-display text-2xl">Lore</h2>
          <p className="tabular text-[12px] text-ink-faint">
            <span className="text-gold">{unlocked}</span> / {total} found
            <span className="ml-2">{total - unlocked} left</span>
          </p>
        </div>
        <p className="mt-0.5 text-[13px] text-ink-muted">
          Field notes, ledgers and rules boards, left where they were written. Fighting and
          levelling turn them up.
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

      <div className="flex flex-wrap items-center gap-0.5 rounded-lg border border-line bg-surface-sunk p-0.5">
        {(['all', 'found'] as Filter[]).map((key) => (
          <button
            key={key}
            type="button"
            aria-pressed={filter === key}
            onClick={() => setFilter(key)}
            data-testid={`lore-filter-${key}`}
            className={`flex-1 rounded-md px-2.5 py-1.5 text-[11.5px] font-medium transition ${
              filter === key
                ? 'bg-surface text-ink shadow-[0_1px_2px_rgb(0_0_0/0.1)]'
                : 'text-ink-faint hover:text-ink-muted'
            }`}
          >
            {key === 'all' ? 'Everything' : 'Found only'}
            <span className="tabular ml-1.5 text-[10px] opacity-60">
              {key === 'all' ? total : unlocked}
            </span>
          </button>
        ))}
      </div>

      <ul className="space-y-2">
        {places.map((place) => (
          <Place
            key={place.key}
            place={place}
            filter={filter}
            open={toggled[place.key] ?? place.key === defaultOpen}
            onToggle={() =>
              setToggled((current) => ({
                ...current,
                [place.key]: !(current[place.key] ?? place.key === defaultOpen),
              }))
            }
          />
        ))}
      </ul>
    </div>
  )
}

function Place({
  place,
  filter,
  open,
  onToggle,
}: {
  place: LorePlace
  filter: Filter
  open: boolean
  onToggle: () => void
}) {
  const shown =
    filter === 'found' ? place.fragments.filter((fragment) => fragment.isUnlocked) : place.fragments

  return (
    <li
      className="panel overflow-hidden rounded-xl"
      data-testid="lore-place"
      data-place={place.key}
      data-unlocked={place.unlocked}
    >
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        className="flex w-full items-center gap-3 px-4 py-3 text-left transition hover:bg-surface-sunk"
      >
        <BookOpen
          size={14}
          className={`shrink-0 ${place.unlocked > 0 ? 'text-gold' : 'text-ink-faint'}`}
        />

        <span className="min-w-0 flex-1">
          <span className="block font-display text-[15px] leading-tight">{place.name}</span>
          <span className="block text-[11px] leading-snug text-ink-muted">{place.blurb}</span>
        </span>

        <span className="tabular shrink-0 text-[11px] text-ink-faint">
          <span className={place.unlocked > 0 ? 'text-gold' : ''}>{place.unlocked}</span>/
          {place.total}
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
            {shown.length === 0 ? (
              <p className="px-4 py-4 text-[12px] text-ink-faint">
                Nothing found here yet. Fight what lives nearby and the pages turn up.
              </p>
            ) : (
              <ul className="divide-y divide-line">
                {shown.map((fragment) => (
                  <FragmentRow key={fragment.key} fragment={fragment} />
                ))}
              </ul>
            )}
          </motion.div>
        )}
      </AnimatePresence>
    </li>
  )
}

function FragmentRow({ fragment }: { fragment: LoreFragment }) {
  return (
    <li
      className="px-4 py-3"
      data-testid="lore-fragment"
      data-fragment={fragment.key}
      data-unlocked={fragment.isUnlocked}
    >
      <h4
        className={`font-display text-[14px] leading-tight ${
          fragment.isUnlocked ? '' : 'text-ink-faint'
        }`}
      >
        {fragment.title}
      </h4>

      {fragment.isUnlocked ? (
        <p className="mt-1 text-[12.5px] leading-relaxed text-ink-muted">{fragment.body}</p>
      ) : (
        <p className="mt-1 flex items-center gap-1.5 text-[11.5px] text-ink-faint">
          <Lock size={11} className="shrink-0" />
          <span className="min-w-0">{fragment.requirement}</span>
        </p>
      )}
    </li>
  )
}
