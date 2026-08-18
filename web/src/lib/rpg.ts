/** Client types and calls for the adventure layer. */

export type ItemSlotName = 'weapon' | 'armour' | 'trinket'
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

export interface Encounter {
  id: string
  monsterKey: string
  monsterName: string
  monsterHitPoints: number
  monsterMaxHitPoints: number
  status: EncounterStatusName
  round: number
  goldAwarded: number
  log: CombatRoll[]
  startedAt: string
  endedAt: string | null
}

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
}

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
  loot: InventoryItem | null
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
