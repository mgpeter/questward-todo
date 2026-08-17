# Spec Requirements Document

> Spec: Auth0 User Accounts
> Created: 2026-08-17
> Completed: 2026-08-17
> Status: Implemented

## Implementation Notes

Delivered against a live Auth0 tenant and confirmed working in local Docker by the product
owner. Tenant values live in `.env` and `appsettings.Local.json`, both gitignored.

Verified automatically: 106 tests (`dotnet test`), including twelve isolation tests that
assert two accounts cannot see or touch each other's data through any read or write path.
Verified against the live tenant: real JWT validation rejects unsigned, malformed and
forged tokens, including a forgery carrying the correct issuer and audience; the sign-in
handoff reaches Universal Login with the right client id, audience and PKCE from both the
local API and the container.

Not yet run: the full `scripts/verify-ui.mjs` browser flow, which needs a test user's
credentials and may be blocked by Auth0 bot detection.

Three decisions changed during implementation:

1. **Scoping landed with the schema, not as a separate step.** Removing
   `Character.SingletonId` broke every call site at compile time, so per-user filtering
   had to happen immediately rather than in a later pass.
2. **`CompleteAsync` has five scoped reads, not the six this spec predicted.**
   `OpenTasksBefore` was removed entirely: see below.
3. **Clean Slate's rule changed.** The original
   `OpenTasksAfter == 0 && OpenTasksBefore >= 3` was unreachable, because tasks complete
   one at a time and reaching zero open implies exactly one was open beforehand. It is now
   `OpenTasksAfter == 0 && CompletedTodayLocal >= 3`, with the badge copy updated to match.

Two pre-existing defects were found by the new tests and fixed here, neither caused by
this work:

- `GET /api/tasks?difficulty=epic` returned 400. Minimal API enum binding is
  case-sensitive and the SPA sends lowercase, so **every difficulty filter chip in the UI
  was broken**. Now parsed case-insensitively.
- Clean Slate could never be earned, as above.

One latent issue in the test harness was also fixed: `Program.cs` read the connection
string into a local before `builder.Build()`, so integration tests silently ran against
the developer's own database instead of the container. It is now resolved from DI.

## Overview

Add Auth0-backed user accounts so several people can share one Questward instance, each
with their own tasks, XP, character and badges. This replaces the current single implicit
profile, closing the gap between the app and its stated mission of serving households and
small teams.

## User Stories

### Signing in to my own list

As a person sharing a household Questward instance, I want to sign in with my own account,
so that my tasks and my character are mine and not mixed in with everyone else's.

I open the instance and see a sign-in screen rather than someone else's task list. I
authenticate through Auth0, using email and password or a social provider, and land on an
empty board with a fresh level 1 character. Nothing I create is visible to the other
people on the instance, and nothing they create appears on my board. My session persists
across reloads and browser restarts until the refresh token expires.

### Keeping my progress separate

As a user of a shared instance, I want my XP, level and badges tracked against my account
alone, so that the progression system still means something.

When I complete a task, XP is awarded to my character. Another user completing a task on
the same instance at the same time does not move my bar, unlock my badges or appear in my
fourteen-day activity chart. The anti-inflation guarantees from Phase 0 hold per user:
completion stays idempotent, reopening refunds only my XP, and my badges unlock once.

### Signing out on a shared machine

As someone using a family computer, I want to sign out, so that the next person at the
keyboard sees their own list rather than mine.

An account menu in the header shows who I am and offers sign out. Signing out clears the
local session and returns me to the sign-in screen. The next sign-in is a fresh
authentication, not a silent resumption of my session.

## Spec Scope

1. **Auth0 integration** - Configure an Auth0 tenant, SPA application and API audience,
   with the SPA reading its Auth0 settings at runtime rather than at build time so one
   Docker image works against any tenant.
2. **API authentication** - Validate JWT bearer tokens against the Auth0 issuer, put every
   `/api` route except `/health` and `/api/config` behind authorization, and resolve the
   `sub` claim to a local user record.
3. **Local user records** - A `users` table keyed by an internal id with the Auth0 `sub`
   stored as a unique mapping, provisioned just in time on a user's first authenticated
   request.
4. **Per-user data scoping** - Add `UserId` to tasks, characters and achievement unlocks,
   scope every query and mutation to the calling user, and make the character row one per
   user rather than a pinned singleton.
5. **Sign-in and account UI** - A sign-in gate, an account menu showing the current user
   with a sign-out action, and token attachment plus expiry handling in the API client.

## Out of Scope

- **Shared leaderboard** - Deferred to its own spec. It raises privacy questions that
  would slow this work down.
- **Sign-up restriction** - Sign-up is deliberately open; anyone who can reach the
  instance and authenticate with Auth0 gets an account. See Security Posture below.
- **Data migration** - Existing tasks, characters and unlocks are dropped. The project has
  never been released, so there is no deployed data to preserve.
- **Account management** - No profile editing beyond the existing character name and
  avatar, no account deletion, no password change in-app. All of that lives in Auth0.
- **Roles or permissions** - Every account is an ordinary user. No admin, no instance
  owner, no sharing tasks between accounts.
- **Self-hosted OIDC provider** - Auth0 only. The user-resolution layer stays
  provider-agnostic per DEC-011, but no second provider is implemented or tested.
- **Offline operation** - Sign-in requires outbound internet and a reachable Auth0 tenant.
  An isolated instance cannot be used at all. This is a documented constraint, not a bug.

## Security Posture

Two decisions here widen the instance's exposure and should be made knowingly:

**Sign-up is open.** Any person who can reach the instance URL can authenticate through
Auth0 and receive an account with storage on the host. On an internet-reachable instance
that means strangers can create accounts. This is acceptable for a private-network
deployment and is not acceptable for a public one. The README must carry this warning
prominently, and per-endpoint rate limiting is included in scope as a partial mitigation.

**Internet is required.** The instance is unusable when Auth0 is unreachable, including
for users already signed in once their token expires. Availability of sign-in is Auth0's
uptime, not the operator's.

## Expected Deliverable

1. Two different Auth0 accounts signing in to the same running instance each see their own
   empty board, and tasks created by one are never visible to the other, verified in a
   browser.
2. An unauthenticated request to any `/api` route except `/health` and `/api/config`
   returns 401, and a request bearing a token issued for a different audience is rejected.
3. The full Phase 0 gamification flow still works per user: completing a task awards the
   correct XP to the signed-in user's character only, level-up fires, badges unlock once,
   and reopening refunds exactly what was granted.

## Cross-References

- Technical specification: `sub-specs/technical-spec.md`
- Database schema: `sub-specs/database-schema.md`
- API specification: `sub-specs/api-spec.md`
- Product decision: DEC-011 in `docs/product/decisions.md`
- Roadmap: Phase 3 in `docs/product/roadmap.md`
