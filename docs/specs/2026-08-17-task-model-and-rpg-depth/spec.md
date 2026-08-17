# Spec Requirements Document

> Spec: Task Model Overhaul and RPG Depth
> Created: 2026-08-17
> Status: Phase 1 and 2 complete, Phases 3-8 planned

## Overview

Turn the task list into something with structure - subtasks, tags, repeats and a
three-column board - and use that structure as the raw material for the RPG layer, without
loosening the rule that only real work grants experience.

Eleven design agents produced six competing subsystem designs. They contained **twelve
direct contradictions, four indirect XP leaks and one live exploit already in production**.
This document records how each was resolved and what remains buildable.

## User Stories

### The task that is really five tasks

As someone with a chore too big to face, I want to break it into steps and tick them off,
so that the list reflects how the work actually feels - without the app pretending I have
done five times the work.

Adding steps to "move house" makes the card show `3/8` and gives me somewhere to start.
Ticking a step is real progress and shows as such. Only finishing the parent pays.

### The chore that comes back

As someone who waters plants every day, I want one task that returns each morning rather
than a new task typed out daily, so that recurring work stops being clerical work.

Ticked on Monday, it sits in Done. On Tuesday it is simply back in To do, with no reset
button and no overnight job. It pays once a day. Ticking it by accident and unticking it
does not cost me the day.

### The board

As someone holding three things at once, I want a column for what I have actually started,
so that "in progress" is a place rather than a memory.

Dragging a card into Done earns exactly what the checkbox earns, floating number and all.
Dragging it back out takes the experience with it.

## Spec Scope

1. **Task model overhaul** - `Status` enum, subtasks as self-referencing rows, tags as a
   Postgres `text[]`, recurrence with a derived rollover, drag-to-reorder. **Shipped.**
2. **Adventurer strip** - class, health, stamina and gold above the board, so the resource
   the RPG spends is visible where it is earned. **Shipped.**
3. **Items, affixes and sets** - prefixes and suffixes rolled on drop, set bonuses, salvage
   and crafting, then 42 new items as a pure catalog commit. **Planned, Phase 3.**
4. **Content and feel** - bestiary codex, lore fragments, flavour text, combat sound.
   **Planned, Phase 4.**
5. **Encounter depth** - status effects, boss phases, consumables, dungeon runs.
   **Planned, Phase 5.**
6. **Tasks as monsters** - a bounty on an overdue task becomes a named opponent whose
   difficulty is drawn from the task's own difficulty and subtask count. **Planned, Phase 6.**
7. **Streaks, decay and the long game** - daily streak with freezes, idle stat decay,
   subclasses, companion, titles, seasons. **Planned, Phases 7-8, split.**

## Out of Scope

- **Overdue debuffs.** Deleted from the design, permanently. See "The Stick, Rebuilt" below.
- **The household leaderboard.** The only cross-user read anyone proposed. It contradicts
  the standing rule that another user's resource returns 404, and needs its own decision
  record before any code.
- **Ascension / prestige** as designed: destructive, irreversible and row-deleting. If it
  returns it must be additive.
- Anything that can convert gold, streaks, loot or time into experience.

## Expected Deliverable

1. Splitting a task into twenty subtasks and ticking them all pays exactly what finishing
   the one task pays. Asserted at HTTP altitude.
2. A daily task pays once per day, returns to the board on its own, and survives an
   accidental tick-and-untick without losing the day's reward.
3. Dragging a card into Done produces the same XP, badges and quest progress as the
   checkbox, and dragging it out reverses all of it.

---

## The Twelve Contradictions, Resolved

The designs are referenced as D1-D6 as the review recorded them. Rulings marked **shipped**
are already in the code; the rest bind whoever builds the phase.

| # | Conflict | Ruling |
|---|---|---|
| 1 | **The task model itself.** D1 drops `IsCompleted` and `IX_tasks_UserId_IsCompleted_SortOrder`; D2 and D5 both plan queries against that index. | D1 wins; the index is now `(UserId, Status, SortOrder)`. **Shipped** - any later design reading the old index name is stale. |
| 2 | **Reopen semantics.** D1 leaves stamina unreversed; D2 makes a symmetric refund a blocking prerequisite. | D2 wins. This was not a design disagreement but a live exploit, fixed alone and first in `5c09943`. **Shipped.** |
| 3 | **Subtask completion route.** D2 hangs its whole hunt loop off `POST /api/tasks/{id}/subtasks/{subtaskId}/complete`. | The route does not exist and will not. A subtask is a task and already has `/complete`. D2's combat loop must rebind to it. |
| 4 | **One active encounter.** D2 replaces the partial unique index with three; D3 depends on it being unchanged. | The index stands. A hunt is an encounter with `TaskId` set and stays covered by it. If concurrent fights are ever wanted, that is its own decision, not a side effect of a feature. |
| 5 | **`Encounter.Effects` claimed twice** - D3 wants `StatusEffect[]`, D5 wants `{key: rounds}`. | D3's shape wins; it is the one with tick rules and riders. D5's subclass effects become `StatusEffectKind` members. Sequence Phase 5 before Phase 8. |
| 6 | **`MonsterDisadvantageRounds`** - D3 drops it, D5 keeps it. | D3 wins; it folds into `Weakened`. The named regression guard is `Weakened_replaces_the_old_disadvantage_column`. |
| 7 | **The six shop slots.** D3 reserves 2, D5 reserves 2 and raises the cap to Epic, D2 adds 2 overflow. That is 6 of 6 reserved plus overflow, leaving no general stock. | `OfferCount` stays 6 and stays capped at Rare. Consumables take at most 1 reserved slot. Seasonal and faction stock replace general offers rather than adding to them. |
| 8 | **`ObjectiveKind = 4`** claimed by three designs. | Assigned in build order: `DiscoverMonster = 4` (Phase 4), `WinHunt = 5` (Phase 6), `HoldStreak = 6` (Phase 7). Counters are keyed by objective id string, so this is a merge conflict, not data corruption - but it still has to be arbitrated once. |
| 9 | **Monster catalog and `IsAvailableAt`.** D5 widens the availability band from `-2` to `-4`; D6 adds a test asserting the current band. | D6 owns levels 1-14 and the coverage test. **No design may change `IsAvailableAt` as a side effect** - the band is shared, and D6 is also right that its doc comment currently contradicts the code. Fix the comment, keep the band. |
| 10 | **`inventory_items`: stacking vs affixes.** Two consumables with different affixes collide on D3's `UNIQUE (UserId, ItemKey, Rarity)` stack index and silently merge, losing one item's affixes. | Consumables never carry affixes. `AffixRules.RollableFor` must exclude `ItemSlot.Consumable`, stated in Phase 3 whether or not Phase 5 has landed. |
| 11 | **Four designs mutate `CompleteTaskResponse`.** | Nothing was added to it. `TaskDto` carries `awardsProgression` instead, which answers "will this pay?" without a new response field per feature. Later phases should widen `TaskDto`, not the response. **Shipped.** |
| 12 | **Tag semantics.** D2 needs insertion order preserved because the first tag is the hunt's primary faction; D1's normaliser slugifies and reorders. | Order is preserved and original case is kept; de-duplication is case-insensitive, cap 10. First-element identity is therefore stable. Consumers matching a faction must case-fold. **Shipped.** |

### Three the review caught in D1 that changed what shipped

**The monotonic gate had a misclick flaw.** The review's ruling was that `XpEligibleFrom`
must be strictly monotonic and never cleared, closing the "set daily, complete, set none,
complete" printer. Correct about editing, wrong about reopening: under a strictly monotonic
gate, ticking a daily task by accident and immediately unticking it destroyed that day's
reward with no way to earn it back. The shipped rule clears the gate only on a reopen that
actually refunded, which closes the printer and the misclick together. Recorded as DEC-014.

**The successor-row model was dropped entirely.** D1 spawned a new row per occurrence under
a `RecurrenceRootId` partial unique index, and the review found that index breaks the normal
path: complete A, start successor B, reopen A, and two live rows in one series make reopen
return 500. Recurrence is now a derived rollover on the same row, so there is no successor,
no index and no 500. This also removes the review's DEC-002 objection to storing
`XpEligibleFrom`, since the stored value is now the eligibility itself rather than a cached
derivation of a rule that can be edited.

**`ParentIsSubtask` was not built.** D1 enforced one-level nesting with a stored constant
column, a `CHECK` constraint and a hand-written composite foreign key, invisible to the model
snapshot - which D1 itself admitted a future scaffold would silently delete. Nesting depth is
enforced on write instead. The honest cost: the guard is in C#, not in Postgres, so a direct
`INSERT` could nest two deep. Accepted, because a guard a migration silently removes is worse
than one that is visibly in the service.

### The Stick, Rebuilt

D5 built overdue debuffs - `Burden`, up to −4 armour class for having a backlog - because the
brief it was handed said debuffs were in scope. **The repository says the opposite.** DEC-013
was amended before any code was written, in a section titled "Overdue: The Carrot, Not The
Stick", precisely because "a stick applied to someone with a backlog compounds the problem it
claims to solve".

Under D2 plus D5 together, a task overdue by a month would simultaneously pay double gold and
have been subtracting 4 armour class from every fight for thirty days. The carrot and the
stick nailed to the same task.

`Burden` and `BurdenRules` are deleted. D2's bounty multiplier - `100 + min(100, days*100/30)`,
capped at 200% - is the mechanic, and its cap answers DEC-013's standing follow-up question
about a year-old task paying absurdly.

D5's `Rust` (idle decay) survives; it is mechanic 4 and still in scope. D5 is also the only
design that answers DEC-013's other two open items: `MaxRust = 6` is the decay floor and
`MinimumLevel = 5` is the threshold below which a new character is never decayed.

**This paraphrase failure is the lesson.** A designer handed a summary of DEC-013 rebuilt the
one mechanic DEC-013 exists to forbid. The decision log has to be written to survive being
summarised.
