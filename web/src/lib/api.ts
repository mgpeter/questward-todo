/** Typed client for the Questward API. Same-origin in production, Vite-proxied in dev. */
import type * as Rpg from './rpg'

export type Difficulty = 'easy' | 'medium' | 'hard' | 'epic'
export type Priority = 'low' | 'normal' | 'high'
export type TaskStatus = 'all' | 'open' | 'done'

/** Where a task sits on the board. The server calls this Status; 'all' above is a filter. */
export type TaskProgress = 'todo' | 'inProgress' | 'completed'

export type Recurrence = 'none' | 'daily' | 'weekly' | 'monthly'

export const taskProgressOrder: TaskProgress[] = ['todo', 'inProgress', 'completed']

export const taskProgressLabels: Record<TaskProgress, string> = {
  todo: 'To do',
  inProgress: 'In progress',
  completed: 'Done',
}

export const recurrenceLabels: Record<Recurrence, string> = {
  none: 'Once',
  daily: 'Daily',
  weekly: 'Weekly',
  monthly: 'Monthly',
}

export interface Task {
  id: string
  parentId: string | null
  title: string
  notes: string | null
  difficulty: Difficulty
  priority: Priority
  tags: string[]
  xpValue: number
  dueDate: string | null
  status: TaskProgress
  isCompleted: boolean
  completedAt: string | null
  startedAt: string | null
  xpAwarded: number
  staminaAwarded: number
  recurrence: Recurrence
  /** False when finishing this pays nothing: a subtask, or a repeat inside its period. */
  awardsProgression: boolean
  daysOverdue: number
  sortOrder: number
  subtasks: Task[]
  createdAt: string
  updatedAt: string
}

export interface Character {
  name: string
  avatarKey: string
  level: number
  title: string
  totalXp: number
  xpIntoLevel: number
  xpForNextLevel: number
  xpToNextLevel: number
  tasksCompleted: number
  achievementsUnlocked: number
  achievementsTotal: number
  createdAt: string
}

export interface Achievement {
  key: string
  name: string
  description: string
  hint: string
  icon: string
  unlocked: boolean
  unlockedAt: string | null
}

export interface CompleteResult {
  task: Task
  xpGained: number
  character: Character
  leveledUp: boolean
  previousLevel: number
  unlockedAchievements: Achievement[]
  /**
   * The contract this completion discharged, if one was standing on the task.
   *
   * Discharging pays nothing. It unlocks the fight, which still costs the one stamina every
   * fight costs, so what arrives here is an invitation and not a purse.
   *
   * Optional on the wire and null the rest of the time. The server discharges it after the
   * completion has committed, so a task always finishes even when the contract does not, and
   * a client that predates the field still reads every other member.
   */
  hunt?: Rpg.HuntContract | null
}

export interface ReopenResult {
  task: Task
  xpLost: number
  character: Character
  leveledDown: boolean
  previousLevel: number
}

export interface DifficultyBreakdown {
  difficulty: Difficulty
  completed: number
  xpEarned: number
}

export interface DailyCompletion {
  date: string
  completed: number
  xpEarned: number
}

export interface Stats {
  totalTasks: number
  openTasks: number
  completedTasks: number
  overdueTasks: number
  totalXp: number
  level: number
  title: string
  byDifficulty: DifficultyBreakdown[]
  last14Days: DailyCompletion[]
}

export interface CreateTaskInput {
  title: string
  notes?: string | null
  difficulty: Difficulty
  priority?: Priority
  dueDate?: string | null
  tags?: string[]
  recurrence?: Recurrence
  /** Set to nest this under an existing task. One level only. */
  parentId?: string | null
}

export interface UpdateTaskInput extends CreateTaskInput {
  priority: Priority
}

/**
 * One shape for every column move. XpDelta is positive when the drag completed a task,
 * negative when it dragged one back out, and zero the rest of the time.
 */
export interface SetStatusResult {
  task: Task
  xpDelta: number
  character: Character
  leveledUp: boolean
  leveledDown: boolean
  previousLevel: number
  unlockedAchievements: Achievement[]
  /** The contract a drag into Done discharged, on the same terms as the checkbox. */
  hunt?: Rpg.HuntContract | null
}

export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors?: Record<string, string[]>

  constructor(message: string, status: number, fieldErrors?: Record<string, string[]>) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

/** Minutes to add to UTC to get the browser's local time, matching the API's contract. */
export const utcOffsetMinutes = () => -new Date().getTimezoneOffset()

type TokenProvider = () => Promise<string>

let getAccessToken: TokenProvider | null = null
let onUnauthorized: (() => void) | null = null

/**
 * Registers the Auth0 token getter once at startup.
 *
 * Deliberately a module-level registration rather than a hook, so this client stays free
 * of React and can keep being used from plain functions.
 */
export function registerAuth(provider: TokenProvider | null, unauthorized?: () => void) {
  getAccessToken = provider
  onUnauthorized = unauthorized ?? null
}

async function authHeaders(): Promise<Record<string, string>> {
  if (!getAccessToken) return {}

  try {
    return { Authorization: `Bearer ${await getAccessToken()}` }
  } catch {
    // A failed silent renewal is not fatal here; the request goes out without a token
    // and the 401 path below drives the user back to sign-in.
    return {}
  }
}

async function request<T>(path: string, init?: RequestInit, allowRetry = true): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...(await authHeaders()),
      ...init?.headers,
    },
  })

  // One silent renewal attempt: an expired access token is the common case, and the SDK
  // refreshes it on the next call. Anything still 401 after that needs a real sign-in.
  if (response.status === 401 && allowRetry && getAccessToken) {
    return request<T>(path, init, false)
  }

  if (response.status === 401) {
    onUnauthorized?.()
  }

  if (!response.ok) {
    let message = `Request failed with ${response.status}`
    let fieldErrors: Record<string, string[]> | undefined

    try {
      const problem = await response.json()
      fieldErrors = problem?.errors
      message = problem?.title ?? problem?.detail ?? message

      // Surface the first field message: it is what the user actually needs to read.
      const firstField = fieldErrors && Object.values(fieldErrors)[0]?.[0]
      if (firstField) message = firstField
    } catch {
      // Non-JSON error body; the status-derived message stands.
    }

    throw new ApiError(message, response.status, fieldErrors)
  }

  if (response.status === 204) return undefined as T

  return (await response.json()) as T
}

const body = (value: unknown): RequestInit => ({ body: JSON.stringify(value) })

export const api = {
  listTasks: (
    params: {
      status?: TaskStatus
      difficulty?: Difficulty
      search?: string
      tag?: string
    } = {},
  ) => {
    const query = new URLSearchParams()
    if (params.status && params.status !== 'all') query.set('status', params.status)
    if (params.difficulty) query.set('difficulty', params.difficulty)
    if (params.search?.trim()) query.set('search', params.search.trim())
    if (params.tag) query.set('tag', params.tag)

    const suffix = query.toString()
    return request<Task[]>(`/api/tasks${suffix ? `?${suffix}` : ''}`)
  },

  createTask: (input: CreateTaskInput) =>
    request<Task>('/api/tasks', { method: 'POST', ...body(input) }),

  updateTask: (id: string, input: UpdateTaskInput) =>
    request<Task>(`/api/tasks/${id}`, { method: 'PUT', ...body(input) }),

  deleteTask: (id: string) => request<void>(`/api/tasks/${id}`, { method: 'DELETE' }),

  completeTask: (id: string) =>
    request<CompleteResult>(`/api/tasks/${id}/complete`, {
      method: 'POST',
      ...body({ utcOffsetMinutes: utcOffsetMinutes() }),
    }),

  reopenTask: (id: string) =>
    request<ReopenResult>(`/api/tasks/${id}/reopen`, { method: 'POST' }),

  setTaskStatus: (id: string, status: TaskProgress) =>
    request<SetStatusResult>(`/api/tasks/${id}/status`, {
      method: 'PUT',
      ...body({ status, utcOffsetMinutes: utcOffsetMinutes() }),
    }),

  listTags: () => request<string[]>('/api/tasks/tags'),

  reorderTasks: (orderedIds: string[]) =>
    request<void>('/api/tasks/reorder', { method: 'POST', ...body({ orderedIds }) }),

  getCharacter: () => request<Character>('/api/character'),

  updateCharacter: (input: { name: string; avatarKey: string }) =>
    request<Character>('/api/character', { method: 'PUT', ...body(input) }),

  listAchievements: () => request<Achievement[]>('/api/achievements'),

  getStats: () => request<Stats>(`/api/stats?utcOffsetMinutes=${utcOffsetMinutes()}`),

  // ------------------------------------------------------------- adventure
  getSheet: () => request<Rpg.CharacterSheet>('/api/rpg/sheet'),

  listClasses: () => request<Rpg.ClassOption[]>('/api/rpg/classes'),

  chooseClass: (classKey: string) =>
    request<Rpg.CharacterSheet>('/api/rpg/class', { method: 'PUT', ...body({ classKey }) }),

  listMonsters: () => request<Rpg.Monster[]>('/api/rpg/monsters'),

  startEncounter: (monsterKey: string) =>
    request<Rpg.Encounter>('/api/rpg/encounters', { method: 'POST', ...body({ monsterKey }) }),

  getActiveEncounter: () => request<Rpg.Encounter | undefined>('/api/rpg/encounters/active'),

  attack: (id: string) =>
    request<Rpg.AttackResult>(`/api/rpg/encounters/${id}/attack`, { method: 'POST' }),

  flee: (id: string) =>
    request<Rpg.Encounter>(`/api/rpg/encounters/${id}/flee`, { method: 'POST' }),

  listInventory: () => request<Rpg.InventoryItem[]>('/api/rpg/inventory'),

  equipItem: (id: string) =>
    request<Rpg.EquipResult>(`/api/rpg/inventory/${id}/equip`, { method: 'POST' }),

  unequipItem: (id: string) =>
    request<Rpg.EquipResult>(`/api/rpg/inventory/${id}/unequip`, { method: 'POST' }),

  sellItem: (id: string) =>
    request<{ goldGained: number; gold: number }>(`/api/rpg/inventory/${id}`, { method: 'DELETE' }),

  listQuests: () => request<Rpg.Quest[]>('/api/rpg/quests'),

  claimQuest: (key: string) =>
    request<{ goldGained: number; gold: number; item: Rpg.InventoryItem | null }>(
      `/api/rpg/quests/${key}/claim`,
      { method: 'POST' },
    ),

  useAbility: (encounterId: string, abilityKey: string) =>
    request<Rpg.AttackResult>(`/api/rpg/encounters/${encounterId}/ability/${abilityKey}`, {
      method: 'POST',
    }),

  /**
   * Drinks or throws one of a stack. Resolves a whole round, so it answers the same shape
   * an attack does and the encounter view can treat all three actions identically.
   */
  useItem: (encounterId: string, itemId: string) =>
    request<Rpg.AttackResult>(`/api/rpg/encounters/${encounterId}/use/${itemId}`, {
      method: 'POST',
    }),

  /** Only what the character's level has unlocked. A dungeon is never retired afterwards. */
  listDungeons: () => request<Rpg.Dungeon[]>('/api/rpg/dungeons'),

  startDungeon: (dungeonKey: string) =>
    request<Rpg.DungeonRun>('/api/rpg/dungeons', { method: 'POST', ...body({ dungeonKey }) }),

  /**
   * The whole of what a reloaded client needs to pick a run back up: the rolled chain, how
   * deep it got, and the fight in progress if a room is open. Undefined on 204.
   */
  getActiveDungeonRun: () => request<Rpg.DungeonRun | undefined>('/api/rpg/dungeons/active'),

  /** Opens the next room, charging one stamina. The run comes back with the new fight on it. */
  enterRoom: (id: string) =>
    request<Rpg.DungeonRun>(`/api/rpg/dungeons/${id}/enter`, { method: 'POST' }),

  abandonDungeonRun: (id: string) =>
    request<Rpg.DungeonRun>(`/api/rpg/dungeons/${id}/abandon`, { method: 'POST' }),

  getChronicle: (limit = 20) => request<Rpg.Chronicle>(`/api/rpg/encounters?limit=${limit}`),

  rest: () => request<Rpg.RestResult>('/api/rpg/rest', { method: 'POST' }),

  getShop: () => request<Rpg.Shop>('/api/rpg/shop'),

  rerollShop: () => request<Rpg.Shop>('/api/rpg/shop/reroll', { method: 'POST' }),

  buyOffer: (offerId: string) =>
    request<Rpg.PurchaseResult>(`/api/rpg/shop/${encodeURIComponent(offerId)}/buy`, {
      method: 'POST',
    }),

  upgradeItem: (id: string) =>
    request<Rpg.UpgradeResult>(`/api/rpg/inventory/${id}/upgrade`, { method: 'POST' }),

  /** Destroys the item for essence. Distinct from selling, which destroys it for gold. */
  salvageItem: (id: string) =>
    request<Rpg.SalvageResult>(`/api/rpg/inventory/${id}/salvage`, { method: 'POST' }),

  imbueItem: (id: string) =>
    request<Rpg.CraftResult>(`/api/rpg/inventory/${id}/imbue`, { method: 'POST' }),

  reforgeItem: (id: string) =>
    request<Rpg.CraftResult>(`/api/rpg/inventory/${id}/reforge`, { method: 'POST' }),

  /** The whole catalog every time, met or not: the unmet rows are what there is to aim at. */
  getBestiary: () => request<Rpg.Bestiary>('/api/rpg/bestiary'),

  getLore: () => request<Rpg.Lore>('/api/rpg/lore'),

  // ----------------------------------------------------------------- contracts

  /** Derived on every read. Rolls nothing, writes nothing and costs no stamina. */
  getHunts: () => request<Rpg.HuntBoard>('/api/rpg/hunts'),

  /**
   * Takes the contract on a task. Free: no stamina, no fight, nothing spent.
   *
   * Answers 201 at the contract, not at an encounter, because what it makes is a promise.
   * The fight is a separate call and only opens once the task itself is finished, which is
   * how the bounty stays attached to doing the work rather than to avoiding it.
   */
  acceptHunt: (taskId: string) =>
    request<Rpg.HuntContract>('/api/rpg/hunts', { method: 'POST', ...body({ taskId }) }),

  /** The contract fight in progress, or undefined on 204. */
  getActiveHunt: () => request<Rpg.Hunt | undefined>('/api/rpg/hunts/active'),

  /**
   * Opens the fight a discharged contract earned. One stamina, like any other fight.
   *
   * Refused with 409 while the task is unfinished, and there is no way round that: a bounty
   * is what finishing pays, never what avoiding pays.
   */
  fightHunt: (contractId: string) =>
    request<Rpg.Hunt>(`/api/rpg/hunts/${contractId}/fight`, { method: 'POST' }),

  /** Tears up a contract. Free, and it takes back nothing that was paid for. */
  abandonHunt: (contractId: string) =>
    request<Rpg.HuntContract>(`/api/rpg/hunts/${contractId}`, { method: 'DELETE' }),
}
