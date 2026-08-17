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
