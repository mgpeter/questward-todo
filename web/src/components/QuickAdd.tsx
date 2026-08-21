import { CalendarDays, Flag, Plus, Repeat } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { recurrenceLabels, type Difficulty, type Priority, type Recurrence } from '../lib/api'
import { DIFFICULTIES, difficultyMeta, PRIORITIES } from '../lib/difficulty'
import { fromDateInputValue, toDateInputValue } from '../lib/format'
import { useCreateTask, useTags } from '../lib/queries'
import { Segmented } from './Segmented'
import { Sheet } from './Sheet'
import { TagInput } from './TagInput'

const RECURRENCES: Recurrence[] = ['none', 'daily', 'weekly', 'monthly']

/**
 * Everything the add form holds, so the inline row and the sheet share one submit.
 *
 * The two lay the same nine fields out completely differently - a phone cannot put four
 * control groups on one wrap line - but there is only ever one create mutation and one set
 * of reset rules, and duplicating those is how the two drift apart.
 */
function useQuickAddForm(onCreated?: () => void) {
  const [title, setTitle] = useState('')
  const [notes, setNotes] = useState('')
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
        notes: notes.trim() || null,
        difficulty,
        priority,
        dueDate: fromDateInputValue(dueDate),
        tags,
        recurrence,
      },
      {
        onSuccess: () => {
          setTitle('')
          setNotes('')
          setDueDate('')
          setPriority('normal')
          setTags([])
          setRecurrence('none')
          setFormGeneration((generation) => generation + 1)
          onCreated?.()
        },
      },
    )
  }

  return {
    title,
    setTitle,
    notes,
    setNotes,
    difficulty,
    setDifficulty,
    priority,
    setPriority,
    dueDate,
    setDueDate,
    tags,
    setTags,
    recurrence,
    setRecurrence,
    formGeneration,
    createTask,
    submit,
  }
}

export function QuickAdd() {
  const form = useQuickAddForm()
  const { title, difficulty, priority, dueDate, tags, recurrence, formGeneration, createTask } = form

  return (
    <form
      onSubmit={form.submit}
      className="panel rounded-2xl px-4 pt-3.5 pb-3"
      data-testid="quick-add"
    >
      <div className="flex items-center gap-3">
        <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full border border-dashed border-line-strong text-ink-faint">
          <Plus size={14} />
        </span>

        <input
          value={title}
          onChange={(event) => form.setTitle(event.target.value)}
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
                onClick={() => form.setDifficulty(meta.value)}
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
            onChange={form.setTags}
            testId="quick-add-tags"
          />

          <label className="flex items-center gap-1.5 text-[11px] text-ink-faint">
            <Repeat size={12} />
            <span className="sr-only">Repeats</span>
            <select
              value={recurrence}
              onChange={(event) => form.setRecurrence(event.target.value as Recurrence)}
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
              onChange={(event) => form.setPriority(event.target.value as Priority)}
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
              onChange={(event) => form.setDueDate(event.target.value)}
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

const GROUP_LABEL = 'mb-2 text-[10px] tracking-[0.18em] text-ink-faint uppercase'

/** Today and tomorrow as yyyy-mm-dd, which is what a date input reads. */
function offsetDay(days: number): string {
  const day = new Date()
  day.setDate(day.getDate() + days)
  return toDateInputValue(day.toISOString())
}

/**
 * The same nine fields, one per line, opened from the bottom bar.
 *
 * The inline row puts four control groups on one wrapping line and two of them are native
 * selects. That is fine with a mouse and unusable with a thumb, so this trades the width it
 * does not have for the height it does.
 */
export function QuickAddSheet({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  /** Dismissed without adding: Cancel, Escape, or a tap on the scrim. */
  onClose: () => void
  /** A quest actually exists now. Separate from onClose so the caller can also navigate. */
  onCreated: () => void
}) {
  const form = useQuickAddForm(onCreated)
  const { title, notes, difficulty, priority, dueDate, tags, recurrence, formGeneration, createTask } =
    form

  const known = useTags()
  const suggestions = (known.data ?? []).filter(
    (tag) => !tags.some((chosen) => chosen.toLowerCase() === tag.toLowerCase()),
  )

  const today = offsetDay(0)
  const tomorrow = offsetDay(1)

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title="New quest"
      autoFocusContent
      testId="add-sheet"
      aside={
        <span className="tabular shrink-0 text-[11px] text-ink-faint">
          earns {difficultyMeta(difficulty).xp} XP
        </span>
      }
      footer={
        <div className="flex gap-2">
          <button
            type="button"
            onClick={onClose}
            className="w-24 shrink-0 rounded-xl border border-line py-3.5 text-[14.5px] text-ink-muted transition hover:border-line-strong"
          >
            Cancel
          </button>
          <button
            type="submit"
            form="quick-add-sheet-form"
            disabled={!title.trim() || createTask.isPending}
            data-testid="quick-add-submit"
            className="flex-1 rounded-xl bg-ink py-3.5 text-[14.5px] font-medium text-canvas transition disabled:opacity-30"
          >
            Add quest
          </button>
        </div>
      }
    >
      {/*
        The submit button lives in the sheet's footer, outside this element, so it reaches
        back by form id. Keeping the button in the footer is the whole point: on a phone it
        has to stay above the keyboard rather than scroll away with the fields.
      */}
      <form id="quick-add-sheet-form" onSubmit={form.submit} className="pt-1 pb-2">
        <input
          value={title}
          onChange={(event) => form.setTitle(event.target.value)}
          placeholder="What needs doing?"
          aria-label="New task title"
          maxLength={200}
          data-testid="quick-add-input"
          className="w-full rounded-lg border border-gold bg-canvas px-3.5 py-3 text-[15.5px] outline-none placeholder:text-ink-faint"
        />

        <textarea
          value={notes}
          onChange={(event) => form.setNotes(event.target.value)}
          rows={2}
          placeholder="Notes (optional)"
          aria-label="Notes"
          className="mt-2.5 w-full resize-none rounded-lg border border-line bg-canvas px-3.5 py-3 text-[13.5px] outline-none placeholder:text-ink-faint"
        />

        <p className={`mt-4 ${GROUP_LABEL}`}>Difficulty</p>
        <div className="grid grid-cols-4 gap-1.5" role="radiogroup" aria-label="Difficulty">
          {DIFFICULTIES.map((meta) => {
            const active = difficulty === meta.value

            return (
              <button
                key={meta.value}
                type="button"
                role="radio"
                aria-checked={active}
                data-testid={`difficulty-option-${meta.value}`}
                onClick={() => form.setDifficulty(meta.value)}
                className={`${meta.tierClass} rounded-lg py-2.5 text-center text-[12.5px] transition ${
                  active ? 'tier-chip font-medium' : 'border border-line text-ink-muted'
                }`}
              >
                {meta.label}
                <span className="tabular mt-0.5 block text-[10px] opacity-75">{meta.xp}</span>
              </button>
            )
          })}
        </div>

        <p className={`mt-4 ${GROUP_LABEL}`}>Priority</p>
        <Segmented
          label="Priority"
          value={priority}
          onChange={form.setPriority}
          testId="quick-add-priority"
          options={PRIORITIES.map((meta) => ({ value: meta.value, label: meta.label }))}
        />

        <p className={`mt-4 ${GROUP_LABEL}`}>Due</p>
        <div className="flex items-center gap-1.5">
          <DueChip
            label="Today"
            active={dueDate === today}
            onClick={() => form.setDueDate(dueDate === today ? '' : today)}
          />
          <DueChip
            label="Tomorrow"
            active={dueDate === tomorrow}
            onClick={() => form.setDueDate(dueDate === tomorrow ? '' : tomorrow)}
          />
          <input
            type="date"
            value={dueDate}
            onChange={(event) => form.setDueDate(event.target.value)}
            aria-label="Due date"
            data-testid="quick-add-due"
            className="tabular ml-auto rounded-lg border border-line bg-canvas px-3 py-2.5 text-[12.5px] text-ink-muted outline-none"
          />
        </div>

        <p className={`mt-4 ${GROUP_LABEL}`}>Repeats</p>
        <Segmented
          label="Repeats"
          value={recurrence}
          onChange={form.setRecurrence}
          testId="quick-add-recurrence"
          options={RECURRENCES.map((rule) => ({ value: rule, label: recurrenceLabels[rule] }))}
        />

        <p className={`mt-4 ${GROUP_LABEL}`}>Tags</p>
        <TagInput
          key={formGeneration}
          value={tags}
          onChange={form.setTags}
          testId="quick-add-tags"
          size="touch"
        />

        {/* The datalist the inline field uses is unreachable on touch: mobile browsers
            either ignore it or bury it behind a keyboard suggestion strip. The same
            vocabulary, as chips you can hit. */}
        {suggestions.length > 0 && (
          <div className="mt-2 flex flex-wrap gap-1.5" data-testid="quick-add-tag-suggestions">
            {suggestions.slice(0, 8).map((tag) => (
              <button
                key={tag}
                type="button"
                onClick={() => form.setTags([...tags, tag])}
                className="rounded-full border border-dashed border-line-strong px-3 py-2 text-[12px] text-ink-faint transition hover:text-ink-muted"
              >
                {tag}
              </button>
            ))}
          </div>
        )}

        {createTask.isError && (
          <p role="alert" className="mt-3 text-[12px] text-rose">
            {(createTask.error as Error).message}
          </p>
        )}
      </form>
    </Sheet>
  )
}

function DueChip({
  label,
  active,
  onClick,
}: {
  label: string
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={`rounded-full px-3.5 py-2.5 text-[12.5px] transition ${
        active
          ? 'border border-gold bg-gold/10 font-medium text-gold'
          : 'border border-line text-ink-muted'
      }`}
    >
      {label}
    </button>
  )
}
