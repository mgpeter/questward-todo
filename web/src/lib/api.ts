/** Typed client for the Questward API. Same-origin in production, Vite-proxied in dev. */

export type Difficulty = 'easy' | 'medium' | 'hard' | 'epic'
export type Priority = 'low' | 'normal' | 'high'
export type TaskStatus = 'all' | 'open' | 'done'

export interface Task {
  id: string
  title: string
  notes: string | null
  difficulty: Difficulty
  priority: Priority
  xpValue: number
  dueDate: string | null
  isCompleted: boolean
  completedAt: string | null
  xpAwarded: number
  sortOrder: number
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
}

export interface UpdateTaskInput extends CreateTaskInput {
  priority: Priority
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
  listTasks: (params: { status?: TaskStatus; difficulty?: Difficulty; search?: string } = {}) => {
    const query = new URLSearchParams()
    if (params.status && params.status !== 'all') query.set('status', params.status)
    if (params.difficulty) query.set('difficulty', params.difficulty)
    if (params.search?.trim()) query.set('search', params.search.trim())

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

  reorderTasks: (orderedIds: string[]) =>
    request<void>('/api/tasks/reorder', { method: 'POST', ...body({ orderedIds }) }),

  getCharacter: () => request<Character>('/api/character'),

  updateCharacter: (input: { name: string; avatarKey: string }) =>
    request<Character>('/api/character', { method: 'PUT', ...body(input) }),

  listAchievements: () => request<Achievement[]>('/api/achievements'),

  getStats: () => request<Stats>(`/api/stats?utcOffsetMinutes=${utcOffsetMinutes()}`),
}
