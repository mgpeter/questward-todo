import { Check, Pencil, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { AVATARS, avatarFor } from '../lib/avatars'
import type { Character } from '../lib/api'
import { useUpdateCharacter } from '../lib/queries'
import { LevelRing } from './LevelRing'

interface CharacterCardProps {
  character: Character
}

export function CharacterCard({ character }: CharacterCardProps) {
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

    updateCharacter.mutate(
      { name: trimmed, avatarKey },
      { onSuccess: () => setEditing(false) },
    )
  }

  return (
    <section
      className="panel relative overflow-hidden rounded-2xl"
      data-testid="character-card"
      aria-label="Character"
    >
      {/* A hairline of gold along the top edge, like the gilding on a ledger. */}
      <div className="absolute inset-x-0 top-0 h-px bg-linear-to-r from-transparent via-gold/60 to-transparent" />

      <div className="flex flex-col items-center px-6 pt-7 pb-6">
        <LevelRing percent={percent}>
          <div className="grid h-[104px] w-[104px] place-items-center rounded-full bg-surface-sunk ring-1 ring-line">
            <span className="text-[44px] leading-none" role="img" aria-label={avatarFor(character.avatarKey).name}>
              {avatarFor(editing ? avatarKey : character.avatarKey).glyph}
            </span>
          </div>
        </LevelRing>

        <p className="mt-4 text-[10px] font-medium uppercase tracking-[0.22em] text-ink-faint">
          Level {character.level}
        </p>

        {editing ? (
          <div className="mt-2 w-full">
            <input
              value={name}
              autoFocus
              maxLength={60}
              onChange={(event) => setName(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') save()
                if (event.key === 'Escape') setEditing(false)
              }}
              aria-label="Character name"
              data-testid="character-name-input"
              className="w-full rounded-lg border border-line bg-canvas px-3 py-1.5 text-center font-display text-xl outline-none focus:border-gold"
            />

            <div className="mt-3 flex flex-wrap justify-center gap-1.5">
              {AVATARS.map((avatar) => (
                <button
                  key={avatar.key}
                  type="button"
                  title={avatar.name}
                  aria-label={avatar.name}
                  aria-pressed={avatarKey === avatar.key}
                  onClick={() => setAvatarKey(avatar.key)}
                  className={`grid h-8 w-8 place-items-center rounded-lg border text-lg transition ${
                    avatarKey === avatar.key
                      ? 'border-gold bg-gold/12'
                      : 'border-line hover:border-line-strong'
                  }`}
                >
                  {avatar.glyph}
                </button>
              ))}
            </div>

            <div className="mt-3 flex justify-center gap-2">
              <button
                type="button"
                onClick={save}
                disabled={updateCharacter.isPending || !name.trim()}
                data-testid="character-save"
                className="inline-flex items-center gap-1.5 rounded-lg bg-ink px-3 py-1.5 text-xs font-medium text-canvas transition hover:opacity-90 disabled:opacity-40"
              >
                <Check size={13} /> Save
              </button>
              <button
                type="button"
                onClick={() => setEditing(false)}
                className="inline-flex items-center gap-1.5 rounded-lg border border-line px-3 py-1.5 text-xs text-ink-muted transition hover:border-line-strong"
              >
                <X size={13} /> Cancel
              </button>
            </div>
          </div>
        ) : (
          <div className="mt-1.5 flex items-center gap-1.5">
            <h2 className="font-display text-[26px] leading-tight" data-testid="character-name">
              {character.name}
            </h2>
            <button
              type="button"
              onClick={() => setEditing(true)}
              aria-label="Edit character"
              data-testid="character-edit"
              className="rounded-md p-1 text-ink-faint opacity-0 transition hover:bg-surface-sunk hover:text-ink-muted focus-visible:opacity-100 group-hover:opacity-100"
              style={{ opacity: 1 }}
            >
              <Pencil size={13} />
            </button>
          </div>
        )}

        <p className="mt-0.5 font-display text-sm italic text-gold" data-testid="character-title">
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
      </div>

      <dl className="grid grid-cols-3 divide-x divide-line border-t border-line bg-surface-sunk/50">
        <Stat label="Done" value={character.tasksCompleted} testId="stat-tasks" />
        <Stat label="Total XP" value={character.totalXp} testId="stat-xp" />
        <Stat
          label="Badges"
          value={`${character.achievementsUnlocked}/${character.achievementsTotal}`}
          testId="stat-badges"
        />
      </dl>
    </section>
  )
}

function Stat({
  label,
  value,
  testId,
}: {
  label: string
  value: string | number
  testId: string
}) {
  return (
    <div className="px-2 py-3 text-center">
      <dd className="tabular text-lg leading-none font-medium" data-testid={testId}>
        {typeof value === 'number' ? value.toLocaleString() : value}
      </dd>
      <dt className="mt-1.5 text-[9px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        {label}
      </dt>
    </div>
  )
}
