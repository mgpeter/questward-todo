import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api } from './api'
import { queryKeys } from './queries'
import type { CharacterSheet } from './rpg'

export const rpgKeys = {
  sheet: ['rpg', 'sheet'] as const,
  classes: ['rpg', 'classes'] as const,
  monsters: ['rpg', 'monsters'] as const,
  encounter: ['rpg', 'encounter'] as const,
  inventory: ['rpg', 'inventory'] as const,
  quests: ['rpg', 'quests'] as const,
  chronicle: ['rpg', 'chronicle'] as const,
  shop: ['rpg', 'shop'] as const,
}

export const useSheet = () => useQuery({ queryKey: rpgKeys.sheet, queryFn: api.getSheet })

export const useClasses = () =>
  useQuery({
    queryKey: rpgKeys.classes,
    queryFn: api.listClasses,
    // The catalog is code-held and cannot change while the tab is open.
    staleTime: Infinity,
  })

export const useMonsters = () => useQuery({ queryKey: rpgKeys.monsters, queryFn: api.listMonsters })

export const useInventory = () =>
  useQuery({ queryKey: rpgKeys.inventory, queryFn: api.listInventory })

export const useQuests = () => useQuery({ queryKey: rpgKeys.quests, queryFn: api.listQuests })

export const useActiveEncounter = () =>
  useQuery({
    queryKey: rpgKeys.encounter,
    queryFn: async () => (await api.getActiveEncounter()) ?? null,
  })

/** A fight can change the sheet, the inventory, quests and the character all at once. */
function invalidateAdventure(client: QueryClient) {
  void client.invalidateQueries({ queryKey: ['rpg'] })
  void client.invalidateQueries({ queryKey: queryKeys.character })
}

export function useChooseClass() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (classKey: string) => api.chooseClass(classKey),
    onSuccess: (sheet) => {
      client.setQueryData<CharacterSheet>(rpgKeys.sheet, sheet)
      invalidateAdventure(client)
    },
  })
}

export function useStartEncounter() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (monsterKey: string) => api.startEncounter(monsterKey),
    onSuccess: (encounter) => {
      client.setQueryData(rpgKeys.encounter, encounter)
      invalidateAdventure(client)
    },
  })
}

export function useAttack() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.attack(id),
    // The response carries the whole updated sheet, so stamina, gold and hit points
    // settle before the refetch lands and nothing flickers mid-fight.
    //
    // The finished encounter is deliberately kept in the cache. Nulling it here is what
    // used to make a victory vanish the instant it happened: the tavern list snapped back
    // and the gold, loot and killing blow were never seen. The caller decides when the
    // result has been read, via useDismissEncounter.
    onSuccess: (result) => {
      client.setQueryData(rpgKeys.encounter, result.encounter)
      client.setQueryData<CharacterSheet>(rpgKeys.sheet, result.sheet)
      void client.invalidateQueries({ queryKey: rpgKeys.inventory })
      void client.invalidateQueries({ queryKey: rpgKeys.quests })
    },
  })
}

/** Clears a finished encounter once the player has read its outcome. */
export function useDismissEncounter() {
  const client = useQueryClient()

  return () => {
    client.setQueryData(rpgKeys.encounter, null)
    void client.invalidateQueries({ queryKey: rpgKeys.encounter })
    void client.invalidateQueries({ queryKey: rpgKeys.monsters })
    void client.invalidateQueries({ queryKey: queryKeys.character })
  }
}

export function useFlee() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.flee(id),
    onSuccess: () => {
      client.setQueryData(rpgKeys.encounter, null)
      invalidateAdventure(client)
    },
  })
}

export function useEquip() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ id, equip }: { id: string; equip: boolean }) =>
      equip ? api.equipItem(id) : api.unequipItem(id),
    onSuccess: (result) => {
      client.setQueryData<CharacterSheet>(rpgKeys.sheet, result.sheet)
      client.setQueryData(rpgKeys.inventory, result.inventory)
    },
  })
}

export function useSellItem() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.sellItem(id),
    onSuccess: () => invalidateAdventure(client),
  })
}

export const useChronicle = () =>
  useQuery({ queryKey: rpgKeys.chronicle, queryFn: () => api.getChronicle() })

export const useShop = () => useQuery({ queryKey: rpgKeys.shop, queryFn: api.getShop })

export function useAbility() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ encounterId, abilityKey }: { encounterId: string; abilityKey: string }) =>
      api.useAbility(encounterId, abilityKey),
    // Same handling as a plain attack: the finished encounter stays put until dismissed.
    onSuccess: (result) => {
      client.setQueryData(rpgKeys.encounter, result.encounter)
      client.setQueryData<CharacterSheet>(rpgKeys.sheet, result.sheet)
      void client.invalidateQueries({ queryKey: rpgKeys.inventory })
      void client.invalidateQueries({ queryKey: rpgKeys.quests })
    },
  })
}

export function useRest() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: () => api.rest(),
    onSuccess: () => invalidateAdventure(client),
  })
}

export function useBuyOffer() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (offerId: string) => api.buyOffer(offerId),
    onSuccess: () => invalidateAdventure(client),
  })
}

export function useUpgradeItem() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.upgradeItem(id),
    onSuccess: () => invalidateAdventure(client),
  })
}

export function useClaimQuest() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (key: string) => api.claimQuest(key),
    onSuccess: () => invalidateAdventure(client),
  })
}
