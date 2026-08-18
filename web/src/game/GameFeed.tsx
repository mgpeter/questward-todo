import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import type { Achievement, CompleteResult, ReopenResult, SetStatusResult } from '../lib/api'
import { titleForLevel } from '../lib/ranks'
import type { HuntContract } from '../lib/rpg'

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
  /**
   * The contract the last completion discharged, until it has been read.
   *
   * Finishing the work is what unlocks the fight, and that happens on the task screen, two
   * tabs away from anything that renders a contract. Without this the creature the player has
   * just earned the right to fight would be waiting on a board they are not looking at. Held
   * here rather than in the query cache for the same reason a fight's outcome is: it belongs
   * to the player's attention, not to the server.
   */
  contract: HuntContract | null
  celebrateCompletion: (result: CompleteResult, origin?: DOMRect | null) => void
  registerRefund: (result: ReopenResult, origin?: DOMRect | null) => void
  celebrateStatusChange: (result: SetStatusResult, origin?: DOMRect | null) => void
  dismissLevelUp: () => void
  dismissContract: () => void
  dismissToast: (id: number) => void
}

const GameFeedContext = createContext<GameFeedValue | null>(null)

const FLOAT_LIFETIME_MS = 1200
const TOAST_LIFETIME_MS = 4500

export function GameFeedProvider({ children }: { children: ReactNode }) {
  const [floats, setFloats] = useState<XpFloat[]>([])
  const [toasts, setToasts] = useState<AchievementToast[]>([])
  const [levelUp, setLevelUp] = useState<LevelUpEvent | null>(null)
  const [contract, setContract] = useState<HuntContract | null>(null)
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

      // Null on every completion that had no contract standing on it, which is most of them.
      if (result.hunt) setContract(result.hunt)

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

  /**
   * A drop into Done should feel exactly like ticking the box, and a drop back out like
   * unticking it. One handler for both, keyed off the sign of the delta.
   */
  const celebrateStatusChange = useCallback(
    (result: SetStatusResult, origin?: DOMRect | null) => {
      pushFloat(result.xpDelta, origin)

      if (result.hunt) setContract(result.hunt)

      if (result.leveledUp) {
        setLevelUp({
          level: result.character.level,
          previousLevel: result.previousLevel,
          title: result.character.title,
          previousTitle: titleForLevel(result.previousLevel),
        })
      }

      result.unlockedAchievements.forEach((achievement, index) =>
        window.setTimeout(() => pushToast(achievement), 350 + index * 450),
      )
    },
    [pushFloat, pushToast],
  )

  // The three dismissals are useCallback'd rather than written inline inside the memo below,
  // and that is a correctness fix rather than a tidying one. An inline arrow gets a fresh
  // identity on every recompute of this value, and this value recomputes on its own timers:
  // pushFloat prunes a float at 1.2s and pushToast prunes a toast at 4.5s, each producing a
  // new array. Every consumer that lists one of these in an effect's dependencies then tore
  // its effect down and ran it again for an event that had not changed, which replayed the
  // contract toast's kill and coin cues about a second after they first sounded and restarted
  // its eleven second dismissal each time. The state setters are stable, so these are too.
  const dismissLevelUp = useCallback(() => setLevelUp(null), [])
  const dismissContract = useCallback(() => setContract(null), [])

  const dismissToast = useCallback(
    (id: number) => setToasts((current) => current.filter((toast) => toast.id !== id)),
    [],
  )

  const value = useMemo(
    () => ({
      floats,
      toasts,
      levelUp,
      contract,
      celebrateCompletion,
      registerRefund,
      celebrateStatusChange,
      dismissLevelUp,
      dismissContract,
      dismissToast,
    }),
    [
      floats,
      toasts,
      levelUp,
      contract,
      celebrateCompletion,
      registerRefund,
      celebrateStatusChange,
      dismissLevelUp,
      dismissContract,
      dismissToast,
    ],
  )

  return <GameFeedContext.Provider value={value}>{children}</GameFeedContext.Provider>
}

export function useGameFeed() {
  const context = useContext(GameFeedContext)
  if (!context) throw new Error('useGameFeed must be used inside a GameFeedProvider')

  return context
}
