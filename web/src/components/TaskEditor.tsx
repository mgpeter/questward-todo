import { useState, type ReactNode } from 'react'
import {
  recurrenceLabels,
  type Difficulty,
  type Priority,
  type Recurrence,
  type Task,
} from '../lib/api'
import { DIFFICULTIES, PRIORITIES } from '../lib/difficulty'
import { fromDateInputValue, toDateInputValue } from '../lib/format'
import { useUpdateTask } from '../lib/queries'
import { useIsMobile } from '../lib/useMediaQuery'
import { TagInput } from './TagInput'

const RECURRENCES: Recurrence[] = ['none', 'daily', 'weekly', 'monthly']

interface TaskEditorProps {
  task: Task
  onClose: () => void
}

/** Inline edit form; replaces the card in place so the list does not jump. */
export function TaskEditor({ task, onClose }: TaskEditorProps) {
  const isMobile = useIsMobile()
  const [title, setTitle] = useState(task.title)
  const [notes, setNotes] = useState(task.notes ?? '')
  const [difficulty, setDifficulty] = useState<Difficulty>(task.difficulty)
  const [priority, setPriority] = useState<Priority>(task.priority)
  const [dueDate, setDueDate] = useState(toDateInputValue(task.dueDate))
  const [tags, setTags] = useState<string[]>(task.tags)
  const [recurrence, setRecurrence] = useState<Recurrence>(task.recurrence)
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
          tags,
          recurrence,
        },
      },
      { onSuccess: onClose },
    )
  }

  if (isMobile) {
    return (
      <li className="panel rounded-xl p-3.5 shadow-lift" data-testid="task-editor">
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
          className="w-full rounded-lg border border-gold bg-canvas px-3.5 py-3 text-[15px] outline-none"
        />

        <textarea
          value={notes}
          rows={3}
          maxLength={4000}
          placeholder="Notes (optional)"
          onChange={(event) => setNotes(event.target.value)}
          aria-label="Task notes"
          className="mt-2.5 w-full resize-none rounded-lg border border-line bg-canvas px-3.5 py-3 text-[13.5px] outline-none placeholder:text-ink-faint"
        />

        <div className="mt-3 grid grid-cols-4 gap-1.5" role="radiogroup" aria-label="Difficulty">
          {DIFFICULTIES.map((meta) => {
            const active = difficulty === meta.value

            return (
              <button
                key={meta.value}
                type="button"
                role="radio"
                aria-checked={active}
                onClick={() => setDifficulty(meta.value)}
                className={`${meta.tierClass} min-h-11 rounded-full py-2.5 text-center text-[12.5px] transition ${
                  active ? 'tier-chip font-medium' : 'border border-line text-ink-muted'
                }`}
              >
                {meta.label}
              </button>
            )
          })}
        </div>

        {/* Rows rather than a stack of segmented controls: three of those is 200px of
            chrome in a card that has to sit inside a list without swallowing it. The value
            is on the right where it can be read at a glance, and the control is the row. */}
        <div className="mt-3 flex flex-col">
          <EditorRow label="Priority">
            <select
              value={priority}
              onChange={(event) => setPriority(event.target.value as Priority)}
              aria-label="Priority"
              className="cursor-pointer bg-transparent text-right text-[13.5px] text-ink outline-none"
            >
              {PRIORITIES.map((meta) => (
                <option key={meta.value} value={meta.value}>
                  {meta.label}
                </option>
              ))}
            </select>
          </EditorRow>

          <EditorRow label="Due">
            <input
              type="date"
              value={dueDate}
              onChange={(event) => setDueDate(event.target.value)}
              aria-label="Due date"
              className="tabular cursor-pointer bg-transparent text-right text-[13.5px] text-ink outline-none"
            />
          </EditorRow>

          {task.parentId === null && (
            <EditorRow label="Repeats">
              <select
                value={recurrence}
                onChange={(event) => setRecurrence(event.target.value as Recurrence)}
                data-testid="task-edit-recurrence"
                aria-label="Repeats"
                className="cursor-pointer bg-transparent text-right text-[13.5px] text-ink outline-none"
              >
                {RECURRENCES.map((rule) => (
                  <option key={rule} value={rule}>
                    {recurrenceLabels[rule]}
                  </option>
                ))}
              </select>
            </EditorRow>
          )}

          <div className="flex items-center gap-3 border-t border-line py-2.5">
            <span className="shrink-0 text-[13.5px] text-ink-muted">Tags</span>
            <div className="ml-auto flex min-w-0 justify-end">
              <TagInput value={tags} onChange={setTags} testId="task-edit-tags" size="touch" />
            </div>
          </div>
        </div>

        <div className="mt-3 flex gap-2">
          <button
            type="button"
            onClick={onClose}
            className="min-h-11 flex-1 rounded-xl border border-line py-3.5 text-[14px] text-ink-muted transition"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={save}
            disabled={updateTask.isPending || !title.trim()}
            data-testid="task-edit-save"
            className="min-h-11 flex-1 rounded-xl bg-ink py-3.5 text-[14px] font-medium text-canvas transition disabled:opacity-40"
          >
            Save
          </button>
        </div>
      </li>
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

        {/* Subtasks pay nothing, so a repeat setting on one would be a control that does
            nothing. The server drops it either way; hiding it is the honest half. */}
        {task.parentId === null && (
          <label className="flex items-center gap-1.5 text-[11px] text-ink-faint">
            Repeats
            <select
              value={recurrence}
              onChange={(event) => setRecurrence(event.target.value as Recurrence)}
              data-testid="task-edit-recurrence"
              className="cursor-pointer bg-transparent text-ink-muted outline-none"
            >
              {RECURRENCES.map((rule) => (
                <option key={rule} value={rule}>
                  {recurrenceLabels[rule]}
                </option>
              ))}
            </select>
          </label>
        )}

        <TagInput value={tags} onChange={setTags} testId="task-edit-tags" />

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

/** One labelled row of the stacked editor: name on the left, the control on the right. */
function EditorRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="flex min-h-11 items-center gap-3 border-t border-line py-2.5">
      <span className="shrink-0 text-[13.5px] text-ink-muted">{label}</span>
      <span className="ml-auto min-w-0">{children}</span>
    </label>
  )
}
