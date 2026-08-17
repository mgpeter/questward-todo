import { AnimatePresence } from 'motion/react'
import type { Task } from '../lib/api'
import { TaskCard } from './TaskCard'

interface TaskListProps {
  open: Task[]
  done: Task[]
  isLoading: boolean
  hasFilters: boolean
}

export function TaskList({ open, done, isLoading, hasFilters }: TaskListProps) {
  if (isLoading) {
    return (
      <div className="space-y-2" aria-busy="true" data-testid="task-list-loading">
        {[0, 1, 2].map((index) => (
          <div key={index} className="panel h-[74px] animate-pulse rounded-xl opacity-60" />
        ))}
      </div>
    )
  }

  if (open.length === 0 && done.length === 0) {
    return (
      <div
        className="panel rounded-2xl px-6 py-14 text-center"
        data-testid="task-list-empty"
      >
        <p className="font-display text-xl">
          {hasFilters ? 'Nothing matches' : 'The board is clear'}
        </p>
        <p className="mt-1.5 text-[13px] text-ink-muted">
          {hasFilters
            ? 'Try a different filter or search term.'
            : 'Add your first quest above and start earning XP.'}
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-6" data-testid="task-list">
      {open.length > 0 && (
        <section>
          <SectionHeading label="In progress" count={open.length} />
          <ul className="space-y-2">
            <AnimatePresence initial={false} mode="popLayout">
              {open.map((task) => (
                <TaskCard key={task.id} task={task} />
              ))}
            </AnimatePresence>
          </ul>
        </section>
      )}

      {done.length > 0 && (
        <section data-testid="completed-section">
          <SectionHeading label="Completed" count={done.length} />
          <ul className="space-y-2">
            <AnimatePresence initial={false} mode="popLayout">
              {done.map((task) => (
                <TaskCard key={task.id} task={task} />
              ))}
            </AnimatePresence>
          </ul>
        </section>
      )}
    </div>
  )
}

function SectionHeading({ label, count }: { label: string; count: number }) {
  return (
    <h3 className="mb-2 flex items-center gap-2 text-[10px] font-medium uppercase tracking-[0.18em] text-ink-faint">
      {label}
      <span className="tabular">{count}</span>
      <span className="h-px flex-1 bg-line" />
    </h3>
  )
}
