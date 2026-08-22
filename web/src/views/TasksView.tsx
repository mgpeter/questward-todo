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

  // Fetched without the status filter, and narrowed below.
  //
  // The counts beside All, Open and Done have to say what each one would show. Asking the
  // server for the current status and then counting what came back made every tally agree
  // with the tab already chosen: on Open, Done read zero and All read the open count. The
  // server's status filter is `Completed` or `not Completed`, which is the same split
  // groupByStatus already performs, so doing it here costs nothing and answers all three.
  //
  // It also means switching tab no longer refetches: the query key stops changing with the
  // status, so the other two filters are the only thing that can move it.
  const tasks = useTasks({ ...filters, status: 'all' })
  const all = groupByStatus(tasks.data)

  const counts = {
    open: all.todo.length + all.inProgress.length,
    done: all.completed.length,
  }

  const columns =
    filters.status === 'open'
      ? { ...all, completed: [] }
      : filters.status === 'done'
        ? { todo: [], inProgress: [], completed: all.completed }
        : all

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

      <FilterBar filters={filters} counts={counts} onChange={setFilters} />

      {tasks.isError && (
        <p role="alert" className="panel rounded-xl p-4 text-[13px] text-rose">
          Could not load tasks: {(tasks.error as Error).message}
        </p>
      )}

      <TaskBoard columns={columns} isLoading={tasks.isLoading} hasFilters={hasFilters} />
    </div>
  )
}
