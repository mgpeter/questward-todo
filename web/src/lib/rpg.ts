/** Client types and calls for the adventure layer. */

export type ItemSlotName = 'weapon' | 'armour' | 'trinket' | 'consumable'
export type RarityName = 'common' | 'uncommon' | 'rare' | 'epic' | 'legendary'
export type EncounterStatusName = 'active' | 'won' | 'lost' | 'fled'

export interface Perk {
  key: string
  name: string
  description: string
}

export interface AbilityView {
  key: string
  abbreviation: string
  score: number
  modifier: number
  bonusFromItems: number
}

export interface ClassAbilityView {
  key: string
  name: string
  description: string
  usesPerEncounter: number
  remaining: number
}

/** One threshold of a set bonus. Active once enough pieces are worn. */
export interface SetTier {
  pieces: number
  description: string
  active: boolean
}

export interface SetProgress {
  key: string
  name: string
  blurb: string
  equipped: number
  total: number
  tiers: SetTier[]
}

export interface CharacterSheet {
  classKey: string | null
  className: string | null
  level: number
  abilities: AbilityView[]
  armourClass: number
  attackBonus: number
  damage: string
  attackAbility: string
  currentHitPoints: number
  maxHitPoints: number
  proficiencyBonus: number
  criticalOn: number
  stamina: number
  gold: number
  perk: Perk | null
  classAbilities: ClassAbilityView[]
  /** Null when already at full health. */
  nextRegenerationAt: string | null
  fullyHealedAt: string | null
  /** Gold a full heal costs right now. Zero when whole. */
  restCost: number
  /** The forge's currency. Earned only by breaking items down. */
  essence: number
  /**
   * Only sets with at least one piece equipped. The rest are discovered through
   * InventoryItem.setName on the pieces themselves.
   */
  sets: SetProgress[]
}

export interface ClassOption {
  key: string
  name: string
  blurb: string
  hitDie: string
  primaryAbility: string
  secondaryAbility: string
  startingScores: AbilityView[]
  perk: Perk
  startingWeapon: string
  startingArmour: string
}

export interface Monster {
  key: string
  name: string
  blurb: string
  level: number
  armourClass: number
  maxHitPoints: number
  damage: string
  minGold: number
  maxGold: number
  staminaCost: number
}

export interface DieRoll {
  sides: number
  value: number
  kept: boolean
}

export interface RollModifier {
  label: string
  value: number
}

export interface CombatRoll {
  round: number
  actor: 'player' | 'monster'
  kind: 'attack' | 'damage' | 'note' | 'loot'
  dice: DieRoll[]
  modifiers: RollModifier[]
  total: number
  target: number | null
  outcome: 'hit' | 'miss' | 'critical' | 'fumble' | 'none'
  critical: boolean
  /** The whole line, mechanical clause and flavour together. */
  text: string
  /** The narrative tail of `text`, null when the line is purely mechanical. */
  flavour: string | null
}

/**
 * What an effect does. One member per place in the round the server reads it, so a new
 * effect is a new case here rather than another flag to interpret.
 */
export type EffectKindName = 'weakened' | 'empowered' | 'guarded' | 'poisoned' | 'regenerating'

/** Who an effect sits on. Both sides share one array, because both die with the fight. */
export type EffectTargetName = 'player' | 'monster'

/**
 * One affliction or blessing riding a fight.
 *
 * `rounds` is applications remaining, not rounds elapsed, which is why a strip that reads
 * "2 left" can still be spent twice in a single exchange.
 */
export interface StatusEffect {
  kind: EffectKindName
  target: EffectTargetName
  rounds: number
  magnitude: number
  /** Key of whatever applied it: an ability, an item or a monster phase. */
  source: string
}

/**
 * The server's marker for an effect meant to last the whole fight rather than a stated
 * number of applications. Shown as a word, because "99 rounds left" reads as a bug.
 */
export const LASTING_ROUNDS = 99

export const isLasting = (effect: StatusEffect): boolean => effect.rounds >= LASTING_ROUNDS

interface EffectShape {
  label: string
  /** True when the effect helps whoever is carrying it. */
  favoursBearer: boolean
  /**
   * The magnitude in as few characters as it can be said in, or null for a kind that
   * carries none.
   *
   * Terse on purpose. A chip cannot wrap without breaking mid-phrase, and "+3 to hit and
   * damage" pushes one past the width of the narrow shell on its own. The sentence version
   * lives in `explain` and is shown on hover, where there is room for it.
   */
  detail: ((magnitude: number) => string) | null
  explain: (magnitude: number) => string
}

const EFFECT_SHAPES: Record<EffectKindName, EffectShape> = {
  weakened: {
    label: 'Weakened',
    favoursBearer: false,
    detail: null,
    explain: () => 'Attacks are made at disadvantage',
  },
  empowered: {
    label: 'Empowered',
    favoursBearer: true,
    detail: (magnitude) => `+${magnitude} hit/dmg`,
    explain: (magnitude) => `Adds ${magnitude} to attack rolls and to the damage they deal`,
  },
  guarded: {
    label: 'Guarded',
    favoursBearer: true,
    detail: (magnitude) => `+${magnitude} AC`,
    explain: (magnitude) => `${magnitude} harder to hit`,
  },
  poisoned: {
    label: 'Poisoned',
    favoursBearer: false,
    detail: (magnitude) => `${magnitude}/round`,
    explain: (magnitude) => `Loses ${magnitude} hit points at the end of each round`,
  },
  regenerating: {
    label: 'Regenerating',
    favoursBearer: true,
    detail: (magnitude) => `+${magnitude}/round`,
    explain: (magnitude) => `Regains ${magnitude} hit points at the end of each round`,
  },
}

export const effectLabel = (kind: EffectKindName): string => EFFECT_SHAPES[kind]?.label ?? kind

/** The magnitude clause, or null for a kind that carries no number worth printing. */
export const effectDetail = (effect: StatusEffect): string | null => {
  const detail = EFFECT_SHAPES[effect.kind]?.detail

  return detail && effect.magnitude > 0 ? detail(effect.magnitude) : null
}

/** The whole rule in a sentence, for the hover where there is room to say it. */
export const effectExplain = (effect: StatusEffect): string =>
  EFFECT_SHAPES[effect.kind]?.explain(effect.magnitude) ?? effectLabel(effect.kind)

/**
 * Whether an effect is in the player's favour, which is the only reading the colour is
 * allowed to carry. Poison on the monster and poison on the player are the same mechanic
 * and must not share a colour, or the strip teaches nothing.
 */
export const favoursPlayer = (effect: StatusEffect): boolean =>
  EFFECT_SHAPES[effect.kind]?.favoursBearer === (effect.target === 'player')

/** How long is left, in words. Never "99". */
export const effectRemaining = (effect: StatusEffect): string =>
  isLasting(effect) ? 'lasting' : `${effect.rounds} left`

/**
 * The effects riding a fight, with the two enum-shaped fields folded to the casing the rest
 * of this module reads.
 *
 * Every other enum on this wire arrives lowercased by hand at the mapping site, the way
 * `status` does. Folding here rather than trusting that costs one pass over an array that is
 * never more than a handful long, and buys a strip that still renders if a producer ever
 * sends `Poisoned` instead of `poisoned`. Silently showing nothing is the one failure mode
 * this feature cannot afford: an effect the player cannot see reads as a bug in the fight.
 */
const readEffects = (encounter: Encounter): StatusEffect[] =>
  (encounter.effects ?? []).map((effect) => ({
    ...effect,
    kind: String(effect.kind).toLowerCase() as EffectKindName,
    target: String(effect.target).toLowerCase() as EffectTargetName,
  }))

/**
 * The effects riding one combatant, worst first so an affliction is never pushed off the
 * end of a narrow strip by a blessing.
 *
 * Tolerates an encounter carrying no effects array at all. The field is additive on the
 * wire, and a client that reached the server before it shipped must render an empty strip
 * rather than throw inside a live fight.
 */
export const effectsOn = (encounter: Encounter, target: EffectTargetName): StatusEffect[] =>
  readEffects(encounter)
    .filter((effect) => effect.target === target && effect.rounds > 0)
    .sort((a, b) => Number(favoursPlayer(a)) - Number(favoursPlayer(b)))

export interface Encounter {
  id: string
  monsterKey: string
  monsterName: string
  monsterHitPoints: number
  monsterMaxHitPoints: number
  status: EncounterStatusName
  round: number
  /** The highest boss phase this fight has entered. Zero for anything with no phases. */
  phase: number
  /** The catalog name of that phase. Null until one has been entered. */
  phaseName: string | null
  /**
   * Afflictions and blessings riding the fight, both combatants in one array.
   *
   * Optional because it is additive on the wire: read it through `effectsOn`, never
   * directly, so a response without it renders an empty strip instead of throwing.
   */
  effects?: StatusEffect[]
  goldAwarded: number
  log: CombatRoll[]
  startedAt: string
  endedAt: string | null
}

export type DungeonRunStatusName = 'active' | 'cleared' | 'failed' | 'abandoned'

/** Where a room sits relative to the player. A finished run has no current room. */
export type DungeonRoomState = 'cleared' | 'current' | 'ahead'

export interface Dungeon {
  key: string
  name: string
  blurb: string
  /** The character level the dungeon unlocks at. It is never retired afterwards. */
  level: number
  rooms: number
  bossKey: string
  bossName: string
  clearGold: number
  rewardFloor: RarityName
  staminaPerRoom: number
  /** What the whole run costs, because a room is a fight and every fight is paid for. */
  totalStaminaCost: number
}

export interface DungeonRoom {
  index: number
  monsterKey: string
  monsterName: string
  state: DungeonRoomState
}

export interface DungeonRun {
  id: string
  dungeonKey: string
  name: string
  status: DungeonRunStatusName
  rooms: DungeonRoom[]
  /** Rooms won. Also the index of the room to enter next, which is all a reload needs. */
  depth: number
  goldAwarded: number
  /** The fight in progress, resumed through the ordinary attack routes. Null when none is open. */
  encounter: Encounter | null
  startedAt: string
  endedAt: string | null
}

/** The room to enter next, or null when the run is finished. */
export const nextRoom = (run: DungeonRun): DungeonRoom | null =>
  run.rooms.find((room) => room.state === 'current') ?? null

/** Everything past the current room, which is what the player is deciding whether to face. */
export const roomsAhead = (run: DungeonRun): DungeonRoom[] =>
  run.rooms.filter((room) => room.state === 'ahead')

/**
 * True while the run can still be pushed further. A cleared, failed or abandoned run keeps
 * its rooms so the track can be read back, so status is the only honest test.
 */
export const isRunOpen = (run: DungeonRun): boolean => run.status === 'active'

/** The last room is always the boss, which is what wires boss phases to dungeons. */
export const isBossRoom = (run: DungeonRun, room: DungeonRoom): boolean =>
  room.index === run.rooms.length - 1

export interface InventoryItem {
  id: string
  itemKey: string
  /** The display name, affixes included: "Keen Silvered Blade of the Fox". */
  name: string
  blurb: string
  slot: ItemSlotName
  rarity: RarityName
  isEquipped: boolean
  damage: string | null
  /** Item plus affixes. Set bonuses are not attributed to a piece; they live on the sheet. */
  armourBonus: number
  abilityBonuses: RollModifier[]
  sellValue: number
  acquiredAt: string
  /** The affix word, not its key: "Keen". Null when the slot is empty. */
  prefix: string | null
  /** "of the Fox". Null when the slot is empty. */
  suffix: string | null
  setName: string | null
  /** How many affixes this rarity and slot can hold at all. Zero on a Common. */
  affixSlots: number
  salvageValue: number
  imbueCost: number
  reforgeCost: number
  /** How many this row holds. One for everything worn; a consumable stacks. */
  quantity: number
  /** What using one does, at this row's rarity. Null for anything that is not a consumable. */
  useDescription: string | null
}

/** Usable in a fight, which is the same test the use endpoint applies. */
export const isConsumable = (item: InventoryItem): boolean =>
  item.slot === 'consumable' && item.useDescription !== null

/**
 * The bag's consumables, ordered best first within a key.
 *
 * A row is a stack, so this is already one entry per key and rarity; sorting by rarity
 * descending puts the Rare draught in front of the Common one, which is the order a player
 * reaching for a potion mid-fight wants and the opposite of acquisition order.
 */
export const usableConsumables = (items: InventoryItem[]): InventoryItem[] =>
  items
    .filter(isConsumable)
    .sort(
      (a, b) =>
        RARITY_ORDER.indexOf(b.rarity) - RARITY_ORDER.indexOf(a.rarity) ||
        a.name.localeCompare(b.name),
    )

/** Affixes in force, which is what imbue and reforge price themselves against. */
export const affixesInForce = (item: InventoryItem): number =>
  (item.prefix ? 1 : 0) + (item.suffix ? 1 : 0)

export const canImbue = (item: InventoryItem): boolean =>
  affixesInForce(item) < item.affixSlots

export const canReforge = (item: InventoryItem): boolean => affixesInForce(item) > 0

export interface QuestObjective {
  id: string
  description: string
  current: number
  required: number
  isComplete: boolean
}

export interface Quest {
  key: string
  name: string
  description: string
  objectives: QuestObjective[]
  isComplete: boolean
  isClaimed: boolean
  claimedAt: string | null
  rewardGold: number
  rewardItemName: string | null
  isLocked: boolean
  minimumLevel: number
}

export interface QuestAdvance {
  key: string
  name: string
  progress: string
  justCompleted: boolean
}

export interface AttackResult {
  encounter: Encounter
  rolls: CombatRoll[]
  playerHitPoints: number
  playerMaxHitPoints: number
  goldAwarded: number
  /** What the monster itself dropped. Null when it dropped nothing. */
  loot: InventoryItem | null
  /**
   * The dungeon's guaranteed reward, on the round that cleared its last room.
   *
   * Beside `loot` rather than inside it because a clear round hands over two items, and the
   * boss's own drop can fail its roll on the very round the run pays out.
   */
  clearReward: InventoryItem | null
  questsAdvanced: QuestAdvance[]
  sheet: CharacterSheet
}

export interface EquipResult {
  sheet: CharacterSheet
  inventory: InventoryItem[]
}

/** Ordered worst to best, so the UI can compare rarities. */
export const RARITY_ORDER: RarityName[] = ['common', 'uncommon', 'rare', 'epic', 'legendary']

export interface ChronicleSummary {
  fought: number
  won: number
  lost: number
  fled: number
  goldEarned: number
  mostFoughtMonster: string | null
  mostFoughtCount: number
}

export interface Chronicle {
  summary: ChronicleSummary
  encounters: Encounter[]
}

export interface ShopOffer {
  offerId: string
  itemKey: string
  name: string
  blurb: string
  slot: ItemSlotName
  rarity: RarityName
  damage: string | null
  armourBonus: number
  abilityBonuses: RollModifier[]
  price: number
  affordable: boolean
  /** Already bought today. Each offer sells once, and the shelf restocks at rotatesAt. */
  soldOut: boolean
}

export interface Shop {
  offers: ShopOffer[]
  rotatesAt: string
  gold: number
}

export interface PurchaseResult {
  item: InventoryItem
  goldSpent: number
  gold: number
}

export interface UpgradeResult {
  item: InventoryItem
  from: RarityName
  to: RarityName
  goldSpent: number
  gold: number
}

export interface SalvageResult {
  essenceGained: number
  essence: number
}

/** The reply to both imbue and reforge: they differ only in what they cost. */
export interface CraftResult {
  item: InventoryItem
  essenceSpent: number
  essence: number
}

export interface RestResult {
  goldSpent: number
  gold: number
  hitPoints: number
  maxHitPoints: number
}

/**
 * One row of the codex. Every monster in the catalog is sent, met or not, so the panel can
 * show what is still out there.
 */
export interface BestiaryEntry {
  key: string
  name: string
  /** Null until met. The description is the reward for the first sighting. */
  blurb: string | null
  level: number
  isDiscovered: boolean
  isSlain: boolean
  /** Sightings, not wins: a fight that went badly still counts as having met the thing. */
  encounters: number
  kills: number
  goldTaken: number
  /** Fewest rounds to a kill. Zero means never killed. */
  bestRound: number
  firstSeenAt: string | null
  lastSeenAt: string | null
}

export interface Bestiary {
  entries: BestiaryEntry[]
  discovered: number
  slain: number
  total: number
}

export interface LoreFragment {
  key: string
  title: string
  /** Null until unlocked. The body is the whole of the reward. */
  body: string | null
  isUnlocked: boolean
  /** What would unlock it, in words: "Defeat the Goblin 10 times". */
  requirement: string
}

export interface LorePlace {
  key: string
  name: string
  blurb: string
  fragments: LoreFragment[]
  unlocked: number
  total: number
}

export interface Lore {
  places: LorePlace[]
  unlocked: number
  total: number
}

/**
 * Splits a combat line into its mechanical sentence and the flavour the API appended to it.
 *
 * The seam is marked by the server, not inferred here. It used to be found by cutting at the
 * last sentence break, which is wrong for every mechanical line that is already two
 * sentences: "6 damage. Goblin has 4 hit points left." put the remaining hit points, the one
 * number the player is actually tracking, in the faint style reserved for decoration, and
 * tagged it as flavour. Only the server knows whether it appended anything, so it says.
 *
 * `line` comes back empty for a line that is nothing but narration, which is how the opening
 * of a fight arrives.
 */
export function splitFlavour(
  text: string,
  flavour?: string | null,
): { line: string; flavour: string | null } {
  // A log written before the API marked its flavour still arrives without it. Rendering it
  // whole is right: unmarked means unknown, and guessing is what this replaced.
  if (!flavour || !text.endsWith(flavour)) return { line: text, flavour: null }

  return { line: text.slice(0, text.length - flavour.length).trimEnd(), flavour }
}
