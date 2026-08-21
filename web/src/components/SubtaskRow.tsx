import { Check, Trash2 } from 'lucide-react'
import type { Task } from '../lib/api'
import { useCompleteTask, useDeleteTask, useReopenTask } from '../lib/queries'

/**
 * A step inside a task. Ticking one is real progress and worth showing, but it pays
 * nothing, so it gets no XP float and no celebration.
 *
 * Lifted out of TaskCard so the detail sheet can draw the same list. The delete button is
 * the reason it needs the `size` prop rather than only a class: on the card it appears on
 * hover, which on a phone means it never appears at all.
 */
export function SubtaskRow({
  subtask,
  size = 'compact',
}: {
  subtask: Task
  size?: 'compact' | 'touch'
}) {
  const completeTask = useCompleteTask()
  const reopenTask = useReopenTask()
  const deleteTask = useDeleteTask()

  const busy = completeTask.isPending || reopenTask.isPending
  const touch = size === 'touch'

  return (
    <li
      className={`group/step flex items-center ${
        touch ? 'gap-3 border-t border-line py-2.5' : 'gap-2'
      }`}
      data-testid="subtask"
    >
      <button
        type="button"
        disabled={busy}
        onClick={() =>
          subtask.isCompleted ? reopenTask.mutate(subtask.id) : completeTask.mutate(subtask.id)
        }
        aria-pressed={subtask.isCompleted}
        aria-label={subtask.isCompleted ? `Reopen ${subtask.title}` : `Complete ${subtask.title}`}
        title="Steps track progress. The XP is paid when the task itself is finished."
        data-testid="subtask-toggle"
        className={
          touch
            ? // 22px of ink inside a 44px target, the same trick the card checkbox uses.
              'grid h-11 w-11 shrink-0 -my-2.5 -ml-2.5 place-items-center disabled:opacity-50'
            : `grid h-[15px] w-[15px] shrink-0 place-items-center rounded border transition disabled:opacity-50 ${
                subtask.isCompleted
                  ? 'border-teal bg-teal text-canvas'
                  : 'border-line-strong hover:border-gold'
              }`
        }
      >
        {touch ? (
          <span
            className={`grid h-[22px] w-[22px] place-items-center rounded-md border-2 ${
              subtask.isCompleted
                ? 'border-teal bg-teal text-canvas'
                : 'border-line-strong'
            }`}
          >
            {subtask.isCompleted && <Check size={12} strokeWidth={3.5} />}
          </span>
        ) : (
          subtask.isCompleted && <Check size={9} strokeWidth={4} />
        )}
      </button>

      <span
        className={`min-w-0 flex-1 ${touch ? 'text-[14px]' : 'truncate text-[12.5px]'} ${
          subtask.isCompleted ? 'text-ink-faint line-through' : 'text-ink-muted'
        }`}
      >
        {subtask.title}
      </span>

      <button
        type="button"
        onClick={() => deleteTask.mutate(subtask.id)}
        aria-label={`Delete ${subtask.title}`}
        className={
          touch
            ? 'grid h-11 w-11 shrink-0 -my-2.5 -mr-2.5 place-items-center text-ink-faint transition hover:text-rose'
            : 'rounded p-0.5 text-ink-faint opacity-0 transition group-hover/step:opacity-100 hover:text-rose'
        }
      >
        <Trash2 size={touch ? 14 : 11} />
      </button>
    </li>
  )
}
