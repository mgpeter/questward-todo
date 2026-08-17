import { Check, Pencil, Trash2 } from 'lucide-react'
import { motion } from 'motion/react'
import { useState, type MouseEvent } from 'react'
import type { Task } from '../lib/api'
import { difficultyMeta } from '../lib/difficulty'
import { describeDue } from '../lib/format'
import { useCompleteTask, useDeleteTask, useReopenTask } from '../lib/queries'
import { useGameFeed } from '../game/GameFeed'
import { TaskEditor } from './TaskEditor'

const DUE_TONE_CLASS: Record<string, string> = {
  overdue: 'text-rose border-rose/35 bg-rose/8',
  today: 'text-tier-hard border-tier-hard/35 bg-tier-hard/8',
  soon: 'text-ink-muted border-line',
  future: 'text-ink-faint border-line',
}

export function TaskCard({ task }: { task: Task }) {
  const [editing, setEditing] = useState(false)
  const completeTask = useCompleteTask()
  const reopenTask = useReopenTask()
  const deleteTask = useDeleteTask()
  const { celebrateCompletion, registerRefund } = useGameFeed()

  const meta = difficultyMeta(task.difficulty)
  const due = describeDue(task.dueDate)
  const busy = completeTask.isPending || reopenTask.isPending

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

  if (editing) {
    return <TaskEditor task={task} onClose={() => setEditing(false)} />
  }

  return (
    <motion.li
      layout
      initial={{ opacity: 0, y: -6 }}
      animate={{ opacity: 1, y: 0 }}
      // Deliberately no exit animation: completing a task moves it between two lists,
      // and an exiting copy would leave the same task visible twice mid-transition.
      transition={{ type: 'spring', stiffness: 420, damping: 34 }}
      className={`${meta.tierClass} group panel relative flex items-start gap-3 overflow-hidden rounded-xl py-3 pr-3 pl-4 transition-shadow hover:shadow-lift ${
        task.isCompleted ? 'opacity-60' : ''
      }`}
      data-testid="task-card"
      data-task-title={task.title}
      data-completed={task.isCompleted}
    >
      <span
        aria-hidden="true"
        className="absolute inset-y-0 left-0 w-[3px]"
        style={{ backgroundColor: 'var(--tier)', opacity: task.isCompleted ? 0.3 : 0.85 }}
      />

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
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
        <button
          type="button"
          onClick={() => setEditing(true)}
          aria-label={`Edit ${task.title}`}
          data-testid="task-edit"
          className="rounded-md p-1.5 text-ink-faint transition hover:bg-surface-sunk hover:text-ink"
        >
          <Pencil size={13} />
        </button>
        <button
          type="button"
          onClick={() => deleteTask.mutate(task.id)}
          disabled={deleteTask.isPending}
          aria-label={`Delete ${task.title}`}
          data-testid="task-delete"
          className="rounded-md p-1.5 text-ink-faint transition hover:bg-rose/10 hover:text-rose"
        >
          <Trash2 size={13} />
        </button>
      </div>
    </motion.li>
  )
}
