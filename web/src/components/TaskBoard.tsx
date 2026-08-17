import { AnimatePresence } from 'motion/react'
import { useState, type DragEvent } from 'react'
import { taskProgressLabels, taskProgressOrder, type Task, type TaskProgress } from '../lib/api'
import { useGameFeed } from '../game/GameFeed'
import { useReorderTasks, useSetTaskStatus } from '../lib/queries'
import { TaskCard } from './TaskCard'

interface TaskBoardProps {
  columns: Record<TaskProgress, Task[]>
  isLoading: boolean
  hasFilters: boolean
}

/** What is currently being dragged, and where it came from. */
interface DragState {
  id: string
  from: TaskProgress
}

const COLUMN_ACCENT: Record<TaskProgress, string> = {
  todo: 'bg-line-strong',
  inProgress: 'bg-gold',
  completed: 'bg-teal',
}

/**
 * Three columns, drag between them.
 *
 * Dragging is HTML5 native rather than a library: the board only needs "which card, which
 * column, before which sibling", and that is the one thing the native API does well.
 * Every move it can make is also reachable from the keyboard through the arrows on each
 * card, so drag is an accelerator and never the only way through.
 */
export function TaskBoard({ columns, isLoading, hasFilters }: TaskBoardProps) {
  const [drag, setDrag] = useState<DragState | null>(null)
  const [over, setOver] = useState<{ column: TaskProgress; beforeId: string | null } | null>(null)
  const setStatus = useSetTaskStatus()
  const reorder = useReorderTasks()
  const { celebrateStatusChange } = useGameFeed()

  const total = taskProgressOrder.reduce((sum, key) => sum + columns[key].length, 0)

  if (isLoading) {
    return (
      <div className="grid gap-3 sm:grid-cols-3" aria-busy="true" data-testid="task-list-loading">
        {taskProgressOrder.map((key) => (
          <div key={key} className="space-y-2">
            <div className="panel h-[74px] animate-pulse rounded-xl opacity-60" />
            <div className="panel h-[74px] animate-pulse rounded-xl opacity-40" />
          </div>
        ))}
      </div>
    )
  }

  if (total === 0) {
    return (
      <div className="panel rounded-2xl px-6 py-14 text-center" data-testid="task-list-empty">
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

  /** Sends the manual order for both open columns, so indices stay globally coherent. */
  const commitOrder = (next: Record<TaskProgress, Task[]>) =>
    reorder.mutate([...next.todo, ...next.inProgress].map((task) => task.id))

  const drop = (column: TaskProgress, beforeId: string | null) => {
    if (!drag) return

    const dragged = columns[drag.from].find((task) => task.id === drag.id)
    setDrag(null)
    setOver(null)

    if (!dragged) return

    if (drag.from !== column) {
      // Crossing into or out of Done moves XP, so it goes through the status route and
      // the result is fed to the same celebration the checkbox uses.
      setStatus.mutate(
        { id: dragged.id, status: column },
        { onSuccess: (result) => celebrateStatusChange(result) },
      )
      return
    }

    if (column === 'completed') return

    const without = columns[column].filter((task) => task.id !== dragged.id)
    const index = beforeId ? without.findIndex((task) => task.id === beforeId) : without.length
    const reordered = [...without]
    reordered.splice(index < 0 ? without.length : index, 0, dragged)

    commitOrder({ ...columns, [column]: reordered })
  }

  return (
    <div className="grid gap-3 sm:grid-cols-3" data-testid="task-list">
      {taskProgressOrder.map((column) => {
        const tasks = columns[column]
        const isTarget = over?.column === column

        return (
          <section
            key={column}
            data-testid={column === 'completed' ? 'completed-section' : `column-${column}`}
            aria-label={taskProgressLabels[column]}
            onDragOver={(event: DragEvent) => {
              if (!drag) return
              event.preventDefault()
              setOver({ column, beforeId: null })
            }}
            onDrop={(event: DragEvent) => {
              event.preventDefault()
              drop(column, over?.column === column ? over.beforeId : null)
            }}
            className={`rounded-2xl border border-dashed p-2 transition ${
              isTarget ? 'border-gold bg-gold/5' : 'border-transparent'
            }`}
          >
            <h3 className="mb-2 flex items-center gap-2 px-1 text-[10px] font-medium tracking-[0.18em] text-ink-faint uppercase">
              <span className={`h-1.5 w-1.5 rounded-full ${COLUMN_ACCENT[column]}`} />
              {taskProgressLabels[column]}
              <span className="tabular">{tasks.length}</span>
              <span className="h-px flex-1 bg-line" />
            </h3>

            <ul className="space-y-2">
              <AnimatePresence initial={false} mode="popLayout">
                {tasks.map((task) => (
                  <TaskCard
                    key={task.id}
                    task={task}
                    isDragging={drag?.id === task.id}
                    showDropLine={isTarget && over?.beforeId === task.id}
                    onDragStart={() => setDrag({ id: task.id, from: column })}
                    onDragEnd={() => {
                      setDrag(null)
                      setOver(null)
                    }}
                    onDragOverCard={() => {
                      if (drag) setOver({ column, beforeId: task.id })
                    }}
                  />
                ))}
              </AnimatePresence>
            </ul>

            {tasks.length === 0 && (
              <p className="px-1 py-6 text-center text-[11.5px] text-ink-faint">
                {column === 'completed' ? 'Nothing finished yet' : 'Drop a task here'}
              </p>
            )}
          </section>
        )
      })}
    </div>
  )
}
