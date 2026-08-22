import { Search, SlidersHorizontal, X } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import type { Difficulty, TaskStatus } from '../lib/api'
import { DIFFICULTIES } from '../lib/difficulty'
import { useTags, type TaskFilters } from '../lib/queries'
import { useIsMobile } from '../lib/useMediaQuery'
import { Sheet } from './Sheet'

interface FilterBarProps {
  filters: TaskFilters
  counts: { open: number; done: number }
  onChange: (filters: TaskFilters) => void
}

const SHEET_LABEL = 'mt-4 mb-2.5 text-[10px] tracking-[0.18em] text-ink-faint uppercase'

const STATUSES: { value: TaskStatus; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'open', label: 'Open' },
  { value: 'done', label: 'Done' },
]

export function FilterBar({ filters, counts, onChange }: FilterBarProps) {
  const isMobile = useIsMobile()
  const tags = useTags()
  const [sheetOpen, setSheetOpen] = useState(false)
  const [focusSearch, setFocusSearch] = useState(false)

  const countFor = (status: TaskStatus) =>
    status === 'open' ? counts.open : status === 'done' ? counts.done : counts.open + counts.done

  const toggleDifficulty = (value: Difficulty) =>
    onChange({ ...filters, difficulty: filters.difficulty === value ? undefined : value })

  // Four control groups and a search field is one wrapping line on a board and four on a
  // phone, above a list you came here to read. Two buttons and a sheet instead.
  if (isMobile) {
    return (
      <div className="flex items-center gap-2" data-testid="filter-bar">
        <div className="flex items-center gap-0.5 rounded-xl border border-line bg-surface-sunk p-[3px]">
          {STATUSES.filter((status) => status.value !== 'all').map((status) => {
            const active = filters.status === status.value

            return (
              <button
                key={status.value}
                type="button"
                aria-pressed={active}
                data-testid={`filter-${status.value}`}
                onClick={() => onChange({ ...filters, status: active ? 'all' : status.value })}
                className={`min-h-11 rounded-lg px-3 text-[12px] font-medium transition ${
                  active ? 'bg-surface text-ink shadow-[0_1px_2px_rgb(0_0_0/0.1)]' : 'text-ink-faint'
                }`}
              >
                {status.label}
                <span className="tabular ml-1.5 text-[10.5px] opacity-60">
                  {countFor(status.value)}
                </span>
              </button>
            )
          })}
        </div>

        <IconButton
          label="Search tasks"
          active={Boolean(filters.search)}
          onClick={() => {
            setFocusSearch(true)
            setSheetOpen(true)
          }}
        >
          <Search size={16} />
        </IconButton>

        <IconButton
          label="Filter tasks"
          active={Boolean(filters.difficulty || filters.tag)}
          testId="filter-open"
          onClick={() => {
            setFocusSearch(false)
            setSheetOpen(true)
          }}
        >
          <SlidersHorizontal size={16} />
        </IconButton>

        <FilterSheet
          open={sheetOpen}
          onClose={() => setSheetOpen(false)}
          autoFocusSearch={focusSearch}
          filters={filters}
          counts={counts}
          onChange={onChange}
        />
      </div>
    )
  }

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

      {/* Only rendered once there is something to filter by: an empty row of chips is a
          control that looks broken rather than one that looks unused. */}
      {(tags.data?.length ?? 0) > 0 && (
        <div className="flex items-center gap-1" data-testid="filter-tags">
          {tags.data!.map((tag) => {
            const active = filters.tag === tag

            return (
              <button
                key={tag}
                type="button"
                aria-pressed={active}
                title={`Only tasks tagged ${tag}`}
                data-testid={`filter-tag-${tag}`}
                onClick={() => onChange({ ...filters, tag: active ? undefined : tag })}
                className={`rounded-full px-2 py-0.5 text-[10.5px] transition ${
                  active
                    ? 'bg-ink text-canvas'
                    : 'bg-surface-sunk text-ink-faint hover:text-ink-muted'
                }`}
              >
                {tag}
              </button>
            )
          })}
        </div>
      )}

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

function IconButton({
  label,
  active,
  onClick,
  testId,
  children,
}: {
  label: string
  active: boolean
  onClick: () => void
  testId?: string
  children: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      data-testid={testId}
      // 44px, not the 40px the mockup drew. Everything else on this screen clears the touch
      // minimum and two buttons sitting 4px under it is the kind of exception that spreads.
      className={`grid h-11 w-11 shrink-0 place-items-center rounded-xl border transition ${
        active ? 'border-gold bg-gold/10 text-gold' : 'border-line bg-surface text-ink-muted'
      }`}
    >
      {children}
    </button>
  )
}

/**
 * The whole desktop bar as one sheet, with the count of what survives before you commit.
 *
 * Filter state stays where it was, in TasksView: this only ever calls the same onChange the
 * inline bar does, so the list behind updates live and the footer count is the real answer
 * rather than a prediction.
 */
function FilterSheet({
  open,
  onClose,
  autoFocusSearch,
  filters,
  counts,
  onChange,
}: FilterBarProps & { open: boolean; onClose: () => void; autoFocusSearch: boolean }) {
  const tags = useTags()

  const matches = counts.open + counts.done
  const cleared: TaskFilters = { status: 'all', search: '' }
  const dirty =
    filters.status !== 'all' ||
    Boolean(filters.difficulty) ||
    Boolean(filters.tag) ||
    filters.search.trim().length > 0

  const countFor = (status: TaskStatus) =>
    status === 'open' ? counts.open : status === 'done' ? counts.done : counts.open + counts.done

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title="Filter and search"
      testId="filter-sheet"
      autoFocusContent={autoFocusSearch}
      footer={
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => onChange(cleared)}
            disabled={!dirty}
            data-testid="filter-sheet-clear"
            className="min-h-11 w-28 shrink-0 rounded-xl border border-line py-3.5 text-[14px] text-ink-muted transition disabled:opacity-40"
          >
            Clear all
          </button>
          <button
            type="button"
            onClick={onClose}
            className="tabular min-h-11 flex-1 rounded-xl bg-ink py-3.5 text-[14px] font-medium text-canvas transition"
          >
            Show {matches} {matches === 1 ? 'match' : 'matches'}
          </button>
        </div>
      }
    >
      <label className="relative block pt-1">
        <Search
          size={16}
          className="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-ink-faint"
        />
        <input
          value={filters.search}
          onChange={(event) => onChange({ ...filters, search: event.target.value })}
          placeholder="Search"
          aria-label="Search tasks"
          data-testid="filter-search"
          className="w-full rounded-xl border border-gold bg-canvas py-3 pr-10 pl-10 text-[15px] outline-none placeholder:text-ink-faint"
        />
        {filters.search && (
          <button
            type="button"
            onClick={() => onChange({ ...filters, search: '' })}
            aria-label="Clear search"
            className="absolute top-1/2 right-1 grid h-11 w-11 -translate-y-1/2 place-items-center text-ink-faint"
          >
            <X size={15} />
          </button>
        )}
      </label>

      <p className={SHEET_LABEL}>Status</p>
      <div className="flex items-stretch gap-[3px] rounded-xl border border-line bg-surface-sunk p-[3px]">
        {STATUSES.map((status) => {
          const active = filters.status === status.value

          return (
            <button
              key={status.value}
              type="button"
              aria-pressed={active}
              data-testid={`filter-${status.value}`}
              onClick={() => onChange({ ...filters, status: status.value })}
              className={`min-h-11 flex-1 rounded-lg py-2.5 text-[13px] transition ${
                active
                  ? 'bg-surface font-medium text-ink shadow-[0_1px_2px_rgb(0_0_0/0.12)]'
                  : 'text-ink-faint'
              }`}
            >
              {status.label}
              <span className="tabular ml-1.5 text-[11px] opacity-60">
                {countFor(status.value)}
              </span>
            </button>
          )
        })}
      </div>

      <p className={SHEET_LABEL}>Difficulty</p>
      <div className="grid grid-cols-4 gap-1.5">
        {DIFFICULTIES.map((meta) => {
          const active = filters.difficulty === meta.value

          return (
            <button
              key={meta.value}
              type="button"
              aria-pressed={active}
              data-testid={`filter-difficulty-${meta.value}`}
              onClick={() =>
                onChange({ ...filters, difficulty: active ? undefined : meta.value })
              }
              className={`${meta.tierClass} min-h-11 rounded-full py-2.5 text-center text-[12.5px] transition ${
                active ? 'tier-chip font-medium' : 'border border-line text-ink-muted'
              }`}
            >
              {meta.label}
            </button>
          )
        })}
      </div>

      {(tags.data?.length ?? 0) > 0 && (
        <>
          <p className={SHEET_LABEL}>Tags</p>
          <div className="flex flex-wrap gap-1.5 pb-2" data-testid="filter-tags">
            {tags.data!.map((tag) => {
              const active = filters.tag === tag

              return (
                <button
                  key={tag}
                  type="button"
                  aria-pressed={active}
                  data-testid={`filter-tag-${tag}`}
                  onClick={() => onChange({ ...filters, tag: active ? undefined : tag })}
                  className={`min-h-11 rounded-full px-3.5 text-[12.5px] transition ${
                    active ? 'bg-ink text-canvas' : 'border border-line text-ink-muted'
                  }`}
                >
                  {tag}
                </button>
              )
            })}
          </div>
        </>
      )}
    </Sheet>
  )
}
