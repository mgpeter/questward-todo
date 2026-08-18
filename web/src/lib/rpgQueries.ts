import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api } from './api'
import { queryKeys } from './queries'
import type { AttackResult, CharacterSheet, DungeonRun } from './rpg'

export const rpgKeys = {
  sheet: ['rpg', 'sheet'] as const,
  classes: ['rpg', 'classes'] as const,
  monsters: ['rpg', 'monsters'] as const,
  encounter: ['rpg', 'encounter'] as const,
  inventory: ['rpg', 'inventory'] as const,
  quests: ['rpg', 'quests'] as const,
  chronicle: ['rpg', 'chronicle'] as const,
  shop: ['rpg', 'shop'] as const,
  bestiary: ['rpg', 'bestiary'] as const,
  lore: ['rpg', 'lore'] as const,
  dungeons: ['rpg', 'dungeons'] as const,
  dungeonRun: ['rpg', 'dungeon-run'] as const,
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

/**
 * What every resolved round settles, whether it was swung, invoked or drunk.
 *
 * One function rather than three copies, because the three differ only in the request. The
 * finished encounter is deliberately left in the cache: nulling it here is what used to make
 * a victory vanish the instant it happened. The caller decides when the result has been
 * read, through useDismissEncounter.
 *
 * The run is invalidated too, since winning a room is what moves a run's depth and nothing
 * in this response says so.
 */
function settleRound(client: QueryClient, result: AttackResult) {
  client.setQueryData(rpgKeys.encounter, result.encounter)
  client.setQueryData<CharacterSheet>(rpgKeys.sheet, result.sheet)
  void client.invalidateQueries({ queryKey: rpgKeys.inventory })
  void client.invalidateQueries({ queryKey: rpgKeys.quests })
  void client.invalidateQueries({ queryKey: rpgKeys.bestiary })
  void client.invalidateQueries({ queryKey: rpgKeys.lore })
  void client.invalidateQueries({ queryKey: rpgKeys.dungeonRun })
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
    onSuccess: (result) => settleRound(client, result),
  })
}

/** Clears a finished encounter once the player has read its outcome. */
export function useDismissEncounter() {
  const client = useQueryClient()

  return () => {
    client.setQueryData(rpgKeys.encounter, null)
    void client.invalidateQueries({ queryKey: rpgKeys.encounter })
    void client.invalidateQueries({ queryKey: rpgKeys.monsters })
    void client.invalidateQueries({ queryKey: rpgKeys.dungeonRun })
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

export function useSalvageItem() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.salvageItem(id),
    onSuccess: () => invalidateAdventure(client),
  })
}

/**
 * Imbue and reforge share one hook because they share one response shape and one
 * invalidation. A single hook also means one pending flag, which is what stops a player
 * spending essence twice on the same item by double-clicking through a slow round trip.
 */
export function useCraftItem() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ id, verb }: { id: string; verb: 'imbue' | 'reforge' }) =>
      verb === 'imbue' ? api.imbueItem(id) : api.reforgeItem(id),
    onSuccess: () => invalidateAdventure(client),
  })
}

export const useChronicle = () =>
  useQuery({ queryKey: rpgKeys.chronicle, queryFn: () => api.getChronicle() })

export const useShop = () => useQuery({ queryKey: rpgKeys.shop, queryFn: api.getShop })

export const useBestiary = () =>
  useQuery({ queryKey: rpgKeys.bestiary, queryFn: api.getBestiary })

/**
 * Lore is derived per request from the level, the codex and the claimed quests, so it has no
 * cache of its own to keep warm: it goes stale the moment any of those three move, which is
 * exactly when the blanket ['rpg'] invalidation fires.
 */
export const useLore = () => useQuery({ queryKey: rpgKeys.lore, queryFn: api.getLore })

export function useAbility() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ encounterId, abilityKey }: { encounterId: string; abilityKey: string }) =>
      api.useAbility(encounterId, abilityKey),
    onSuccess: (result) => settleRound(client, result),
  })
}

/**
 * Drinking or throwing one of a stack. A whole round, so it settles exactly what an attack
 * settles, the inventory refetch included: the stack it came off is one shorter.
 */
export function useConsumeItem() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ encounterId, itemId }: { encounterId: string; itemId: string }) =>
      api.useItem(encounterId, itemId),
    onSuccess: (result) => settleRound(client, result),
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

/** Code-held content that cannot change while the tab is open, but the level gate can. */
export const useDungeons = () =>
  useQuery({ queryKey: rpgKeys.dungeons, queryFn: api.listDungeons })

/**
 * The one run in progress, or null.
 *
 * This is the whole of resume: the client holds nothing between requests, so a reload asks
 * the server what it was doing and gets back the rolled chain, the depth and the open fight.
 */
export const useActiveDungeonRun = () =>
  useQuery({
    queryKey: rpgKeys.dungeonRun,
    queryFn: async () => (await api.getActiveDungeonRun()) ?? null,
  })

/**
 * Puts the run's open fight into the encounter cache alongside the run itself.
 *
 * Both reads describe the same row, and letting them disagree is what would show a stale
 * monster list over a live fight for as long as the refetch took.
 */
function settleRun(client: QueryClient, run: DungeonRun) {
  client.setQueryData<DungeonRun | null>(rpgKeys.dungeonRun, run)
  client.setQueryData(rpgKeys.encounter, run.encounter)
}

export function useStartDungeon() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (dungeonKey: string) => api.startDungeon(dungeonKey),
    // Opening a run costs no stamina of its own, but it does take the one encounter slot,
    // so the tavern has to be told before it offers a fight it cannot start.
    onSuccess: (run) => {
      settleRun(client, run)
      invalidateAdventure(client)
    },
  })
}

export function useEnterRoom() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.enterRoom(id),
    onSuccess: (run) => {
      settleRun(client, run)
      // A room costs a stamina and the reply carries no sheet, so the counter has to be
      // refetched rather than adjusted here.
      void client.invalidateQueries({ queryKey: rpgKeys.sheet })
    },
  })
}

export function useAbandonDungeonRun() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.abandonDungeonRun(id),
    // An open room is fled through the ordinary path on the way out, so the encounter goes
    // with the run rather than being left behind pointing at a fight that has ended.
    onSuccess: () => {
      client.setQueryData<DungeonRun | null>(rpgKeys.dungeonRun, null)
      client.setQueryData(rpgKeys.encounter, null)
      invalidateAdventure(client)
    },
  })
}

export function useClaimQuest() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (key: string) => api.claimQuest(key),
    onSuccess: () => invalidateAdventure(client),
  })
}
