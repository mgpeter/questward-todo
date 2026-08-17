import { Search, X } from 'lucide-react'
import type { Difficulty, TaskStatus } from '../lib/api'
import { DIFFICULTIES } from '../lib/difficulty'
import type { TaskFilters } from '../lib/queries'

interface FilterBarProps {
  filters: TaskFilters
  counts: { open: number; done: number }
  onChange: (filters: TaskFilters) => void
}

const STATUSES: { value: TaskStatus; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'open', label: 'Open' },
  { value: 'done', label: 'Done' },
]

export function FilterBar({ filters, counts, onChange }: FilterBarProps) {
  const countFor = (status: TaskStatus) =>
    status === 'open' ? counts.open : status === 'done' ? counts.done : counts.open + counts.done

  const toggleDifficulty = (value: Difficulty) =>
    onChange({ ...filters, difficulty: filters.difficulty === value ? undefined : value })

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2" data-testid="filter-bar">
      <div className="flex items-center gap-0.5 rounded-lg border border-line bg-surface-sunk p-0.5">
        {STATUSES.map((status) => {
          const active = filters.status === status.value

          return (
            <button
              key={status.value}
              type="button"
              aria-pressed={active}
              data-testid={`filter-${status.value}`}
              onClick={() => onChange({ ...filters, status: status.value })}
              className={`rounded-md px-2.5 py-1 text-[11.5px] font-medium transition ${
                active
                  ? 'bg-surface text-ink shadow-[0_1px_2px_rgb(0_0_0/0.1)]'
                  : 'text-ink-faint hover:text-ink-muted'
              }`}
            >
              {status.label}
              <span className="tabular ml-1.5 text-[10px] opacity-60">{countFor(status.value)}</span>
            </button>
          )
        })}
      </div>

      <div className="flex items-center gap-1">
        {DIFFICULTIES.map((meta) => {
          const active = filters.difficulty === meta.value

          return (
            <button
              key={meta.value}
              type="button"
              aria-pressed={active}
              title={`Only ${meta.label} tasks`}
              data-testid={`filter-difficulty-${meta.value}`}
              onClick={() => toggleDifficulty(meta.value)}
              className={`${meta.tierClass} rounded-full px-2 py-0.5 text-[10.5px] font-medium transition ${
                active ? 'tier-chip' : 'border border-transparent text-ink-faint hover:text-ink-muted'
              }`}
            >
              {meta.label}
            </button>
          )
        })}
      </div>

      <label className="relative ml-auto">
        <Search
          size={13}
          className="pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2 text-ink-faint"
        />
        <input
          value={filters.search}
          onChange={(event) => onChange({ ...filters, search: event.target.value })}
          placeholder="Search"
          aria-label="Search tasks"
          data-testid="filter-search"
          className="w-36 rounded-lg border border-line bg-surface py-1.5 pr-7 pl-7 text-[12px] outline-none transition focus:w-48 focus:border-gold"
        />
        {filters.search && (
          <button
            type="button"
            onClick={() => onChange({ ...filters, search: '' })}
            aria-label="Clear search"
            className="absolute top-1/2 right-2 -translate-y-1/2 text-ink-faint hover:text-ink"
          >
            <X size={12} />
          </button>
        )}
      </label>
    </div>
  )
}
