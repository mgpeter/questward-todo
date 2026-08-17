/**
 * Mirrors RankTitles.cs on the server. Duplicated rather than fetched because it is
 * static display copy - the server stays the authority on which title is current,
 * and that value arrives on the character payload.
 */
export const RANK_LADDER: { minLevel: number; title: string }[] = [
  { minLevel: 1, title: 'Novice' },
  { minLevel: 3, title: 'Apprentice' },
  { minLevel: 5, title: 'Adept' },
  { minLevel: 8, title: 'Journeyman' },
  { minLevel: 12, title: 'Expert' },
  { minLevel: 17, title: 'Master' },
  { minLevel: 23, title: 'Champion' },
  { minLevel: 30, title: 'Legend' },
]

export const titleForLevel = (level: number): string =>
  [...RANK_LADDER].reverse().find((rank) => level >= rank.minLevel)?.title ?? 'Novice'
