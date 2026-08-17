# Spec Requirements Document

> Spec: RPG Layer - Classes, Stats, Combat, Loot and Quests
> Created: 2026-08-17
> Completed: 2026-08-17
> Status: Implemented

## Overview

Turn the character from a level counter into an actual RPG character: pick a class, carry
ability scores, fight monsters on a d20 system, collect equipment that changes those
scores, and complete small quests. The point is to give the XP you earn from real work
somewhere to go.

## The Load-Bearing Constraint

DEC-003 established that the score can only be moved by doing the work. An RPG layer is
the most obvious way to break that: if killing goblins grants XP, the todo app becomes
optional. So:

- **Monsters and quests never grant character XP.** They grant gold, loot and quest
  progress. Levels remain purely a function of completed tasks.
- **Combat costs Stamina, and only completing a real task produces Stamina.** Easy 1,
  Medium 2, Hard 3, Epic 5. An encounter costs 1.
- **Equipment changes power, not progression.** Better gear wins fights faster; it never
  produces a level.

The result: your level is a record of work done, your gear is what you did with it. The
game layer is a sink for productivity, never a substitute.

## User Stories

### Choosing what kind of character I am

As someone setting up my character, I want to pick a class, so that my character has an
identity and a play style rather than being an anonymous XP total.

On first visit after this ships I am asked to choose from six classes. Each shows its hit
die, its ability score spread and its passive perk in plain language. Choosing sets my
starting ability scores and grants a starting weapon and armour. I can change class later,
which re-rolls my scores and perks but leaves my level, XP and badges untouched.

### Spending a day's work on a fight

As someone who has just cleared several tasks, I want to spend the stamina I earned
fighting a monster, so that finishing chores pays into something enjoyable.

The Tavern lists monsters appropriate to my level. Starting an encounter costs 1 stamina,
which I only have because I completed tasks. Each round I attack: the server rolls d20
plus my attack bonus against the monster's armour class, then rolls damage. The monster
strikes back. I see every roll, including the dice, the modifiers and the target number,
so the outcome is legible rather than magic. Winning grants gold and a chance of loot.
Losing costs me nothing but the stamina and some hit points.

### Getting something out of a fight

As a player who won a fight, I want a chance at equipment, so that fights compound into a
stronger character.

On a win the server rolls for loot against the monster's table. Drops have a rarity, an
item type and ability score bonuses. I can equip a weapon, armour and a trinket; equipping
recalculates my armour class, attack bonus and damage. Anything I do not want can be sold
for gold.

### Small goals beyond the task list

As a player, I want short quests, so that there is a reason to fight particular monsters
or clear particular kinds of task.

Quests come from a code-held catalog, the same way badges do. Each has objectives that
count real events, such as defeating three goblins or completing five Hard tasks. Turning
one in grants gold and sometimes an item. Quests never grant XP.

## Spec Scope

1. **Ability scores and derived stats** - The six D&D abilities with modifiers, plus
   derived armour class, attack bonus, damage, max hit points and proficiency bonus,
   computed from class, level and equipped items.
2. **Classes** - Six classes with distinct hit dice, ability spreads, starting equipment
   and one passive perk each, selectable and changeable.
3. **A d20 engine** - Server-side dice with an injectable roller, natural 20 and natural 1
   handling, advantage and disadvantage, and a fully itemised roll breakdown returned to
   the client.
4. **Monster combat** - A code-held bestiary, round-by-round encounters costing stamina,
   persistent player hit points, and a complete combat log.
5. **Equipment and loot** - Item catalog with rarities, loot tables per monster, an
   inventory, three equipment slots and selling for gold.
6. **Quests** - A code-held quest catalog with countable objectives, progress driven by
   real events, and gold or item rewards.

## Out of Scope

- **Any XP from monsters or quests.** Non-negotiable; see the constraint above.
- **Player versus player, trading, or any interaction between accounts.** Every RPG
  entity is per-user and private, exactly like tasks.
- **Spells, abilities with resource pools, or a turn economy beyond attacking.** One
  attack action per round in this pass.
- **Randomly generated items or procedural monsters.** Both catalogs are code-held so they
  need no migration, matching DEC-004.
- **Consumables, potions and healing items.** Hit points recover with time and task
  completion instead.
- **Multi-monster encounters and dungeons.** One monster at a time.
- **Item enchantment, crafting, upgrades or a shop to spend gold in.** Gold accumulates
  and is spendable only via selling in this pass; a shop is the obvious follow-up.

## Expected Deliverable

1. A signed-in user can pick a class, see ability scores and derived stats change, and
   have those persist across a reload.
2. Completing tasks grants stamina; spending it starts an encounter that resolves round by
   round with visible dice rolls; winning grants gold and sometimes an item that can be
   equipped and visibly changes the character's stats.
3. Character XP and level are provably unchanged by any amount of fighting or quest
   turn-in, asserted by a test that fights until a monster dies and checks the XP total
   never moved.

## Cross-References

- Technical specification: `sub-specs/technical-spec.md`
- Database schema: `sub-specs/database-schema.md`
- API specification: `sub-specs/api-spec.md`
- Anti-inflation stance: DEC-003 in `docs/product/decisions.md`
- Code-held catalog precedent: DEC-004
