import { useState } from 'react'
import { AdventurerStrip } from '../components/AdventurerStrip'
import { FilterBar } from '../components/FilterBar'
import { QuickAdd } from '../components/QuickAdd'
import { TaskBoard } from '../components/TaskBoard'
import { groupByStatus, useTasks, type TaskFilters } from '../lib/queries'
import { useIsMobile } from '../lib/useMediaQuery'

export function TasksView() {
  const isMobile = useIsMobile()
  const [filters, setFilters] = useState<TaskFilters>({ status: 'all', search: '' })
  const tasks = useTasks(filters)
  const columns = groupByStatus(tasks.data)

  const hasFilters =
    filters.status !== 'all' ||
    Boolean(filters.difficulty) ||
    Boolean(filters.tag) ||
    filters.search.trim().length > 0

  return (
    <div className="space-y-5">
      {/*
        The adventurer sits above the board rather than behind a tab. The whole design
        rests on tasks feeding the character (DEC-003), and that link is invisible if you
        have to navigate away to see the character it feeds.
      */}
      <AdventurerStrip />

      {/* On a phone this is the add sheet, opened from the bottom bar. Inline it would be
          a permanent 90px of form above a list you came here to read. */}
      {!isMobile && <QuickAdd />}

      <FilterBar
        filters={filters}
        counts={{
          open: columns.todo.length + columns.inProgress.length,
          done: columns.completed.length,
        }}
        onChange={setFilters}
      />

      {tasks.isError && (
        <p role="alert" className="panel rounded-xl p-4 text-[13px] text-rose">
          Could not load tasks: {(tasks.error as Error).message}
        </p>
      )}

      <TaskBoard columns={columns} isLoading={tasks.isLoading} hasFilters={hasFilters} />
    </div>
  )
}
