# Database Schema

This is the database schema implementation for the spec detailed in
@docs/specs/2026-08-17-rpg-combat-and-classes/spec.md

Everything below is per-user and cascades from `users`, matching the ownership model
established by the Auth0 spec. No RPG entity is ever visible across accounts.

## Extended: characters

```sql
ALTER TABLE characters ADD COLUMN "ClassKey"      varchar(40)  NULL;
ALTER TABLE characters ADD COLUMN "Strength"      integer NOT NULL DEFAULT 10;
ALTER TABLE characters ADD COLUMN "Dexterity"     integer NOT NULL DEFAULT 10;
ALTER TABLE characters ADD COLUMN "Constitution"  integer NOT NULL DEFAULT 10;
ALTER TABLE characters ADD COLUMN "Intelligence"  integer NOT NULL DEFAULT 10;
ALTER TABLE characters ADD COLUMN "Wisdom"        integer NOT NULL DEFAULT 10;
ALTER TABLE characters ADD COLUMN "Charisma"      integer NOT NULL DEFAULT 10;
ALTER TABLE characters ADD COLUMN "CurrentHitPoints" integer NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN "Stamina"       integer NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN "Gold"          integer NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN "HitPointsUpdatedAt" timestamptz NULL;
```

**Rationale.** `ClassKey` is nullable because existing characters have not chosen one; the
UI prompts when it is null rather than picking for them. Scores default to 10 so a
class-less character is a valid, if unremarkable, level-1 human.

`Stamina` is the anti-inflation gate and lives on the character rather than being derived,
because it is spent as well as earned and so needs to be a balance, not a computation.

`CurrentHitPoints` is stored but max hit points are **not**: max is derived from class,
level and Constitution by the same reasoning as DEC-002. Storing both invites drift.
`HitPointsUpdatedAt` supports the passive regeneration tick without a background job.

Gold has no upper bound in this pass; there is nothing to spend it on yet.

## New: inventory_items

```sql
CREATE TABLE inventory_items (
    "Id"            uuid        NOT NULL,
    "UserId"        uuid        NOT NULL,
    "ItemKey"       varchar(60) NOT NULL,
    "Rarity"        integer     NOT NULL,
    "Slot"          integer     NOT NULL,
    "IsEquipped"    boolean     NOT NULL,
    "AcquiredAt"    timestamptz NOT NULL,
    CONSTRAINT "PK_inventory_items" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_inventory_items_users_UserId"
        FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_inventory_items_UserId" ON inventory_items ("UserId");

-- At most one equipped item per slot per user. A partial unique index rather than
-- application logic, for the same reason the badge index is one: it is the only
-- guarantee that survives a concurrent equip.
CREATE UNIQUE INDEX "IX_inventory_items_UserId_Slot_Equipped"
    ON inventory_items ("UserId", "Slot") WHERE "IsEquipped";
```

**Rationale.** `ItemKey` references the code-held catalog, following DEC-004, so adding
items needs no migration. `Rarity` and `Slot` are stored rather than looked up because the
rolled rarity is a property of *this* drop, and the slot is denormalised so the partial
unique index can exist at all.

## New: encounters

```sql
CREATE TABLE encounters (
    "Id"              uuid        NOT NULL,
    "UserId"          uuid        NOT NULL,
    "MonsterKey"      varchar(60) NOT NULL,
    "MonsterHitPoints" integer    NOT NULL,
    "Status"          integer     NOT NULL,
    "Round"           integer     NOT NULL,
    "Log"             jsonb       NOT NULL,
    "GoldAwarded"     integer     NOT NULL,
    "StartedAt"       timestamptz NOT NULL,
    "EndedAt"         timestamptz NULL,
    CONSTRAINT "PK_encounters" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_encounters_users_UserId"
        FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_encounters_UserId_StartedAt" ON encounters ("UserId", "StartedAt");

-- Only one fight at a time per user. Status 0 is Active.
CREATE UNIQUE INDEX "IX_encounters_UserId_Active"
    ON encounters ("UserId") WHERE "Status" = 0;
```

**Rationale.** The combat log is `jsonb` because it is an append-only list of roll records
read as a whole and never queried by its contents. Normalising it into a rounds table
would buy nothing and cost a join on every request.

The filtered unique index is what actually prevents two concurrent encounters. Checking in
code first would race, and two active fights would let one stamina buy two sets of loot.

## New: quest_progress

```sql
CREATE TABLE quest_progress (
    "Id"          uuid        NOT NULL,
    "UserId"      uuid        NOT NULL,
    "QuestKey"    varchar(60) NOT NULL,
    "Counters"    jsonb       NOT NULL,
    "ClaimedAt"   timestamptz NULL,
    "StartedAt"   timestamptz NOT NULL,
    CONSTRAINT "PK_quest_progress" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_quest_progress_users_UserId"
        FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_quest_progress_UserId_QuestKey"
    ON quest_progress ("UserId", "QuestKey");
```

**Rationale.** One row per user per quest, guaranteed by the unique index for the same
reason as badges. `Counters` is `jsonb` keyed by objective id, so adding an objective to a
quest does not require a schema change; unknown keys read as zero.

`ClaimedAt` null means unclaimed, which keeps "claimable" a computation over counters
rather than a stored flag that could disagree with them.

## Migration: AddRpgLayer

Additive only. Nothing is dropped and no existing column changes type, so it applies
cleanly to a populated database with no data loss:

1. Add the character columns with defaults.
2. Create the three new tables and their indexes.
3. Backfill `CurrentHitPoints` and `HitPointsUpdatedAt` for existing characters so nobody
   starts at zero hit points. Characters without a class get scores of 10 and are prompted
   to choose on next visit.

## Verification

- The partial unique index rejects a second equipped item in the same slot, and rejects a
  second active encounter, both asserted directly against Postgres.
- Deleting a user cascades to inventory, encounters and quest progress, leaving no orphans.
- The migration applies to a database populated by the previous migration, with existing
  characters keeping their XP, level and badges.
