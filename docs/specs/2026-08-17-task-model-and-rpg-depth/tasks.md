# Spec Tasks

## Tasks

- [x] 0. Fix the stamina and hit-point refund asymmetry
  - [x] 0.1 Write the ledger tests, including a 25-cycle complete/reopen loop
  - [x] 0.2 Add `TodoTask.StaminaAwarded` and the backfilling migration
  - [x] 0.3 Refund stamina and hit points symmetrically in `ReopenAsync`
  - [x] 0.4 Verify all tests pass

- [x] 1. Task model overhaul
  - [x] 1.1 Write tests for subtasks, tags, recurrence and the status route
  - [x] 1.2 `TaskProgress`, `RecurrenceRule`, and the single `MayAwardAt` / `StatusAt` gate
  - [x] 1.3 Schema, indexes, and the hand-corrected `TaskModelOverhaul` migration
  - [x] 1.4 DTOs, `?tag=`, `/tags`, `PUT /{id}/status`, one-level nesting guard
  - [x] 1.5 `TaskBoard` with drag, subtask rows, tag chips, recurrence controls
  - [x] 1.6 Verify all tests pass, and the migration in both directions on real data

- [x] 2. Adventurer strip on the task screen
  - [x] 2.1 Class, health, stamina and gold from the existing sheet endpoint
  - [x] 2.2 Verify the frontend builds and the SPA boots clean in Chrome

- [x] 3. Items, affixes and sets
  - [x] 3.1 Write tests for affix rolling, set completion, salvage and crafting
  - [x] 3.2 Affix mechanics against the existing 34 items, everything derived
  - [x] 3.3 `AffixRules.RollableFor` excludes `ItemSlot.Consumable`
  - [x] 3.4 42 new items as a separate catalog-only commit
  - [x] 3.5 Verify all tests pass

- [x] 4. Content, bestiary and feel
  - [x] 4.1 Write tests for bestiary recording, isolation and level coverage
  - [x] 4.2 `bestiary_entries`, two GETs, the deliberate DEC-002 exception comment
  - [x] 4.3 Monsters for levels 1-14; fix the `IsAvailableAt` doc comment, keep the band
  - [x] 4.4 Lore fragments, flavour text, combat sound - budget this as writing
  - [x] 4.5 Verify all tests pass

- [x] 5. Encounter depth
  - [x] 5.1 Write tests for effect ticks, phases, consumable stacking
  - [x] 5.2 `StatusEffect[]`; fold `MonsterDisadvantageRounds` into `Weakened`
  - [x] 5.3 Boss phases derived from `PhaseAt`, storing only the highest entered
  - [x] 5.4 Consumables, no affixes, one reserved shop slot
  - [x] 5.5 Dungeon runs - stop here if the budget is gone
  - [x] 5.6 Revisit every `SequenceDiceRoller` script; verify all tests pass

- [ ] 6. Tasks as monsters
  - [ ] 6.1 Write tests: hunt XP invariance, derived stats, bounty cap, isolation
  - [ ] 6.2 Hunt as an `Encounter` with `TaskId`, reusing the existing index and route
  - [ ] 6.3 Store frozen inputs, derive the stat block
  - [ ] 6.4 Resolve the hunt after the XP transaction commits, in its own unit of work
  - [ ] 6.5 Bounty gold capped at 200%; factions from tags, case-folded
  - [ ] 6.6 Verify all tests pass

- [ ] 7. Streaks, decay and the daily reward
  - [ ] 7.1 Write tests for streaks, freezes, and the decay floor
  - [ ] 7.2 Streak with freezes and the daily reward
  - [ ] 7.3 `Rust` with `MaxRust = 6` and `MinimumLevel = 5`; record both against DEC-013
  - [ ] 7.4 Verify all tests pass, and that nothing named `Burden` exists

- [ ] 8. The long game
  - [ ] 8.1 Split `ResolvePlayerAction` before any new combat branch
  - [ ] 8.2 One subclass per class
  - [ ] 8.3 Companion
  - [ ] 8.4 Titles and seasons
  - [ ] 8.5 Verify all tests pass
