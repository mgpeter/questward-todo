import { useCallback, useMemo, useSyncExternalStore } from 'react'

/**
 * One MediaQueryList per query string, shared by every caller.
 *
 * Not a micro-optimisation. useSyncExternalStore reads its snapshot during render, so two
 * components asking the same question in the same pass have to read the same object. A
 * fresh MediaQueryList per hook instance is also a fresh listener per instance for nothing.
 */
const lists = new Map<string, MediaQueryList>()

function listFor(query: string): MediaQueryList | null {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return null

  let list = lists.get(query)

  if (!list) {
    list = window.matchMedia(query)
    lists.set(query, list)
  }

  return list
}

/**
 * A media query as a reactive boolean.
 *
 * useSyncExternalStore rather than useState plus an effect, which is what ThemeProvider does
 * for the colour-scheme query. The difference matters here: this value decides which of two
 * component trees renders, in more than a dozen places. Reading the snapshot during render
 * is what guarantees every one of them sees the same answer in the same pass. Two of them
 * disagreeing is DEC-010's duplicated XP rail all over again - two elements carrying one
 * test id, and two ARIA progressbars.
 */
export function useMediaQuery(query: string): boolean {
  const list = useMemo(() => listFor(query), [query])

  const subscribe = useCallback(
    (onStoreChange: () => void) => {
      if (!list) return () => {}

      list.addEventListener('change', onStoreChange)
      return () => list.removeEventListener('change', onStoreChange)
    },
    [list],
  )

  // Read during render, so the first paint is already the right tree. The effect-driven
  // equivalent paints the desktop board once and then corrects itself, which at 390px is a
  // visible flash of three columns collapsing into one.
  const getSnapshot = useCallback(() => list?.matches ?? false, [list])

  // Only reached where matchMedia is missing. False means desktop, which is the tree the
  // existing markup already assumes.
  const getServerSnapshot = useCallback(() => false, [])

  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot)
}

/**
 * The exact complement of Tailwind's `sm:`, which is `(width >= 40rem)`.
 *
 * In rem rather than 639.98px on purpose: Tailwind v4 breakpoints are rem, so a reader with
 * a 20px root font moves the CSS breakpoint and leaves a pixel query sitting behind it.
 */
export const MOBILE_QUERY = '(width < 40rem)'

export function useIsMobile(): boolean {
  return useMediaQuery(MOBILE_QUERY)
}

/**
 * The reactive counterpart to the one-shot read in lib/sound.ts.
 *
 * That one is right where it is used, deciding a default at call time. Anything that draws
 * the setting has to re-render when it changes.
 */
export function usePrefersReducedMotion(): boolean {
  return useMediaQuery('(prefers-reduced-motion: reduce)')
}
