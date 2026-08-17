import { motion } from 'motion/react'
import type { Character } from '../lib/api'
import { ThemeToggle } from './ThemeToggle'
import { XpRail } from './XpRail'

export type TabKey = 'tasks' | 'record' | 'badges'

const TABS: { key: TabKey; label: string }[] = [
  { key: 'tasks', label: 'Quests' },
  { key: 'record', label: 'Record' },
  { key: 'badges', label: 'Badges' },
]

interface HeaderProps {
  character: Character | undefined
  tab: TabKey
  onTabChange: (tab: TabKey) => void
  badgeCount: number
}

export function Header({ character, tab, onTabChange, badgeCount }: HeaderProps) {
  return (
    <header className="sticky top-0 z-30 border-b border-line bg-canvas/85 backdrop-blur-md">
      {/* One rail, reordered rather than duplicated: on narrow screens it wraps onto its
          own row instead of being squeezed between the wordmark and the theme switch. */}
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-4 gap-y-2.5 px-4 py-3 sm:gap-x-6">
        <div className="order-1">
          <Wordmark />
        </div>

        <div className="order-3 w-full sm:order-2 sm:w-auto sm:max-w-sm sm:flex-1 md:max-w-md">
          {character && <XpRail character={character} />}
        </div>

        <div className="order-2 ml-auto sm:order-3">
          <ThemeToggle />
        </div>
      </div>

      <nav className="mx-auto flex max-w-6xl gap-1 px-4" aria-label="Sections">
        {TABS.map((entry) => {
          const active = tab === entry.key

          return (
            <button
              key={entry.key}
              type="button"
              onClick={() => onTabChange(entry.key)}
              aria-current={active ? 'page' : undefined}
              data-testid={`tab-${entry.key}`}
              className={`relative px-3 py-2 text-[13px] font-medium transition-colors ${
                active ? 'text-ink' : 'text-ink-faint hover:text-ink-muted'
              }`}
            >
              {entry.label}
              {entry.key === 'badges' && badgeCount > 0 && (
                <span className="tabular ml-1.5 text-[10px] text-gold">{badgeCount}</span>
              )}
              {active && (
                <motion.span
                  layoutId="tab-underline"
                  transition={{ type: 'spring', stiffness: 420, damping: 34 }}
                  className="absolute inset-x-2 -bottom-px h-[2px] rounded-full bg-gold"
                />
              )}
            </button>
          )
        })}
      </nav>
    </header>
  )
}

function Wordmark() {
  return (
    <div className="flex shrink-0 items-center gap-2.5">
      <span
        aria-hidden="true"
        className="grid h-7 w-7 rotate-45 place-items-center rounded-[7px] border border-gold/50 bg-linear-to-br from-gold/25 to-transparent"
      >
        <span className="-rotate-45 text-[11px] leading-none text-gold">&#9670;</span>
      </span>
      <span className="font-display text-[19px] leading-none tracking-tight">
        Quest<span className="text-gold">ward</span>
      </span>
    </div>
  )
}
