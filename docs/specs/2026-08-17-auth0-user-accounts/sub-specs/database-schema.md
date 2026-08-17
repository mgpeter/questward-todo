# Database Schema

This is the database schema implementation for the spec detailed in
@docs/specs/2026-08-17-auth0-user-accounts/spec.md

## Summary of Changes

| Change | Table | Reason |
|---|---|---|
| New table | `users` | Local identity record mapping an Auth0 `sub` to an internal id |
| Restructure | `character` to `characters` | One character per user instead of a pinned singleton |
| New column | `tasks.UserId` | Ownership |
| New column | `achievement_unlocks.UserId` | Ownership |
| Reindex | `tasks`, `achievement_unlocks` | Every query now leads with `UserId` |

Existing rows are deleted rather than migrated, per the decision recorded in the spec.
The project has never been released, so there is no deployed data at risk.

## New Table: users

```sql
CREATE TABLE users (
    "Id"          uuid         NOT NULL,
    "Auth0Sub"    varchar(128) NOT NULL,
    "Email"       varchar(320) NULL,
    "DisplayName" varchar(200) NULL,
    "CreatedAt"   timestamptz  NOT NULL,
    "LastSeenAt"  timestamptz  NOT NULL,
    CONSTRAINT "PK_users" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX "IX_users_Auth0Sub" ON users ("Auth0Sub");
```

**Rationale.** The internal `Id` is what every other table references, not the Auth0
`sub`. This is what makes DEC-011's portability claim real: swapping identity providers
becomes an update to one column in one table rather than a rewrite of every foreign key.

`Auth0Sub` is 128 characters because Auth0 subject values are provider-prefixed
(`auth0|...`, `google-oauth2|...`) and have no documented short bound. `Email` is 320,
the maximum length of an addr-spec. Both `Email` and `DisplayName` are nullable: not
every Auth0 connection returns them, and neither is used as an identifier.

The unique index on `Auth0Sub` is the concurrency guard for just-in-time provisioning,
not a convenience. Two simultaneous first requests from one user must not create two
rows; the insert that loses raises a unique violation and the handler re-reads.

## Restructured Table: characters

The current `character` table holds exactly one row, pinned by
`CONSTRAINT ck_character_singleton CHECK ("Id" = 1)`. That constraint and the integer key
both go.

```sql
DROP TABLE character;

CREATE TABLE characters (
    "UserId"         uuid        NOT NULL,
    "Name"           varchar(60) NOT NULL,
    "AvatarKey"      varchar(40) NOT NULL,
    "TotalXp"        integer     NOT NULL,
    "TasksCompleted" integer     NOT NULL,
    "CreatedAt"      timestamptz NOT NULL,
    CONSTRAINT "PK_characters" PRIMARY KEY ("UserId"),
    CONSTRAINT "FK_characters_users_UserId" FOREIGN KEY ("UserId")
        REFERENCES users ("Id") ON DELETE CASCADE
);
```

**Rationale.** Using `UserId` as the primary key rather than a separate surrogate enforces
one character per user in the schema itself, so the invariant cannot be violated by a bug
in provisioning. Cascade delete matches the existing convention for owned relationships.

`TotalXp` remains the single source of truth for progression. Level is still derived by
`LevelCurve` and still never stored, per DEC-002.

## Altered Table: tasks

```sql
DELETE FROM tasks;

ALTER TABLE tasks ADD COLUMN "UserId" uuid NOT NULL;

ALTER TABLE tasks ADD CONSTRAINT "FK_tasks_users_UserId"
    FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE;

DROP INDEX "IX_tasks_IsCompleted_SortOrder";
DROP INDEX "IX_tasks_CompletedAt";

CREATE INDEX "IX_tasks_UserId_IsCompleted_SortOrder"
    ON tasks ("UserId", "IsCompleted", "SortOrder");

CREATE INDEX "IX_tasks_UserId_CompletedAt"
    ON tasks ("UserId", "CompletedAt");
```

**Rationale.** `NOT NULL` with no default is only safe because the table is emptied first;
an unowned task must be impossible to represent. Both indexes are recreated with `UserId`
leading, because no query reads tasks across users any more. Leaving the old indexes in
place would make them dead weight that Postgres maintains on every write and never uses.

## Altered Table: achievement_unlocks

```sql
DELETE FROM achievement_unlocks;

ALTER TABLE achievement_unlocks ADD COLUMN "UserId" uuid NOT NULL;

ALTER TABLE achievement_unlocks ADD CONSTRAINT "FK_achievement_unlocks_users_UserId"
    FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE;

DROP INDEX "IX_achievement_unlocks_AchievementKey";

CREATE UNIQUE INDEX "IX_achievement_unlocks_UserId_AchievementKey"
    ON achievement_unlocks ("UserId", "AchievementKey");
```

**Rationale.** This is the most important index change in the migration. The existing
unique index is on `AchievementKey` alone, which under multi-user means the first user to
earn First Blood permanently prevents anyone else from earning it. The uniqueness that
matters is per user per badge, and it must stay a database constraint rather than a code
check, because it is what makes the badge grant in `GamificationService` safe under
concurrency.

## EF Core Implementation

- New entity `TodoApp.Models/User.cs` with `Id`, `Auth0Sub`, `Email`, `DisplayName`,
  `CreatedAt`, `LastSeenAt`.
- New configuration `TodoApp.Data/Configuration/UserConfiguration.cs`, following the
  existing pattern: explicit `HasColumnType` on every property, table name set via
  `ToTable`.
- `CharacterConfiguration` loses the `ck_character_singleton` check constraint, changes
  the key to `UserId`, and gains the cascade relationship to `User`.
- `TodoTaskConfiguration` and `AchievementUnlockConfiguration` gain `UserId`, its foreign
  key and the reindexing above.
- `Character.SingletonId` is deleted from `TodoApp.Models/Character.cs`. Every reference
  to it, including in `DatabaseInitializer.EnsureCharacterAsync` and
  `GamificationService.GetCharacterAsync`, must go.
- `DatabaseInitializer` stops seeding a character entirely. Characters are created during
  just-in-time user provisioning, not at startup.
- One migration, `AddUserAccounts`, containing the deletes and the schema changes in the
  order above. The deletes must precede the `NOT NULL` column additions or the migration
  fails on any non-empty table.

## Verification

- `dotnet ef migrations script` output reviewed before applying, specifically to confirm
  the `DELETE` statements precede the `ALTER TABLE ... NOT NULL` statements.
- Applying the migration to a database populated by the current schema succeeds.
- Applying the migration to an empty database succeeds, which is the fresh-install path.
- An integration test asserts that inserting two `achievement_unlocks` rows with the same
  `AchievementKey` and different `UserId` succeeds, and that the same key twice for one
  user fails.
- An integration test asserts that deleting a user cascades to their tasks, character and
  unlocks, leaving no orphans.
