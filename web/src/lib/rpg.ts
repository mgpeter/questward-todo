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
  text: string
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
  name: string
  blurb: string
  slot: ItemSlotName
  rarity: RarityName
  isEquipped: boolean
  damage: string | null
  armourBonus: number
  abilityBonuses: RollModifier[]
  sellValue: number
  acquiredAt: string
}

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

export interface RestResult {
  goldSpent: number
  gold: number
  hitPoints: number
  maxHitPoints: number
}
