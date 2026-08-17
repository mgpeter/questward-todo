export interface Avatar {
  key: string
  glyph: string
  name: string
}

/** The medallion frame carries the styling; the glyph just needs to be recognisable. */
export const AVATARS: Avatar[] = [
  { key: 'fox', glyph: '\u{1F98A}', name: 'Fox' },
  { key: 'owl', glyph: '\u{1F989}', name: 'Owl' },
  { key: 'wolf', glyph: '\u{1F43A}', name: 'Wolf' },
  { key: 'stag', glyph: '\u{1F98C}', name: 'Stag' },
  { key: 'bear', glyph: '\u{1F43B}', name: 'Bear' },
  { key: 'cat', glyph: '\u{1F431}', name: 'Cat' },
  { key: 'dragon', glyph: '\u{1F409}', name: 'Dragon' },
  { key: 'frog', glyph: '\u{1F438}', name: 'Frog' },
  { key: 'octopus', glyph: '\u{1F419}', name: 'Octopus' },
  { key: 'hedgehog', glyph: '\u{1F994}', name: 'Hedgehog' },
]

export const avatarFor = (key: string): Avatar =>
  AVATARS.find((avatar) => avatar.key === key) ?? AVATARS[0]
