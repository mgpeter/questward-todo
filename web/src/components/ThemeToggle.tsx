import { Monitor, Moon, Sun } from 'lucide-react'
import { motion } from 'motion/react'
import { useTheme, type ThemePreference } from '../theme/ThemeProvider'

const OPTIONS: { value: ThemePreference; label: string; Icon: typeof Sun }[] = [
  { value: 'light', label: 'Light', Icon: Sun },
  { value: 'dark', label: 'Dark', Icon: Moon },
  { value: 'system', label: 'System', Icon: Monitor },
]

interface ThemeToggleProps {
  /**
   * `pill` is the header's icon-only row. `stacked` is three labelled columns for the
   * account sheet, where there is width to spell the options out and a 28px target would
   * be too small to hit.
   */
  variant?: 'pill' | 'stacked'
}

export function ThemeToggle({ variant = 'pill' }: ThemeToggleProps) {
  const { preference, setPreference } = useTheme()
  const stacked = variant === 'stacked'

  return (
    <div
      role="radiogroup"
      aria-label="Colour theme"
      data-testid="theme-toggle"
      className={
        stacked
          ? 'flex items-stretch gap-[3px] rounded-xl border border-line bg-surface-sunk p-[3px]'
          : 'flex items-center gap-0.5 rounded-full border border-line bg-surface-sunk p-0.5'
      }
    >
      {OPTIONS.map(({ value, label, Icon }) => {
        const active = preference === value

        return (
          <button
            key={value}
            type="button"
            role="radio"
            aria-checked={active}
            aria-label={`${label} theme`}
            title={`${label} theme`}
            data-theme-option={value}
            onClick={() => setPreference(value)}
            className={
              stacked
                ? 'relative flex flex-1 flex-col items-center gap-1.5 rounded-[9px] py-3 text-[12px] transition-colors'
                : 'relative grid h-7 w-8 place-items-center rounded-full transition-colors'
            }
          >
            {active && (
              <motion.span
                layoutId="theme-pill"
                transition={{ type: 'spring', stiffness: 420, damping: 34 }}
                className={`absolute inset-0 bg-surface shadow-[0_1px_2px_rgb(0_0_0/0.14)] ring-1 ring-line-strong/50 ${
                  stacked ? 'rounded-[9px]' : 'rounded-full'
                }`}
              />
            )}
            <Icon
              size={stacked ? 18 : 14}
              strokeWidth={2}
              className={`relative transition-colors ${
                active ? 'text-gold' : 'text-ink-faint hover:text-ink-muted'
              }`}
            />
            {stacked && (
              <span
                className={`relative transition-colors ${
                  active ? 'font-medium text-gold' : 'text-ink-faint'
                }`}
              >
                {label}
              </span>
            )}
          </button>
        )
      })}
    </div>
  )
}
