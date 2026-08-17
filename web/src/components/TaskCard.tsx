import { Check, ChevronLeft, ChevronRight, GripVertical, Pencil, Plus, Repeat, Trash2 } from 'lucide-react'
import { motion } from 'motion/react'
import { useState, type KeyboardEvent, type MouseEvent } from 'react'
import {
  recurrenceLabels,
  taskProgressLabels,
  taskProgressOrder,
  type Task,
  type TaskProgress,
} from '../lib/api'
import { difficultyMeta } from '../lib/difficulty'
import { describeDue } from '../lib/format'
import {
  useCompleteTask,
  useCreateTask,
  useDeleteTask,
  useReopenTask,
  useSetTaskStatus,
} from '../lib/queries'
import { useGameFeed } from '../game/GameFeed'
import { TaskEditor } from './TaskEditor'

const DUE_TONE_CLASS: Record<string, string> = {
  overdue: 'text-rose border-rose/35 bg-rose/8',
  today: 'text-tier-hard border-tier-hard/35 bg-tier-hard/8',
  soon: 'text-ink-muted border-line',
  future: 'text-ink-faint border-line',
}

interface TaskCardProps {
  task: Task
  isDragging?: boolean
  showDropLine?: boolean
  onDragStart?: () => void
  onDragEnd?: () => void
  onDragOverCard?: () => void
}

export function TaskCard({
  task,
  isDragging = false,
  showDropLine = false,
  onDragStart,
  onDragEnd,
  onDragOverCard,
}: TaskCardProps) {
  const [editing, setEditing] = useState(false)
  const [addingSubtask, setAddingSubtask] = useState(false)
  const [subtaskTitle, setSubtaskTitle] = useState('')
  const completeTask = useCompleteTask()
  const reopenTask = useReopenTask()
  const deleteTask = useDeleteTask()
  const createTask = useCreateTask()
  const setStatus = useSetTaskStatus()
  const { celebrateCompletion, registerRefund, celebrateStatusChange } = useGameFeed()

  const meta = difficultyMeta(task.difficulty)
  const due = describeDue(task.dueDate)
  const busy = completeTask.isPending || reopenTask.isPending || setStatus.isPending

  const doneSubtasks = task.subtasks.filter((subtask) => subtask.isCompleted).length

  const toggle = (event: MouseEvent<HTMLButtonElement>) => {
    if (busy) return

    // The button's position seeds the floating "+25 XP", so the number rises
    // from the thing that earned it rather than from the middle of the screen.
    const origin = event.currentTarget.getBoundingClientRect()

    if (task.isCompleted) {
      reopenTask.mutate(task.id, { onSuccess: (result) => registerRefund(result, origin) })
    } else {
      completeTask.mutate(task.id, {
        onSuccess: (result) => celebrateCompletion(result, origin),
      })
    }
  }

  /** The keyboard equivalent of dragging the card one column left or right. */
  const shift = (event: MouseEvent<HTMLButtonElement>, direction: -1 | 1) => {
    const index = taskProgressOrder.indexOf(task.status)
    const next = taskProgressOrder[index + direction]
    if (!next || busy) return

    const origin = event.currentTarget.getBoundingClientRect()

    setStatus.mutate(
      { id: task.id, status: next },
      { onSuccess: (result) => celebrateStatusChange(result, origin) },
    )
  }

  const submitSubtask = () => {
    const trimmed = subtaskTitle.trim()
    if (!trimmed) return

    createTask.mutate(
      { title: trimmed, difficulty: task.difficulty, parentId: task.id },
      {
        onSuccess: () => {
          setSubtaskTitle('')
          setAddingSubtask(false)
        },
      },
    )
  }

  if (editing) {
    return <TaskEditor task={task} onClose={() => setEditing(false)} />
  }

  const canMoveLeft = taskProgressOrder.indexOf(task.status) > 0
  const canMoveRight = taskProgressOrder.indexOf(task.status) < taskProgressOrder.length - 1

  return (
    <motion.li
      layout
      draggable
      onDragStart={onDragStart}
      onDragEnd={onDragEnd}
      // stopPropagation, not decoration: dragover bubbles, and the column's own handler
      // resets the insert position to "end of list". Without this the drop line never
      // settles on a card and every reorder lands at the bottom.
      onDragOver={(event) => {
        event.preventDefault()
        event.stopPropagation()
        onDragOverCard?.()
      }}
      initial={{ opacity: 0, y: -6 }}
      animate={{ opacity: isDragging ? 0.4 : 1, y: 0 }}
      // Deliberately no exit animation: completing a task moves it between two lists,
      // and an exiting copy would leave the same task visible twice mid-transition.
      transition={{ type: 'spring', stiffness: 420, damping: 34 }}
      className={`${meta.tierClass} group panel relative flex items-start gap-2.5 overflow-hidden rounded-xl py-3 pr-3 pl-3.5 transition-shadow hover:shadow-lift ${
        task.isCompleted ? 'opacity-60' : ''
      } ${showDropLine ? 'shadow-[0_-2px_0_0_var(--color-gold)]' : ''}`}
      data-testid="task-card"
      data-task-title={task.title}
      data-completed={task.isCompleted}
      data-status={task.status}
    >
      <span
        aria-hidden="true"
        className="absolute inset-y-0 left-0 w-[3px]"
        style={{ backgroundColor: 'var(--tier)', opacity: task.isCompleted ? 0.3 : 0.85 }}
      />

      <span
        aria-hidden="true"
        title="Drag to move"
        className="mt-1 shrink-0 cursor-grab text-ink-faint opacity-0 transition-opacity group-hover:opacity-100 active:cursor-grabbing"
      >
        <GripVertical size={13} />
      </span>

      <button
        type="button"
        onClick={toggle}
        disabled={busy}
        aria-pressed={task.isCompleted}
        aria-label={task.isCompleted ? `Reopen ${task.title}` : `Complete ${task.title}`}
        data-testid="task-toggle"
        className={`mt-0.5 grid h-[22px] w-[22px] shrink-0 place-items-center rounded-full border-2 transition disabled:opacity-50 ${
          task.isCompleted
            ? 'border-teal bg-teal text-canvas'
            : 'border-line-strong hover:border-gold hover:bg-gold/10'
        }`}
      >
        {task.isCompleted && <Check size={13} strokeWidth={3.5} />}
      </button>

      <div className="min-w-0 flex-1">
        <p
          className={`text-[14.5px] leading-snug break-words ${
            task.isCompleted ? 'text-ink-muted line-through decoration-ink-faint' : ''
          }`}
        >
          {task.title}
        </p>

        {task.notes && (
          <p className="mt-1 text-[12.5px] leading-snug text-ink-muted">{task.notes}</p>
        )}

        <div className="mt-2 flex flex-wrap items-center gap-1.5">
          <span className="tier-chip rounded-full px-2 py-0.5 text-[10px] font-medium">
            {meta.label}
          </span>

          <span className="tabular text-[10px] text-ink-faint">
            {task.isCompleted ? `+${task.xpAwarded}` : `${meta.xp}`} XP
          </span>

          {task.recurrence !== 'none' && (
            <span
              data-testid="task-recurrence"
              className="flex items-center gap-1 rounded-full border border-line px-2 py-0.5 text-[10px] text-ink-muted"
            >
              <Repeat size={9} />
              {recurrenceLabels[task.recurrence]}
            </span>
          )}

          {task.priority === 'high' && !task.isCompleted && (
            <span className="rounded-full border border-rose/35 bg-rose/8 px-2 py-0.5 text-[10px] font-medium text-rose">
              High
            </span>
          )}

          {due.tone !== 'none' && !task.isCompleted && (
            <span
              data-testid="task-due"
              className={`rounded-full border px-2 py-0.5 text-[10px] ${DUE_TONE_CLASS[due.tone]}`}
            >
              {due.label}
            </span>
          )}

          {task.tags.map((tag) => (
            <span
              key={tag}
              data-testid="task-tag"
              className="rounded-full bg-surface-sunk px-2 py-0.5 text-[10px] text-ink-muted"
            >
              {tag}
            </span>
          ))}

          {/*
            Said plainly rather than discovered by watching the XP not move: a subtask,
            or a repeat inside its own period, is worth doing but is not worth XP.
          */}
          {!task.awardsProgression && !task.isCompleted && (
            <span
              data-testid="task-no-xp"
              title="Subtasks and repeats inside their period do not pay again."
              className="rounded-full border border-line px-2 py-0.5 text-[10px] text-ink-faint"
            >
              No XP
            </span>
          )}
        </div>

        {(task.subtasks.length > 0 || addingSubtask) && (
          <div className="mt-2.5 border-t border-line pt-2">
            {task.subtasks.length > 0 && (
              <p className="mb-1.5 text-[10px] tracking-[0.14em] text-ink-faint uppercase">
                Steps
                <span className="tabular ml-1.5">
                  {doneSubtasks}/{task.subtasks.length}
                </span>
              </p>
            )}

            <ul className="space-y-1" data-testid="subtask-list">
              {task.subtasks.map((subtask) => (
                <SubtaskRow key={subtask.id} subtask={subtask} />
              ))}
            </ul>

            {addingSubtask && (
              <input
                autoFocus
                value={subtaskTitle}
                maxLength={200}
                placeholder="Add a step"
                aria-label={`New step for ${task.title}`}
                data-testid="subtask-input"
                onChange={(event) => setSubtaskTitle(event.target.value)}
                onBlur={() => setAddingSubtask(false)}
                onKeyDown={(event: KeyboardEvent<HTMLInputElement>) => {
                  if (event.key === 'Enter') submitSubtask()
                  if (event.key === 'Escape') setAddingSubtask(false)
                }}
                className="mt-1.5 w-full rounded-md border border-line bg-canvas px-2 py-1 text-[12.5px] outline-none focus:border-gold"
              />
            )}
          </div>
        )}
      </div>

      <div className="flex shrink-0 items-center gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
        <button
          type="button"
          onClick={(event) => shift(event, -1)}
          disabled={!canMoveLeft || busy}
          aria-label={`Move ${task.title} to ${
            canMoveLeft ? taskProgressLabels[taskProgressOrder[taskProgressOrder.indexOf(task.status) - 1] as TaskProgress] : ''
          }`}
          data-testid="task-move-left"
          className="rounded-md p-1 text-ink-faint transition hover:bg-surface-sunk hover:text-ink disabled:invisible"
        >
          <ChevronLeft size={13} />
        </button>
        <button
          type="button"
          onClick={(event) => shift(event, 1)}
          disabled={!canMoveRight || busy}
          aria-label={`Move ${task.title} to ${
            canMoveRight ? taskProgressLabels[taskProgressOrder[taskProgressOrder.indexOf(task.status) + 1] as TaskProgress] : ''
          }`}
          data-testid="task-move-right"
          className="rounded-md p-1 text-ink-faint transition hover:bg-surface-sunk hover:text-ink disabled:invisible"
        >
          <ChevronRight size={13} />
        </button>
        <button
          type="button"
          onClick={() => setAddingSubtask(true)}
          aria-label={`Add a step to ${task.title}`}
          data-testid="task-add-subtask"
          className="rounded-md p-1 text-ink-faint transition hover:bg-surface-sunk hover:text-ink"
        >
          <Plus size={13} />
        </button>
        <button
          type="button"
          onClick={() => setEditing(true)}
          aria-label={`Edit ${task.title}`}
          data-testid="task-edit"
          className="rounded-md p-1 text-ink-faint transition hover:bg-surface-sunk hover:text-ink"
        >
          <Pencil size={13} />
        </button>
        <button
          type="button"
          onClick={() => deleteTask.mutate(task.id)}
          disabled={deleteTask.isPending}
          aria-label={`Delete ${task.title}`}
          data-testid="task-delete"
          className="rounded-md p-1 text-ink-faint transition hover:bg-rose/10 hover:text-rose"
        >
          <Trash2 size={13} />
        </button>
      </div>
    </motion.li>
  )
}

/**
 * A step inside a task. Ticking one is real progress and worth showing, but it pays
 * nothing, so it gets no XP float and no celebration.
 */
function SubtaskRow({ subtask }: { subtask: Task }) {
  const completeTask = useCompleteTask()
  const reopenTask = useReopenTask()
  const deleteTask = useDeleteTask()

  const busy = completeTask.isPending || reopenTask.isPending

  return (
    <li className="group/step flex items-center gap-2" data-testid="subtask">
      <button
        type="button"
        disabled={busy}
        onClick={() =>
          subtask.isCompleted
            ? reopenTask.mutate(subtask.id)
            : completeTask.mutate(subtask.id)
        }
        aria-pressed={subtask.isCompleted}
        aria-label={subtask.isCompleted ? `Reopen ${subtask.title}` : `Complete ${subtask.title}`}
        data-testid="subtask-toggle"
        className={`grid h-[15px] w-[15px] shrink-0 place-items-center rounded border transition disabled:opacity-50 ${
          subtask.isCompleted
            ? 'border-teal bg-teal text-canvas'
            : 'border-line-strong hover:border-gold'
        }`}
      >
        {subtask.isCompleted && <Check size={9} strokeWidth={4} />}
      </button>

      <span
        className={`min-w-0 flex-1 truncate text-[12.5px] ${
          subtask.isCompleted ? 'text-ink-faint line-through' : 'text-ink-muted'
        }`}
      >
        {subtask.title}
      </span>

      <button
        type="button"
        onClick={() => deleteTask.mutate(subtask.id)}
        aria-label={`Delete ${subtask.title}`}
        className="rounded p-0.5 text-ink-faint opacity-0 transition group-hover/step:opacity-100 hover:text-rose"
      >
        <Trash2 size={11} />
      </button>
    </li>
  )
}
