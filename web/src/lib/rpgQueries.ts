import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from '@tanstack/react-query'
import { api } from './api'
import { queryKeys } from './queries'
import type {
  AscendResult,
  AttackResult,
  CharacterSheet,
  Chronicle,
  DungeonRun,
  HuntBoard,
  HuntContract,
  HuntOffer,
} from './rpg'

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
  hunts: ['rpg', 'hunts'] as const,
  activeHunt: ['rpg', 'hunt-active'] as const,
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

  // A won contract is what moves faction standing, and a finished fight is what frees the
  // board to offer the next one. Neither is stated anywhere in this response.
  void client.invalidateQueries({ queryKey: rpgKeys.hunts })
  void client.invalidateQueries({ queryKey: rpgKeys.activeHunt })

  // The round wrote journal lines: the fight itself, and possibly a dungeon ended, a contract
  // settled or a banner's standing raised. This key used to be the one nothing invalidated,
  // which was survivable while the chronicle only held fights the player had just watched.
  void client.invalidateQueries({ queryKey: rpgKeys.chronicle })
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
    void client.invalidateQueries({ queryKey: rpgKeys.hunts })
    void client.invalidateQueries({ queryKey: rpgKeys.activeHunt })
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

const CHRONICLE_PAGE = 20

/**
 * The journal, one page at a time.
 *
 * Keyset paging on the timestamp of the last entry read, rather than a page number: entries are
 * written while the panel is open, and an offset would show the same line twice.
 */
export const useChronicle = () =>
  useInfiniteQuery({
    queryKey: rpgKeys.chronicle,
    queryFn: ({ pageParam }: { pageParam: string | undefined }) =>
      api.getChronicle(CHRONICLE_PAGE, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last: Chronicle) =>
      last.entries.length < CHRONICLE_PAGE
        ? undefined
        : last.entries[last.entries.length - 1]?.occurredAt,
  })

/**
 * Ends an era.
 *
 * Everything is invalidated rather than patched from the response: the wipe reaches gear,
 * quests, contracts, runs and the bestiary, and there is no part of the adventure screen that
 * is still true afterwards. Tasks and badges survive, but the character strip above them does
 * not, which is why the character key goes too.
 */
export function useAscend() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: () => api.ascend(),
    onSuccess: (result: AscendResult) => {
      client.setQueryData<CharacterSheet>(rpgKeys.sheet, result.sheet)
      invalidateAdventure(client)
    },
  })
}

export const useShop = () => useQuery({ queryKey: rpgKeys.shop, queryFn: api.getShop })

/**
 * Pays stamina for a fresh shelf.
 *
 * The response IS the new shelf, so it is written straight into the cache rather than
 * invalidated: refetching would compute the same stock a second time for no reason, and the
 * gap in between would show the old shelf the player has just paid to be rid of.
 */
export function useRerollShop() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: () => api.rerollShop(),
    onSuccess: (shop) => {
      client.setQueryData(rpgKeys.shop, shop)

      // Stamina moved, and the sheet is where the rest of the app reads it from.
      void client.invalidateQueries({ queryKey: rpgKeys.sheet })
    },
  })
}

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

// ---------------------------------------------------------------------- hunts

/**
 * The contract board: what could be written up, and what already has been.
 *
 * Derived on the server on every read, so it goes stale whenever a task moves, a contract is
 * taken or discharged, or a fight ends. All of those already invalidate it.
 */
export const useHuntBoard = () => useQuery({ queryKey: rpgKeys.hunts, queryFn: api.getHunts })

/**
 * What one task is worth as a contract, or null if it cannot carry one.
 *
 * Selected out of the one board query rather than fetched per card. Every task card on the
 * screen asks this and they all share a single request, and `select` keeps a card whose own
 * offer has not moved from re-rendering when a different one has. The server sends the whole
 * list for exactly this reason: a board trimmed to its first twenty rows would answer "no
 * contract" for every task past the twentieth, which is the opposite of what a backlog is
 * supposed to be worth.
 */
export const useHuntOffer = (taskId: string) =>
  useQuery({
    queryKey: rpgKeys.hunts,
    queryFn: api.getHunts,
    select: (board: HuntBoard): HuntOffer | null =>
      board.offers.find((offer) => offer.taskId === taskId) ?? null,
  })

/** The live contract standing on one task, accepted or discharged, or null. */
export const useTaskContract = (taskId: string) =>
  useQuery({
    queryKey: rpgKeys.hunts,
    queryFn: api.getHunts,
    select: (board: HuntBoard): HuntContract | null =>
      board.contracts.find((contract) => contract.taskId === taskId) ?? null,
  })

/**
 * The contract fight in progress, or null.
 *
 * This is the whole of resume for a hunt: the client holds nothing between requests, so a
 * reload asks the server what it was fighting and gets back the frozen block, the banner and
 * the open fight.
 */
export const useActiveHunt = () =>
  useQuery({
    queryKey: rpgKeys.activeHunt,
    queryFn: async () => (await api.getActiveHunt()) ?? null,
  })

/**
 * Takes a contract on a task. Free, so there is nothing to confirm and nothing to undo.
 *
 * Nothing is written into the encounter cache, because nothing was started: what comes back
 * is a promise, and the board is what renders it.
 */
export function useAcceptHunt() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (taskId: string) => api.acceptHunt(taskId),
    onSuccess: () => invalidateAdventure(client),
  })
}

/**
 * Opens the fight a discharged contract earned.
 *
 * The fight goes into the encounter cache alongside the hunt, because both reads describe
 * the same row: letting them disagree is what would show the tavern's monster list over a
 * live contract for as long as the refetch took.
 */
export function useFightHunt() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (contractId: string) => api.fightHunt(contractId),
    onSuccess: (hunt) => {
      client.setQueryData(rpgKeys.activeHunt, hunt)
      client.setQueryData(rpgKeys.encounter, hunt.encounter)
      invalidateAdventure(client)
    },
  })
}

/** Tears up a contract. Free, and it takes back nothing that was paid for. */
export function useAbandonHunt() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (contractId: string) => api.abandonHunt(contractId),
    onSuccess: () => invalidateAdventure(client),
  })
}

/** Clears a finished contract fight once its outcome has been read. */
export function useDismissHunt() {
  const client = useQueryClient()

  return () => {
    client.setQueryData(rpgKeys.activeHunt, null)
    client.setQueryData(rpgKeys.encounter, null)
    void client.invalidateQueries({ queryKey: ['rpg'] })
    void client.invalidateQueries({ queryKey: queryKeys.character })
  }
}
