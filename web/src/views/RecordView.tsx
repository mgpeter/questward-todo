import { StatsPanel } from '../components/StatsPanel'
import type { Character } from '../lib/api'
import { useStats } from '../lib/queries'

export function RecordView({ character }: { character: Character | undefined }) {
  const stats = useStats()

  if (stats.isLoading || !character) {
    return <div className="panel h-96 animate-pulse rounded-2xl opacity-60" />
  }

  if (stats.isError || !stats.data) {
    return (
      <p role="alert" className="panel rounded-xl p-4 text-[13px] text-rose">
        Could not load stats: {(stats.error as Error)?.message ?? 'unknown error'}
      </p>
    )
  }

  return <StatsPanel stats={stats.data} character={character} />
}
