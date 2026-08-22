import { BarChart3, List, Plus, Swords, Trophy } from 'lucide-react'
import type { ComponentType } from 'react'
import type { TabKey } from '../game/Navigation'
import { useNavigation } from '../game/Navigation'

interface BottomNavProps {
  badgeCount: number
  onAdd: () => void
}

const TABS: { key: TabKey; label: string; Icon: ComponentType<{ size?: number }> }[] = [
  { key: 'tasks', label: 'Tasks', Icon: List },
  { key: 'adventure', label: 'Adventure', Icon: Swords },
  { key: 'record', label: 'Record', Icon: BarChart3 },
  { key: 'badges', label: 'Badges', Icon: Trophy },
]

/**
 * The four sections and the add button, within reach of a thumb.
 *
 * Five columns rather than four with the add button floating: a floating action button over
 * a scrolling list covers the last row of it, and the one row nobody wants covered is the
 * oldest task. Sitting in the bar means the list can simply end above it.
 */
export function BottomNav({ badgeCount, onAdd }: BottomNavProps) {
  const { tab, setTab } = useNavigation()

  const [tasks, adventure, record, badges] = TABS

  return (
    <nav
      aria-label="Sections"
      data-testid="bottom-nav"
      className="fixed inset-x-0 bottom-0 z-30 grid grid-cols-5 items-center border-t border-line bg-canvas/96 pt-2 pb-[calc(0.75rem+env(safe-area-inset-bottom))] backdrop-blur-md"
    >
      <Tab entry={tasks} active={tab === tasks.key} onSelect={setTab} />
      <Tab entry={adventure} active={tab === adventure.key} onSelect={setTab} />

      <div className="grid place-items-center">
        <button
          type="button"
          onClick={onAdd}
          aria-label="Add a quest"
          data-testid="bottom-nav-add"
          className="grid h-[52px] w-[52px] place-items-center rounded-full bg-ink text-canvas shadow-[0_8px_20px_-8px_rgb(60_45_20/0.7)] transition active:scale-95"
        >
          <Plus size={22} strokeWidth={2.2} />
        </button>
      </div>

      <Tab entry={record} active={tab === record.key} onSelect={setTab} />
      <Tab entry={badges} active={tab === badges.key} onSelect={setTab} count={badgeCount} />
    </nav>
  )
}

function Tab({
  entry,
  active,
  onSelect,
  count,
}: {
  entry: (typeof TABS)[number]
  active: boolean
  onSelect: (tab: TabKey) => void
  count?: number
}) {
  const { key, label, Icon } = entry

  return (
    <button
      type="button"
      onClick={() => onSelect(key)}
      aria-current={active ? 'page' : undefined}
      data-testid={`bottom-nav-${key}`}
      // min-h rather than a fixed height: the badge count adds a third line to one cell,
      // and a fixed height would either clip it or pad the other four to match.
      className={`flex min-h-11 flex-col items-center justify-center gap-1 text-[10.5px] transition-colors ${
        active ? 'text-gold' : 'text-ink-faint'
      }`}
    >
      <Icon size={20} />
      {label}
      {count !== undefined && count > 0 && (
        <span className="tabular text-[9px] leading-none text-gold">{count}</span>
      )}
    </button>
  )
}
