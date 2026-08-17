import { CalendarDays, Flag, Plus, Repeat } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { recurrenceLabels, type Difficulty, type Priority, type Recurrence } from '../lib/api'
import { DIFFICULTIES, PRIORITIES } from '../lib/difficulty'
import { fromDateInputValue } from '../lib/format'
import { useCreateTask } from '../lib/queries'
import { TagInput } from './TagInput'

const RECURRENCES: Recurrence[] = ['none', 'daily', 'weekly', 'monthly']

export function QuickAdd() {
  const [title, setTitle] = useState('')
  const [difficulty, setDifficulty] = useState<Difficulty>('medium')
  const [priority, setPriority] = useState<Priority>('normal')
  const [dueDate, setDueDate] = useState('')
  const [tags, setTags] = useState<string[]>([])
  const [recurrence, setRecurrence] = useState<Recurrence>('none')
  // Bumped on submit to remount TagInput. Clearing `tags` empties the chips but leaves
  // whatever half-typed word is still sitting in its field, which then attaches itself to
  // the next task you add.
  const [formGeneration, setFormGeneration] = useState(0)
  const createTask = useCreateTask()

  const submit = (event: FormEvent) => {
    event.preventDefault()

    const trimmed = title.trim()
    if (!trimmed) return

    createTask.mutate(
      {
        title: trimmed,
        difficulty,
        priority,
        dueDate: fromDateInputValue(dueDate),
        tags,
        recurrence,
      },
      {
        onSuccess: () => {
          setTitle('')
          setDueDate('')
          setPriority('normal')
          setTags([])
          setRecurrence('none')
          setFormGeneration((generation) => generation + 1)
        },
      },
    )
  }

  return (
    <form onSubmit={submit} className="panel rounded-2xl px-4 pt-3.5 pb-3" data-testid="quick-add">
      <div className="flex items-center gap-3">
        <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full border border-dashed border-line-strong text-ink-faint">
          <Plus size={14} />
        </span>

        <input
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          placeholder="What needs doing?"
          aria-label="New task title"
          maxLength={200}
          data-testid="quick-add-input"
          className="min-w-0 flex-1 bg-transparent py-1 text-[15px] outline-none placeholder:text-ink-faint"
        />

        <button
          type="submit"
          disabled={!title.trim() || createTask.isPending}
          data-testid="quick-add-submit"
          className="shrink-0 rounded-lg bg-ink px-3.5 py-1.5 text-xs font-medium text-canvas transition hover:opacity-90 disabled:opacity-30"
        >
          Add
        </button>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2 border-t border-line pt-2.5">
        <div className="flex items-center gap-1" role="radiogroup" aria-label="Difficulty">
          {DIFFICULTIES.map((meta) => {
            const active = difficulty === meta.value

            return (
              <button
                key={meta.value}
                type="button"
                role="radio"
                aria-checked={active}
                title={`${meta.label} - ${meta.blurb}, ${meta.xp} XP`}
                data-testid={`difficulty-option-${meta.value}`}
                onClick={() => setDifficulty(meta.value)}
                className={`${meta.tierClass} rounded-full px-2.5 py-1 text-[11px] font-medium transition ${
                  active
                    ? 'tier-chip'
                    : 'border border-transparent text-ink-faint hover:text-ink-muted'
                }`}
              >
                {meta.label}
                <span className="tabular ml-1.5 opacity-70">{meta.xp}</span>
              </button>
            )
          })}
        </div>

        <div className="ml-auto flex items-center gap-3">
          <TagInput
            key={formGeneration}
            value={tags}
            onChange={setTags}
            testId="quick-add-tags"
          />

          <label className="flex items-center gap-1.5 text-[11px] text-ink-faint">
            <Repeat size={12} />
            <span className="sr-only">Repeats</span>
            <select
              value={recurrence}
              onChange={(event) => setRecurrence(event.target.value as Recurrence)}
              data-testid="quick-add-recurrence"
              className="cursor-pointer bg-transparent text-[11px] text-ink-muted outline-none"
            >
              {RECURRENCES.map((rule) => (
                <option key={rule} value={rule}>
                  {recurrenceLabels[rule]}
                </option>
              ))}
            </select>
          </label>

          <label className="flex items-center gap-1.5 text-[11px] text-ink-faint">
            <Flag size={12} />
            <span className="sr-only">Priority</span>
            <select
              value={priority}
              onChange={(event) => setPriority(event.target.value as Priority)}
              data-testid="quick-add-priority"
              className="cursor-pointer bg-transparent text-[11px] text-ink-muted outline-none"
            >
              {PRIORITIES.map((meta) => (
                <option key={meta.value} value={meta.value}>
                  {meta.label}
                </option>
              ))}
            </select>
          </label>

          <label className="flex items-center gap-1.5 text-[11px] text-ink-faint">
            <CalendarDays size={12} />
            <span className="sr-only">Due date</span>
            <input
              type="date"
              value={dueDate}
              onChange={(event) => setDueDate(event.target.value)}
              data-testid="quick-add-due"
              className="cursor-pointer bg-transparent text-[11px] text-ink-muted outline-none"
            />
          </label>
        </div>
      </div>

      {createTask.isError && (
        <p role="alert" className="mt-2 text-[11px] text-rose">
          {(createTask.error as Error).message}
        </p>
      )}
    </form>
  )
}
