# API Specification

This is the API specification for the spec detailed in
@docs/specs/2026-08-17-auth0-user-accounts/spec.md

## Authorization Model

| Route | Auth |
|---|---|
| `GET /health` | Anonymous |
| `GET /api/config` | Anonymous (new) |
| `GET /api/me` | Required (new) |
| `/api/tasks/*` | Required |
| `/api/character` | Required |
| `/api/achievements` | Required |
| `/api/stats` | Required |
| `/api/{**rest}` catch-all | Anonymous, still returns 404 |

The catch-all stays anonymous deliberately. If it required authorization, an
unauthenticated request to a route that does not exist would return 401 instead of 404,
telling an anonymous caller the difference between a real endpoint and a typo.

Every authenticated route resolves the token's `sub` claim to a local user via
`ICurrentUser` and scopes its queries to that user's id. A resource belonging to another
user returns **404, not 403**, so ids cannot be probed for existence.

## New Endpoints

### GET /api/config

**Purpose:** Give the SPA its Auth0 settings at runtime, so one Docker image works
against any tenant. Vite would otherwise inline these at build time.
**Auth:** Anonymous. It must be, since the SPA needs it before it can authenticate.
**Parameters:** None
**Response:** `200 OK`

```json
{
  "auth0Domain": "questward.eu.auth0.com",
  "auth0ClientId": "K9x...public...",
  "auth0Audience": "https://questward.api"
}
```

**Errors:** None by design. If configuration is missing the app fails at startup rather
than serving an incomplete document, so this endpoint either works or the app is not
running.

**Note:** Only values that are public in a PKCE flow appear here. The SPA client id is
public by design. No client secret exists in this architecture and none may be added.

### GET /api/me

**Purpose:** Identify the signed-in user for the account menu, and force just-in-time
provisioning on first sign-in.
**Auth:** Required
**Parameters:** None
**Response:** `200 OK`

```json
{
  "id": "0198f2c1-...",
  "email": "someone@example.com",
  "displayName": "Someone",
  "createdAt": "2026-08-17T09:00:00+00:00"
}
```

**Errors:**
- `401` no token, expired token, wrong issuer or wrong audience

## Changed Endpoints

No route paths, request bodies or response shapes change. Every endpoint below gains
authentication and per-user scoping. The SPA's existing types in `web/src/lib/api.ts`
stay valid apart from the token header.

### GET /api/tasks

Returns only the caller's tasks. The `status`, `difficulty` and `search` filters are
unchanged and apply within the caller's tasks. The `UserId` predicate is applied in the
query, not by filtering results afterwards.

### POST /api/tasks

Stamps the new task with the caller's `UserId`. The "newest task sorts to the top"
behaviour, which reads `MIN(SortOrder)`, must be scoped to the caller, or one user's
sort order drags another's new tasks around.

### PUT /api/tasks/{id}, DELETE /api/tasks/{id}, GET /api/tasks/{id}

Scoped by `UserId` in the `WHERE` clause. A task owned by someone else is
indistinguishable from a task that does not exist.

**Errors:** `401` unauthenticated, `404` not found or not owned

### POST /api/tasks/{id}/complete

The most sensitive change. `GamificationService.CompleteAsync` currently reads the
singleton character and counts tasks globally. Under multi-user, every one of those reads
must be scoped:

- the task lookup, by `UserId`
- the character, by `UserId` instead of `Character.SingletonId`
- `openTasksBefore` and `openTasksAfter`, by `UserId`
- `HardOrEpicCompleted`, by `UserId`
- `CompletedTodayLocal`, by `UserId`
- the existing-unlock check and the new unlock rows, by `UserId`

Missing any one of these means another user's activity silently unlocks the caller's
badges. The Clean Slate badge is the clearest example: unscoped, it fires when the
instance is empty rather than when the caller's board is.

Response shape is unchanged, including `unlockedAchievements`, so the SPA's completion
animation needs no change.

**Errors:** `401` unauthenticated, `404` not found or not owned

### POST /api/tasks/{id}/reopen

Scoped identically. The XP refund applies to the caller's character.

### POST /api/tasks/reorder

Only reorders ids owned by the caller. Ids belonging to another user are skipped silently,
matching the endpoint's existing behaviour for unknown ids, rather than failing the whole
batch.

### GET /api/character, PUT /api/character

Reads and writes the caller's character, created during provisioning. `Character.
SingletonId` no longer exists.

### GET /api/achievements

Returns the full catalog joined with the caller's unlocks. The catalog itself stays global
and code-held, per DEC-004; only the unlock state is per user.

### GET /api/stats

All counts, the difficulty breakdown and the fourteen-day trend are scoped to the caller.
The `utcOffsetMinutes` parameter is unchanged.

## Rate Limiting

Fixed-window limiting on the authenticated `/api` group, partitioned by `UserId`. This is
a partial mitigation for open sign-up: it bounds what one account can do to the host, but
does nothing to prevent account creation.

`/api/config` is anonymous and therefore partitioned by IP instead, or it becomes a free
amplification target.

## Error Contract

`ProblemDetails` is already registered and stays the error format.

| Status | When |
|---|---|
| `401` | Missing, expired, malformed, wrong-issuer or wrong-audience token |
| `404` | Resource does not exist, or exists but belongs to another user |
| `429` | Rate limit exceeded |

`403` is deliberately unused. With no roles and no sharing, every authorization failure is
either "you are not signed in" or "that is not yours", and the second is expressed as 404.

## OpenAPI

The existing `AddOpenApi` and Scalar UI at `/scalar/v1` must be updated to describe the
bearer scheme, so the API reference stays usable in Development. Without it, every
endpoint in Scalar returns 401 and the page becomes useless for exploration.
