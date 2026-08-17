# Product Roadmap

Effort scale: `XS` 1 day, `S` 2-3 days, `M` 1 week, `L` 2 weeks, `XL` 3+ weeks.

## Phase 0: Already Completed

Built and verified on 2026-08-16. Both verification suites pass against local `dotnet run`
and against the Compose stack, and a fresh-volume install plus a restart-persistence check
were run.

### Features

- [x] **Task CRUD** - Create, edit, complete, reopen and delete tasks with title, notes,
      difficulty, priority and due date `M`
- [x] **Difficulty-scaled XP** - Easy 10, Medium 25, Hard 50, Epic 100, snapshotted onto
      the task at completion `S`
- [x] **Level curve and ranks** - Cumulative XP `25 x L x (L - 1)`, level derived rather
      than stored, eight rank titles from Novice to Legend `S`
- [x] **Achievement system** - Thirteen badges evaluated after every completion, catalog
      held in code so new badges need no migration `M`
- [x] **Idempotent completion and refunds** - Re-completing awards nothing, reopening
      refunds exactly what was granted, clamped at zero, badges never revoked `S`
- [x] **Character panel** - Editable name, ten avatars, level ring, stat tiles `S`
- [x] **Filtering and search** - By status, by difficulty, and free-text over title and
      notes using Postgres ILIKE `XS`
- [x] **Stats view** - Fourteen-day activity chart, completed-by-difficulty breakdown,
      rank ladder `S`
- [x] **Dark / light / system theming** - Persisted to localStorage, applied pre-paint,
      live-updating when the OS preference changes `S`
- [x] **Completion feedback** - XP floats anchored to the task, animated XP rail,
      level-up overlay, badge toasts, all driven from a single response `M`
- [x] **Colourblind-validated palette** - Difficulty hues checked with a palette
      validator for both themes, charts direct-labelled `XS`
- [x] **Single-container deployment** - Three-stage Dockerfile, Compose stack, startup
      migrations with retry `M`
- [x] **Verification suites** - 40 API checks and 41 browser checks driving real Chrome,
      failing on any console error or failed request `M`

### Known Gaps Leaving Phase 0

- `POST /api/tasks/reorder` and the `SortOrder` column work, but no drag UI calls them.
- No unit tests. Coverage is entirely end-to-end through the two scripts.
- No repository published yet.

## Phase 1: Close the Gaps

**Goal:** Bring the shipped surface up to the level of the code behind it, and make the
project safe to change.
**Success Criteria:** Drag-to-reorder works end to end and is covered by the UI script;
`dotnet test` runs green in CI; the repository is public with a working first-run path.

### Features

- [ ] **Drag-to-reorder tasks** - Wire the existing reorder endpoint to a drag
      interaction on the open list, with optimistic ordering `S`
- [ ] **Unit tests for progression** - `LevelCurve`, `RankTitles` and
      `AchievementEvaluator` are pure and directly testable; cover the curve boundaries
      and every badge rule `S`
- [ ] **API integration tests** - `WebApplicationFactory` over a Testcontainers Postgres,
      covering completion, idempotency, refund clamping and the achievement transaction `M`
- [ ] **CI pipeline** - GitHub Actions running build, tests, lint and the API
      verification script against a service container `S`
- [ ] **Publish the repository** - Public GitHub repo, licence, contributing notes, and
      screenshots in the README `XS`

### Dependencies

- A GitHub organisation or account decision, and a licence choice.

## Phase 2: A Complete Todo App

**Goal:** Close the functional distance between Questward and an app someone would use
instead of their current list.
**Success Criteria:** A recurring task can be completed repeatedly without distorting the
XP economy, and a multi-step task can be tracked as one item.

### Features

- [ ] **Recurring tasks** - Daily, weekly and custom repeats, with the completed instance
      archived and the next occurrence generated `L`
- [ ] **Subtasks** - One level of nesting, with parent progress derived from children and
      a decision on whether children pay XP individually `M`
- [ ] **Tags** - Postgres `text[]` on the task, chip input on the card, tag filtering
      alongside the existing difficulty filter `S`
- [ ] **Keyboard-first interaction** - Quick-add focus shortcut, j/k navigation, complete
      without reaching for the mouse `S`

### Dependencies

- Recurrence needs an XP design decision first: repeats are the most obvious inflation
  vector in the whole system, and the anti-inflation stance from Phase 0 has to survive
  contact with them. See DEC-003 in `decisions.md`.
- Subtasks need a schema migration and a rethink of the Clean Slate badge, which counts
  open tasks.

## Phase 3: User Accounts with Auth0

**Goal:** Let a household or a small team share an instance without sharing a character,
using Auth0 as the identity provider.
**Success Criteria:** Two Auth0 accounts on one instance keep separate tasks, XP and
badges; the API rejects unauthenticated calls; and an existing single-user database
migrates without losing progress.

### Features

- [x] **Auth0 tenant and application** - Tenant, SPA application and API audience
      configured, with the settings supplied to the container as environment variables
      rather than baked into the image `S`
- [x] **API authentication** - JWT bearer validation against the Auth0 issuer, endpoints
      moved behind `RequireAuthorization`, `sub` claim resolved to the local user `M`
- [x] **SPA sign-in** - Auth0 React SDK with authorization code plus PKCE, token attached
      to API requests in `web/src/lib/api.ts`, sign-in and sign-out UI `M`
- [x] **Per-user data** - `UserId` on tasks and achievement unlocks; the singleton
      character row becomes one row per user, keyed by the Auth0 `sub` `M`
- [x] **Migration path** - Superseded during the spec: existing tasks, characters and
      unlocks are dropped by the `AddUserAccounts` migration rather than adopted, since
      the project has never been released and there is no deployed data at risk `XS`
- [ ] **Shared leaderboard** - Optional per-instance ranking across accounts, opt-in `S`

### Dependencies

- Phase 1 test coverage should land first. Retrofitting auth across every endpoint
  without integration tests is how a working app quietly stops working.
- An Auth0 tenant, and a decision on what happens when it is unreachable. See DEC-011.

### Open Questions

- **Offline and LAN-only instances.** Auth0 sign-in needs outbound internet and a
  reachable Auth0 tenant, so an instance on an isolated network cannot log in at all.
  Decide whether to ship a fallback (a local single-user mode, or a self-hosted OIDC
  option) or to state the internet requirement plainly as a constraint.
- **Free-tier limits.** Confirm the Auth0 free tier covers the expected user count for a
  household instance before committing publicly.
- **Who may sign up.** An Auth0 tenant will accept any sign-up by default. A self-hosted
  instance needs an allowlist, an invite flow, or connection-level restriction, or the
  first stranger to find the URL gets an account.

## Phase 4: The RPG Layer (Shipped 2026-08-17)

**Goal:** Give the XP earned from real work somewhere to go, without ever becoming an
alternative source of it.
**Success Criteria:** Fighting and questing provably cannot move character XP, asserted by
tests at both the service and HTTP layers.

### Features

- [x] **Ability scores and derived stats** - The six D&D abilities, with armour class,
      attack bonus, damage, proficiency and max hit points all derived from class, level
      and equipment `M`
- [x] **Six classes** - Distinct hit dice, ability spreads, starting gear and one
      rule-bending perk each `S`
- [x] **A d20 engine** - Injectable dice roller, natural 20 and natural 1 overrides,
      advantage and disadvantage, and a fully itemised roll breakdown on the wire `M`
- [x] **Monster combat** - Code-held bestiary, round-by-round encounters gated on Stamina,
      persistent hit points and a complete combat log `L`
- [x] **Equipment and loot** - Rarity rolls, weighted loot tables, three equipment slots
      and selling for gold `M`
- [x] **Quests** - Code-held catalog with countable objectives driven by real events `M`

### The Constraint That Shaped It

Monsters and quests grant gold and loot, never XP, and every fight costs Stamina that only
completing a task produces. Your level is a record of work done; your gear is what you did
with it. See DEC-012.

### Known Gaps Leaving Phase 4

- Gold accumulated with nothing to spend it on.
- Winning a fight discarded the result before it could be read.
- Healing happened silently, so it looked broken.
- All six class perks were passive; combat was one button for everyone.

## Phase 5: The Expansion (Shipped 2026-08-17)

**Goal:** Make the RPG layer legible and give it depth, without loosening the rule that
only real work grants experience.

### Features

- [x] **Victory and defeat summary** - The finished encounter stays on screen with gold,
      loot, quests advanced and an explicit note that experience did not move `S`
- [x] **Chronicle** - Every finished fight with its full log, plus lifetime totals `M`
- [x] **Visible regeneration and tavern rest** - The countdown to the next hit point and to
      full, plus a paid full heal priced by damage and level `S`
- [x] **Quest log** - Filters, a claimed counter, and above-level quests shown locked
      rather than hidden `S`
- [x] **Market and reforging** - Six daily offers computed from user and date, capped at
      Rare, plus paying to raise an item one rarity `M`
- [x] **More gear** - 17 items to 34, with every monster's loot table widened `S`
- [x] **Class abilities** - One active ability per class, twice per fight, each bending a
      different combat rule `L`

### Known Gaps

- No consumables, multi-monster encounters or procedural items.
- The Adventure UI still has not been driven end to end in a browser, because that needs a
  signed-in session.
- Class abilities are one per class; a second tier at higher levels is the obvious follow-up.

## Phase 6: The Task Model (Shipped 2026-08-17)

**Goal:** Give tasks the structure the RPG layer needs to draw on, without giving anyone a
second way to earn experience for the same work.

### Features

- [x] **Subtasks** - a task nested under a task, one level, with a `3/8` counter on the
      card. Ticking a step is progress and pays nothing `M`
- [x] **Repeating tasks** - daily, weekly or monthly, returning to the board on their own
      with no reset button and no overnight job `M`
- [x] **Tags** - Postgres `text[]` with a GIN index, chips on the card, a filter row, and
      autocomplete from what has already been used `S`
- [x] **Three-column board** - to do, in progress, done, with drag between columns and
      within them, and the same moves on the keyboard `M`
- [x] **One status route** - all six transitions through `PUT /api/tasks/{id}/status`,
      returning a signed `xpDelta` so the client never branches on response shape `S`
- [x] **Adventurer strip** - class, health, stamina and gold above the board, so the
      resource the adventure spends is visible where it is earned `S`

### The Constraint That Shaped It

Three features arrived that each add a new way to press "I finished something". The gate is
asked in exactly one place, `TodoTask.MayAwardAt`, and everything downstream hangs off that
one answer. Twenty subtasks pay what one task pays; a daily task pays once a day. See
DEC-014.

### What The Review Caught

Six competing designs contained twelve direct contradictions, four indirect XP leaks and
one exploit that was already live: reopening a task refunded its XP but kept the stamina
and hit points, so a complete/reopen loop minted five of each per cycle out of no work.
Fixed alone and first. The rulings on the rest are in
@docs/specs/2026-08-17-task-model-and-rpg-depth/spec.md.

One design rebuilt overdue debuffs - the single mechanic DEC-013 was amended to forbid -
because the brief it was handed paraphrased the decision instead of quoting it.

### Known Gaps

- `Status` is stored but `StatusAt` is what is true, so any new query filtering on the
  column has to reproduce the recurrence rollover in SQL.
- One-level nesting is enforced in the service, not in Postgres.
- The board has not been driven end to end in a browser: that needs a signed-in session,
  and the sign-in credentials are the product owner's.
- Phases 3-8 of the RPG expansion are specified but unbuilt.
