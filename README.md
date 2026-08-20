# Questward

A self-hosted todo list that pays you in experience points. Finish a task, earn XP scaled
to how hard it was, level up a character, and collect badges along the way.

- **Backend** - .NET 10 Minimal API, EF Core 10, PostgreSQL
- **Frontend** - React 19 + TypeScript + Vite, Tailwind CSS v4, TanStack Query, Motion
- **Deployment** - a YARP gateway serving the SPA and proxying the API on a single
  published port, in front of the API and Postgres
- **Development** - one command: a .NET Aspire AppHost runs Postgres, the API, the gateway
  and the Vite dev server together, with a dashboard for logs, traces and health
- **Users** - Auth0-backed accounts. Several people can share one instance, each with
  their own tasks, XP, badges and character.
- **Adventure** - classes, ability scores, d20 combat, loot, a shop and quests, all fuelled
  by finishing real tasks.

---

> [!WARNING]
> **Sign-up is open.** Anyone who can reach this instance can authenticate through your
> Auth0 tenant and get an account with storage on your host. That is fine on a private
> network and **not** fine on a publicly reachable one. Restrict access at the network
> level, or add an allowlist, before exposing it.

## Set up Auth0 first

The app will not start without an Auth0 tenant. Sign-in also needs outbound internet, so
an isolated or offline instance cannot be used at all. Both are deliberate; see DEC-011.

1. In the Auth0 dashboard create an **API**. Its Identifier becomes your audience, for
   example `https://questward.api`. It does not need to resolve.
2. Create an **Application** of type **Single Page Application**.
3. On that application set **Allowed Callback URLs**, **Allowed Logout URLs** and
   **Allowed Web Origins** to the origins you will use:
   `http://localhost:5173, http://localhost:5080, http://localhost:8080`
4. Copy three values into `.env` (see `.env.example`): the tenant **Domain**, the SPA
   **Client ID**, and the API **Identifier**.

None of the three is secret. A PKCE flow has no client secret, and none may be added.

## Run it with Docker

```bash
cp .env.example .env        # then fill in the three AUTH0_* values
docker compose up -d --build
```

Open <http://localhost:8080>. Postgres data lives in the `questward-data` volume;
migrations are applied automatically on startup.

Three services, still one published port: `gateway` owns the origin and serves the SPA,
`api` answers `/api` and is reachable only from the gateway, `db` is Postgres.

```bash
docker compose logs -f gateway  # follow the front door
docker compose logs -f api      # follow the API
docker compose down             # stop, keeping data
docker compose down -v          # stop and delete the database
```

## Run it for development

One command. The Aspire AppHost starts Postgres in a container, the API, the gateway and
the Vite dev server, and reads the Auth0 settings straight out of the `.env` you already
made for Docker:

```bash
dotnet run --project src/TodoApp.AppHost
```

Open <http://localhost:5080>. That is the gateway, and it is the same shape the container
ships: the SPA and `/api` on one origin. Hot reload works through it - edit a `.tsx` and
the browser updates. The Aspire dashboard is at <http://localhost:15080> (the console
prints a login link) and has logs, traces and health for every resource in one place.

Postgres runs on the `questward-aspire-data` volume and survives restarts. It is
deliberately not the same volume `docker-compose.dev.yml` uses, so the two never fight over
one cluster.

<details>
<summary>Without Aspire</summary>

Three terminals, and still supported:

```bash
# 1. Postgres only
docker compose -f docker-compose.dev.yml up -d

# 2. API on http://localhost:5081  (OpenAPI UI at /scalar/v1)
#    Needs the Auth0 settings. Put them in src/TodoApp.Api/appsettings.Local.json
#    (gitignored) or export them:
#      Auth0__Domain, Auth0__Audience, Auth0__SpaClientId
dotnet run --project src/TodoApp.Api

# 3. SPA on http://localhost:5173 with hot reload, /api proxied to 5081
cd web && npm install && npm run dev
```

To try the production shape without Docker, run `npm run build` in `web/` - Vite writes the
bundle into `src/TodoApp.Gateway/wwwroot` - then run the gateway and the API together and
open <http://localhost:5080>.

</details>

---

## Debugging

### VS Code

`.vscode/launch.json` and `.vscode/tasks.json` are checked in. Pick a configuration from
the Run panel:

| Configuration | What it does |
| --- | --- |
| **Aspire AppHost** | The one to press F5 on. Starts Postgres, the API, the gateway and Vite together, and opens the dashboard. |
| **Full stack (API + Gateway + Vite + Chrome)** | The pre-Aspire loop, still here. Starts Postgres, launches the API under the debugger on 5081 and the gateway on 5080, starts Vite, opens Chrome. Breakpoints work in both C# and `.tsx`. |
| **API (.NET)** | API only, on 5081. Opens `/scalar/v1` when it is ready. |
| **Gateway (.NET)** | Gateway only, on 5080. Useful for debugging routing without the AppHost. |
| **SPA (Chrome)** | Chrome attached to an already-running Vite on 5173. |
| **SPA (Chrome, through the gateway)** | Chrome against <http://localhost:5080>, the single origin - use this to debug something that only reproduces in the shape that ships. |
| **Attach to a running .NET process** | Pick an existing `TodoApp.Api` process. |

Requires the C# extension (`ms-dotnettools.csharp`); the Chrome configs use the debugger
built into VS Code.

### Visual Studio / Rider

Open `TodoApp.slnx`, set `TodoApp.AppHost` as the startup project, F5: it starts everything
and opens the dashboard. To debug the API alone, set `TodoApp.Api` instead - it uses the
`http` profile in `launchSettings.json` (port 5081, Development) and needs the gateway and
Vite started separately.

### Where to put a breakpoint

| Symptom | File |
| --- | --- |
| Wrong XP or level after completing | `src/TodoApp.Api/Services/GamificationService.cs` - `CompleteAsync` |
| A badge fired when it should not have | `src/TodoApp.Api/Services/AchievementEvaluator.cs` - `Evaluate` |
| Level thresholds feel wrong | `src/TodoApp.Models/Progression/LevelCurve.cs` |
| Request rejected with a validation error | `src/TodoApp.Api/Validation/ValidationFilter.cs` |
| XP bar or level-up animation misbehaving | `web/src/game/GameFeed.tsx` - `celebrateCompletion` |
| Stale data on screen | `web/src/lib/queries.ts` - the `invalidateProgression` calls |

### Seeing the traffic

- **API surface**: <http://localhost:5080/scalar/v1> (through the gateway) or
  <http://localhost:5081/scalar/v1> - browsable, and every endpoint is
  callable from the page. Raw document at `/openapi/v1.json`. Development only.
- **SQL**: set `Microsoft.EntityFrameworkCore.Database.Command` to `Information` in
  `src/TodoApp.Api/appsettings.Development.json` to log every statement EF Core runs.
- **The database**:

  ```bash
  docker exec -it questward-dev-db-1 psql -U questward -d questward
  \dt                       -- tables
  select * from character;  -- your XP total
  select "Title", "Difficulty", "XpAwarded", "CompletedAt" from tasks;
  ```

- **The container**: `docker compose logs -f app`, or `docker compose exec app sh`.

### Resetting

There is no reset button in the UI, by design. To wipe progress on the dev database:

```bash
docker exec questward-dev-db-1 psql -U questward -d questward -c \
  'TRUNCATE tasks; TRUNCATE achievement_unlocks; UPDATE character SET "TotalXp"=0, "TasksCompleted"=0;'
```

For the Docker stack, `docker compose down -v` deletes the volume and starts clean.

### Reproducing a bug quickly

`node scripts/verify-ui.mjs --url http://localhost:5173 --headed` drives the whole flow in
a visible Chrome window in about thirty seconds. Adding a `page.pause()` in the script
opens Playwright Inspector so you can step through it.

---

## How the game works

**XP per task, by difficulty:**

| Difficulty | XP |
| --- | --- |
| Easy | 10 |
| Medium | 25 |
| Hard | 50 |
| Epic | 100 |

Priority affects sort order only. It deliberately grants no XP, so it stays an
organisational tool rather than a way to farm levels.

**Levels.** Cumulative XP to reach level *L* is `25 x L x (L - 1)`: level 2 at 50 XP,
3 at 150, 4 at 300, 10 at 2250. Two Medium tasks earn the first level up. The level is
never stored - it is derived from the XP total by `LevelCurve` on every read, so the two
can never disagree.

**Ranks.** Novice, Apprentice (3), Adept (5), Journeyman (8), Expert (12), Master (17),
Champion (23), Legend (30).

**Badges.** Thirteen, evaluated after every completion - first task, 10 tasks, 100 tasks,
first Epic, ten Hard-or-Epic, a single 50 XP task, levels 5/10/25, clearing a full board,
finishing something between midnight and 4am, before 6am, and five tasks in one day.
Time-of-day badges use the browser's UTC offset, which the client sends with each
completion, so they follow your day rather than the server's.

**XP is snapshotted at completion.** Editing a task's difficulty afterwards never
rewrites XP that was already banked. Reopening a task refunds exactly what it granted,
clamped so the total can never go negative - but badges are never revoked. Deleting a
completed task also leaves its XP banked: the work still happened.

---

## The adventure layer

Behind the **Adventure** tab there is a small D&D-flavoured RPG: pick one of six classes,
carry the six ability scores, fight monsters on a d20 system, collect equipment and
complete quests.

**It grants no experience, ever.** Monsters and quests pay gold and loot; your level stays
a pure function of completed tasks. Every fight costs **Stamina**, and the only thing that
produces Stamina is finishing a real task:

| Difficulty | XP | Stamina |
|---|---|---|
| Easy | 10 | 1 |
| Medium | 25 | 2 |
| Hard | 50 | 3 |
| Epic | 100 | 5 |

Your level is a record of work done; your gear is what you did with it. The game is a sink
for productivity rather than a substitute for it, and there is deliberately **no endpoint
capable of moving XP** outside task completion. See DEC-012.

**Classes.** Each has a passive perk and an active ability usable twice per fight, so a
Wizard and a Fighter do not play the same way:

| Class | Perk | Ability |
|---|---|---|
| Fighter | Second Wind: heals on a win | Power Attack: -2 to hit, damage dice doubled |
| Rogue | Crits on a natural 19 | Sneak Strike: attack with advantage |
| Wizard | Some fights cost no stamina | Magic Missile: no attack roll, always hits |
| Cleric | Rerolls its first natural 1 | Healing Word: heal, forfeiting the swing |
| Ranger | Loot rarity rolled with advantage | Aimed Shot: advantage, crits on 19 |
| Bard | Gold rewards increased by half | Vicious Mockery: its answering swing goes wide |

**Healing.** One hit point returns every 8 minutes, and completing tasks restores more.
The character sheet shows when the next point lands and when you will be whole. If you are
in a hurry, sleep at the tavern: a full heal for gold, priced by how hurt you are and what
level you are.

**The Market.** Six offers, rotating daily. Stock is *computed* from your id and the date
rather than stored, so there is no stock table and no nightly job. It never carries Epic or
Legendary: the best gear still has to be won, or reforged into by paying to raise an item
one rarity at a time. Buying costs full price against a half-price sell, and that spread is
where gold goes.

**The Chronicle.** Every finished fight is kept with its full roll-by-roll log, so you can
reread exactly how something went.

**The d20 core.** `d20 + modifier vs target`. A natural 20 always hits and doubles the
damage dice; a natural 1 always misses. Every roll comes back fully itemised - the dice,
each labelled modifier, the total and the target - so a miss reads as bad luck rather than
an unexplained verdict. Dice go through an injectable roller, which is what makes the
combat rules exhaustively testable.

**Derived, never stored.** Armour class, attack bonus, damage and maximum hit points are
all recomputed from class, level and equipment on every read, for the same reason level is
derived from XP: two copies of a derived value eventually disagree.

---

## Layout

```
src/TodoApp.Models/          entities, enums, LevelCurve, RankTitles, AchievementCatalog
src/TodoApp.Data/            TodoDbContext, IEntityTypeConfiguration classes, migrations
src/TodoApp.Api/             Minimal API endpoints, gamification services
src/TodoApp.Gateway/         YARP front door: serves the SPA, proxies /api
src/TodoApp.ServiceDefaults/ OpenTelemetry, service discovery, /alive - shared by both
src/TodoApp.AppHost/         Aspire orchestration for development
web/                         React SPA
scripts/                     verification scripts
```

The achievement catalog lives in code rather than the database, so adding a badge never
needs a migration - only the unlock rows are persisted.

## API

| Method | Route | |
| --- | --- | --- |
| GET | `/api/tasks?status=&difficulty=&search=` | list |
| POST | `/api/tasks` | create |
| PUT | `/api/tasks/{id}` | update |
| DELETE | `/api/tasks/{id}` | delete |
| POST | `/api/tasks/{id}/complete` | award XP, evaluate badges |
| POST | `/api/tasks/{id}/reopen` | refund XP |
| POST | `/api/tasks/reorder` | persist manual order |
| GET/PUT | `/api/character` | read / rename + change avatar |
| GET | `/api/achievements` | catalog joined with unlock state |
| GET | `/api/stats?utcOffsetMinutes=` | totals, difficulty breakdown, 14-day trend |
| GET | `/health` | liveness |

`POST /api/tasks/{id}/complete` returns the updated task, the XP gained, the new character
state, whether a level was crossed, and any badges unlocked - everything the UI needs to
animate in one round trip, so nothing flickers waiting for a refetch. It is idempotent:
completing an already-complete task awards nothing.

In Development the OpenAPI document is at `/openapi/v1.json` with a browsable UI at
`/scalar/v1`.

---

## Verifying it

```bash
dotnet test
```

870 tests: unit tests over the progression and combat rules, plus integration tests that
boot the real API against a throwaway Postgres container. Tests never call Auth0; a test
authentication handler mints principals locally.

Three groups carry most of the weight:

- `tests/TodoApp.Tests/Isolation` - two accounts on one instance cannot see or touch each
  other's tasks, XP or badges through any path.
- `Fighting_a_monster_to_death_never_moves_experience_or_level` and
  `No_adventure_route_can_move_experience` - the guarantee the whole RPG design rests on,
  asserted at both the service layer and through HTTP.
- `tests/TodoApp.Tests/Gateway` - the routing contract, read from the gateway's own
  `appsettings.json` rather than a copy of it, plus the invariant that the API container
  publishes no host port (DEC-016 explains why that one is load-bearing).

The end-to-end scripts are destructive to task data, so run them against a development
database. Both now need a token, since every `/api` route requires one. Their default base
URL is <http://localhost:5080>, which is the gateway under the AppHost - unchanged, because
the gateway took over the port the API used to answer on. Use 8080 for the container.

```bash
# API: XP maths, level thresholds, idempotency, refunds, filters, 404s
pwsh ./scripts/verify-api.ps1 -BaseUrl http://localhost:5080 -AccessToken "<jwt>"

# UI: drives your real installed Chrome through the whole flow and screenshots
# light, dark, mobile and the level-up moment into artifacts/
npm install
node scripts/verify-ui.mjs --url http://localhost:5080 --username you@example.com --password '<pw>'
node scripts/verify-ui.mjs --url http://localhost:5080 --headed   # watch it
```

The UI script fails on any console error or failed request, not just on a missing
element, and it asserts the browser and the API agree about XP after every mutation.

Grab a token for the API script from the browser devtools console while signed in, or
from Auth0's API test tab.

## Notes

- The single character row is pinned to `Id = 1` by a check constraint. Adding the planned
  Auth0-backed accounts means adding a `UserId` to tasks and unlocks and unpinning that
  row; nothing else about the schema has to change.
- Until accounts land, anyone who can reach the port is the user. Do not expose an
  instance to an untrusted network.
- The app applies migrations on startup with a bounded retry loop, because in Compose the
  app container regularly wins the race against Postgres even with a healthcheck in front.
- Theme is stored in `localStorage` under `questward.theme` and applied by an inline
  script before first paint, so a dark-theme reload never flashes light.

## License

MIT. See [LICENSE](LICENSE).
