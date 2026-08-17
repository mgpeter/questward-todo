# API Specification

This is the API specification for the spec detailed in
@docs/specs/2026-08-17-rpg-combat-and-classes/spec.md

Every route below requires authentication, is scoped to the calling user, and returns 404
rather than 403 for anything belonging to someone else, matching the conventions already
established.

## Character Sheet

### GET /api/rpg/sheet

**Purpose:** Everything the character panel needs in one payload.
**Response:** `200`

```jsonc
{
  "classKey": "ranger",
  "className": "Ranger",
  "level": 4,
  "abilities": {
    "strength":     { "score": 12, "modifier": 1, "bonusFromItems": 0 },
    "dexterity":    { "score": 17, "modifier": 3, "bonusFromItems": 2 }
    // ... con, int, wis, cha
  },
  "armourClass": 15,
  "attackBonus": 5,
  "damage": "1d8+3",
  "hitPoints": { "current": 27, "max": 34 },
  "proficiencyBonus": 2,
  "stamina": 4,
  "gold": 120,
  "perk": { "key": "favoured-quarry", "name": "Favoured Quarry", "description": "..." }
}
```

### GET /api/rpg/classes

**Purpose:** The class catalog for the selection screen. Static, so it is cacheable.
**Response:** `200`, an array of `{ key, name, hitDie, primaryAbilities, perk, blurb }`.

### PUT /api/rpg/class

**Purpose:** Choose or change class.
**Body:** `{ "classKey": "ranger" }`
**Response:** `200` with the updated sheet.
**Errors:** `400` unknown class key.

Re-rolls ability scores, max hit points and starting equipment. Level, XP, badges, gold
and inventory are untouched, because those record work already done.

## Combat

### GET /api/rpg/monsters

**Purpose:** The bestiary filtered to the caller's level band, for the tavern list.
**Response:** `200`, array of `{ key, name, level, armourClass, maxHitPoints, blurb, goldRange, staminaCost }`.

### POST /api/rpg/encounters

**Purpose:** Start a fight.
**Body:** `{ "monsterKey": "goblin" }`
**Response:** `201` with the encounter.
**Errors:**
- `400` unknown monster, or monster outside the caller's level band
- `409` an encounter is already active
- `422` not enough stamina, with the current and required amounts in the problem detail

Spending stamina and creating the encounter happen in one transaction, so a failure cannot
consume stamina without producing a fight.

### GET /api/rpg/encounters/active

**Purpose:** Resume a fight after a reload.
**Response:** `200` with the encounter, or `204` when there is none.

### POST /api/rpg/encounters/{id}/attack

**Purpose:** Resolve one round.
**Response:** `200`

```jsonc
{
  "encounter": { "id": "...", "status": "active", "round": 3, "monsterHitPoints": 4 },
  "rolls": [
    {
      "actor": "player", "kind": "attack",
      "dice": [ { "sides": 20, "value": 17, "kept": true } ],
      "modifiers": [ { "label": "DEX", "value": 3 }, { "label": "proficiency", "value": 2 } ],
      "total": 22, "target": 15, "outcome": "hit", "critical": false
    },
    { "actor": "player", "kind": "damage", "dice": [ { "sides": 8, "value": 6, "kept": true } ], "total": 9 },
    { "actor": "monster", "kind": "attack", "total": 9, "target": 15, "outcome": "miss" }
  ],
  "result": {
    "status": "won",
    "goldAwarded": 18,
    "loot": [ { "id": "...", "itemKey": "hunters-bow", "name": "Hunter's Bow", "rarity": "rare" } ],
    "questsAdvanced": [ { "key": "goblin-cull", "name": "Goblin Cull", "progress": "2/3" } ]
  }
}
```

Every roll is itemised down to individual dice, including ones discarded by advantage.
That breakdown is the whole point: the client renders the arithmetic so a loss reads as
bad luck rather than an unexplained outcome.

**Errors:** `404` unknown or not the caller's, `409` the encounter has already ended.

### POST /api/rpg/encounters/{id}/flee

**Purpose:** Abandon a fight without a reward. The stamina is not refunded.
**Response:** `200` with the closed encounter.

## Inventory

### GET /api/rpg/inventory

**Response:** `200`, array of `{ id, itemKey, name, slot, rarity, isEquipped, abilityBonuses, damage, armourBonus, sellValue }`.

### POST /api/rpg/inventory/{id}/equip

**Purpose:** Equip an item, unequipping whatever occupied that slot.
**Response:** `200` with the updated sheet and inventory, so one round trip refreshes both.
**Errors:** `404` not the caller's item.

### POST /api/rpg/inventory/{id}/unequip

**Response:** `200` with the updated sheet and inventory.

### DELETE /api/rpg/inventory/{id}

**Purpose:** Sell for gold.
**Response:** `200` with `{ goldGained, gold }`.
**Errors:** `404` not the caller's, `409` the item is equipped.

## Quests

### GET /api/rpg/quests

**Response:** `200`, array of `{ key, name, description, objectives: [{ id, description, current, required }], isComplete, claimedAt, rewards }`.

### POST /api/rpg/quests/{key}/claim

**Purpose:** Claim the reward for a completed quest.
**Response:** `200` with `{ goldGained, item, gold }`.
**Errors:** `404` unknown quest, `409` not complete or already claimed.

## What Deliberately Does Not Exist

There is **no endpoint that grants XP**. Nothing in this surface can move
`character.TotalXp`; only `POST /api/tasks/{id}/complete` does, exactly as before. That is
the guarantee DEC-003 rests on, and it is asserted by a test rather than left to review.
