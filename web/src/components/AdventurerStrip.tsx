import { Coins, Heart, Moon, Shield, Swords, Zap } from 'lucide-react'
import type { ReactNode } from 'react'
import { useSheet } from '../lib/rpgQueries'

/**
 * Class, health, stamina and gold, on the screen where the work happens.
 *
 * Everything here is spent in the adventure tab but earned here, and a resource you only
 * see on the page where you spend it teaches the wrong loop. Completing an Epic task and
 * watching stamina tick up in the corner is the whole of DEC-003 in one glance.
 */
export function AdventurerStrip() {
  const sheet = useSheet()

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

      <div className="min-w-[130px] flex-1" data-testid="strip-health">
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
