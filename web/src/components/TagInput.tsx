import { Tag, X } from 'lucide-react'
import { useState, type KeyboardEvent } from 'react'
import { useTags } from '../lib/queries'

interface TagInputProps {
  value: string[]
  onChange: (tags: string[]) => void
  testId?: string
}

/**
 * Tags as chips with a free-text field, backed by a datalist of everything already used.
 *
 * The suggestion list is the point: tags are only useful if the same idea gets the same
 * word each time, and an empty box invites "work", "Work" and "job". The server also
 * de-duplicates case-insensitively, so this is help rather than the guarantee.
 */
export function TagInput({ value, onChange, testId = 'tag-input' }: TagInputProps) {
  const [draft, setDraft] = useState('')
  const known = useTags()

  const add = (raw: string) => {
    const tag = raw.trim()
    if (!tag || value.length >= 10) return
    if (value.some((existing) => existing.toLowerCase() === tag.toLowerCase())) return

    onChange([...value, tag])
    setDraft('')
  }

  const keyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter' || event.key === ',') {
      event.preventDefault()
      add(draft)
      return
    }

    // Backspace on an empty field removes the last chip, which is what every other
    // chip input does and what fingers expect.
    if (event.key === 'Backspace' && !draft && value.length > 0) {
      onChange(value.slice(0, -1))
    }
  }

  return (
    <div className="flex flex-wrap items-center gap-1.5" data-testid={testId}>
      <Tag size={12} className="shrink-0 text-ink-faint" />

      {value.map((tag) => (
        <span
          key={tag}
          className="flex items-center gap-1 rounded-full bg-surface-sunk px-2 py-0.5 text-[10.5px] text-ink-muted"
        >
          {tag}
          <button
            type="button"
            onClick={() => onChange(value.filter((existing) => existing !== tag))}
            aria-label={`Remove tag ${tag}`}
            className="text-ink-faint transition hover:text-rose"
          >
            <X size={9} />
          </button>
        </span>
      ))}

      <input
        value={draft}
        list="known-tags"
        maxLength={32}
        placeholder={value.length ? '' : 'Tags'}
        aria-label="Add a tag"
        data-testid={`${testId}-field`}
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={keyDown}
        onBlur={() => add(draft)}
        className="w-20 min-w-0 bg-transparent text-[11px] text-ink-muted outline-none placeholder:text-ink-faint"
      />

      <datalist id="known-tags">
        {(known.data ?? []).map((tag) => (
          <option key={tag} value={tag} />
        ))}
      </datalist>
    </div>
  )
}
