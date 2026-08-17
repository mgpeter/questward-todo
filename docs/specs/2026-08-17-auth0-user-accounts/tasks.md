# Spec Tasks

These are the tasks to be completed for the spec detailed in
@docs/specs/2026-08-17-auth0-user-accounts/spec.md

> Ordered so each task leaves the codebase testable. The app itself is not usable in a
> browser between Task 3 and Task 4, because the API starts requiring a token before the
> SPA learns to send one.

## Tasks

- [x] 1. Test foundation and user schema
  - [x] 1.1 Create `tests/TodoApp.Tests` (xUnit v3, Testcontainers Postgres) and add it to
        `TodoApp.slnx`; add `public partial class Program;` so `WebApplicationFactory` can
        see the top-level-statement host
  - [x] 1.2 Write unit tests for `LevelCurve` boundaries and every `AchievementEvaluator`
        rule, covering the logic that has never had a test
  - [x] 1.3 Write failing schema tests: per-user badge uniqueness, one character per user,
        cascade delete leaving no orphans
  - [x] 1.4 Add the `User` entity and `UserConfiguration`
  - [x] 1.5 Restructure `Character` to `characters` keyed by `UserId`; remove
        `Character.SingletonId` and stop seeding in `DatabaseInitializer`
  - [x] 1.6 Add `UserId` to `tasks` and `achievement_unlocks`; rebuild indexes to lead with
        `UserId` and change the unlock unique index to `(UserId, AchievementKey)`
  - [x] 1.7 Generate the `AddUserAccounts` migration and review the SQL script for
        delete-before-not-null ordering
  - [x] 1.8 Verify all tests pass

- [x] 2. Authentication and current-user resolution
  - [x] 2.1 Write tests for token validation (no token, expired, wrong audience, wrong
        issuer) and for just-in-time user provisioning
  - [x] 2.2 Add `Microsoft.AspNetCore.Authentication.JwtBearer` and configure it against
        the Auth0 issuer and audience with all validation on
  - [x] 2.3 Add fail-fast startup validation for the Auth0 settings
  - [x] 2.4 Implement `ICurrentUser`: resolve `sub`, provision user plus character in one
        transaction, handle the unique-violation race by re-reading
  - [x] 2.5 Add `GET /api/config` (anonymous) and `GET /api/me` (authenticated)
  - [x] 2.6 Add the test authentication handler so tests never call Auth0
  - [x] 2.7 Verify all tests pass

- [x] 3. Per-user scoping
  - [x] 3.1 Write isolation tests: create as user A, assert user B sees nothing through
        tasks list, task by id, character, achievements and stats
  - [x] 3.2 Add `.RequireAuthorization()` to the API groups, keeping `/health`,
        `/api/config` and the `/api/{**rest}` catch-all anonymous
  - [x] 3.3 Scope `TaskEndpoints` by `UserId`, returning 404 rather than 403 for another
        user's task, and scope the `MIN(SortOrder)` read used by create
  - [x] 3.4 Scope all six reads in `GamificationService.CompleteAsync`, plus `ReopenAsync`
        and `GetCharacterAsync`
  - [x] 3.5 Scope `CharacterEndpoints`, `AchievementEndpoints` and `StatsEndpoints`
  - [x] 3.6 Add fixed-window rate limiting partitioned by `UserId`, and by IP on
        `/api/config`
  - [x] 3.7 Verify all tests pass, including a per-user regression of the Phase 0 XP flow

- [x] 4. Frontend sign-in
  - [x] 4.1 Add `@auth0/auth0-react`; bootstrap from `/api/config` before rendering, with
        an explicit failure screen
  - [x] 4.2 Configure `Auth0Provider` with the API audience, localstorage cache and
        refresh token rotation
  - [x] 4.3 Attach bearer tokens in `web/src/lib/api.ts` via a registered token getter,
        keeping the client free of React dependencies; handle 401 with one silent renewal
  - [x] 4.4 Add `<AuthGate>` and the sign-in screen
  - [x] 4.5 Add the account menu to `Header.tsx`, clearing the TanStack Query cache on
        sign-out
  - [x] 4.6 Verify the SPA builds and typechecks

- [x] 5. Verification, config and docs
  - [x] 5.1 Add the Auth0 variables to `.env.example`, `docker-compose.yml` and
        `docker-compose.dev.yml`
  - [x] 5.2 Rework `scripts/verify-api.ps1` to take a token parameter
  - [x] 5.3 Rework `scripts/verify-ui.mjs` to sign in through Auth0 and add a two-user
        isolation check
  - [x] 5.4 Update the README: Auth0 setup, the Users bullet, and the open sign-up warning
  - [x] 5.5 Verification: `dotnet test` (106) green; live JWT validation, sign-in handoff
        and the container path verified against the real tenant; full flow confirmed
        working in local Docker by the product owner. The automated `verify-ui.mjs` run
        still needs a test user's credentials and is not yet exercised.
  - [x] 5.6 Tick off this file and mark the Phase 3 roadmap items complete
