import { AchievementGrid } from '../components/AchievementGrid'
import { useAchievements } from '../lib/queries'

export function BadgesView() {
  const achievements = useAchievements()

  if (achievements.isLoading) {
    return <div className="panel h-96 animate-pulse rounded-2xl opacity-60" />
  }

  if (achievements.isError || !achievements.data) {
    return (
      <p role="alert" className="panel rounded-xl p-4 text-[13px] text-rose">
        Could not load badges: {(achievements.error as Error)?.message ?? 'unknown error'}
      </p>
    )
  }

  return <AchievementGrid achievements={achievements.data} />
}
