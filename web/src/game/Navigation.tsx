import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'

export type TabKey = 'tasks' | 'adventure' | 'record' | 'badges'

export type AdventurePanel =
  | 'sheet'
  | 'tavern'
  | 'hunts'
  | 'dungeons'
  | 'shop'
  | 'quests'
  | 'bestiary'
  | 'lore'
  | 'chronicle'
  | 'ascend'

interface NavigationValue {
  tab: TabKey
  panel: AdventurePanel
  setTab: (tab: TabKey) => void
  setPanel: (panel: AdventurePanel) => void
  /** Crosses to another section, optionally landing on a particular panel within it. */
  goTo: (tab: TabKey, panel?: AdventurePanel) => void
}

const NavigationContext = createContext<NavigationValue | null>(null)

/**
 * Which section is open, held above the sections themselves.
 *
 * It used to live in App as plain state, which was enough while nothing but the tab strip
 * could move it. Taking a contract from a task card has to hand the player the fight it just
 * opened, and a card three components deep cannot reach App's setState. There is no router
 * to lean on and adding one for four tabs would be heavier than this.
 */
export function NavigationProvider({ children }: { children: ReactNode }) {
  const [tab, setTab] = useState<TabKey>('tasks')
  const [panel, setPanel] = useState<AdventurePanel>('sheet')

  const goTo = useCallback((next: TabKey, nextPanel?: AdventurePanel) => {
    setTab(next)
    if (nextPanel) setPanel(nextPanel)

    // The destination is a whole screen further down a scrolled page, so a jump that left
    // the viewport where it was would look like the click did nothing.
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }, [])

  const value = useMemo(
    () => ({ tab, panel, setTab, setPanel, goTo }),
    [tab, panel, goTo],
  )

  return <NavigationContext.Provider value={value}>{children}</NavigationContext.Provider>
}

export function useNavigation() {
  const context = useContext(NavigationContext)
  if (!context) throw new Error('useNavigation must be used inside a NavigationProvider')

  return context
}
