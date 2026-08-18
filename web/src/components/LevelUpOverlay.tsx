import { AnimatePresence, motion } from 'motion/react'
import { useEffect } from 'react'
import { useGameFeed } from '../game/GameFeed'
import { play } from '../lib/sound'

const RAY_COUNT = 14
const SPARK_COUNT = 18
const MEDALLION = 128

export function LevelUpOverlay() {
  const { levelUp, dismissLevelUp } = useGameFeed()
  const rankChanged = levelUp ? levelUp.title !== levelUp.previousTitle : false

  useEffect(() => {
    if (!levelUp) return

    // The one cue allowed to sound pleased, and it belongs to finishing tasks: a level can
    // only ever come from there.
    play('levelUp')

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') dismissLevelUp()
    }

    // Long enough to register, short enough not to block the next completion.
    const timer = window.setTimeout(dismissLevelUp, 6000)
    window.addEventListener('keydown', onKey)

    return () => {
      window.clearTimeout(timer)
      window.removeEventListener('keydown', onKey)
    }
  }, [levelUp, dismissLevelUp])

  return (
    <AnimatePresence>
      {levelUp && (
        <motion.div
          key="level-up"
          role="dialog"
          aria-modal="true"
          aria-label={`Level ${levelUp.level} reached`}
          data-testid="level-up-overlay"
          data-level={levelUp.level}
          onClick={dismissLevelUp}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.25 }}
          className="fixed inset-0 z-60 grid cursor-pointer place-items-center backdrop-blur-[3px]"
          // A fixed dark scrim rather than a themed one: in light mode a pale wash
          // leaves the page behind fully legible and the moment reads as an accident.
          style={{ backgroundColor: 'rgb(16 14 11 / 0.82)' }}
        >
          <motion.div
            initial={{ scale: 0.6, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: 'spring', stiffness: 240, damping: 17, delay: 0.06 }}
            className="flex flex-col items-center px-8"
          >
            {/* Rays and sparks are anchored to the medallion itself, so the burst
                radiates from the number rather than from the middle of the column. */}
            <div className="relative grid place-items-center" style={{ width: MEDALLION, height: MEDALLION }}>
              <Rays />
              <Sparks />

              <div
                className="relative grid h-32 w-32 place-items-center rounded-full border-2 border-gold/70 bg-linear-to-b from-gold/30 to-gold/5"
                style={{ boxShadow: '0 0 70px var(--gold-glow), inset 0 0 34px var(--gold-glow)' }}
              >
                <span
                  className="tabular text-[54px] leading-none font-semibold text-gold-bright"
                  data-testid="level-up-number"
                >
                  {levelUp.level}
                </span>
              </div>
            </div>

            <motion.p
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.3 }}
              className="mt-7 text-[11px] font-medium uppercase tracking-[0.42em] text-gold-bright"
            >
              Level up
            </motion.p>

            <motion.p
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.4 }}
              className="tabular mt-3 font-display text-4xl text-[#f4ece0]"
              data-testid="level-up-headline"
            >
              Level {levelUp.level}
            </motion.p>

            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              transition={{ delay: 0.52 }}
              className="mt-2.5 flex items-center gap-2"
            >
              {rankChanged && (
                <span className="rounded-full border border-gold/50 bg-gold/15 px-2 py-0.5 text-[9px] font-medium uppercase tracking-[0.18em] text-gold-bright">
                  New rank
                </span>
              )}
              <span
                className={`font-display text-[15px] italic ${
                  rankChanged ? 'text-gold-bright' : 'text-[#a89e8d]'
                }`}
                data-testid="level-up-title"
              >
                {levelUp.title}
              </span>
            </motion.div>

            <motion.button
              type="button"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              transition={{ delay: 0.68 }}
              onClick={dismissLevelUp}
              data-testid="level-up-dismiss"
              className="mt-8 rounded-full border border-[#4a4133] px-6 py-2 text-xs font-medium text-[#a89e8d] transition hover:border-gold hover:text-gold-bright"
            >
              Onward
            </motion.button>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  )
}

function Rays() {
  return (
    <motion.div
      aria-hidden="true"
      className="absolute top-1/2 left-1/2"
      initial={{ scale: 0.65, opacity: 0, rotate: 0 }}
      animate={{ scale: 1, opacity: 0.45, rotate: 20 }}
      transition={{ duration: 1.5, ease: 'easeOut' }}
    >
      {Array.from({ length: RAY_COUNT }, (_, index) => (
        <span
          key={index}
          className="absolute top-0 left-0 origin-top"
          style={{
            width: 2,
            height: 250,
            marginLeft: -1,
            transform: `rotate(${(360 / RAY_COUNT) * index}deg)`,
            background: 'linear-gradient(to bottom, var(--gold-bright), transparent 80%)',
          }}
        />
      ))}
    </motion.div>
  )
}

function Sparks() {
  return (
    <div aria-hidden="true" className="absolute top-1/2 left-1/2">
      {Array.from({ length: SPARK_COUNT }, (_, index) => {
        const angle = (360 / SPARK_COUNT) * index + (index % 3) * 5
        const distance = 110 + (index % 4) * 36
        const radians = (angle * Math.PI) / 180

        return (
          <motion.span
            key={index}
            className="absolute rounded-full"
            style={{
              width: index % 3 === 0 ? 5 : 3,
              height: index % 3 === 0 ? 5 : 3,
              backgroundColor: 'var(--gold-bright)',
            }}
            initial={{ x: 0, y: 0, opacity: 0, scale: 0.5 }}
            animate={{
              x: Math.cos(radians) * distance,
              y: Math.sin(radians) * distance,
              opacity: [0, 1, 0],
              scale: [0.5, 1, 0.4],
            }}
            transition={{ duration: 1.1 + (index % 5) * 0.12, ease: 'easeOut', delay: 0.05 }}
          />
        )
      })}
    </div>
  )
}
