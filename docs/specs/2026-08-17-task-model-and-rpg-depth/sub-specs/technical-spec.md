# Technical Specification

This is the technical specification for the spec detailed in
@docs/specs/2026-08-17-task-model-and-rpg-depth/spec.md

## Phase 1: Task Model - Shipped

### Schema

`TaskModelOverhaul` on `tasks`:

| Column | Type | Note |
|---|---|---|
| `Status` | `integer` | `Todo=0, InProgress=1, Completed=2`. Replaces `IsCompleted`. |
| `ParentId` | `uuid` null | Self-reference, cascade delete. One level, enforced on write. |
| `Tags` | `text[]` not null | Default `'{}'`. GIN index. |
| `StartedAt` | `timestamptz` null | Stamped on entering In progress. |
| `Recurrence` | `integer` | `None=0, Daily=1, Weekly=2, Monthly=3`. |
| `XpEligibleFrom` | `timestamptz` null | The recurrence gate. |
| `StaminaAwarded` | `integer` | Added earlier in `SnapshotStaminaAwarded`. |

Indexes: `(UserId, Status, SortOrder)` replaces `(UserId, IsCompleted, SortOrder)`;
`(UserId, CompletedAt)` unchanged; `ParentId`; GIN on `Tags`.

**The migration is not mechanical.** The scaffold added `Status` with default 0 and then
dropped `IsCompleted`, which reopens every completed task in the database while leaving its
XP banked. The backfill (`UPDATE tasks SET "Status" = 2 WHERE "IsCompleted"`) is hand-added
and the drop moved after it. `Tags` needs `defaultValueSql: "'{}'"` or the `NOT NULL` add
fails on any non-empty table. `Down` mirrors the backfill. Both directions verified against
a populated database.

### The gate

```csharp
public bool IsProgressionBearing => ParentId is null;

public bool MayAwardAt(DateTimeOffset moment) =>
    IsProgressionBearing && (XpEligibleFrom is null || moment >= XpEligibleFrom.Value);

public TaskProgress StatusAt(DateTimeOffset moment) =>
    Status == TaskProgress.Completed
    && Recurrence != RecurrenceRule.None
    && XpEligibleFrom is not null
    && moment >= XpEligibleFrom.Value
        ? TaskProgress.Todo
        : Status;
```

`CompleteAsync` asks `MayAwardAt` once into `awards` and returns early when false, paying
nothing: no XP, no stamina, no hit points, no `TasksCompleted`, no badges, no quest
progress. `ReopenAsync` clears `XpEligibleFrom` when and only when it refunded XP.

`StatusAt` is the rollover. Any query filtering on the `Status` column must reproduce it in
SQL; `GetTasks` does, in the `open`/`done` branches. This is the standing hazard of the
approach and the thing to check in review.

### API

- `GET /api/tasks` gains `?tag=`; returns parents only, each with `subtasks[]`.
- `GET /api/tasks/tags` - the caller's distinct tags.
- `PUT /api/tasks/{id}/status` - one route for all six transitions, returning one
  `SetStatusResponse` with a signed `xpDelta`. Completing and reopening route through
  `GamificationService`; Todo↔InProgress is just the column.
- `POST /api/tasks` gains `tags`, `recurrence`, `parentId`. A `parentId` that is missing,
  belongs to another user, or is itself a subtask returns 400.
- `TaskDto` gains `parentId`, `tags`, `status`, `startedAt`, `staminaAwarded`,
  `recurrence`, `awardsProgression`, `daysOverdue`, `subtasks`.

### Frontend

`TaskBoard` replaces `TaskList`: three columns, native HTML5 drag. The card's `dragover`
must `stopPropagation` or the column handler resets the insert position on every event and
all reorders land at the bottom. Every drag is also reachable from the keyboard through the
chevrons on each card. Reorder posts both open columns concatenated so `SortOrder` stays
globally coherent.

## Phase 2: Adventurer Strip - Shipped

`AdventurerStrip` above the board: class, level, a health bar that recolours off its own
fraction, stamina, gold. Reads the existing `/api/rpg/sheet`; no new endpoint.

## Phase 3: Items, Affixes and Sets

Independent of every other phase. Split as the design proposed, in two commits:

1. **Mechanics** against the existing 34 items - prefix/suffix rolled on drop by rarity,
   set bonuses, salvage, crafting. `DisplayName` and every affix effect computed, never
   stored; this is the only design in the batch with no DEC-002 exposure and it should stay
   that way.
2. **Content** - 42 new items as a pure catalog commit with no code change. This is the
   strongest available demonstration that DEC-004 works.

**Binding:** `AffixRules.RollableFor` returns zero slots for `ItemSlot.Consumable`, stated
here whether or not Phase 5 exists yet (conflict 10).

## Phase 4: Content, Bestiary and Feel

One table (`bestiary_entries`), two GETs, an XP float layer, a WebAudio synth. The code is
M; **the schedule is the writing** - roughly 700 strings across 81 lore fragments, ~240
flavour lines, 19 monsters and 24 quests. Estimate it as content, not engineering, and
expect tonal drift to be the real risk.

Owns monster levels 1-14 and the `Every_level_from_one_to_fourteen_has_an_opponent`
coverage test. Fix the `IsAvailableAt` doc comment, which currently says "within one level
either way" while the code is `Level <= level+1 && Level >= level-2`. Do not change the band.

`BestiaryEntry.Kills`/`Encounters`/`GoldTaken`/`BestRound` are derivable from `encounters`
and the migration backfills them with a `GROUP BY`, which is an argument against the
columns. They are kept anyway, so the chronicle stays prunable and so sightings of monsters
never killed can be recorded - **write that down as a deliberate DEC-002 exception in the
migration**, because "we backfilled it from the source of truth" reads as a mistake later.

Flavour text must never draw from the injected `IDiceRoller`, or every existing
`SequenceDiceRoller` script in the suite changes meaning.

## Phase 5: Encounter Depth

Status effects, boss phases, consumables, dungeon runs. Roughly twice its label; dungeons
alone are XL, so ship in that order and stop at any point.

- `Encounter.Effects` is `StatusEffect[]` (conflict 5). `MonsterDisadvantageRounds` is
  dropped and folded into `Weakened` (conflict 6), with
  `Weakened_replaces_the_old_disadvantage_column` as the named guard.
- The active-encounter partial unique index is unchanged (conflict 4).
- `Encounter.Phase` stores the highest phase entered and is allowed to disagree with
  `PhaseAt(currentHitPoints)` when a monster heals. Per-fight scratch state - name it as
  intentional in the entity comment.
- Consumables stack on `UNIQUE (UserId, ItemKey, Rarity) WHERE Slot = 3` and carry no
  affixes.
- Effect ticks change how many rolls a round consumes, so **every** existing
  `SequenceDiceRoller` script needs revisiting. Land this before Phase 8's companion
  attacks so they are rewritten once.

## Phase 6: Tasks as Monsters

The user's own framing: an overdue task becomes a named opponent whose difficulty comes
from the task's difficulty and subtask count.

- A hunt is an `Encounter` with `TaskId` set. It reuses the existing active-encounter index
  and `POST /api/tasks/{id}/complete`; there is no subtask-completion endpoint and no
  second combat loop (conflicts 3 and 4).
- **Do not store the derived stat block.** The design froze `MaxHitPoints`, `Level`,
  `ArmourClass` and the damage notation computed from the hunter's own sheet, and its own
  risk list then noted that equipping a better weapon mid-hunt trivialises the fight while
  selling one strands the player. That is exactly the drift DEC-002 exists to prevent.
  Store the frozen **inputs** - `(ArchetypeKey, Level, DaysOverdueAtStart,
  SubtaskCountAtStart)` - and derive the block, the way boss phases do.
- **Hunt resolution runs after the XP transaction commits, never inside it.** Nesting the
  combat, loot and faction writes into `CompleteAsync`'s transaction turns DEC-012's
  structural guarantee ("no endpoint can grant XP") into a discipline problem, because
  every future combat change becomes a change to the XP code path. A swallow-and-continue
  `catch` around it is not a substitute.
- Bounty gold is `100 + min(100, days * 100 / 30)` percent, capped at 200%.
- `WinHunt = 5` in `ObjectiveKind` (conflict 8).
- Factions derive from tags; match case-insensitively (conflict 12).

## Phases 7-8: The Long Game

Split into at least three specs. As designed this was three to four times its label, and it
requires `ResolvePlayerAction` to be split before any new combat branch lands, not after.

**7a. Streaks, decay and the daily reward.** Streak with freezes, daily reward, `Rust` with
`MaxRust = 6` and `MinimumLevel = 5`. Those two constants close DEC-013's remaining
follow-up items and should be recorded against it when they land. `HoldStreak = 6`.
`Burden` is not built.

**7b. Refactor `ResolvePlayerAction`** before 8a. Mandatory, and it is the codebase's
longest method.

**8a. Subclasses** - one per class first, not twelve at once.
**8b. Companion** - new table, new combat actor, new roll style. After Phase 5, so the dice
scripts are rewritten once.
**8c. Titles and seasons.**

**Not scheduled:** the household leaderboard (needs its own decision record; it is the only
cross-user read anyone has proposed and it contradicts "another user's resource returns
404") and ascension (destructive and row-deleting as designed).

## Standing Constraints

- **DEC-012.** No route added by any phase may write `TotalXp`. The invariance tests at
  `CombatServiceTests.Fighting_a_monster_to_death_never_moves_experience_or_level` and
  `RpgEndpointTests.No_adventure_route_can_move_experience` keep passing untouched.
- **The ledger invariant.** At any moment the character holds exactly the XP its completed
  tasks record. `No_sequence_of_ticks_edits_and_reopens_can_unbalance_the_ledger` asserts
  it across an arbitrary operation sequence; every phase that touches completion should
  extend that sequence rather than write a new bespoke test.
- **The wire-shape tripwire.** The hand-written `private record TaskDto` / `CharacterDto` at
  the bottom of `UserIsolationTests` and `RpgEndpointTests` fail to deserialise on any DTO
  change. That is intended - a wire rename fails a test instead of compiling silently - so
  every DTO-widening phase touches the isolation tests in the same commit.
- **Every new route joins the isolation suite.** One user's chronicle, purchases, hunts and
  bestiary must be invisible and untouchable by another.
