import { Volume2, VolumeX } from 'lucide-react'
import { useState, useSyncExternalStore } from 'react'
import { isSoundOn, prefersReducedMotion, setSoundOn, subscribeSound } from '../lib/sound'

/**
 * One control, two states, off by default.
 *
 * Sound that starts on is the most hostile default a web app has, and a user who wants it
 * will find one obvious button. Reduced motion is read once, to explain the default rather
 * than to enforce it: someone who turned sound on and later asks for reduced motion keeps
 * their sound, because quietly reversing a decision they made by hand is worse than the
 * thing it would be protecting them from.
 */
export function SoundToggle() {
  const on = useSyncExternalStore(subscribeSound, isSoundOn)
  const [reduced] = useState(prefersReducedMotion)

  const title = on
    ? 'Combat sound on'
    : reduced
      ? 'Combat sound off. You have asked for reduced motion, so it stays off until you turn it on.'
      : 'Combat sound off'

  const Icon = on ? Volume2 : VolumeX

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
