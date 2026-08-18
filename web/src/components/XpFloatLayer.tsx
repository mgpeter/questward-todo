import { useGameFeed } from '../game/GameFeed'

/**
 * Fixed layer so the rising numbers are never clipped by a scroll container.
 *
 * The rise is a CSS animation rather than a scripted one. A reduced-motion request can only
 * reach animations the browser owns, so a float driven from JavaScript would keep sailing up
 * the page for exactly the user who asked it not to. The keyframes and the reduced-motion
 * variant both live in index.css beside the global block that governs them.
 */
export function XpFloatLayer() {
  const { floats } = useGameFeed()

  return (
    <div className="pointer-events-none fixed inset-0 z-50" aria-hidden="true">
      {floats.map((float) => (
        <span
          key={float.id}
          data-testid="xp-float"
          data-amount={float.amount}
          className="xp-float tabular absolute text-[15px] font-semibold"
          style={{
            left: float.x,
            top: float.y,
            color: float.amount >= 0 ? 'var(--gold)' : 'var(--ink-faint)',
            textShadow: float.amount >= 0 ? '0 0 14px var(--gold-glow)' : 'none',
          }}
        >
          {float.amount >= 0 ? `+${float.amount}` : float.amount} XP
        </span>
      ))}
    </div>
  )
}
