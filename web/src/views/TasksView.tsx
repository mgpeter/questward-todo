import { useState } from 'react'
import { FilterBar } from '../components/FilterBar'
import { QuickAdd } from '../components/QuickAdd'
import { TaskList } from '../components/TaskList'
import { partitionTasks, useTasks, type TaskFilters } from '../lib/queries'

export function TasksView() {
  const [filters, setFilters] = useState<TaskFilters>({ status: 'all', search: '' })
  const tasks = useTasks(filters)
  const { open, done } = partitionTasks(tasks.data)

  const hasFilters =
    filters.status !== 'all' || Boolean(filters.difficulty) || filters.search.trim().length > 0

  return (
    <div className="space-y-5">
      <QuickAdd />

      <FilterBar
        filters={filters}
        counts={{ open: open.length, done: done.length }}
        onChange={setFilters}
      />

      {tasks.isError && (
        <p role="alert" className="panel rounded-xl p-4 text-[13px] text-rose">
          Could not load tasks: {(tasks.error as Error).message}
        </p>
      )}

      <TaskList
        open={open}
        done={done}
        isLoading={tasks.isLoading}
        hasFilters={hasFilters}
      />
    </div>
  )
}
