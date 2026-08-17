import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import { useChooseClass, useClasses } from '../../lib/rpgQueries'

interface ClassSelectProps {
  open: boolean
  currentClassKey: string | null
  onClose: () => void
}

export function ClassSelect({ open, currentClassKey, onClose }: ClassSelectProps) {
  const classes = useClasses()
  const chooseClass = useChooseClass()
  const [selected, setSelected] = useState<string | null>(currentClassKey)

  const confirm = () => {
    if (!selected) return

    chooseClass.mutate(selected, { onSuccess: onClose })
  }

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-60 grid place-items-center overflow-y-auto p-4 backdrop-blur-[3px]"
          style={{ backgroundColor: 'rgb(16 14 11 / 0.8)' }}
          role="dialog"
          aria-modal="true"
          aria-label="Choose a class"
          data-testid="class-select"
        >
          <motion.div
            initial={{ scale: 0.96, y: 10 }}
            animate={{ scale: 1, y: 0 }}
            className="panel my-auto w-full max-w-3xl rounded-2xl p-6"
          >
            <h2 className="font-display text-2xl">Choose a class</h2>
            <p className="mt-1 text-[13px] text-ink-muted">
              This sets your ability scores and starting gear. You can change it later; your
              level, XP and badges are never affected.
            </p>

            {classes.isLoading && <div className="mt-5 h-56 animate-pulse rounded-xl bg-surface-sunk" />}

            <div className="mt-5 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-3">
              {classes.data?.map((option) => {
                const active = selected === option.key

                return (
                  <button
                    key={option.key}
                    type="button"
                    onClick={() => setSelected(option.key)}
                    aria-pressed={active}
                    data-testid={`class-option-${option.key}`}
                    className={`rounded-xl border p-3.5 text-left transition ${
                      active
                        ? 'border-gold bg-gold/8'
                        : 'border-line hover:border-line-strong'
                    }`}
                  >
                    <div className="flex items-baseline justify-between gap-2">
                      <span className="font-display text-[17px]">{option.name}</span>
                      <span className="tabular text-[10.5px] text-ink-faint">{option.hitDie}</span>
                    </div>

                    <p className="mt-1 text-[11.5px] leading-snug text-ink-muted">{option.blurb}</p>

                    <div className="mt-2.5 flex flex-wrap gap-1">
                      {option.startingScores
                        .filter((s) => s.score >= 14)
                        .map((score) => (
                          <span
                            key={score.abbreviation}
                            className="tabular rounded-md border border-line bg-surface-sunk px-1.5 py-0.5 text-[10px] text-ink-muted"
                          >
                            {score.abbreviation} {score.score}
                          </span>
                        ))}
                    </div>

                    <p className="mt-2.5 text-[11px] leading-snug">
                      <span className="text-gold">{option.perk.name}</span>
                      <span className="text-ink-muted"> {option.perk.description}</span>
                    </p>
                  </button>
                )
              })}
            </div>

            {chooseClass.isError && (
              <p role="alert" className="mt-3 text-[12px] text-rose">
                {(chooseClass.error as Error).message}
              </p>
            )}

            <div className="mt-6 flex justify-end gap-2">
              {currentClassKey && (
                <button
                  type="button"
                  onClick={onClose}
                  className="rounded-lg border border-line px-4 py-2 text-xs text-ink-muted transition hover:border-line-strong"
                >
                  Cancel
                </button>
              )}
              <button
                type="button"
                onClick={confirm}
                disabled={!selected || chooseClass.isPending}
                data-testid="class-confirm"
                className="rounded-lg bg-ink px-4 py-2 text-xs font-medium text-canvas transition hover:opacity-90 disabled:opacity-40"
              >
                {currentClassKey ? 'Change class' : 'Begin'}
              </button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  )
}
