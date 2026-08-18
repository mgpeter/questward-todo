import { Eraser } from 'lucide-react'
import { AnimatePresence } from 'motion/react'
import { useRef, useState, type DragEvent } from 'react'
import { taskProgressLabels, taskProgressOrder, type Task, type TaskProgress } from '../lib/api'
import { useGameFeed } from '../game/GameFeed'
import { useClearCompleted, useReorderTasks, useSetTaskStatus } from '../lib/queries'
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

/** Where it would land: a column, and the card it would be inserted before. */
interface DropTarget {
  column: TaskProgress
  beforeId: string | null
}

/**
 * How many finished tasks the Done column draws before it stops.
 *
 * A repeating task spawns a successor on every completion (DEC-015), so a daily kept for a
 * year is 365 finished rows in one column. They are worth keeping, since the record panel is
 * computed from them, but nobody is scrolling to last March. The same cap and the same
 * "and N more" line as the contract board.
 *
 * Only Done is capped. The other two columns are work outstanding, and hiding some of that
 * would be hiding the point of the app.
 */
const DONE_LIMIT = 20

/**
 * Days of finished work the record panel draws, and therefore the floor on clearing.
 *
 * Mirrors StatsEndpoints.TrendDays. The server clamps to its own copy whatever is asked for,
 * so this is only here to decide whether the button is worth offering at all.
 */
const KEPT_DAYS = 14

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
  // Refs, not state, are what the drag actually runs on.
  //
  // A drop only happens if the preceding dragover called preventDefault, and dragover can
  // fire in the same tick as dragstart. Reading React state there sees null, skips the
  // preventDefault and the browser silently rejects the drop: dragstart, dragover,
  // dragend, no drop event at all. A slow drag survives it because later dragovers see the
  // committed state; a quick flick does not, which made short reorders fail at random.
  // The state below is duplicated for rendering only.
  const dragRef = useRef<DragState | null>(null)
  const overRef = useRef<DropTarget | null>(null)
  const [drag, setDrag] = useState<DragState | null>(null)
  const [over, setOver] = useState<DropTarget | null>(null)
  const setStatus = useSetTaskStatus()
  const reorder = useReorderTasks()
  const clear = useClearCompleted()
  const { celebrateStatusChange } = useGameFeed()

  const total = taskProgressOrder.reduce((sum, key) => sum + columns[key].length, 0)

  // Only what the record has already stopped drawing. Offering to clear anything newer would
  // be offering to blank the activity chart, which is the one thing this must not do.
  const clearableBefore = Date.now() - KEPT_DAYS * 24 * 60 * 60 * 1000
  const clearable = columns.completed.filter(
    (task) => task.completedAt !== null && Date.parse(task.completedAt) < clearableBefore,
  ).length

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

  /** Records where a drop would land, and allows it. Both halves must happen every time. */
  const trackOver = (event: DragEvent, target: DropTarget) => {
    if (!dragRef.current) return

    event.preventDefault()

    const current = overRef.current
    if (current?.column === target.column && current.beforeId === target.beforeId) return

    overRef.current = target
    setOver(target)
  }

  const endDrag = () => {
    dragRef.current = null
    overRef.current = null
    setDrag(null)
    setOver(null)
  }

  const drop = (column: TaskProgress) => {
    const active = dragRef.current
    const target = overRef.current
    if (!active) return

    const dragged = columns[active.from].find((task) => task.id === active.id)
    const beforeId = target?.column === column ? target.beforeId : null

    endDrag()

    if (!dragged) return

    if (active.from !== column) {
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
        const all = columns[column]
        const tasks = column === 'completed' ? all.slice(0, DONE_LIMIT) : all
        const hidden = all.length - tasks.length
        const isTarget = over?.column === column

        return (
          <section
            key={column}
            data-testid={column === 'completed' ? 'completed-section' : `column-${column}`}
            aria-label={taskProgressLabels[column]}
            onDragOver={(event: DragEvent) => trackOver(event, { column, beforeId: null })}
            onDrop={(event: DragEvent) => {
              event.preventDefault()
              drop(column)
            }}
            className={`rounded-2xl border border-dashed p-2 transition ${
              isTarget ? 'border-gold bg-gold/5' : 'border-transparent'
            }`}
          >
            <h3 className="mb-2 flex items-center gap-2 px-1 text-[10px] font-medium tracking-[0.18em] text-ink-faint uppercase">
              <span className={`h-1.5 w-1.5 rounded-full ${COLUMN_ACCENT[column]}`} />
              {taskProgressLabels[column]}
              <span className="tabular">{all.length}</span>
              <span className="h-px flex-1 bg-line" />
              {column === 'completed' && clearable > 0 && (
                <button
                  type="button"
                  onClick={() => clear.mutate()}
                  disabled={clear.isPending}
                  data-testid="clear-completed"
                  title={`Delete ${clearable} finished ${clearable === 1 ? 'task' : 'tasks'} older than ${KEPT_DAYS} days. The record panel only reaches back ${KEPT_DAYS} days, so nothing it shows will change.`}
                  className="flex items-center gap-1 rounded px-1.5 py-0.5 text-[9.5px] tracking-normal text-ink-faint normal-case transition hover:bg-surface-sunk hover:text-ink-muted disabled:opacity-40"
                >
                  <Eraser size={10} />
                  Clear {clearable}
                </button>
              )}
            </h3>

            <ul className="space-y-2">
              <AnimatePresence initial={false} mode="popLayout">
                {tasks.map((task) => (
                  <TaskCard
                    key={task.id}
                    task={task}
                    isDragging={drag?.id === task.id}
                    showDropLine={isTarget && over?.beforeId === task.id}
                    onDragStart={() => {
                      dragRef.current = { id: task.id, from: column }
                      setDrag({ id: task.id, from: column })
                    }}
                    onDragEnd={endDrag}
                    onDragOverCard={(event) => trackOver(event, { column, beforeId: task.id })}
                  />
                ))}
              </AnimatePresence>
            </ul>

            {hidden > 0 && (
              <p
                className="px-1 pt-2.5 text-center text-[11px] text-ink-faint"
                data-testid="done-hidden"
              >
                and {hidden.toLocaleString()} more finished
              </p>
            )}

            {all.length === 0 && (
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
