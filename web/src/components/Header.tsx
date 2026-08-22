import { Zap } from 'lucide-react'
import { motion } from 'motion/react'
import type { Character } from '../lib/api'
import type { TabKey } from '../game/Navigation'
import { useSheet } from '../lib/rpgQueries'
import { useIsMobile } from '../lib/useMediaQuery'
import { AdventurerHud } from './AdventurerStrip'
import { AccountMenu } from './AccountMenu'
import { SoundToggle } from './SoundToggle'
import { ThemeToggle } from './ThemeToggle'
import { XpRail } from './XpRail'

const TABS: { key: TabKey; label: string }[] = [
  { key: 'tasks', label: 'Tasks' },
  { key: 'adventure', label: 'Adventure' },
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
  const isMobile = useIsMobile()

  // One row and one HUD line, where the desktop header takes two rows and a tab strip. The
  // wordmark loses its text and keeps its diamond: on a 390px header the six letters were
  // competing with the only reading that changes.
  if (isMobile) {
    return (
      <header className="sticky top-0 z-30 border-b border-line bg-canvas/94 backdrop-blur-md">
        <div className="flex items-center gap-3 px-3.5 py-2.5">
          <WordmarkDiamond />
          {character && <XpRail character={character} />}
          {/* Theme and sound moved behind the avatar; AccountMenu draws them in its sheet. */}
          <AccountMenu />
        </div>

        <AdventurerHud />
      </header>
    )
  }

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

        <div className="order-2 ml-auto flex items-center gap-2 sm:order-3">
          <StaminaPip />
          <SoundToggle />
          <ThemeToggle />
          <AccountMenu />
        </div>
      </div>

      <nav className="mx-auto flex max-w-6xl gap-1 px-4" aria-label="Sections">
        <Tabs tab={tab} onTabChange={onTabChange} badgeCount={badgeCount} />
      </nav>
    </header>
  )
}

function Tabs({ tab, onTabChange, badgeCount }: Omit<HeaderProps, 'character'>) {
  return (
    <>
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
    </>
  )
}

/**
 * Stamina in the header, because it is the bridge between the two halves of the app:
 * it only comes from finishing real work, and it is the only thing that buys a fight.
 */
function StaminaPip() {
  const sheet = useSheet()

  if (!sheet.data) return null

  const { stamina } = sheet.data

  return (
    <span
      title={`${stamina} stamina. Complete tasks to earn more.`}
      data-testid="stamina-pip"
      data-stamina={stamina}
      className={`tabular hidden items-center gap-1 rounded-full border px-2 py-1 text-[11px] sm:flex ${
        stamina > 0 ? 'border-teal/40 bg-teal/8 text-teal' : 'border-line text-ink-faint'
      }`}
    >
      <Zap size={11} />
      {stamina}
    </span>
  )
}

function WordmarkDiamond({ size = 'h-7 w-7' }: { size?: string }) {
  return (
    <span
      aria-hidden="true"
      className={`grid ${size} shrink-0 rotate-45 place-items-center rounded-[7px] border border-gold/50 bg-linear-to-br from-gold/25 to-transparent`}
    >
      <span className="-rotate-45 text-[11px] leading-none text-gold">&#9670;</span>
    </span>
  )
}

function Wordmark() {
  return (
    <div className="flex shrink-0 items-center gap-2.5">
      <WordmarkDiamond />
      <span className="font-display text-[19px] leading-none tracking-tight">
        Quest<span className="text-gold">ward</span>
      </span>
    </div>
  )
}
