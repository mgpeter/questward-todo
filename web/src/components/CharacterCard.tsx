import { Check, Pencil, X } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'
import { AVATARS, avatarFor } from '../lib/avatars'
import type { Character } from '../lib/api'
import { useUpdateCharacter } from '../lib/queries'
import { useMediaQuery } from '../lib/useMediaQuery'
import { LevelRing } from './LevelRing'

interface CharacterCardProps {
  character: Character
}

export function CharacterCard({ character }: CharacterCardProps) {
  // Not useIsMobile: the card is horizontal wherever it has the width, which is everywhere
  // except the lg sidebar. That column is a fixed 290px - narrower than this card on a phone -
  // and an avatar plus a stat column would leave about ninety pixels for the name.
  const wide = useMediaQuery('(width < 64rem)')

  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(character.name)
  const [avatarKey, setAvatarKey] = useState(character.avatarKey)
  const updateCharacter = useUpdateCharacter()

  // Keep the draft in step when the character changes underneath the form.
  useEffect(() => {
    if (editing) return

    setName(character.name)
    setAvatarKey(character.avatarKey)
  }, [character.name, character.avatarKey, editing])

  const percent = Math.min(
    100,
    (character.xpIntoLevel / Math.max(1, character.xpForNextLevel)) * 100,
  )

  const save = () => {
    const trimmed = name.trim()
    if (!trimmed) return

    updateCharacter.mutate({ name: trimmed, avatarKey }, { onSuccess: () => setEditing(false) })
  }

  // The drafted avatar, so the preview and the label it is announced by agree. They did not:
  // the glyph read the draft while the label read the saved key, so mid-edit a screen reader
  // named the avatar being replaced.
  const shown = avatarFor(editing ? avatarKey : character.avatarKey)

  const editor = (
    <NameEditor
      name={name}
      avatarKey={avatarKey}
      pending={updateCharacter.isPending}
      onName={setName}
      onAvatar={setAvatarKey}
      onSave={save}
      onCancel={() => setEditing(false)}
    />
  )

  const stats = [
    { label: 'Done', value: character.tasksCompleted, testId: 'stat-tasks' },
    { label: 'Total XP', value: character.totalXp, testId: 'stat-xp' },
    {
      label: 'Badges',
      value: `${character.achievementsUnlocked}/${character.achievementsTotal}`,
      testId: 'stat-badges',
    },
  ]

  const gilding = (
    <div
      aria-hidden="true"
      className="absolute inset-x-0 top-0 h-px bg-linear-to-r from-transparent via-gold/60 to-transparent"
    />
  )

  if (wide) {
    return (
      <section
        className="panel relative overflow-hidden rounded-2xl"
        data-testid="character-card"
        aria-label="Character"
      >
        {gilding}

        <div className="flex items-center gap-4 p-4">
          {/*
            No ring out here. The compact header already draws the gold XP bar and the
            "490 / 500 XP" beside it on every screen this layout appears on, so the ring was
            the same reading twice - and it was a 132px SVG for it.
          */}
          <span
            className="grid h-16 w-16 shrink-0 place-items-center rounded-full bg-surface-sunk ring-1 ring-line"
            role="img"
            aria-label={shown.name}
          >
            <span className="text-[30px] leading-none">{shown.glyph}</span>
          </span>

          <div className="min-w-0 flex-1">
            {editing ? (
              editor
            ) : (
              <>
                <div className="flex items-center gap-1.5">
                  <h2
                    className="font-display min-w-0 truncate text-[21px] leading-tight"
                    data-testid="character-name"
                  >
                    {character.name}
                  </h2>
                  <EditButton onClick={() => setEditing(true)} />
                </div>

                <p
                  className="font-display mt-0.5 truncate text-[13px] text-gold italic"
                  data-testid="character-title"
                >
                  {character.title}
                </p>

                <p className="tabular mt-1 text-[11px] text-ink-faint">
                  <span className="tracking-[0.14em] uppercase">Level {character.level}</span>
                  {character.xpToNextLevel > 0 && (
                    <span className="ml-2">{character.xpToNextLevel} XP to go</span>
                  )}
                </p>
              </>
            )}
          </div>

          {!editing && (
            <dl className="shrink-0 divide-y divide-line border-l border-line pl-4">
              {stats.map((stat) => (
                <Stat key={stat.testId} {...stat} align="right" />
              ))}
            </dl>
          )}
        </div>
      </section>
    )
  }

  return (
    <section
      className="panel relative overflow-hidden rounded-2xl"
      data-testid="character-card"
      aria-label="Character"
    >
      {gilding}

      <div className="flex flex-col items-center px-6 pt-7 pb-6">
        <LevelRing percent={percent}>
          <span
            className="grid h-[104px] w-[104px] place-items-center rounded-full bg-surface-sunk ring-1 ring-line"
            role="img"
            aria-label={shown.name}
          >
            <span className="text-[44px] leading-none">{shown.glyph}</span>
          </span>
        </LevelRing>

        <p className="mt-4 text-[10px] font-medium tracking-[0.22em] text-ink-faint uppercase">
          Level {character.level}
        </p>

        {editing ? (
          <div className="mt-2 w-full">{editor}</div>
        ) : (
          <>
            <div className="mt-1.5 flex items-center gap-1.5">
              <h2 className="font-display text-[26px] leading-tight" data-testid="character-name">
                {character.name}
              </h2>
              <EditButton onClick={() => setEditing(true)} />
            </div>

            <p
              className="font-display mt-0.5 text-sm text-gold italic"
              data-testid="character-title"
            >
              {character.title}
            </p>

            <p className="tabular mt-3 text-[11px] text-ink-faint">
              {character.xpToNextLevel > 0 ? (
                <>
                  <span className="text-ink-muted">{character.xpToNextLevel} XP</span> to level{' '}
                  {character.level + 1}
                </>
              ) : (
                'Maximum level'
              )}
            </p>
          </>
        )}
      </div>

      {!editing && (
        <dl className="grid grid-cols-3 divide-x divide-line border-t border-line bg-surface-sunk/50">
          {stats.map((stat) => (
            <Stat key={stat.testId} {...stat} align="center" />
          ))}
        </dl>
      )}
    </section>
  )
}

function EditButton({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label="Edit character"
      data-testid="character-edit"
      // Was a reveal-on-hover pencil with no `group` ancestor to reveal it and an inline
      // opacity:1 overriding the classes anyway. It has always been visible; now it says so.
      className="-m-1.5 grid h-9 w-9 shrink-0 place-items-center rounded-md p-1.5 text-ink-faint transition hover:bg-surface-sunk hover:text-ink-muted"
    >
      <Pencil size={13} />
    </button>
  )
}

/**
 * One reading and its name.
 *
 * `dt` before `dd`, which is the order a definition list is defined in. It read the other way
 * round to put the number on top; that is a job for the flex direction, not for source order,
 * and the old way left the list without a term to define.
 */
function Stat({
  label,
  value,
  testId,
  align,
}: {
  label: string
  value: string | number
  testId: string
  align: 'center' | 'right'
}) {
  return (
    <div
      className={`flex flex-col-reverse ${
        align === 'center' ? 'px-2 py-3 text-center' : 'py-1.5 text-right'
      }`}
    >
      <dt
        className={`font-medium tracking-[0.16em] text-ink-faint uppercase ${
          align === 'center' ? 'mt-1.5 text-[9px]' : 'text-[8.5px]'
        }`}
      >
        {label}
      </dt>
      <dd
        className={`tabular leading-none font-medium ${
          align === 'center' ? 'text-lg' : 'text-[13px]'
        }`}
        data-testid={testId}
      >
        {typeof value === 'number' ? value.toLocaleString() : value}
      </dd>
    </div>
  )
}

function NameEditor({
  name,
  avatarKey,
  pending,
  onName,
  onAvatar,
  onSave,
  onCancel,
}: {
  name: string
  avatarKey: string
  pending: boolean
  onName: (name: string) => void
  onAvatar: (key: string) => void
  onSave: () => void
  onCancel: () => void
}): ReactNode {
  return (
    <div className="w-full">
      <input
        value={name}
        autoFocus
        maxLength={60}
        onChange={(event) => onName(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Enter') onSave()
          if (event.key === 'Escape') onCancel()
        }}
        aria-label="Character name"
        data-testid="character-name-input"
        className="font-display w-full rounded-lg border border-line bg-canvas px-3 py-2 text-xl outline-none focus:border-gold"
      />

      {/* 44px, like every other target the phone can reach. These were 32px, and this card is
          on the Adventure tab where a thumb is the only pointer there is. */}
      <div className="mt-3 flex flex-wrap gap-1">
        {AVATARS.map((avatar) => (
          <button
            key={avatar.key}
            type="button"
            title={avatar.name}
            aria-label={avatar.name}
            aria-pressed={avatarKey === avatar.key}
            onClick={() => onAvatar(avatar.key)}
            className={`grid h-11 w-11 place-items-center rounded-lg border text-lg transition ${
              avatarKey === avatar.key
                ? 'border-gold bg-gold/12'
                : 'border-line hover:border-line-strong'
            }`}
          >
            {avatar.glyph}
          </button>
        ))}
      </div>

      <div className="mt-3 flex gap-2">
        <button
          type="button"
          onClick={onSave}
          disabled={pending || !name.trim()}
          data-testid="character-save"
          className="inline-flex min-h-11 flex-1 items-center justify-center gap-1.5 rounded-lg bg-ink px-3 text-xs font-medium text-canvas transition hover:opacity-90 disabled:opacity-40"
        >
          <Check size={13} /> Save
        </button>
        <button
          type="button"
          onClick={onCancel}
          className="inline-flex min-h-11 flex-1 items-center justify-center gap-1.5 rounded-lg border border-line px-3 text-xs text-ink-muted transition hover:border-line-strong"
        >
          <X size={13} /> Cancel
        </button>
      </div>
    </div>
  )
}
