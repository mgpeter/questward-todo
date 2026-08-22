import { Tag, X } from 'lucide-react'
import { useState, type KeyboardEvent } from 'react'
import { useTags } from '../lib/queries'

interface TagInputProps {
  value: string[]
  onChange: (tags: string[]) => void
  testId?: string
  /**
   * `touch` grows the chips and the field to a thumb-sized target, for the sheets. The
   * datalist is dropped there too: mobile browsers either ignore it or bury it behind the
   * keyboard's own suggestion strip, so the callers offer the known tags as chips instead.
   */
  size?: 'compact' | 'touch'
}

/**
 * Tags as chips with a free-text field, backed by a datalist of everything already used.
 *
 * The suggestion list is the point: tags are only useful if the same idea gets the same
 * word each time, and an empty box invites "work", "Work" and "job". The server also
 * de-duplicates case-insensitively, so this is help rather than the guarantee.
 */
export function TagInput({
  value,
  onChange,
  testId = 'tag-input',
  size = 'compact',
}: TagInputProps) {
  const touch = size === 'touch'
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
      <Tag size={touch ? 14 : 12} className="shrink-0 text-ink-faint" />

      {value.map((tag) => (
        <span
          key={tag}
          className={`flex items-center gap-1 rounded-full bg-surface-sunk text-ink-muted ${
            touch ? 'gap-1.5 px-3 py-2 text-[12px]' : 'px-2 py-0.5 text-[10.5px]'
          }`}
        >
          {tag}
          <button
            type="button"
            onClick={() => onChange(value.filter((existing) => existing !== tag))}
            aria-label={`Remove tag ${tag}`}
            className="text-ink-faint transition hover:text-rose"
          >
            <X size={touch ? 12 : 9} />
          </button>
        </span>
      ))}

      <input
        value={draft}
        list={touch ? undefined : 'known-tags'}
        maxLength={32}
        placeholder={value.length ? '' : 'Tags'}
        aria-label="Add a tag"
        data-testid={`${testId}-field`}
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={keyDown}
        onBlur={() => add(draft)}
        className={`min-w-0 bg-transparent text-ink-muted outline-none placeholder:text-ink-faint ${
          touch ? 'w-24 py-2 text-[12px]' : 'w-20 text-[11px]'
        }`}
      />

      {/* One datalist, one fixed id, so only the compact fields may draw it: two mounted at
          once would put two #known-tags in the document. */}
      {!touch && (
        <datalist id="known-tags">
          {(known.data ?? []).map((tag) => (
            <option key={tag} value={tag} />
          ))}
        </datalist>
      )}
    </div>
  )
}
