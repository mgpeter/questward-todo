# Product Mission

## Pitch

Questward is a self-hosted todo app that helps people who struggle to start small
chores actually finish them, by paying out experience points scaled to how hard each
task was and levelling up a character as the list gets cleared.

## Users

### Primary Customers

- **Self-hosters**: People who already run a home server and prefer their task list to
  live on hardware they control rather than in someone else's SaaS.
- **Households and small teams**: Several people sharing one instance, each with their
  own account, tasks and character.
- **Developers evaluating the stack**: People looking for a small, complete, honest
  example of .NET 10 Minimal API plus React 19 and Tailwind v4 wired together and
  actually verified.

### User Personas

**The Self-Hoster** (25-45 years old)

- **Role:** Software or ops professional running a personal home server
- **Context:** Already runs a handful of containers behind Docker Compose. Adds a new
  service when it earns its slot: one container, one port, a volume, no fuss.
- **Pain Points:** Hosted todo apps hold their data and change terms; most self-hosted
  alternatives are heavyweight project managers rather than a personal list.
- **Goals:** A list that starts in one `docker compose up`, backs up as a single
  Postgres volume, and gives them a reason to come back to it daily.

**The Stack Evaluator** (22-50 years old)

- **Role:** Backend or full-stack developer
- **Context:** Wants to see how .NET 10 Minimal API, EF Core 10 and a modern React
  frontend fit together in a real app rather than a tutorial fragment.
- **Pain Points:** Sample projects are either trivial to the point of uselessness or
  buried under abstraction; almost none of them show the verification story.
- **Goals:** Read a codebase small enough to hold in their head, see the reasoning
  written down, and lift the patterns that are worth lifting.

## The Problem

### Small tasks do not feel worth starting

The chores that clog a todo list are individually trivial, which is exactly why they
get skipped. A flat checkbox list gives identical feedback for repotting a plant and
for shipping a release, so there is no felt difference between clearing something easy
and clearing something that mattered.

**Our Solution:** Tasks carry a difficulty, difficulty determines XP, and XP visibly
moves a progress bar towards the next level, so the reward is proportional to the work.

### Gamified todo apps usually own your data

The task apps that do gamify well are hosted services. Progress, history and habits
live on someone else's infrastructure, subject to their pricing and their shutdown
notices.

**Our Solution:** Your tasks, XP and history live in a Postgres volume you control, on
hardware you run. No product telemetry, and fonts are bundled rather than fetched from a
CDN, so the app itself phones nobody.

### Points systems are easy to cheat, which makes them meaningless

If a scoring system can be gamed, it stops functioning as feedback. Editing a task's
difficulty after finishing it, or reopening and re-completing it, should not print
free levels.

**Our Solution:** XP is snapshotted onto the task at completion, completion is
idempotent, and reopening refunds exactly what was granted. The score can only be
moved by doing the work.

## Differentiators

### Your data stays in your database

Unlike Habitica and Todoist, where your tasks and progress live on the vendor's
infrastructure, Questward keeps every task, XP total and badge in a Postgres volume on
your own hardware. Accounts identify who you are; they are not where your data lives.
The result is a task list whose availability and retention are yours to decide.

### A progression system that resists inflation

Unlike most gamified trackers, where points are awarded on the honour system and can
be farmed by editing history, Questward derives level from the XP total rather than
storing it, snapshots XP at the moment of completion, and makes completion idempotent.
Progress reflects work done, which is the only thing that makes the number worth
looking at.

The stance survives contact with the RPG layer, which is where it would normally break.
Monsters and quests pay gold and loot but never experience, and there is no endpoint in
the entire API capable of moving XP outside task completion. A game bolted onto a todo app
usually ends up replacing it; here it is deliberately built as a sink for the productivity
rather than a second source of it.

### Small enough to read end to end

Unlike the self-hosted project managers it sits alongside, Questward is roughly ninety
source files across three .NET projects and one React app, with the reasoning behind
each significant decision written down in `docs/product/decisions.md`. Anyone can audit
what their task list actually does.

## Key Features

### Core Features

- **Difficulty-scaled XP:** Easy, Medium, Hard and Epic tasks pay 10, 25, 50 and 100 XP,
  so finishing something demanding feels different from ticking off a chore.
- **Character levelling:** A rising XP curve turns accumulated work into levels and rank
  titles, from Novice through to Legend.
- **Badges:** Thirteen achievements for milestones worth noticing, including clearing a
  full board, finishing something before 6am, and taking down an Epic.
- **Task management:** Create, edit, complete, reopen and delete tasks with notes, due
  dates and priority, filtered by status, difficulty or free-text search.
- **User accounts:** Auth0-backed sign-in so several people can share one instance, each
  with their own tasks, XP, badges and character.

### Adventure Features

- **Classes and ability scores:** Six classes with distinct hit dice, ability spreads and
  a rule-bending perk each, on the six familiar D&D abilities.
- **d20 combat:** Fight monsters round by round, with every roll shown as arithmetic:
  the dice, each labelled modifier, the total and the number it had to beat.
- **Equipment and loot:** Wins drop gear with rolled rarities that change your ability
  scores, armour class and damage. Three slots, and anything spare sells for gold.
- **Quests:** Short goals that count real events, paying gold and equipment.
- **Stamina, the bridge:** Fighting costs stamina, and only finishing a real task produces
  it. The adventure is a reason to clear the list, never a way to avoid it.
- **Due-date urgency:** Due pills shift colour as a task approaches and passes its date,
  so the list surfaces what is actually pressing.

### Presentation Features

- **Dark, light and system themes:** A three-way switch, persisted locally and applied
  before first paint so a reload never flashes the wrong theme.
- **Completion feedback:** XP floats up from the task that earned it, the bar animates,
  and crossing a level opens a full-screen level-up moment.
- **Progress record:** A fourteen-day activity chart, a completed-by-difficulty
  breakdown, and a rank ladder showing what comes next.
- **Accessible colour:** Difficulty colours are validated for colourblind separation
  with a palette checker rather than picked by eye, and every chart is direct-labelled
  so identity never rests on colour alone.

### Operational Features

- **Single-container deployment:** One image serves the API and the SPA on one port,
  with Postgres alongside it in Compose and migrations applied on startup.
- **Verifiable behaviour:** Two checked-in scripts exercise the XP mathematics against
  a live API and drive the whole user flow through a real Chrome, failing on any console
  error or failed request.
