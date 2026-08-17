# Spec Tasks

These are the tasks to be completed for the spec detailed in
@docs/specs/2026-08-17-rpg-combat-and-classes/spec.md

> Ordered so the pure domain lands first and is fully tested before anything touches the
> database or the wire. The d20 engine is the foundation everything else computes on.

## Tasks

- [x] 1. The d20 engine and character sheet maths
  - [x] 1.1 Write tests for the ability modifier table, dice parsing and roll resolution,
        including natural 20, natural 1, advantage and disadvantage
  - [x] 1.2 Add `IDiceRoller`, `SecureDiceRoller` and the test `SequenceDiceRoller`
  - [x] 1.3 Add `DiceExpression`, `RollResult` and the itemised breakdown types
  - [x] 1.4 Add `AbilityScores` and the modifier computation
  - [x] 1.5 Add `ClassCatalog` with six classes, and `CharacterSheet` deriving proficiency,
        armour class, attack bonus, damage and max hit points
  - [x] 1.6 Write tests proving item bonuses apply to scores before modifiers, not after
  - [x] 1.7 Verify all tests pass

- [x] 2. Catalogs: items, monsters and quests
  - [x] 2.1 Write tests asserting every catalog entry is well formed and every key unique
  - [x] 2.2 Add `ItemCatalog` with rarities, slots, ability bonuses and sell values
  - [x] 2.3 Add `MonsterCatalog` with level bands, stats and loot tables
  - [x] 2.4 Add `QuestCatalog` with countable objectives
  - [x] 2.5 Write tests that every monster loot table and quest reward references a real
        item key, so a typo cannot ship a quest that pays nothing
  - [x] 2.6 Verify all tests pass

- [x] 3. Schema and persistence
  - [x] 3.1 Write failing schema tests: one equipped item per slot, one active encounter
        per user, one quest progress row per quest, cascade delete leaving no orphans
  - [x] 3.2 Extend `Character` with class, ability scores, hit points, stamina and gold
  - [x] 3.3 Add `InventoryItem`, `Encounter` and `QuestProgress` entities and configurations
  - [x] 3.4 Add the `AddRpgLayer` migration, additive only, with a backfill for existing
        characters
  - [x] 3.5 Verify all tests pass and the migration applies to a populated database

- [x] 4. Combat, loot and quest services
  - [x] 4.1 Write tests for the encounter lifecycle, stamina gating and loot rolls
  - [x] 4.2 **Write the XP invariance test**: fight a monster to death and assert character
        XP and level are byte-identical afterwards
  - [x] 4.3 Add `CombatService` for start, attack round resolution and flee
  - [x] 4.4 Add `LootService` for rarity rolls and item grants
  - [x] 4.5 Add `QuestService` for objective counting and claiming
  - [x] 4.6 Grant stamina and hit point recovery from task completion in
        `GamificationService`, without touching the XP path
  - [x] 4.7 Verify all tests pass

- [x] 5. API surface
  - [x] 5.1 Write endpoint tests including per-user isolation for every new route
  - [x] 5.2 Add sheet and class endpoints
  - [x] 5.3 Add monster, encounter, attack and flee endpoints
  - [x] 5.4 Add inventory equip, unequip and sell endpoints
  - [x] 5.5 Add quest listing and claiming endpoints
  - [x] 5.6 Verify all tests pass, including that no RPG route can move XP

- [x] 6. Frontend
  - [x] 6.1 Add the API client types and TanStack Query hooks
  - [x] 6.2 Add the class selection modal
  - [x] 6.3 Add the character sheet panel with abilities, derived stats and inventory
  - [x] 6.4 Add the tavern: monster list, encounter view and the animated dice roll
        breakdown
  - [x] 6.5 Add the quest board
  - [x] 6.6 Add the Adventure tab and wire stamina into the header
  - [x] 6.7 Verify the SPA builds and typechecks

- [x] 7. Verification and docs
  - [x] 7.1 Run the full suite and the browser checks
  - [x] 7.2 Update the README, mission and roadmap
  - [x] 7.3 Add a decision entry recording why the RPG layer grants no XP
