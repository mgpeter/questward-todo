import type { Character, Stats } from '../lib/api'
import { DIFFICULTIES } from '../lib/difficulty'
import { RANK_LADDER } from '../lib/ranks'

interface StatsPanelProps {
  stats: Stats
  character: Character
}

export function StatsPanel({ stats, character }: StatsPanelProps) {
  return (
    <div className="space-y-6" data-testid="stats-panel">
      <div>
        <h2 className="font-display text-2xl">Record</h2>
        <p className="mt-0.5 text-[13px] text-ink-muted">
          What {character.name} has to show for it so far.
        </p>
      </div>

      <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-4">
        <StatTile label="Completed" value={stats.completedTasks} />
        <StatTile label="Open" value={stats.openTasks} />
        <StatTile label="Overdue" value={stats.overdueTasks} tone={stats.overdueTasks > 0 ? 'alert' : 'plain'} />
        <StatTile label="Total XP" value={stats.totalXp} tone="gold" />
      </div>

      <ActivityChart days={stats.last14Days} />
      <DifficultyChart stats={stats} />
      <RankLadder level={character.level} />
    </div>
  )
}

function StatTile({
  label,
  value,
  tone = 'plain',
}: {
  label: string
  value: number
  tone?: 'plain' | 'gold' | 'alert'
}) {
  const valueColor =
    tone === 'gold' ? 'text-gold' : tone === 'alert' ? 'text-rose' : 'text-ink'

  return (
    <div className="panel rounded-xl px-3.5 py-3">
      <p className={`tabular text-[22px] leading-none font-medium ${valueColor}`}>
        {value.toLocaleString()}
      </p>
      <p className="mt-1.5 text-[9.5px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        {label}
      </p>
    </div>
  )
}

/** Single series over time: one hue, no legend needed, the heading names it. */
function ActivityChart({ days }: { days: Stats['last14Days'] }) {
  const peak = Math.max(1, ...days.map((day) => day.completed))
  const total = days.reduce((sum, day) => sum + day.completed, 0)

  return (
    <section className="panel rounded-2xl p-4" data-testid="activity-chart">
      <header className="mb-4 flex items-baseline justify-between gap-3">
        <h3 className="text-[11px] font-medium uppercase tracking-[0.16em] text-ink-faint">
          Last 14 days
        </h3>
        <p className="tabular text-[11px] text-ink-faint">
          <span className="text-ink-muted">{total}</span> completed
        </p>
      </header>

      <div className="flex h-24 items-end gap-[3px]">
        {days.map((day) => {
          const height = day.completed === 0 ? 3 : Math.max(8, (day.completed / peak) * 96)
          const date = new Date(`${day.date}T00:00:00`)

          return (
            <div
              key={day.date}
              className="group relative flex flex-1 flex-col justify-end"
              style={{ height: 96 }}
            >
              <div
                className="w-full rounded-t-[4px] transition-colors"
                style={{
                  height,
                  backgroundColor: day.completed === 0 ? 'var(--line)' : 'var(--gold)',
                  opacity: day.completed === 0 ? 1 : 0.35 + 0.65 * (day.completed / peak),
                }}
              />

              <div className="pointer-events-none absolute bottom-full left-1/2 z-10 mb-1.5 hidden -translate-x-1/2 whitespace-nowrap rounded-md border border-line bg-surface px-2 py-1 text-[10.5px] shadow-lift group-hover:block">
                <span className="tabular text-ink">
                  {day.completed} {day.completed === 1 ? 'task' : 'tasks'}
                </span>
                <span className="tabular text-ink-faint"> &middot; {day.xpEarned} XP</span>
                <span className="block text-ink-faint">
                  {date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
                </span>
              </div>
            </div>
          )
        })}
      </div>

      <div className="mt-2 flex justify-between text-[9.5px] tracking-wide text-ink-faint">
        <span>
          {new Date(`${days[0]?.date}T00:00:00`).toLocaleDateString(undefined, {
            month: 'short',
            day: 'numeric',
          })}
        </span>
        <span>Today</span>
      </div>
    </section>
  )
}

/** Four categories, each direct-labelled, so identity never rests on colour alone. */
function DifficultyChart({ stats }: { stats: Stats }) {
  const peak = Math.max(1, ...stats.byDifficulty.map((entry) => entry.completed))

  return (
    <section className="panel rounded-2xl p-4" data-testid="difficulty-chart">
      <h3 className="mb-4 text-[11px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        Completed by difficulty
      </h3>

      <ul className="space-y-2.5">
        {DIFFICULTIES.map((meta) => {
          const entry = stats.byDifficulty.find((item) => item.difficulty === meta.value)
          const completed = entry?.completed ?? 0
          const xp = entry?.xpEarned ?? 0

          return (
            <li key={meta.value} className={`${meta.tierClass} flex items-center gap-3`}>
              <span className="w-14 shrink-0 text-[11.5px] text-ink-muted">{meta.label}</span>

              <div className="h-[9px] flex-1 overflow-hidden rounded-full bg-surface-sunk">
                <div
                  className="h-full rounded-full transition-all"
                  style={{
                    width: `${Math.max(completed === 0 ? 0 : 3, (completed / peak) * 100)}%`,
                    backgroundColor: 'var(--mark)',
                  }}
                />
              </div>

              <span className="tabular w-20 shrink-0 text-right text-[11px] text-ink-faint">
                <span className="text-ink-muted">{completed}</span>
                <span className="mx-1 opacity-50">&middot;</span>
                {xp} XP
              </span>
            </li>
          )
        })}
      </ul>
    </section>
  )
}

function RankLadder({ level }: { level: number }) {
  return (
    <section className="panel rounded-2xl p-4" data-testid="rank-ladder">
      <h3 className="mb-4 text-[11px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        Ranks
      </h3>

      <ol className="space-y-0">
        {RANK_LADDER.map((rank, index) => {
          const next = RANK_LADDER[index + 1]
          const current = level >= rank.minLevel && (!next || level < next.minLevel)
          const reached = level >= rank.minLevel

          return (
            <li
              key={rank.title}
              className="flex items-center gap-3 border-b border-line py-2 last:border-0"
              data-current={current}
            >
              <span
                className={`h-1.5 w-1.5 shrink-0 rounded-full ${
                  current ? 'bg-gold' : reached ? 'bg-teal' : 'bg-line-strong'
                }`}
              />
              <span
                className={`flex-1 font-display text-[15px] ${
                  current ? 'text-gold' : reached ? 'text-ink' : 'text-ink-faint'
                }`}
              >
                {rank.title}
              </span>
              <span className="tabular text-[11px] text-ink-faint">Lv {rank.minLevel}+</span>
            </li>
          )
        })}
      </ol>
    </section>
  )
}
