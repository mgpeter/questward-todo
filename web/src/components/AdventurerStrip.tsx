import { Coins, Heart, Moon, Shield, Swords, Zap } from 'lucide-react'
import type { ReactNode } from 'react'
import { useSheet } from '../lib/rpgQueries'
import { useIsMobile } from '../lib/useMediaQuery'

/**
 * Class, health, stamina and gold, on the screen where the work happens.
 *
 * Everything here is spent in the adventure tab but earned here, and a resource you only
 * see on the page where you spend it teaches the wrong loop. Completing an Epic task and
 * watching stamina tick up in the corner is the whole of DEC-003 in one glance.
 */
export function AdventurerStrip() {
  const isMobile = useIsMobile()
  const sheet = useSheet()

  // On a phone these four readings live in the header instead, as AdventurerHud, because
  // health and stamina have to survive scrolling. Nulling here rather than guarding the two
  // views that render this keeps the rule in one place - a third view added later would
  // otherwise get it wrong, and a strip and a HUD both on screen is two elements reporting
  // the same number.
  if (isMobile) return null

  if (sheet.isLoading) {
    return <div className="panel h-[58px] animate-pulse rounded-2xl opacity-60" />
  }

  if (!sheet.data) return null

  const { classKey, className, level, currentHitPoints, maxHitPoints, stamina, gold } = sheet.data
  const healthFraction = maxHitPoints > 0 ? currentHitPoints / maxHitPoints : 0

  return (
    <div
      className="panel flex flex-wrap items-center gap-x-5 gap-y-3 rounded-2xl px-4 py-3"
      data-testid="adventurer-strip"
    >
      <div className="flex min-w-0 items-center gap-2.5">
        <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full border border-line-strong text-gold">
          {classKey ? <Swords size={14} /> : <Shield size={14} />}
        </span>
        <div className="min-w-0">
          <p className="truncate font-display text-[15px]" data-testid="strip-class">
            {className ?? 'Unclassed'}
          </p>
          <p className="text-[10.5px] tracking-[0.14em] text-ink-faint uppercase">
            Level {level}
          </p>
        </div>
      </div>

      {/* Capped: flex-1 alone let the bar swallow every spare pixel, so a 380px slab of
          teal dominated a strip whose other three readings are two characters wide. */}
      <div className="min-w-[130px] max-w-[230px] flex-1" data-testid="strip-health">
        <div className="flex items-baseline justify-between text-[10.5px] text-ink-faint">
          <span className="flex items-center gap-1 tracking-[0.14em] uppercase">
            <Heart size={10} /> Health
          </span>
          <span className="tabular">
            {currentHitPoints}
            <span className="opacity-60">/{maxHitPoints}</span>
          </span>
        </div>
        <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-surface-sunk">
          <div
            className="h-full rounded-full transition-[width] duration-500"
            style={{
              width: `${Math.max(0, Math.min(1, healthFraction)) * 100}%`,
              // Green while healthy, red once it is genuinely worrying. Read off the
              // fraction so the colour cannot disagree with the number beside it.
              backgroundColor: healthFraction > 0.5 ? 'var(--color-teal)' : 'var(--color-rose)',
            }}
          />
        </div>
      </div>

      <Resource icon={<Zap size={11} />} label="Stamina" testId="strip-stamina">
        {stamina}
      </Resource>

      <Resource icon={<Coins size={11} />} label="Gold" testId="strip-gold">
        {gold}
      </Resource>

      {sheet.data.nextRegenerationAt && (
        <span
          title="Hit points come back on their own, or all at once at the tavern."
          className="flex items-center gap-1 text-[10.5px] text-ink-faint"
        >
          <Moon size={10} />
          healing
        </span>
      )}
    </div>
  )
}

function Resource({
  icon,
  label,
  testId,
  children,
}: {
  icon: ReactNode
  label: string
  testId: string
  children: ReactNode
}) {
  return (
    <div data-testid={testId}>
      <p className="flex items-center gap-1 text-[10.5px] tracking-[0.14em] text-ink-faint uppercase">
        {icon}
        {label}
      </p>
      <p className="tabular mt-0.5 text-[15px]">{children}</p>
    </div>
  )
}

/**
 * The character sheet's headline numbers as one line, for the mobile header.
 *
 * The six ability scores are deliberately not here. At 390px this row has 362px to spend,
 * and six of "STR +2" in the mono face is 263px of it before the class name, the health bar
 * or a single resource: they fit only by evicting everything that changes minute to minute.
 * Armour class and attack stand in for them, being what the abilities are read for anyway.
 *
 * Wraps rather than truncating, because the widths here are not bounded - four figures of
 * gold and three of hit points are both reachable - and verify-ui asserts the document
 * never scrolls sideways at 390px.
 *
 * Separate test ids from the strip on purpose: an assertion written for one must not quietly
 * pass against the other.
 */
export function AdventurerHud() {
  const sheet = useSheet()

  if (!sheet.data) return null

  const {
    className,
    currentHitPoints,
    maxHitPoints,
    stamina,
    gold,
    essence,
    armourClass,
    attackBonus,
    nextRegenerationAt,
  } = sheet.data

  const fraction = maxHitPoints > 0 ? currentHitPoints / maxHitPoints : 0
  const hurt = fraction <= 0.5

  return (
    <div
      className="flex flex-wrap items-center gap-x-2 gap-y-1 border-t border-line bg-surface-sunk/55 px-3.5 py-[7px] text-[11px] text-ink-muted"
      data-testid="adventurer-hud"
    >
      <span className="font-display max-w-[7rem] truncate text-[12.5px]" data-testid="hud-class">
        {className ?? 'Unclassed'}
      </span>

      <span aria-hidden="true" className="h-[11px] w-px shrink-0 bg-line" />

      <span className="flex shrink-0 items-center gap-1.5" data-testid="hud-health">
        <span className={`tabular ${hurt ? 'text-rose' : ''}`}>
          HP {currentHitPoints}/{maxHitPoints}
        </span>
        {/* Small, and worth the 24px: the number says how much is left, the bar says how
            close that is to none, and at a glance the second question is the urgent one. */}
        <span
          aria-hidden="true"
          className="h-1 w-6 overflow-hidden rounded-full bg-surface-sunk ring-1 ring-line/70 ring-inset"
        >
          <span
            className="block h-full rounded-full transition-[width] duration-500"
            style={{
              width: `${Math.max(0, Math.min(1, fraction)) * 100}%`,
              backgroundColor: hurt ? 'var(--color-rose)' : 'var(--color-teal)',
            }}
          />
        </span>
      </span>

      <span className="tabular shrink-0" data-testid="hud-stamina">
        STA {stamina}
      </span>
      <span className="tabular shrink-0" data-testid="hud-gold">
        GOLD {gold}
      </span>
      <span className="tabular shrink-0" title="Essence, the forge's currency" data-testid="hud-essence">
        ESS {essence}
      </span>
      <span className="tabular shrink-0" title="Armour class" data-testid="hud-ac">
        AC {armourClass}
      </span>
      <span className="tabular shrink-0" title="Attack bonus" data-testid="hud-attack">
        ATK {attackBonus >= 0 ? '+' : ''}
        {attackBonus}
      </span>

      {nextRegenerationAt && (
        <span
          title="Hit points come back on their own, or all at once at the tavern."
          className="ml-auto shrink-0 text-[10px] text-ink-faint"
        >
          healing
        </span>
      )}
    </div>
  )
}
