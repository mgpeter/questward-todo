import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import type { Achievement, CompleteResult, ReopenResult } from '../lib/api'
import { titleForLevel } from '../lib/ranks'

export interface XpFloat {
  id: number
  amount: number
  x: number
  y: number
}

export interface AchievementToast {
  id: number
  achievement: Achievement
}

export interface LevelUpEvent {
  level: number
  previousLevel: number
  title: string
  /** Lets the overlay call out a rank change, which only happens on some levels. */
  previousTitle: string
}

interface GameFeedValue {
  floats: XpFloat[]
  toasts: AchievementToast[]
  levelUp: LevelUpEvent | null
  celebrateCompletion: (result: CompleteResult, origin?: DOMRect | null) => void
  registerRefund: (result: ReopenResult, origin?: DOMRect | null) => void
  dismissLevelUp: () => void
  dismissToast: (id: number) => void
}

const GameFeedContext = createContext<GameFeedValue | null>(null)

const FLOAT_LIFETIME_MS = 1200
const TOAST_LIFETIME_MS = 4500

export function GameFeedProvider({ children }: { children: ReactNode }) {
  const [floats, setFloats] = useState<XpFloat[]>([])
  const [toasts, setToasts] = useState<AchievementToast[]>([])
  const [levelUp, setLevelUp] = useState<LevelUpEvent | null>(null)
  const nextId = useRef(1)

  const pushFloat = useCallback((amount: number, origin?: DOMRect | null) => {
    if (amount === 0) return

    const id = nextId.current++
    const x = origin ? origin.left + origin.width / 2 : window.innerWidth / 2
    const y = origin ? origin.top : window.innerHeight / 2

    setFloats((current) => [...current, { id, amount, x, y }])
    window.setTimeout(
      () => setFloats((current) => current.filter((float) => float.id !== id)),
      FLOAT_LIFETIME_MS,
    )
  }, [])

  const pushToast = useCallback((achievement: Achievement) => {
    const id = nextId.current++

    setToasts((current) => [...current, { id, achievement }])
    window.setTimeout(
      () => setToasts((current) => current.filter((toast) => toast.id !== id)),
      TOAST_LIFETIME_MS,
    )
  }, [])

  const celebrateCompletion = useCallback(
    (result: CompleteResult, origin?: DOMRect | null) => {
      pushFloat(result.xpGained, origin)

      if (result.leveledUp) {
        setLevelUp({
          level: result.character.level,
          previousLevel: result.previousLevel,
          title: result.character.title,
          previousTitle: titleForLevel(result.previousLevel),
        })
      }

      // Badges stagger in behind the level-up so they are not all competing at once.
      result.unlockedAchievements.forEach((achievement, index) =>
        window.setTimeout(() => pushToast(achievement), 350 + index * 450),
      )
    },
    [pushFloat, pushToast],
  )

  const registerRefund = useCallback(
    (result: ReopenResult, origin?: DOMRect | null) => pushFloat(-result.xpLost, origin),
    [pushFloat],
  )

  const value = useMemo(
    () => ({
      floats,
      toasts,
      levelUp,
      celebrateCompletion,
      registerRefund,
      dismissLevelUp: () => setLevelUp(null),
      dismissToast: (id: number) =>
        setToasts((current) => current.filter((toast) => toast.id !== id)),
    }),
    [floats, toasts, levelUp, celebrateCompletion, registerRefund],
  )

  return <GameFeedContext.Provider value={value}>{children}</GameFeedContext.Provider>
}

export function useGameFeed() {
  const context = useContext(GameFeedContext)
  if (!context) throw new Error('useGameFeed must be used inside a GameFeedProvider')

  return context
}
