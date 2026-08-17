# Technical Specification

This is the technical specification for the spec detailed in
@docs/specs/2026-08-17-rpg-combat-and-classes/spec.md

## The d20 Engine

`TodoApp.Models/Dice/` holds the whole engine, with no dependency on EF Core or ASP.NET so
it stays directly unit-testable.

- `DiceExpression` parses and represents `1d20`, `2d6+3`, `1d8-1`.
- `IDiceRoller` has one method, `Roll(int sides)`. Production uses
  `RandomNumberGenerator`-backed `SecureDiceRoller`; tests inject `SequenceDiceRoller`,
  which returns a scripted list. **Nothing in the domain calls `Random` directly**, or the
  combat rules become untestable.
- `Check.Ability(modifier, dc, advantage)` performs `d20 + modifier vs DC`, returning a
  `RollResult` carrying the raw die, each modifier with its label, the total, the target
  and the outcome.
- A natural 20 always hits and doubles the damage dice; a natural 1 always misses. These
  override the arithmetic, exactly as in the tabletop rules.
- Advantage and disadvantage roll twice and take the higher or lower, keeping both dice in
  the breakdown so the UI can show what was discarded.

Every roll returned to the client is fully itemised. Showing `d20: 14 +3 STR +2 prof = 19
vs AC 15, hit` is what makes the system feel fair rather than arbitrary.

## Ability Scores and Derived Stats

Six abilities: STR, DEX, CON, INT, WIS, CHA. Modifier is `floor((score - 10) / 2)`, so 10
is +0 and 18 is +4.

`CharacterSheet` is a pure computation over class, level and equipped items:

| Stat | Formula |
|---|---|
| Proficiency bonus | `2 + (level - 1) / 4`, capped at 6 |
| Armour class | `10 + DEX modifier + armour bonus` |
| Attack bonus | `proficiency + modifier of the weapon's governing ability` |
| Damage | `weapon die + governing ability modifier` |
| Max hit points | `class hit die max at level 1, then average per level, plus CON modifier per level` |
| Carry stat bonuses | Equipped items add directly to ability scores before modifiers are derived |

Order matters: item bonuses apply to the raw scores first, then modifiers are recomputed.
Applying them to modifiers instead would silently halve them.

Ability score improvements at levels 4, 8, 12, 16 and 19, matching the tabletop cadence.
Applied automatically to the class's two primary abilities rather than prompting, since a
todo app is the wrong place for a build planner.

## Classes

Code-held in `ClassCatalog`, so adding one needs no migration.

| Class | Hit die | Primary | Perk |
|---|---|---|---|
| Fighter | d10 | STR, CON | Second Wind: heals on a win |
| Rogue | d8 | DEX, INT | Sneak Attack: crits on 19 as well as 20 |
| Wizard | d6 | INT, WIS | Arcane Recovery: encounters cost less stamina sometimes |
| Cleric | d8 | WIS, CON | Blessing: rerolls a natural 1 once per encounter |
| Ranger | d10 | DEX, WIS | Favoured Quarry: improved loot rarity rolls |
| Bard | d8 | CHA, DEX | Silver Tongue: increased gold rewards |

Weapons are governed by STR unless flagged finesse, which uses the better of STR and DEX.

Changing class re-rolls scores, hit points and starting gear. Level, XP, badges, gold and
inventory survive, since those record real work or past play.

## Combat

An `Encounter` is a persisted row so a fight survives a reload.

- Starting one costs 1 stamina and fails with a clear error at zero.
- One request resolves one round: the player attacks, then the monster attacks back if it
  is still standing. Both rolls are appended to a stored combat log.
- Player hit points persist on the character between encounters. Reaching zero ends the
  encounter as a loss and leaves the player at 1 hit point rather than dead, because a
  todo app should not punish people.
- Hit points regenerate at one per completed task plus a slow passive tick, so the loop
  keeps pointing back at real work.
- An encounter is `Active`, `Won`, `Lost` or `Fled`. Only one may be active per user,
  enforced by a filtered unique index.

The bestiary is code-held: name, level band, armour class, hit points, attack bonus,
damage expression, gold range and a loot table.

## Loot

- Items are code-held definitions; an inventory row references a definition key and stores
  its rolled rarity.
- Rarity is a d100 against the monster's table, modified by the Ranger perk.
- Three slots: weapon, armour, trinket. Equipping is idempotent and swaps whatever was in
  the slot back to the inventory.
- Selling converts an item to gold at a rarity-scaled price and removes the row.

## Quests

Code-held catalog mirroring `AchievementCatalog`. Each quest has one or more objectives,
each a `(kind, target, count)` triple such as `DefeatMonster/goblin/3` or
`CompleteTask/hard/5`. Progress rows count events as they happen, driven from the same
places that already evaluate achievements. Completing all objectives makes a quest
claimable; claiming grants gold and possibly an item.

## Frontend

A new **Adventure** tab beside Quests, Record and Badges, holding three panels: the
character sheet, the tavern with its monster list and active encounter, and the quest
board. Inventory lives on the character sheet.

- The dice roll display is the centrepiece: each roll animates the d20 and shows its
  breakdown as chips, so the arithmetic is visible.
- Class selection is a modal on first visit after this ships, dismissible but re-openable
  from the character sheet.
- Reuses the existing tier-colour tokens for rarity, extended with two more validated
  steps for legendary and mythic.

## Testing

The combat and progression rules are pure functions over an injected roller, so they are
tested exhaustively without a database:

- Modifier table across the full score range, including odd scores and scores below 10.
- Natural 20 hits regardless of armour class; natural 1 misses regardless of bonuses.
- Advantage takes the higher die and keeps both in the breakdown.
- Derived stats change correctly when items are equipped, and item bonuses apply to scores
  before modifiers, not after.
- **XP invariance**: a full encounter fought to the death leaves character XP and level
  byte-identical. This is the test that protects DEC-003 and it is the reason the whole
  design is shaped this way.
- Stamina gates encounters: at zero, starting one fails.

## External Dependencies

None. The engine, catalogs and combat rules are all plain C# in existing projects.
