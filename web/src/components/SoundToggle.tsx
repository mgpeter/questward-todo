import { Volume2, VolumeX } from 'lucide-react'
import { useState, useSyncExternalStore } from 'react'
import { isSoundOn, prefersReducedMotion, setSoundOn, subscribeSound } from '../lib/sound'

interface SoundToggleProps {
  /**
   * `icon` is the header's single round button. `row` is a labelled switch for the account
   * sheet, where the copy that was only ever a tooltip can finally be read on a phone.
   */
  variant?: 'icon' | 'row'
}

/**
 * One control, two states, off by default.
 *
 * Sound that starts on is the most hostile default a web app has, and a user who wants it
 * will find one obvious button. Reduced motion is read once, to explain the default rather
 * than to enforce it: someone who turned sound on and later asks for reduced motion keeps
 * their sound, because quietly reversing a decision they made by hand is worse than the
 * thing it would be protecting them from.
 */
export function SoundToggle({ variant = 'icon' }: SoundToggleProps) {
  const on = useSyncExternalStore(subscribeSound, isSoundOn)
  const [reduced] = useState(prefersReducedMotion)

  const title = on
    ? 'Combat sound on'
    : reduced
      ? 'Combat sound off. You have asked for reduced motion, so it stays off until you turn it on.'
      : 'Combat sound off'

  const Icon = on ? Volume2 : VolumeX

  if (variant === 'row') {
    return (
      <button
        type="button"
        role="switch"
        aria-checked={on}
        data-testid="sound-toggle"
        data-sound={on ? 'on' : 'off'}
        onClick={() => setSoundOn(!on)}
        className="flex w-full items-center gap-3 border-t border-line py-3.5 text-left"
      >
        <span className="min-w-0 flex-1">
          <span className="block text-[14.5px]">Combat sound</span>
          <span className="mt-0.5 block text-[11.5px] text-ink-faint">
            Off by default. Dice, hits and coins.
          </span>
        </span>

        <span
          aria-hidden="true"
          className={`flex h-7 w-[46px] shrink-0 items-center rounded-full border p-[2px] transition-colors ${
            on ? 'justify-end border-transparent bg-gold/90' : 'justify-start border-line bg-surface-sunk'
          }`}
        >
          <span className="h-[22px] w-[22px] rounded-full bg-surface shadow-[0_1px_2px_rgb(0_0_0/0.2)]" />
        </span>
      </button>
    )
  }

  return (
    <button
      type="button"
      aria-pressed={on}
      aria-label={on ? 'Turn combat sound off' : 'Turn combat sound on'}
      title={title}
      data-testid="sound-toggle"
      data-sound={on ? 'on' : 'off'}
      onClick={() => setSoundOn(!on)}
      className={`grid h-8 w-8 place-items-center rounded-full border transition-colors ${
        on
          ? 'border-gold/50 bg-gold/10 text-gold'
          : 'border-line bg-surface-sunk text-ink-faint hover:text-ink-muted'
      }`}
    >
      <Icon size={14} strokeWidth={2} />
    </button>
  )
}
