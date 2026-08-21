import { Check, Plus, Repeat } from 'lucide-react'
import { useState, type KeyboardEvent, type MouseEvent } from 'react'
import { recurrenceLabels, taskProgressLabels, taskProgressOrder, type Task } from '../lib/api'
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
import { TaskHuntSeal } from './rpg/TaskHuntSeal'
import { Sheet } from './Sheet'
import { SubtaskRow } from './SubtaskRow'

const DUE_TONE_CLASS: Record<string, string> = {
  overdue: 'text-gold border-gold/45 bg-gold/10',
  today: 'text-tier-hard border-tier-hard/35 bg-tier-hard/8',
  soon: 'text-ink-muted border-line',
  future: 'text-ink-faint border-line',
}

interface TaskSheetProps {
  task: Task
  open: boolean
  onClose: () => void
  /** Hands the card back its inline editor, which is still the right shape on a phone. */
  onEdit: () => void
}

/**
 * Everything the mobile card stopped showing.
 *
 * The card carries a title, one meta line and the contract. Priority, recurrence, the rest
 * of the tags, the steps and every destructive action live here, because a card that showed
 * them all was 180px tall and still hid its actions behind a hover nobody on a phone has.
 */
export function TaskSheet({ task, open, onClose, onEdit }: TaskSheetProps) {
  const [addingStep, setAddingStep] = useState(false)
  const [stepTitle, setStepTitle] = useState('')

  const completeTask = useCompleteTask()
  const reopenTask = useReopenTask()
  const deleteTask = useDeleteTask()
  const createTask = useCreateTask()
  const setStatus = useSetTaskStatus()
  const { celebrateCompletion, registerRefund, celebrateStatusChange } = useGameFeed()

  const meta = difficultyMeta(task.difficulty)
  const due = describeDue(task.dueDate)
  const busy = completeTask.isPending || reopenTask.isPending || setStatus.isPending
  const doneSteps = task.subtasks.filter((step) => step.isCompleted).length

  const index = taskProgressOrder.indexOf(task.status)
  const next = taskProgressOrder[index + 1]

  const toggle = (event: MouseEvent<HTMLButtonElement>) => {
    if (busy) return

    // The button seeds the floating XP, same as the card's checkbox does. The sheet is
    // about to close, but the layer it rises in is fixed to the viewport and outlives it.
    const origin = event.currentTarget.getBoundingClientRect()

    if (task.isCompleted) {
      reopenTask.mutate(task.id, {
        onSuccess: (result) => {
          registerRefund(result, origin)
          onClose()
        },
      })
    } else {
      completeTask.mutate(task.id, {
        onSuccess: (result) => {
          celebrateCompletion(result, origin)
          onClose()
        },
      })
    }
  }

  /**
   * The touch equivalent of dragging a card one column right.
   *
   * Drag is a mouse gesture and stays one; this is the same setStatus route the keyboard
   * chevrons take, so nothing on a phone is reachable only by dragging.
   */
  const start = (event: MouseEvent<HTMLButtonElement>) => {
    if (!next || busy) return

    const origin = event.currentTarget.getBoundingClientRect()

    setStatus.mutate(
      { id: task.id, status: next },
      {
        onSuccess: (result) => {
          celebrateStatusChange(result, origin)
          onClose()
        },
      },
    )
  }

  const submitStep = () => {
    const trimmed = stepTitle.trim()
    if (!trimmed) return

    createTask.mutate(
      { title: trimmed, difficulty: task.difficulty, parentId: task.id },
      {
        onSuccess: () => {
          setStepTitle('')
          setAddingStep(false)
        },
      },
    )
  }

  return (
    <Sheet open={open} onClose={onClose} title={task.title} testId="task-sheet">
      <div className="flex flex-wrap items-center gap-1.5 pt-1">
        <span className={`${meta.tierClass} tier-chip rounded-full px-2.5 py-1 text-[11px] font-medium`}>
          {meta.label}
        </span>

        <span className="tabular px-1 text-[11px] text-ink-faint">
          {task.isCompleted ? `+${task.xpAwarded}` : meta.xp} XP
        </span>

        {task.priority === 'high' && !task.isCompleted && (
          <span className="rounded-full border border-rose/35 bg-rose/8 px-2.5 py-1 text-[11px] font-medium text-rose">
            High
          </span>
        )}

        {task.recurrence !== 'none' && (
          <span
            data-testid="task-recurrence"
            className="flex items-center gap-1 rounded-full border border-line px-2.5 py-1 text-[11px] text-ink-muted"
          >
            <Repeat size={10} />
            {recurrenceLabels[task.recurrence]}
          </span>
        )}

        {due.tone !== 'none' && !task.isCompleted && (
          <span
            data-testid="task-due"
            className={`rounded-full border px-2.5 py-1 text-[11px] ${DUE_TONE_CLASS[due.tone]}`}
          >
            {due.label}
          </span>
        )}

        {task.tags.map((tag) => (
          <span
            key={tag}
            data-testid="task-tag"
            className="rounded-full bg-surface-sunk px-2.5 py-1 text-[11px] text-ink-muted"
          >
            {tag}
          </span>
        ))}
      </div>

      {task.notes && (
        <p className="mt-3 text-[13.5px] leading-relaxed text-ink-muted">{task.notes}</p>
      )}

      {/* The full seal, not the card's strip: this is where the age, the faction and the
          reward floor have room to be read. */}
      {!task.isCompleted && (
        <div className="mt-3">
          <TaskHuntSeal task={task} />
        </div>
      )}

      <p className="mt-5 mb-1 text-[10px] tracking-[0.18em] text-ink-faint uppercase">
        Steps
        {task.subtasks.length > 0 && (
          <span className="tabular ml-1.5">
            {doneSteps}/{task.subtasks.length}
          </span>
        )}
      </p>

      <ul data-testid="subtask-list">
        {task.subtasks.map((step) => (
          <SubtaskRow key={step.id} subtask={step} size="touch" />
        ))}
      </ul>

      {addingStep ? (
        <input
          autoFocus
          value={stepTitle}
          maxLength={200}
          placeholder="Add a step"
          aria-label={`New step for ${task.title}`}
          data-testid="subtask-input"
          onChange={(event) => setStepTitle(event.target.value)}
          onBlur={submitStep}
          onKeyDown={(event: KeyboardEvent<HTMLInputElement>) => {
            if (event.key === 'Enter') submitStep()
            if (event.key === 'Escape') setAddingStep(false)
          }}
          className="mt-2 w-full rounded-lg border border-gold bg-canvas px-3 py-2.5 text-[14px] outline-none"
        />
      ) : (
        <button
          type="button"
          onClick={() => setAddingStep(true)}
          data-testid="task-add-subtask"
          className="flex min-h-11 w-full items-center gap-3 border-t border-line py-2.5 text-left text-[14px] text-ink-faint"
        >
          <span className="grid h-[22px] w-[22px] shrink-0 place-items-center rounded-md border border-dashed border-line-strong">
            <Plus size={12} />
          </span>
          Add a step
        </button>
      )}

      <div className="mt-5 flex flex-col gap-2">
        <button
          type="button"
          onClick={toggle}
          disabled={busy}
          aria-pressed={task.isCompleted}
          data-testid="task-toggle"
          className="flex min-h-11 items-center justify-center gap-2.5 rounded-xl bg-ink py-3.5 text-[14.5px] font-medium text-canvas transition disabled:opacity-40"
        >
          <Check size={16} strokeWidth={3} />
          {task.isCompleted ? 'Reopen this quest' : `Complete — earn ${meta.xp} XP`}
        </button>

        <div className="flex gap-2">
          {next && !task.isCompleted && (
            <button
              type="button"
              onClick={start}
              disabled={busy}
              data-testid="task-move-right"
              className="min-h-11 flex-1 rounded-xl border border-line py-3 text-[13.5px] text-ink-muted transition disabled:opacity-40"
            >
              {taskProgressLabels[next] === 'In progress' ? 'Start' : taskProgressLabels[next]}
            </button>
          )}

          <button
            type="button"
            onClick={onEdit}
            data-testid="task-edit"
            className="min-h-11 flex-1 rounded-xl border border-line py-3 text-[13.5px] text-ink-muted transition"
          >
            Edit
          </button>

          <button
            type="button"
            onClick={() => {
              deleteTask.mutate(task.id)
              onClose()
            }}
            disabled={deleteTask.isPending}
            data-testid="task-delete"
            className="min-h-11 flex-1 rounded-xl border border-rose/40 py-3 text-[13.5px] text-rose transition disabled:opacity-40"
          >
            Delete
          </button>
        </div>
      </div>
    </Sheet>
  )
}
