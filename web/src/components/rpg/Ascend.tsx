import { Sparkles } from 'lucide-react'
import { useState } from 'react'
import type { CharacterSheet } from '../../lib/rpg'
import { useAscend } from '../../lib/rpgQueries'
import { ConfirmSheet } from '../ConfirmSheet'

/** What an era leaves behind, and what it takes with it. Stated in full, before the button. */
const KEPT = [
  'Your tasks, and every one you have already finished',
  'Badges, and the count of tasks completed behind them',
  'Essence, including what this ascension pays',
  'Your class, your name and your face',
  'The chronicle: every line of the era you are ending',
]

const LOST = [
  'Your level and all experience, back to level one',
  'Every item, worn or stored, and the affixes on them',
  'Gold and stamina, rendered down to essence',
  'Quests claimed and in progress',
  'Contracts taken, and standing with every banner',
  'Dungeon runs, and the bestiary you have filled in',
]

export function Ascend({ sheet }: { sheet: CharacterSheet }) {
  const [confirming, setConfirming] = useState(false)
  const ascend = useAscend()

  const { count, eligible, minimumLevel, essenceOnAscend } = sheet.ascension

  return (
    <div className="space-y-4" data-testid="ascend">
      <header>
        <h2 className="font-display text-2xl">Ascend</h2>
        <p className="mt-0.5 text-[13px] text-ink-muted">
          End the era and begin again at level one, carrying nothing but essence.
        </p>
      </header>

      {count > 0 && (
        <p className="text-[12px] text-ink-muted">
          You have ascended <span className="text-ink">{count}</span>{' '}
          {count === 1 ? 'time' : 'times'}.
        </p>
      )}

      <div className="panel rounded-2xl p-5">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <p className="font-display text-lg">
            {eligible ? 'The road begins again' : `Ascending opens at level ${minimumLevel}`}
          </p>

          <p className="tabular flex items-center gap-1.5 text-[13px] text-gold">
            <Sparkles size={13} />
            {essenceOnAscend.toLocaleString()} essence
          </p>
        </div>

        <p className="mt-1.5 text-[13px] text-ink-muted">
          {eligible
            ? 'Ten gold and five stamina render down to one essence each, and every level reached is worth five more. Essence buys affixes at the forge and nothing else: it can never become experience, stamina or a finished task.'
            : `You are level ${sheet.level}. Nothing here is hidden from you until then; it is simply not worth doing yet.`}
        </p>

        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <Column label="Kept" items={KEPT} tone="text-ink" />
          <Column label="Spent" items={LOST} tone="text-ink-muted" />
        </div>

        <button
          type="button"
          onClick={() => setConfirming(true)}
          disabled={!eligible || ascend.isPending}
          data-testid="ascend-open"
          className="mt-5 w-full rounded-xl border border-gold/40 bg-gold/10 py-2.5 text-[14px] text-gold transition hover:bg-gold/20 disabled:opacity-40"
        >
          {eligible ? `Ascend for ${essenceOnAscend.toLocaleString()} essence` : 'Not yet'}
        </button>

        {ascend.isError && (
          <p role="alert" className="mt-2.5 text-[12.5px] text-rose">
            {(ascend.error as Error).message}
          </p>
        )}
      </div>

      <ConfirmSheet
        open={confirming}
        onClose={() => setConfirming(false)}
        onConfirm={() =>
          ascend.mutate(undefined, {
            onSuccess: () => setConfirming(false),
          })
        }
        title="Ascend?"
        description="There is no way back to this character as it stands."
        confirmLabel="Ascend"
        pending={ascend.isPending}
        testId="ascend-confirm"
      >
        <p>
          You are level {sheet.level} with {sheet.gold.toLocaleString()} gold and{' '}
          {sheet.stamina.toLocaleString()} stamina. All of it becomes{' '}
          <span className="text-gold">{essenceOnAscend.toLocaleString()} essence</span>, and
          everything else in the list goes.
        </p>

        <Column label="Spent" items={LOST} tone="text-ink-muted" />
      </ConfirmSheet>
    </div>
  )
}

function Column({ label, items, tone }: { label: string; items: string[]; tone: string }) {
  return (
    <div>
      <p className="text-[9.5px] font-medium uppercase tracking-[0.18em] text-ink-faint">{label}</p>

      <ul className={`mt-2 space-y-1.5 text-[13px] ${tone}`}>
        {items.map((item) => (
          <li key={item} className="flex gap-2">
            <span aria-hidden="true" className="text-ink-faint">
              -
            </span>
            {item}
          </li>
        ))}
      </ul>
    </div>
  )
}
