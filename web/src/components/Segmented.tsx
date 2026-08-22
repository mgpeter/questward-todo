import { motion } from 'motion/react'
import { useId, type ReactNode } from 'react'

interface SegmentedProps<T extends string> {
  label: string
  value: T
  onChange: (value: T) => void
  /** `hint` sits after the label in a quieter weight, for a count or a unit. */
  options: { value: T; label: string; hint?: ReactNode; testId?: string }[]
  testId?: string
  /**
   * Off when the control should be its own width rather than filling the row - the task
   * filter sits beside two icon buttons and cannot take the whole line.
   */
  grow?: boolean
}

/**
 * A full-width segmented control, standing in for a native select on touch.
 *
 * The inline forms use `<select>`, which is right with a mouse and is a system modal with a
 * spinning drum on a phone - three taps and a full-screen takeover to choose between Low,
 * Normal and High. Every option visible and one tap to pick is the whole trade.
 *
 * One geometry for all of these on purpose: the mockups drifted between radius 10 with a 7px
 * inner and radius 12 with a 9px inner across four different sheets.
 */
export function Segmented<T extends string>({
  label,
  value,
  onChange,
  options,
  testId,
  grow = true,
}: SegmentedProps<T>) {
  // The sliding pill is shared by layoutId, so each control needs its own or the pill flies
  // between the Priority row and the Repeats row whenever either changes.
  const pillId = useId()

  return (
    <div
      role="radiogroup"
      aria-label={label}
      data-testid={testId}
      data-value={value}
      className="flex items-stretch gap-[3px] rounded-xl border border-line bg-surface-sunk p-[3px]"
    >
      {options.map((option) => {
        const active = option.value === value

        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => onChange(option.value)}
            data-testid={option.testId}
            className={`relative min-h-11 rounded-[9px] py-2.5 text-[12.5px] transition-colors ${
              grow ? 'flex-1' : 'px-3'
            }`}
          >
            {active && (
              <motion.span
                layoutId={pillId}
                transition={{ type: 'spring', stiffness: 420, damping: 34 }}
                className="absolute inset-0 rounded-[9px] bg-surface shadow-[0_1px_2px_rgb(0_0_0/0.1)]"
              />
            )}
            <span className={`relative ${active ? 'font-medium text-ink' : 'text-ink-faint'}`}>
              {option.label}
              {option.hint !== undefined && (
                <span className="tabular ml-1.5 text-[10.5px] opacity-60">{option.hint}</span>
              )}
            </span>
          </button>
        )
      })}
    </div>
  )
}
