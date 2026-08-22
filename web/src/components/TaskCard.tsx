import { Check, ChevronLeft, ChevronRight, GripVertical, Pencil, Plus, Repeat, Trash2 } from 'lucide-react'
import { motion } from 'motion/react'
import { useState, type DragEvent, type KeyboardEvent, type MouseEvent } from 'react'
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
import { useIsMobile } from '../lib/useMediaQuery'
import { TaskHuntSeal } from './rpg/TaskHuntSeal'
import { SubtaskRow } from './SubtaskRow'
import { TaskEditor } from './TaskEditor'
import { TaskSheet } from './TaskSheet'

const DUE_TONE_CLASS: Record<string, string> = {
  // Gold, not rose. An overdue task is a bounty and never a debuff (DEC-013), and a card
  // that turned red for having waited was the game telling the player off for the exact
  // thing it is about to pay them double to finish.
  overdue: 'text-gold border-gold/45 bg-gold/10',
  today: 'text-tier-hard border-tier-hard/35 bg-tier-hard/8',
  soon: 'text-ink-muted border-line',
  future: 'text-ink-faint border-line',
}

/** The same tones as DUE_TONE_CLASS, without the pill the mobile meta line does not draw. */
const DUE_TONE_TEXT: Record<string, string> = {
  overdue: 'text-gold',
  today: 'text-tier-hard',
  soon: 'text-ink-muted',
  future: 'text-ink-faint',
}

interface TaskCardProps {
  task: Task
  isDragging?: boolean
  showDropLine?: boolean
  onDragStart?: () => void
  onDragEnd?: () => void
  onDragOverCard?: (event: DragEvent<HTMLLIElement>) => void
}

export function TaskCard({
  task,
  isDragging = false,
  showDropLine = false,
  onDragStart,
  onDragEnd,
  onDragOverCard,
}: TaskCardProps) {
  const isMobile = useIsMobile()
  const [editing, setEditing] = useState(false)
  const [sheetOpen, setSheetOpen] = useState(false)
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

  if (isMobile) {
    return (
      <>
        <motion.li
          layout
          initial={{ opacity: 0, y: -6 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ type: 'spring', stiffness: 420, damping: 34 }}
          className={`${meta.tierClass} panel relative overflow-hidden rounded-xl`}
          data-testid="task-card"
          data-task-title={task.title}
          data-completed={task.isCompleted}
          data-status={task.status}
        >
          {task.isCompleted ? (
            // A finished quest is a receipt. The title, the tick and what it paid; every
            // chip it used to carry is answerable in the sheet if anyone asks.
            <div className="flex items-center gap-3 py-2.5 pr-3.5 pl-3">
              <button
                type="button"
                onClick={toggle}
                disabled={busy}
                aria-pressed
                aria-label={`Reopen ${task.title}`}
                data-testid="task-toggle"
                className="grid h-11 w-11 shrink-0 -my-2.5 place-items-center disabled:opacity-50"
              >
                <span className="grid h-6 w-6 place-items-center rounded-full bg-teal text-canvas">
                  <Check size={13} strokeWidth={3.5} />
                </span>
              </button>

              <button
                type="button"
                onClick={() => setSheetOpen(true)}
                data-testid="task-open"
                className="min-w-0 flex-1 truncate py-1 text-left text-[14px] text-ink-muted line-through decoration-ink-faint"
              >
                {task.title}
              </button>

              <span className="tabular shrink-0 text-[11px]" style={{ color: 'var(--tier)' }}>
                +{task.xpAwarded} XP
              </span>
            </div>
          ) : (
            <>
              <span
                aria-hidden="true"
                className="absolute inset-y-0 left-0 w-[3px]"
                style={{ backgroundColor: 'var(--tier)', opacity: 0.85 }}
              />

              <div className="flex items-center gap-3 py-3 pr-3 pl-3.5">
                {/* 26px of ring inside a 44px target. The negative margins keep the card the
                    height the ring implies rather than the height the target needs. */}
                <button
                  type="button"
                  onClick={toggle}
                  disabled={busy}
                  aria-pressed={false}
                  aria-label={`Complete ${task.title}`}
                  data-testid="task-toggle"
                  className="grid h-11 w-11 shrink-0 -my-3 -ml-1 place-items-center disabled:opacity-50"
                >
                  <span className="block h-[26px] w-[26px] rounded-full border-2 border-line-strong" />
                </button>

                <button
                  type="button"
                  onClick={() => setSheetOpen(true)}
                  data-testid="task-open"
                  className="min-w-0 flex-1 text-left"
                >
                  <span className="block text-[15.5px] leading-snug text-pretty">{task.title}</span>

                  {/* One line, and it never wraps. Priority, recurrence and the rest of the
                      tags are in the sheet: four wrapping chip rows was the old card's
                      whole height problem. */}
                  <span
                    className="mt-1.5 flex items-center gap-2 text-[11px] text-ink-faint"
                    data-testid="task-meta"
                  >
                    <span className="flex shrink-0 items-center gap-1.5">
                      <span
                        aria-hidden="true"
                        className="h-[7px] w-[7px] rounded-[2px]"
                        style={{ backgroundColor: 'var(--tier)' }}
                      />
                      {meta.label}
                    </span>

                    <span className="tabular shrink-0">{meta.xp} XP</span>

                    {due.tone !== 'none' && (
                      <span
                        data-testid="task-due"
                        className={`shrink-0 truncate ${DUE_TONE_TEXT[due.tone]}`}
                      >
                        {due.label}
                      </span>
                    )}

                    {task.tags.length > 0 && (
                      <span data-testid="task-tag" className="ml-auto min-w-0 truncate">
                        {task.tags[0]}
                      </span>
                    )}
                  </span>
                </button>
              </div>

              <TaskHuntSeal task={task} variant="strip" />
            </>
          )}
        </motion.li>

        <TaskSheet
          task={task}
          open={sheetOpen}
          onClose={() => setSheetOpen(false)}
          onEdit={() => {
            setSheetOpen(false)
            setEditing(true)
          }}
        />
      </>
    )
  }

  const canMoveLeft = taskProgressOrder.indexOf(task.status) > 0
  const canMoveRight = taskProgressOrder.indexOf(task.status) < taskProgressOrder.length - 1

  return (
    <motion.li
      layout
      draggable
      // Capture-phase handlers throughout, for two reasons that happen to agree.
      //
      // motion.li claims onDragStart/onDragEnd for its own pan gestures, so the native
      // names are typed as pan handlers and cannot carry a DragEvent. The *Capture names
      // are untouched and are plain DOM.
      //
      // Capture is also the phase this wants: dragover reaches the column before the card
      // on the way down, so stopping it here is what keeps "insert before this card" from
      // being overwritten by the column's "append to the end".
      onDragStartCapture={(event: DragEvent<HTMLLIElement>) => {
        // Chrome drags without this, but a drag carrying no payload is not a drag
        // everywhere else, and effectAllowed is what gives the cursor its move affordance.
        event.dataTransfer.effectAllowed = 'move'
        event.dataTransfer.setData('text/plain', task.id)
        onDragStart?.()
      }}
      onDragEndCapture={onDragEnd}
      onDragOverCapture={(event: DragEvent<HTMLLIElement>) => {
        event.stopPropagation()
        onDragOverCard?.(event)
      }}
      initial={{ opacity: 0, y: -6 }}
      animate={{ opacity: isDragging ? 0.4 : 1, y: 0 }}
      // Deliberately no exit animation: completing a task moves it between two lists,
      // and an exiting copy would leave the same task visible twice mid-transition.
      transition={{ type: 'spring', stiffness: 420, damping: 34 }}
      className={`${meta.tierClass} group panel relative overflow-hidden rounded-xl py-3 pr-3 pl-3.5 transition-shadow hover:shadow-lift ${
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

      <div className="flex items-start gap-2.5">
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

      </div>

      {/*
        Full card width rather than inside the title column, for the same reason the action
        row below is: the text column is 180px in the narrowest board column and a creature
        called "The Immemorial Bulwark" has nowhere to go in it. Out here it has 216px and a
        line of its own. Draws nothing at all unless the task is overdue.
      */}
      {!task.isCompleted && <TaskHuntSeal task={task} />}

      {/*
        Its own row, not the title's row. Inline, this cluster took 113px of a 242px card
        and left the title 36px, which broke "Move house" across three lines mid-word.
        Always rendered rather than mounted on hover, so the card does not jump.
      */}
      <div className="mt-1 flex items-center justify-end gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
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
