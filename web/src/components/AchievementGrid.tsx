import { Lock } from 'lucide-react'
import { motion } from 'motion/react'
import type { Achievement } from '../lib/api'
import { formatDate } from '../lib/format'

export function AchievementGrid({ achievements }: { achievements: Achievement[] }) {
  const unlocked = achievements.filter((achievement) => achievement.unlocked).length

  return (
    <div data-testid="achievement-grid">
      <header className="mb-4 flex items-baseline justify-between gap-4">
        <div>
          <h2 className="font-display text-2xl">Badges</h2>
          <p className="mt-0.5 text-[13px] text-ink-muted">
            Earned for milestones, not for showing up.
          </p>
        </div>
        <p className="tabular shrink-0 text-[13px] text-ink-faint">
          <span className="text-gold">{unlocked}</span> / {achievements.length}
        </p>
      </header>

      <ul className="grid gap-2.5 sm:grid-cols-2 xl:grid-cols-3">
        {achievements.map((achievement, index) => (
          <motion.li
            key={achievement.key}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: Math.min(index * 0.025, 0.3), duration: 0.28 }}
            data-testid="achievement"
            data-key={achievement.key}
            data-unlocked={achievement.unlocked}
            className={`panel flex items-start gap-3 rounded-xl p-3.5 transition ${
              achievement.unlocked ? 'border-gold/35' : 'opacity-70'
            }`}
          >
            <span
              className={`grid h-11 w-11 shrink-0 place-items-center rounded-xl text-xl ring-1 ${
                achievement.unlocked
                  ? 'bg-gold/12 ring-gold/30'
                  : 'bg-surface-sunk ring-line grayscale'
              }`}
              style={achievement.unlocked ? undefined : { filter: 'grayscale(1)', opacity: 0.45 }}
            >
              {achievement.icon}
            </span>

            <div className="min-w-0 flex-1">
              <p className="flex items-center gap-1.5 font-display text-[15px] leading-tight">
                {achievement.name}
                {!achievement.unlocked && <Lock size={11} className="text-ink-faint" />}
              </p>
              <p className="mt-1 text-[12px] leading-snug text-ink-muted">
                {achievement.unlocked ? achievement.description : achievement.hint}
              </p>
              {achievement.unlocked && achievement.unlockedAt && (
                <p className="tabular mt-1.5 text-[10px] tracking-wide text-gold/80">
                  {formatDate(achievement.unlockedAt)}
                </p>
              )}
            </div>
          </motion.li>
        ))}
      </ul>
    </div>
  )
}
