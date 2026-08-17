import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  api,
  type Character,
  type CreateTaskInput,
  type Difficulty,
  type Task,
  type TaskStatus,
  type UpdateTaskInput,
} from './api'

export const queryKeys = {
  tasks: (filters: TaskFilters) => ['tasks', filters] as const,
  character: ['character'] as const,
  achievements: ['achievements'] as const,
  stats: ['stats'] as const,
}

export interface TaskFilters {
  status: TaskStatus
  difficulty?: Difficulty
  search: string
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

/** Everything a completion touches. Called after any XP-moving mutation. */
function invalidateProgression(client: ReturnType<typeof useQueryClient>) {
  void client.invalidateQueries({ queryKey: ['tasks'] })
  void client.invalidateQueries({ queryKey: queryKeys.character })
  void client.invalidateQueries({ queryKey: queryKeys.achievements })
  void client.invalidateQueries({ queryKey: queryKeys.stats })
}

export function useCreateTask() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (input: CreateTaskInput) => api.createTask(input),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['tasks'] })
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

export function useUpdateCharacter() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (input: { name: string; avatarKey: string }) => api.updateCharacter(input),
    onSuccess: (character) => client.setQueryData<Character>(queryKeys.character, character),
  })
}

/** Open tasks first, then the completed ones, matching the server's ordering. */
export const partitionTasks = (tasks: Task[] | undefined) => ({
  open: tasks?.filter((task) => !task.isCompleted) ?? [],
  done: tasks?.filter((task) => task.isCompleted) ?? [],
})
