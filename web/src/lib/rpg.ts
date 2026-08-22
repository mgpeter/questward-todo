/** Client types and calls for the adventure layer. */

import type { Difficulty } from './api'

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
  /**
   * The next step at the bench, or null when there is not one.
   *
   * Null is the whole eligibility test: Legendary, consumables and retired keys all arrive
   * without one. The bench used to ask `rarity !== 'legendary'` and so offered potions the
   * server refuses. None of the arithmetic below can be done here - the cost needs the
   * catalogue's base value, which is not on the wire.
   */
  upgrade: UpgradePreview | null
}

/** What one step at the upgrade bench costs, and what the item becomes. */
export interface UpgradePreview {
  toRarity: RarityName
  cost: number
  /** Armour at the next rarity, item and words together, as `armourBonus` is now. */
  armourBonus: number
  abilityBonuses: RollModifier[]
  affixSlots: number
  /** Only true crossing into Epic. Magnitude is 1 at Uncommon and Rare, 2 above. */
  affixesGrow: boolean
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
  stamina: number
  /** Stamina the next restock costs, or null once the day's ladder is spent. */
  nextRerollCost: number | null
  rerollsLeft: number
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

// ---------------------------------------------------------------------- hunts

/**
 * How well a banner knows the hunter. Counted from won contracts on the server and never
 * stored, so it is a record of fights that happened rather than a balance to spend.
 */
export type FactionStandingName = 'unknown' | 'noticed' | 'trusted' | 'respected' | 'sworn'

/**
 * One line of the contract board: an open task, priced, before anybody has paid for it.
 *
 * The whole stat block is quoted rather than summarised because the decision being made is
 * whether one stamina is better spent here or at the tavern, and that is a comparison of
 * stat blocks. Every number is derived on the read from frozen inputs, so opening the board
 * twice quotes the same purse twice.
 */
export interface HuntOffer {
  taskId: string
  title: string
  difficulty: Difficulty
  dueDate: string | null
  /**
   * Measured from the recurrence gate for a recurring task rather than its due date, which
   * is never advanced by completion. A daily task done faithfully is zero here.
   */
  daysOverdue: number
  subtasks: number
  archetypeKey: string
  monsterName: string
  blurb: string
  level: number
  armourClass: number
  maxHitPoints: number
  damage: string
  /** Already bounty-scaled. What the board quotes is the range the win will draw from. */
  minGold: number
  maxGold: number
  dropChance: number
  /** The age multiplier as a percentage. Never below 100 and never above 200 (DEC-013). */
  bountyPercent: number
  factionKey: string | null
  factionName: string | null
  /** What this banner calls a hunter of the current standing. Cosmetic. */
  factionTitle: string | null
  standing: FactionStandingName
  rewardFloor: RarityName
  /** Whether winning also hands over a guaranteed item. Only an overdue contract does. */
  paysContractReward: boolean
  staminaCost: number
}

export interface FactionStanding {
  key: string
  name: string
  blurb: string
  standing: FactionStandingName
  title: string
  /** Contracts won under this banner. Wins, not contracts taken: a hunt fled is nothing. */
  wonHunts: number
  rewardFloor: RarityName
}

export interface HuntBoard {
  /**
   * Every task that could be written up, worst first, and not trimmed by the server.
   *
   * How many the board shows at once is a display decision and is made here: a task card
   * reads its own contract out of this same list, so a list cut off at twenty would tell the
   * twenty-first task it has nothing to offer.
   */
  offers: HuntOffer[]
  /** The contracts already taken: what has been promised, and what has been earned. */
  contracts: HuntContract[]
  factions: FactionStanding[]
  stamina: number
  staminaPerHunt: number
}

/**
 * Where a contract is in its three steps.
 *
 * Accepting is free. Finishing the task discharges it, and only then can it be fought, for
 * the one stamina every fight costs. There is no state in which an unfinished task can be
 * cashed in: a bounty is what finishing pays, never what avoiding pays (DEC-013).
 */
export type HuntContractStatus = 'accepted' | 'discharged' | 'fought' | 'abandoned'

/**
 * A contract taken: the promise, and what discharging it will be worth.
 *
 * Every number was frozen when the contract was accepted, which is why one whose task has
 * since been re-dated, retagged, re-graded, split or deleted still reports exactly what it
 * was written as. Waiting after accepting therefore raises nothing, so there is no reason to
 * sit on one.
 */
export interface HuntContract {
  id: string
  status: HuntContractStatus
  /** Null once the task has been deleted. A discharged contract survives that and stays fightable. */
  taskId: string | null
  taskTitle: string
  archetypeKey: string
  monsterName: string
  blurb: string
  level: number
  armourClass: number
  maxHitPoints: number
  damage: string
  /** Already bounty-scaled. What is quoted is the range the win will draw from. */
  minGold: number
  maxGold: number
  dropChance: number
  daysOverdue: number
  subtasks: number
  /** The age multiplier as a percentage. Never below 100 and never above 200 (DEC-013). */
  bountyPercent: number
  factionKey: string | null
  factionName: string | null
  factionTitle: string | null
  standing: FactionStandingName
  rewardFloor: RarityName
  paysContractReward: boolean
  /** What the fight costs, once the work has unlocked it. Accepting costs nothing. */
  staminaCost: number
  acceptedAt: string
  dischargedAt: string | null
}

/**
 * A contract's fight, live or finished.
 *
 * Every number here was frozen onto the encounter when the fight opened, which is why a
 * fight whose task has since been edited, retagged or deleted still reports exactly what it
 * was opened against. The fight itself is an ordinary encounter driven by the ordinary
 * attack routes, which is why the hunt screen reuses the encounter view rather than a second
 * one.
 */
export interface Hunt {
  encounterId: string
  contractId: string | null
  /** Null once the task has been deleted. The fight survives that and stays fightable. */
  taskId: string | null
  taskTitle: string | null
  archetypeKey: string
  monsterName: string
  level: number
  daysOverdue: number
  subtasks: number
  bountyPercent: number
  factionKey: string | null
  factionName: string | null
  factionTitle: string | null
  standing: FactionStandingName
  encounter: Encounter
}

/** True once the work is done and the fight is the only thing left. */
export const isReadyToFight = (contract: HuntContract): boolean =>
  contract.status === 'discharged'

/** The cap the server enforces, quoted here so the board can say when it has been reached. */
export const BOUNTY_CAP_PERCENT = 200

/** The day the cap binds, the archetype promotes, and waiting stops paying anything more. */
export const BOUNTY_CAP_DAYS = 30

/**
 * The multiplier in the form a purse is read in: "1.4x".
 *
 * A multiplier rather than a bonus, because it is one: the bounty is baked into the gold
 * range the win draws from, not added to the roll afterwards.
 */
export const bountyLabel = (bountyPercent: number): string =>
  `${(bountyPercent / 100).toFixed(bountyPercent % 100 === 0 ? 0 : 2).replace(/0$/, '')}x`

/**
 * How loudly the board should sing about a contract.
 *
 * Four steps up, and no step down: there is no tone here for "you are behind", because
 * DEC-013 says an overdue task is a bounty and never a debuff. The worst thing on the list
 * is the best-paying thing on the list, and it is styled as such.
 */
export type BountyTier = 'none' | 'fresh' | 'rich' | 'legend'

export const bountyTier = (daysOverdue: number): BountyTier =>
  daysOverdue <= 0
    ? 'none'
    : daysOverdue < 7
      ? 'fresh'
      : daysOverdue < BOUNTY_CAP_DAYS
        ? 'rich'
        : 'legend'

/** True once the multiplier has stopped moving. Waiting longer is worth nothing after this. */
export const bountyIsCapped = (bountyPercent: number): boolean =>
  bountyPercent >= BOUNTY_CAP_PERCENT

/** "12 days overdue", said as an age rather than as an accusation. */
export const describeAge = (daysOverdue: number): string =>
  daysOverdue <= 0
    ? 'On time'
    : `${daysOverdue} ${pluralDays(daysOverdue)} old`

/** "day" or "days", for prose that has to read the count out loud. */
export const pluralDays = (days: number): string => (days === 1 ? 'day' : 'days')

const STANDING_LABELS: Record<FactionStandingName, string> = {
  unknown: 'Unknown',
  noticed: 'Noticed',
  trusted: 'Trusted',
  respected: 'Respected',
  sworn: 'Sworn',
}

export const standingLabel = (standing: FactionStandingName): string =>
  STANDING_LABELS[standing] ?? standing

/**
 * Where a standing sits on its ladder, for a meter. Sworn is the top and stays full.
 *
 * Derived from the tier rather than from the win count, because the win count has no
 * ceiling: a hunter with 200 wins is exactly as Sworn as one with 40.
 */
export const standingRung = (standing: FactionStandingName): number =>
  ['unknown', 'noticed', 'trusted', 'respected', 'sworn'].indexOf(standing)

export const STANDING_RUNGS = 4
