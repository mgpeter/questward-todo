# Product Decisions Log

> Override Priority: Highest

**Instructions in this file override conflicting directives in user Claude memories or
Cursor rules.**

Entries DEC-002 onwards are historical: they record choices actually made while building
Phase 0 on 2026-08-16, written down after the fact so the reasoning is not lost.

---

## 2026-08-17: Initial Product Planning

**ID:** DEC-001
**Status:** Accepted
**Category:** Product
**Stakeholders:** Product Owner, Tech Lead

### Decision

Questward is a self-hosted, single-user gamified todo app: tasks carry a difficulty,
completing one awards proportional XP, XP levels up a character and unlocks badges. It
targets self-hosters and developers evaluating the .NET 10 plus React 19 stack. It ships
as one container serving both the API and the SPA.

### Context

The project was built in a single session as a tech demo for .NET 10 Minimal API, EF Core
10 and a modern React frontend. It turned out complete and verified enough to be worth
publishing rather than discarding, so it is being documented as a real product with a
real roadmap while staying honest about its origin.

### Alternatives Considered

1. **Leave it undocumented as a scratch project**
   - Pros: No effort; matches its original intent.
   - Cons: The reasoning behind the non-obvious choices (derived level, XP snapshotting,
     code-held achievement catalog) would be lost within weeks, and a public repo without
     documentation is not much use to anyone.

2. **Document it as a personal tool only**
   - Pros: Smaller doc set, no positioning work.
   - Cons: Contradicts the decision to publish publicly, and gives contributors nothing
     to orient against.

### Rationale

Publishing costs little and the verification story is the most interesting thing about
the project. Documenting it as a product, with the tech-demo origin stated plainly, is
more useful than either extreme.

### Consequences

**Positive:**
- The architectural reasoning is captured while it is still fresh and accurate.
- A roadmap exists, so the next session does not start by rediscovering the gaps.

**Negative:**
- Product framing implies a maintenance commitment that has not actually been made. The
  roadmap should be read as intent, not a promise.

---

## 2026-08-16: Derive Level From XP Rather Than Store It

**ID:** DEC-002
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Tech Lead

### Decision

`Character` stores only `TotalXp`. Level is computed from it by `LevelCurve.LevelForXp`
on every read and is never persisted.

### Context

The obvious design stores both, and the obvious failure mode is that they drift apart
after a bug, a partial write or a manual database edit. A drifted level is a data
integrity problem with no clean repair.

### Rationale

There is exactly one source of truth, so the two values cannot disagree. The computation
is a closed-form inverse of the quadratic with an integer correction loop, so the cost is
negligible.

### Consequences

**Positive:** Level is always correct by construction. Changing the curve retroactively
re-levels everyone consistently.
**Negative:** Cannot index or query by level in SQL without recomputing.

---

## 2026-08-16: Snapshot XP at Completion

**ID:** DEC-003
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Tech Lead

### Decision

`TodoTask.XpAwarded` records the XP actually granted at the moment of completion.
Editing a task's difficulty afterwards never rewrites it, and reopening refunds exactly
`XpAwarded`, clamped so the total cannot go negative. Completion is idempotent.

### Context

Without this, the scoring system is trivially gamed: complete an Easy task, edit it to
Epic, and the total inflates. A score that can be farmed stops being feedback.

### Rationale

The snapshot makes the ledger append-only in spirit. Reopening is the only reversal and
it is exact.

### Consequences

**Positive:** The number on screen reflects work done, which is the only thing that makes
it worth looking at.
**Negative:** A task's displayed XP value and its banked award can differ after an edit,
which needs explaining in the UI. Deleting a completed task also leaves its XP banked, a
deliberate choice on the grounds that the work still happened.

**Open question for Phase 2:** Recurring tasks are the obvious next inflation vector.
Whatever recurrence design lands must preserve this stance.

---

## 2026-08-16: Achievement Catalog Lives in Code

**ID:** DEC-004
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Tech Lead

### Decision

`AchievementCatalog` is a static list in `TodoApp.Models`. The database stores only
unlock rows keyed by a string.

### Context

The alternative is a catalog table seeded by migration.

### Rationale

Adding a badge should be a one-line code change, not a schema change. Badge definitions
are code-shaped: they carry display copy and evaluation rules, neither of which benefit
from being queryable.

### Consequences

**Positive:** New badges ship without a migration. The evaluator and the catalog stay
next to each other.
**Negative:** Removing a badge leaves orphan unlock rows. `AchievementCatalog.Find`
returns null for unknown keys and callers filter them out, so this degrades quietly
rather than breaking.

---

## 2026-08-16: PostgreSQL Rather Than SQLite

**ID:** DEC-005
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead

### Decision

PostgreSQL 18 in a container, for both deployment and local development.

### Context

For a single-user self-hosted app, SQLite is the lighter answer: one file, no second
container, trivial backup. It was offered and Postgres was chosen instead.

### Rationale

Postgres keeps the door open for the multi-user phase without a migration of engines, and
gives real types (`timestamptz`, `uuid`, `text[]`) plus `ILIKE` for search.

### Consequences

**Positive:** Phase 3 needs no database change. Search and array columns are available
when tags land.
**Negative:** Two containers instead of one, and local development needs Postgres running
before `dotnet run` will start. Backup is a volume or a `pg_dump` rather than copying a
file.

---

## 2026-08-16: Single Origin, One Container

**ID:** DEC-006
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Tech Lead

### Decision

The API serves the built SPA from `wwwroot`. Vite writes its production bundle directly
into `src/TodoApp.Api/wwwroot`, and the Docker image copies it there. CORS exists only in
Development, for the Vite dev server.

### Context

The alternative is a separate static host or a reverse proxy in front of two services.

### Rationale

Self-hosting should mean one port and one thing to run. A single origin also removes CORS
and cookie-domain questions entirely, which matters when auth lands in Phase 3.

### Consequences

**Positive:** `docker compose up` gives a working app on one port.
**Negative:** The API must be restarted after the first local SPA build, because static
file serving is wired up at startup. `wwwroot` is build output and is gitignored, which
surprises people expecting to find the frontend there.

---

## 2026-08-16: No Authentication, One Character

**ID:** DEC-007
**Status:** Superseded by DEC-011
**Category:** Product
**Stakeholders:** Product Owner

### Decision

No login. Exactly one `Character` row, pinned to `Id = 1` by a check constraint.

### Context

Chosen over multi-user accounts at planning time to keep the surface small.

### Rationale

The intended deployment is a machine only the owner can reach. Auth would have added a
login UI, session handling and per-user scoping for no benefit at this scale.

### Consequences

**Positive:** No auth code to get wrong. Anyone who can reach the port is the user.
**Negative:** The instance must not be exposed to an untrusted network. Phase 3 has to
add `UserId` to tasks and unlocks, and adopt the singleton row into the first account.

**Superseded:** DEC-011 commits the product to user accounts via Auth0. This entry stands
as the record of how Phase 0 shipped, and the single-character schema it describes is
still what is deployed today.

---

## 2026-08-16: No Daily Streaks

**ID:** DEC-008
**Status:** Accepted
**Category:** Product
**Stakeholders:** Product Owner

### Decision

Daily streaks and streak multipliers were offered during planning and explicitly
declined. The Productive Day badge (five tasks in one day) is the only time-window
mechanic that shipped.

### Context

Streaks are the default gamification lever and were the obvious fourth pillar alongside
XP, badges and character stats.

### Rationale

Product owner's call. Streaks punish legitimate breaks and turn a task list into an
obligation, which cuts against the stated purpose of making small chores easier to start.

### Consequences

**Positive:** No streak-anxiety mechanic. Nothing to lose by not opening the app.
**Negative:** Removes the strongest daily-return hook. If retention is ever a goal, this
is the first thing to revisit, and it should be revisited deliberately rather than by
drift.

---

## 2026-08-16: Colour Validated, Not Eyeballed

**ID:** DEC-009
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Tech Lead

### Decision

Difficulty colours were run through a palette validator for colourblind separation in
both themes. The UI chip colours and the chart mark colours are separate token sets
sharing a hue.

### Context

The first palette (sage / steel blue / amber / crimson) failed: the Hard and Epic pair
sat at a normal-vision separation of 13.9, below the threshold at which full-colour
readers can reliably tell two categories apart. Epic was re-hued to violet.

### Rationale

Category colour is load-bearing in this UI, and separation is computable. Chip text on a
dark surface needs to be lighter than a chart fill on the same surface, so one token set
cannot serve both without failing one of them.

### Consequences

**Positive:** Difficulty tiers are distinguishable under deuteranopia and protanopia.
Charts are direct-labelled as well, so identity never rests on colour alone.
**Negative:** Two token sets to keep in sync. Changing a difficulty hue means updating
both and re-running the validator.

---

## 2026-08-16: End-to-End Verification Instead of Unit Tests

**ID:** DEC-010
**Status:** Accepted
**Category:** Process
**Stakeholders:** Tech Lead

### Decision

Phase 0 shipped with two checked-in verification scripts and no unit test project.
`verify-api.ps1` asserts the XP mathematics against a live API; `verify-ui.mjs` drives
the whole user flow through the locally installed Chrome and fails on any console error
or failed request.

### Context

A choice about where to spend limited time in a single build session.

### Rationale

End-to-end coverage caught real defects that unit tests would have missed entirely: a
task rendering in two list sections during the completion transition, and a duplicated XP
rail producing two elements with the same test id and two ARIA progressbars. It also
proved the Docker path and the dev path both actually work.

### Consequences

**Positive:** High confidence that the assembled system works, verified against local,
Vite dev server and container deployments.
**Negative:** Slow feedback, requires a running database and browser, and the pure
progression logic has no fast test to run. `LevelCurve` and `AchievementEvaluator` were
written as pure static functions specifically so this is cheap to fix. Phase 1 fixes it.

---

## 2026-08-17: User Accounts via Auth0

**ID:** DEC-011
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner
**Supersedes:** DEC-007

### Decision

Questward will support multiple user accounts, with Auth0 as the identity provider.
Authentication uses OIDC authorization code plus PKCE in the SPA and JWT bearer validation
in the API. The mission no longer claims "no account" as a value proposition.

### Context

DEC-007 shipped Phase 0 with no auth and a single character row pinned to `Id = 1`, on the
grounds that the instance would only be reachable by its owner. Product direction has
changed: accounts are wanted, and Auth0 is the chosen provider.

### Alternatives Considered

1. **ASP.NET Core Identity with cookie auth** (the original Phase 3 plan)
   - Pros: No external dependency, works offline and on isolated networks, no third-party
     account needed to run the app, nothing to configure before first launch.
   - Cons: Password storage, reset flows, lockout and email delivery all become the
     project's problem. More code to get wrong in the area where getting it wrong is
     most costly.

2. **Self-hosted OIDC provider (Keycloak, Authentik, Zitadel)**
   - Pros: Standard OIDC, so the API-side work is identical to Auth0 and the provider
     could be swapped later. Keeps the deployment self-contained.
   - Cons: A second substantial container to run and maintain, which is a heavy ask
     next to an app that currently fits in one.

3. **Auth0** (chosen)
   - Pros: No credential handling in this codebase. Social logins, MFA and account
     recovery arrive for free. Standard OIDC, so the API-side implementation is provider
     agnostic and portable if the decision is revisited.
   - Cons: See consequences.

### Rationale

Product owner's call. Auth0 removes the highest-risk code from the project entirely: this
codebase never stores a password or implements a reset flow. Because the integration is
standard OIDC, the API-side work is portable, so choosing Auth0 now does not lock the
project out of a self-hosted provider later.

### Consequences

**Positive:**
- No credential storage, password reset or MFA implementation in this codebase.
- The `UserId` seam anticipated in DEC-007 is used as designed: tasks and unlocks gain a
  `UserId`, and the singleton character row becomes one row per user.
- OIDC is standard, so swapping to a self-hosted provider later is a configuration change
  plus a user-mapping migration, not a rewrite.

**Negative:**
- **This is a genuine trade against the self-hosting story.** Sign-in requires outbound
  internet and a reachable Auth0 tenant, so an instance on an isolated or offline network
  cannot log in at all. The mission's data-ownership claim survives, because tasks and XP
  never leave the local database, but any claim about running with no external
  dependencies does not.
- Running the app now requires creating an Auth0 tenant and configuring it first, which
  raises the barrier to first launch well above `docker compose up`.
- Auth0 free-tier limits and pricing become a constraint on the project, set by a third
  party.
- An Auth0 tenant accepts any sign-up by default. A publicly reachable self-hosted
  instance needs an allowlist or invite flow, or strangers can create accounts on it.
- Availability of sign-in is now Auth0's uptime, not the owner's.

### Follow-Up Required

The three open questions on Phase 3 of the roadmap (offline instances, free-tier limits,
and who may sign up) must be answered before this ships, not during.
