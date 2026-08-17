import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'

export type ThemePreference = 'light' | 'dark' | 'system'
export type ResolvedTheme = 'light' | 'dark'

export const THEME_STORAGE_KEY = 'questward.theme'

interface ThemeContextValue {
  preference: ThemePreference
  resolved: ResolvedTheme
  setPreference: (preference: ThemePreference) => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

const darkQuery = () => window.matchMedia('(prefers-color-scheme: dark)')

function readStoredPreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY)
    if (stored === 'light' || stored === 'dark' || stored === 'system') return stored
  } catch {
    // Private mode or storage disabled; fall through to the system default.
  }

  return 'system'
}

function applyTheme(resolved: ResolvedTheme) {
  document.documentElement.classList.toggle('dark', resolved === 'dark')
  document.documentElement.dataset.theme = resolved
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] = useState<ThemePreference>(readStoredPreference)
  const [systemIsDark, setSystemIsDark] = useState(() => darkQuery().matches)

  // "system" has to stay live: flipping the OS theme should move the app with it.
  useEffect(() => {
    const media = darkQuery()
    const onChange = (event: MediaQueryListEvent) => setSystemIsDark(event.matches)

    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [])

  const resolved: ResolvedTheme =
    preference === 'system' ? (systemIsDark ? 'dark' : 'light') : preference

  useEffect(() => applyTheme(resolved), [resolved])

  const setPreference = useCallback((next: ThemePreference) => {
    setPreferenceState(next)

    try {
      localStorage.setItem(THEME_STORAGE_KEY, next)
    } catch {
      // Not fatal - the choice simply will not survive a reload.
    }
  }, [])

  const value = useMemo(
    () => ({ preference, resolved, setPreference }),
    [preference, resolved, setPreference],
  )

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

export function useTheme() {
  const context = useContext(ThemeContext)
  if (!context) throw new Error('useTheme must be used inside a ThemeProvider')

  return context
}
