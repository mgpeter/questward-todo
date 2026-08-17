# Technical Specification

This is the technical specification for the spec detailed in
@docs/specs/2026-08-17-auth0-user-accounts/spec.md

## Technical Requirements

### Auth0 tenant configuration

- One Auth0 tenant with two entities: a **Single Page Application** for the SPA and an
  **API** whose identifier becomes the token audience.
- The API identifier is an opaque URI, for example `https://questward.api`. It does not
  need to resolve.
- Allowed Callback URLs, Logout URLs and Web Origins must cover all three development
  origins plus the deployment origin: `http://localhost:5173`, `http://localhost:5080`,
  `http://localhost:8080`, and whatever host the instance is published on.
- Refresh Token Rotation enabled, so the SPA can hold a session across reloads without a
  long-lived token.
- Token expiry left at Auth0 defaults; the SPA renews silently.

### Runtime configuration, not build-time

Vite inlines `import.meta.env.VITE_*` at build time, and the SPA is built inside the
Docker image. Baking Auth0 settings in would produce an image tied to one tenant, which
contradicts the roadmap's requirement that settings arrive as environment variables.

- The API exposes an unauthenticated `GET /api/config` returning the tenant domain, SPA
  client id and audience.
- The SPA fetches `/api/config` before rendering and passes the result into
  `Auth0Provider`. A loading state covers the fetch; a failure renders an explicit
  "cannot reach the server" screen rather than a blank page.
- Only public Auth0 values are served from this endpoint. The client id is public by
  design in a PKCE flow. No client secret exists in this architecture and none may be
  added to the SPA or to `/api/config`.

### API authentication

- Add `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.11, matching the existing
  10.0.11 pins in `TodoApp.Data`.
- Configure JWT bearer in `Program.cs` with `Authority = https://{domain}/` and
  `Audience` from configuration. Signing keys are fetched from the tenant's JWKS endpoint
  and cached by the middleware.
- Token validation must check issuer, audience, lifetime and signature. None of these may
  be disabled, including in Development.
- `app.UseAuthentication()` and `app.UseAuthorization()` are added before endpoint
  mapping. Route groups in `Endpoints/` gain `.RequireAuthorization()`.
- `/health` and `/api/config` stay anonymous. The existing catch-all
  `app.MapMethods("/api/{**rest}", ...)` returning 404 must stay anonymous too, or an
  unauthenticated request to an unknown route leaks the difference between "no such route"
  and "not signed in" by returning 401 instead of 404. Decide deliberately: this spec
  keeps it anonymous so unknown routes 404 consistently.

### Current-user resolution

- A scoped `ICurrentUser` service resolves the authenticated principal to a local `User`
  record, provisioned just in time on first authenticated request.
- Keyed on the `sub` claim. Email and name are read from the token when present and
  refreshed on each sign-in, but `sub` is the only identifier treated as stable.
- Provisioning creates the `User` row and its `Character` row in a single transaction, so
  a user can never exist without a character.
- Concurrent first requests must not create two users for one `sub`. The unique index on
  `Auth0Sub` is the guard; the provisioning path catches the unique violation and re-reads
  rather than relying on a check-then-insert race.
- `ICurrentUser` is the only place the Auth0 `sub` is read. Endpoints and services see an
  internal `UserId` only, keeping the rest of the codebase provider-agnostic per DEC-011.

### Per-user scoping

- Every query in `TaskEndpoints`, `CharacterEndpoints`, `AchievementEndpoints`,
  `StatsEndpoints` and `GamificationService` filters by `UserId`.
- `GamificationService.GetCharacterAsync` stops looking for `Character.SingletonId` and
  takes the current user's id.
- `AchievementEvaluator` counts (tasks completed, hard-or-epic completed, open tasks
  before and after, completed today) must all be scoped, or one user's activity unlocks
  another's badges.
- A task fetched by id that belongs to another user returns **404, not 403**, so ids
  cannot be probed for existence.
- Scoping is enforced in the query, not by post-filtering a global result set.

### Frontend

- Add `@auth0/auth0-react` 2.24.0.
- `Auth0Provider` configured with `authorizationParams.audience` set to the API
  identifier, otherwise Auth0 issues an opaque token that the API cannot validate. Use
  `cacheLocation: 'localstorage'` and `useRefreshTokens: true` so sessions survive
  reloads.
- `web/src/lib/api.ts` gains a token provider. The module currently exports a plain
  `api` object with no React dependency; keep that shape by registering a token getter
  once at startup rather than turning the client into a hook.
- Every request attaches `Authorization: Bearer <token>` obtained via
  `getAccessTokenSilently`. A 401 response triggers one silent renewal attempt and, if
  that fails, a redirect to sign-in.
- An `<AuthGate>` renders the sign-in screen when unauthenticated and the app when
  authenticated, replacing the current unconditional render in `App.tsx`.
- The account menu sits in `Header.tsx` beside the theme toggle, showing the Auth0
  `picture` and `name` with a sign-out action. Sign-out clears the TanStack Query cache so
  the next user never sees cached data from the previous session.
- The existing theme preference stays in `localStorage` and is deliberately not per-user;
  it is a device preference.

### Rate limiting

- Add ASP.NET Core rate limiting on the authenticated `/api` group, partitioned by
  `UserId`, as a partial mitigation for open sign-up. A fixed window is sufficient.
- This does not stop account creation. It bounds what one account can do to the host.

### Testing

- Tests must never call the real Auth0 tenant. Integration tests replace the JWT bearer
  scheme with a test authentication handler that mints a principal with a chosen `sub`,
  so multi-user isolation can be exercised by switching `sub` between requests.
- The isolation tests are the point of this spec: create data as user A, assert user B
  sees none of it through every read path (tasks list, task by id, character,
  achievements, stats).
- Token validation itself is covered by asserting that a request with no token, an
  expired token and a wrong-audience token are all rejected.
- `scripts/verify-ui.mjs` needs rework. It currently drives an unauthenticated app and
  wipes state via the API. It must sign in first, which means either a test Auth0 user
  with a known password driven through the login form, or a bypass flag. Prefer a real
  test user so the verified path is the real one.

### Configuration surface

| Setting | Where | Example |
|---|---|---|
| `Auth0__Domain` | API env | `questward.eu.auth0.com` |
| `Auth0__Audience` | API env | `https://questward.api` |
| `Auth0__SpaClientId` | API env, served via `/api/config` | `abc123...` |
| `AUTH0_DOMAIN` | compose `.env` | passed through to the app service |
| `AUTH0_AUDIENCE` | compose `.env` | passed through to the app service |
| `AUTH0_SPA_CLIENT_ID` | compose `.env` | passed through to the app service |

`.env.example` gains all three with placeholder values and a comment pointing at the Auth0
dashboard. The app must fail fast with a clear message at startup if any are missing,
rather than starting and rejecting every request with an opaque 401.

## External Dependencies

- **Microsoft.AspNetCore.Authentication.JwtBearer** 10.0.11 - JWT validation against the
  Auth0 JWKS endpoint.
  - **Justification:** The first-party ASP.NET Core way to validate bearer tokens. Version
    matches the existing 10.0.11 pins, avoiding the assembly version conflict already
    documented in `TodoApp.Data.csproj`.

- **@auth0/auth0-react** 2.24.0 - Auth0 SPA SDK with React bindings.
  - **Justification:** Handles authorization code plus PKCE, token caching, silent renewal
    and refresh token rotation. Writing this by hand is exactly the kind of security code
    DEC-011 chose Auth0 to avoid.

- **Testcontainers.PostgreSql** 4.14.0 (test project only) - real Postgres for integration
  tests.
  - **Justification:** Per-user scoping is enforced in SQL. An in-memory provider would
    not exercise the queries or the unique constraints that make the isolation correct.
