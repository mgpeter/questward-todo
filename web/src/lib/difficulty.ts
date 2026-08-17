import type { Difficulty, Priority } from './api'

export interface DifficultyMeta {
  value: Difficulty
  label: string
  xp: number
  /** Sets the --tier custom property consumed by .tier-chip and the card accent. */
  tierClass: string
  blurb: string
}

export const DIFFICULTIES: DifficultyMeta[] = [
  { value: 'easy', label: 'Easy', xp: 10, tierClass: 'tier-easy', blurb: 'A few minutes' },
  { value: 'medium', label: 'Medium', xp: 25, tierClass: 'tier-medium', blurb: 'An hour or so' },
  { value: 'hard', label: 'Hard', xp: 50, tierClass: 'tier-hard', blurb: 'A serious block' },
  { value: 'epic', label: 'Epic', xp: 100, tierClass: 'tier-epic', blurb: 'A whole quest' },
]

const byValue = new Map(DIFFICULTIES.map((meta) => [meta.value, meta]))

export const difficultyMeta = (value: Difficulty): DifficultyMeta =>
  byValue.get(value) ?? DIFFICULTIES[1]

export interface PriorityMeta {
  value: Priority
  label: string
}

export const PRIORITIES: PriorityMeta[] = [
  { value: 'low', label: 'Low' },
  { value: 'normal', label: 'Normal' },
  { value: 'high', label: 'High' },
]
