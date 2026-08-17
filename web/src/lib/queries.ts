import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  api,
  type Character,
  type CreateTaskInput,
  type Difficulty,
  type Task,
  type TaskProgress,
  type TaskStatus,
  type UpdateTaskInput,
} from './api'

export const queryKeys = {
  tasks: (filters: TaskFilters) => ['tasks', filters] as const,
  tags: ['tags'] as const,
  character: ['character'] as const,
  achievements: ['achievements'] as const,
  stats: ['stats'] as const,
}

export interface TaskFilters {
  status: TaskStatus
  difficulty?: Difficulty
  search: string
  tag?: string
}

export const useTasks = (filters: TaskFilters) =>
  useQuery({
    queryKey: queryKeys.tasks(filters),
    queryFn: () => api.listTasks(filters),
  })

export const useCharacter = () =>
  useQuery({ queryKey: queryKeys.character, queryFn: api.getCharacter })

export const useAchievements = () =>
  useQuery({ queryKey: queryKeys.achievements, queryFn: api.listAchievements })

export const useStats = () => useQuery({ queryKey: queryKeys.stats, queryFn: api.getStats })

export const useTags = () => useQuery({ queryKey: queryKeys.tags, queryFn: api.listTags })

/** Everything a completion touches. Called after any XP-moving mutation. */
function invalidateProgression(client: ReturnType<typeof useQueryClient>) {
  void client.invalidateQueries({ queryKey: ['tasks'] })
  void client.invalidateQueries({ queryKey: queryKeys.character })
  void client.invalidateQueries({ queryKey: queryKeys.achievements })
  void client.invalidateQueries({ queryKey: queryKeys.stats })

  // The adventurer sheet too, by prefix. Finishing a task grants stamina and hit points
  // (DEC-003), so leaving this out left the strip on the task screen showing level 1 and
  // zero stamina while the header already said level 2 - the one connection the strip
  // exists to make, silently not made.
  void client.invalidateQueries({ queryKey: ['rpg'] })
}

export function useCreateTask() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (input: CreateTaskInput) => api.createTask(input),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['tasks'] })
      void client.invalidateQueries({ queryKey: queryKeys.tags })
      void client.invalidateQueries({ queryKey: queryKeys.stats })
    },
  })
}

export function useUpdateTask() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateTaskInput }) =>
      api.updateTask(id, input),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['tasks'] })
      void client.invalidateQueries({ queryKey: queryKeys.tags })
      void client.invalidateQueries({ queryKey: queryKeys.stats })
    },
  })
}

export function useDeleteTask() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.deleteTask(id),
    onSuccess: () => invalidateProgression(client),
  })
}

export function useCompleteTask() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.completeTask(id),
    // The response carries the new character state, so the XP rail can update
    // before the refetch lands and the bar never jumps backwards.
    onSuccess: (result) => {
      client.setQueryData<Character>(queryKeys.character, result.character)
      invalidateProgression(client)
    },
  })
}

export function useReopenTask() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.reopenTask(id),
    onSuccess: (result) => {
      client.setQueryData<Character>(queryKeys.character, result.character)
      invalidateProgression(client)
    },
  })
}

/**
 * The board's drag target. One route for all six transitions, so the caller never has to
 * work out whether a drop was a completion, a reopening or just a column change.
 */
export function useSetTaskStatus() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: TaskProgress }) =>
      api.setTaskStatus(id, status),
    onSuccess: (result) => {
      client.setQueryData<Character>(queryKeys.character, result.character)
      invalidateProgression(client)
    },
  })
}

export function useReorderTasks() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (orderedIds: string[]) => api.reorderTasks(orderedIds),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['tasks'] }),
  })
}

export function useUpdateCharacter() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (input: { name: string; avatarKey: string }) => api.updateCharacter(input),
    onSuccess: (character) => client.setQueryData<Character>(queryKeys.character, character),
  })
}

/** The board's three columns, in the server's order within each. */
export const groupByStatus = (tasks: Task[] | undefined) => ({
  todo: tasks?.filter((task) => task.status === 'todo') ?? [],
  inProgress: tasks?.filter((task) => task.status === 'inProgress') ?? [],
  completed: tasks?.filter((task) => task.status === 'completed') ?? [],
})
