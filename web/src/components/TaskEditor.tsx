import { useState } from 'react'
import type { Difficulty, Priority, Task } from '../lib/api'
import { DIFFICULTIES, PRIORITIES } from '../lib/difficulty'
import { fromDateInputValue, toDateInputValue } from '../lib/format'
import { useUpdateTask } from '../lib/queries'

interface TaskEditorProps {
  task: Task
  onClose: () => void
}

/** Inline edit form; replaces the card in place so the list does not jump. */
export function TaskEditor({ task, onClose }: TaskEditorProps) {
  const [title, setTitle] = useState(task.title)
  const [notes, setNotes] = useState(task.notes ?? '')
  const [difficulty, setDifficulty] = useState<Difficulty>(task.difficulty)
  const [priority, setPriority] = useState<Priority>(task.priority)
  const [dueDate, setDueDate] = useState(toDateInputValue(task.dueDate))
  const updateTask = useUpdateTask()

  const save = () => {
    const trimmed = title.trim()
    if (!trimmed) return

    updateTask.mutate(
      {
        id: task.id,
        input: {
          title: trimmed,
          notes: notes.trim() || null,
          difficulty,
          priority,
          dueDate: fromDateInputValue(dueDate),
        },
      },
      { onSuccess: onClose },
    )
  }

  return (
    <li className="panel rounded-xl p-4" data-testid="task-editor">
      <input
        value={title}
        autoFocus
        maxLength={200}
        onChange={(event) => setTitle(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Enter') save()
          if (event.key === 'Escape') onClose()
        }}
        aria-label="Task title"
        data-testid="task-edit-title"
        className="w-full rounded-lg border border-line bg-canvas px-3 py-2 text-[14.5px] outline-none focus:border-gold"
      />

      <textarea
        value={notes}
        rows={2}
        maxLength={4000}
        placeholder="Notes (optional)"
        onChange={(event) => setNotes(event.target.value)}
        aria-label="Task notes"
        className="mt-2 w-full resize-y rounded-lg border border-line bg-canvas px-3 py-2 text-[13px] outline-none placeholder:text-ink-faint focus:border-gold"
      />

      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2">
        <div className="flex items-center gap-1" role="radiogroup" aria-label="Difficulty">
          {DIFFICULTIES.map((meta) => {
            const active = difficulty === meta.value

            return (
              <button
                key={meta.value}
                type="button"
                role="radio"
                aria-checked={active}
                onClick={() => setDifficulty(meta.value)}
                className={`${meta.tierClass} rounded-full px-2.5 py-1 text-[11px] font-medium transition ${
                  active ? 'tier-chip' : 'border border-transparent text-ink-faint hover:text-ink-muted'
                }`}
              >
                {meta.label}
              </button>
            )
          })}
        </div>

        <label className="flex items-center gap-1.5 text-[11px] text-ink-faint">
          Priority
          <select
            value={priority}
            onChange={(event) => setPriority(event.target.value as Priority)}
            className="cursor-pointer bg-transparent text-ink-muted outline-none"
          >
            {PRIORITIES.map((meta) => (
              <option key={meta.value} value={meta.value}>
                {meta.label}
              </option>
            ))}
          </select>
        </label>

        <label className="flex items-center gap-1.5 text-[11px] text-ink-faint">
          Due
          <input
            type="date"
            value={dueDate}
            onChange={(event) => setDueDate(event.target.value)}
            className="cursor-pointer bg-transparent text-ink-muted outline-none"
          />
        </label>

        <div className="ml-auto flex gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-line px-3 py-1.5 text-xs text-ink-muted transition hover:border-line-strong"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={save}
            disabled={updateTask.isPending || !title.trim()}
            data-testid="task-edit-save"
            className="rounded-lg bg-ink px-3 py-1.5 text-xs font-medium text-canvas transition hover:opacity-90 disabled:opacity-40"
          >
            Save
          </button>
        </div>
      </div>
    </li>
  )
}
