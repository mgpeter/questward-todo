using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

public sealed record ChronicleSummary(
    int Fought,
    int Won,
    int Lost,
    int Fled,
    int GoldEarned,
    string? MostFoughtMonster,
    int MostFoughtCount);

public sealed record AttackOutcome(
    Encounter Encounter,
    IReadOnlyList<CombatRoll> Rolls,
    int PlayerHitPoints,
    int PlayerMaxHitPoints,
    int GoldAwarded,
    InventoryItem? Loot,
    IReadOnlyList<QuestAdvance> QuestsAdvanced);

/// <summary>
/// Runs fights.
/// </summary>
/// <remarks>
/// Nothing here touches <c>Character.TotalXp</c>, and nothing ever should. Combat pays in
/// gold and loot; experience is reserved for real work (DEC-003). The invariant is covered
/// by a test rather than left to review.
/// </remarks>
public sealed class CombatService(
    TodoDbContext db,
    IDiceRoller roller,
    CharacterSheetService sheets,
    LootService loot,
    QuestService quests)
{
    public const int StaminaPerEncounter = 1;

    /// <summary>Chance in 20 that the Wizard's Arcane Recovery refunds the stamina.</summary>
    private const int ArcaneRecoveryThreshold = 16;

    // Built from this service's own context rather than injected, because a bestiary row has
    // to be tracked by the very context that commits the fight. Tracked anywhere else, the
    // sighting and the encounter it belongs to could land in two different transactions.
    private readonly BestiaryService bestiary = new(db);

    public async Task<RpgResult<Encounter>> StartAsync(
        Guid userId,
        string monsterKey,
        CancellationToken cancellationToken)
    {
        var monster = MonsterCatalog.Find(monsterKey);

        if (monster is null)
        {
            return RpgResult<Encounter>.Fail(RpgFailure.NotFound, $"No monster called '{monsterKey}'.");
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var sheet = await sheets.BuildAsync(character, cancellationToken);

        if (!monster.IsAvailableAt(sheet.Level))
        {
            return RpgResult<Encounter>.Fail(
                RpgFailure.MonsterOutOfRange,
                $"{monster.Name} is not an appropriate opponent at level {sheet.Level}.");
        }

        if (await db.Encounters.AnyAsync(
                e => e.UserId == userId && e.Status == EncounterStatus.Active, cancellationToken))
        {
            return RpgResult<Encounter>.Fail(
                RpgFailure.EncounterAlreadyActive, "You are already in a fight.");
        }

        // The gate that keeps the game a sink for real work rather than a substitute.
        var free = character.ClassKey == ClassCatalog.Wizard && roller.Roll(20) >= ArcaneRecoveryThreshold;

        if (!free && character.Stamina < StaminaPerEncounter)
        {
            return RpgResult<Encounter>.Fail(
                RpgFailure.NotEnoughStamina,
                "You need stamina to fight. Complete a task to earn some.");
        }

        CharacterSheetService.NormaliseHitPoints(character, sheet, DateTimeOffset.UtcNow);

        if (!free)
        {
            character.Stamina -= StaminaPerEncounter;
        }

        // The id is settled here rather than left to the initialiser because the opening
        // line is keyed off it, and a line has to be chosen before the encounter exists.
        var encounterId = Guid.CreateVersion7();

        var opening = free
            ? $"Arcane Recovery: {monster.Name} approaches at no cost."
            : $"{monster.Name} approaches.";

        // Its own entry rather than appended to the line above, so the narration reads as a
        // sentence of its own. It is all flavour, which is what the second argument says.
        var openingFlavour = Flavour(FlavourMoment.Opening, encounterId, 0, monster);

        var encounter = new Encounter
        {
            Id = encounterId,
            UserId = userId,
            MonsterKey = monster.Key,
            MonsterHitPoints = monster.MaxHitPoints,
            Status = EncounterStatus.Active,
            Round = 0,
            Log = Serialise([
                CombatRoll.Note(0, CombatRoll.Player, opening),
                CombatRoll.Note(0, CombatRoll.Player, openingFlavour, openingFlavour)
            ])
        };

        db.Encounters.Add(encounter);

        // The chronicle counts a fight begun, not a fight won, so this sits before the first
        // round rather than at any of the three endings. Every gate that can refuse the fight
        // has already returned above, so nothing recorded here is a monster never met.
        var firstSighting = await bestiary.RecordSightingAsync(userId, monster.Key, cancellationToken);

        if (firstSighting)
        {
            // Discovery counts a kind met for the first time. It is recorded here and nowhere
            // else, so the only way to reach it is to spend stamina on a fight. Task
            // completion cannot pay a discovery quest by any route, which is what keeps the
            // DEC-014 progression gate the single answer to "may this pay out?".
            await quests.RecordAsync(
                userId, ObjectiveKind.DiscoverMonster, monster.Key, 1, cancellationToken);
        }

        // Spending the stamina and opening the fight commit together, so a failure can
        // never take the cost without producing the encounter.
        await SaveLettingBookkeepingGoAsync(cancellationToken);

        return RpgResult<Encounter>.Success(encounter);
    }

    public Task<RpgResult<AttackOutcome>> AttackAsync(
        Guid userId,
        Guid encounterId,
        CancellationToken cancellationToken) =>
        ResolveRoundAsync(userId, encounterId, abilityKey: null, cancellationToken);

    /// <summary>
    /// Resolves one round using a class ability instead of a plain attack. The monster
    /// answers exactly as it would otherwise, so the turn structure is unchanged.
    /// </summary>
    public Task<RpgResult<AttackOutcome>> UseAbilityAsync(
        Guid userId,
        Guid encounterId,
        string abilityKey,
        CancellationToken cancellationToken) =>
        ResolveRoundAsync(userId, encounterId, abilityKey, cancellationToken);

    private async Task<RpgResult<AttackOutcome>> ResolveRoundAsync(
        Guid userId,
        Guid encounterId,
        string? abilityKey,
        CancellationToken cancellationToken)
    {
        var encounter = await db.Encounters
            .FirstOrDefaultAsync(e => e.Id == encounterId && e.UserId == userId, cancellationToken);

        if (encounter is null)
        {
            return RpgResult<AttackOutcome>.Fail(RpgFailure.NotFound, "No such encounter.");
        }

        if (encounter.IsOver)
        {
            return RpgResult<AttackOutcome>.Fail(RpgFailure.EncounterOver, "That fight is already over.");
        }

        var monster = encounter.Monster;

        if (monster is null)
        {
            return RpgResult<AttackOutcome>.Fail(RpgFailure.NotFound, "That monster no longer exists.");
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var sheet = await sheets.BuildAsync(character, cancellationToken);

        CharacterSheetService.NormaliseHitPoints(character, sheet, DateTimeOffset.UtcNow);

        ClassAbility? ability = null;
        var uses = ReadUses(encounter);

        if (abilityKey is not null)
        {
            ability = ClassAbilities.Find(character.ClassKey, abilityKey);

            if (ability is null)
            {
                return RpgResult<AttackOutcome>.Fail(
                    RpgFailure.NotFound, "Your class does not have that ability.");
            }

            if (uses.GetValueOrDefault(ability.Key) >= ability.UsesPerEncounter)
            {
                return RpgResult<AttackOutcome>.Fail(
                    RpgFailure.AbilityExhausted,
                    $"{ability.Name} is spent for this fight.");
            }

            uses[ability.Key] = uses.GetValueOrDefault(ability.Key) + 1;
            WriteUses(encounter, uses);
        }

        var log = Deserialise(encounter.Log);
        var rolls = new List<CombatRoll>();

        encounter.Round++;

        // --- the player acts --------------------------------------------------
        var skipsAttack = ResolvePlayerAction(
            encounter, character, sheet, monster, ability, rolls);

        var goldAwarded = 0;
        InventoryItem? drop = null;
        IReadOnlyList<QuestAdvance> advances = [];

        if (encounter.MonsterHitPoints <= 0)
        {
            (goldAwarded, drop, advances) = await ResolveVictoryAsync(
                userId, character, sheet, encounter, monster, rolls, cancellationToken);
        }
        else
        {
            // --- the monster answers ------------------------------------------
            // Vicious Mockery lingers: the monster swings at disadvantage while it lasts.
            var mocked = encounter.MonsterDisadvantageRounds > 0;

            if (mocked)
            {
                encounter.MonsterDisadvantageRounds--;
            }

            var reply = D20.Attack(
                roller,
                [new RollModifier(monster.Name, monster.AttackBonus)],
                sheet.ArmourClass,
                mocked ? RollMode.Disadvantage : RollMode.Normal);

            var replyLine = reply.Outcome == RollOutcome.Hit
                ? $"{monster.Name} connects."
                : mocked
                    ? $"{monster.Name} misses, still stung by the remark."
                    : $"{monster.Name} misses.";

            var replyMoment = reply.Outcome switch
            {
                RollOutcome.Hit when reply.Critical => FlavourMoment.MonsterCritical,
                RollOutcome.Hit => FlavourMoment.MonsterHit,
                _ => FlavourMoment.MonsterMiss
            };

            var replyFlavour = Flavour(replyMoment, encounter.Id, encounter.Round, monster);

            rolls.Add(CombatRoll.From(
                encounter.Round, CombatRoll.Monster, reply,
                CombatRoll.Compose(replyLine, replyFlavour), replyFlavour));

            if (reply.Outcome == RollOutcome.Hit)
            {
                var damage = D20.Damage(roller, monster.Damage, [], reply.Critical);
                character.CurrentHitPoints = Math.Max(0, character.CurrentHitPoints - damage.Total);

                rolls.Add(CombatRoll.From(
                    encounter.Round, CombatRoll.Monster, damage,
                    $"{damage.Total} damage. You have {character.CurrentHitPoints} hit points left."));

                if (character.CurrentHitPoints <= 0)
                {
                    encounter.Status = EncounterStatus.Lost;
                    encounter.EndedAt = DateTimeOffset.UtcNow;

                    // Left standing on one hit point rather than killed. A todo app has no
                    // business punishing someone for losing a dice roll.
                    character.CurrentHitPoints = 1;
                    character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;

                    var defeatFlavour = Flavour(
                        FlavourMoment.Defeat, encounter.Id, encounter.Round, monster);

                    rolls.Add(CombatRoll.Note(
                        encounter.Round, CombatRoll.Player,
                        CombatRoll.Compose(
                            "You are driven off, battered but breathing.", defeatFlavour),
                        defeatFlavour));
                }
            }
        }

        log.AddRange(rolls);
        encounter.Log = Serialise(log);
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;

        await SaveLettingBookkeepingGoAsync(cancellationToken);

        return RpgResult<AttackOutcome>.Success(new AttackOutcome(
            encounter, rolls, character.CurrentHitPoints, sheet.MaxHitPoints, goldAwarded, drop, advances));
    }

    public async Task<RpgResult<Encounter>> FleeAsync(
        Guid userId,
        Guid encounterId,
        CancellationToken cancellationToken)
    {
        var encounter = await db.Encounters
            .FirstOrDefaultAsync(e => e.Id == encounterId && e.UserId == userId, cancellationToken);

        if (encounter is null)
        {
            return RpgResult<Encounter>.Fail(RpgFailure.NotFound, "No such encounter.");
        }

        if (encounter.IsOver)
        {
            return RpgResult<Encounter>.Fail(RpgFailure.EncounterOver, "That fight is already over.");
        }

        var log = Deserialise(encounter.Log);

        var fleeFlavour = Flavour(
            FlavourMoment.Flee, encounter.Id, encounter.Round, encounter.Monster);

        log.Add(CombatRoll.Note(
            encounter.Round, CombatRoll.Player,
            CombatRoll.Compose("You withdraw. The stamina is spent.", fleeFlavour),
            fleeFlavour));

        encounter.Status = EncounterStatus.Fled;
        encounter.EndedAt = DateTimeOffset.UtcNow;
        encounter.Log = Serialise(log);

        await db.SaveChangesAsync(cancellationToken);

        return RpgResult<Encounter>.Success(encounter);
    }

    /// <summary>
    /// Finished encounters, newest first. Every fight and its full roll-by-roll log has
    /// always been persisted; this is what finally reads it back.
    /// </summary>
    public Task<List<Encounter>> HistoryAsync(
        Guid userId,
        int limit,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
        var query = db.Encounters
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.Status != EncounterStatus.Active);

        if (before is not null)
        {
            query = query.Where(e => e.StartedAt < before);
        }

        // Ordered to match IX_encounters_UserId_StartedAt.
        return query
            .OrderByDescending(e => e.StartedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Totals across every finished fight, for the chronicle's summary strip.</summary>
    public async Task<ChronicleSummary> SummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.Encounters
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.Status != EncounterStatus.Active)
            .Select(e => new { e.Status, e.GoldAwarded, e.MonsterKey })
            .ToListAsync(cancellationToken);

        var favourite = rows
            .GroupBy(r => r.MonsterKey, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return new ChronicleSummary(
            Fought: rows.Count,
            Won: rows.Count(r => r.Status == EncounterStatus.Won),
            Lost: rows.Count(r => r.Status == EncounterStatus.Lost),
            Fled: rows.Count(r => r.Status == EncounterStatus.Fled),
            GoldEarned: rows.Sum(r => r.GoldAwarded),
            MostFoughtMonster: favourite is null ? null : MonsterCatalog.Find(favourite.Key)?.Name,
            MostFoughtCount: favourite?.Count() ?? 0);
    }

    public Task<Encounter?> ActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Encounters.FirstOrDefaultAsync(
            e => e.UserId == userId && e.Status == EncounterStatus.Active, cancellationToken);

    public static IReadOnlyList<CombatRoll> ReadLog(Encounter encounter) => Deserialise(encounter.Log);

    private async Task<(int Gold, InventoryItem? Drop, IReadOnlyList<QuestAdvance> Advances)> ResolveVictoryAsync(
        Guid userId,
        Character character,
        Models.Rpg.CharacterSheet sheet,
        Encounter encounter,
        MonsterDefinition monster,
        List<CombatRoll> rolls,
        CancellationToken cancellationToken)
    {
        encounter.Status = EncounterStatus.Won;
        encounter.EndedAt = DateTimeOffset.UtcNow;

        var killFlavour = Flavour(FlavourMoment.Kill, encounter.Id, encounter.Round, monster);

        rolls.Add(CombatRoll.Note(
            encounter.Round, CombatRoll.Player,
            CombatRoll.Compose($"{monster.Name} falls.", killFlavour),
            killFlavour));

        var perk = sheet.Class?.Perk;

        var gold = loot.RollGold(monster, perk == ClassPerk.SilverTongue);
        character.Gold += gold;
        encounter.GoldAwarded = gold;

        rolls.Add(CombatRoll.Note(encounter.Round, CombatRoll.Player, $"You recover {gold} gold."));

        if (perk == ClassPerk.SecondWind)
        {
            var healed = Math.Max(1, sheet.MaxHitPoints / 4);
            character.CurrentHitPoints = Math.Min(sheet.MaxHitPoints, character.CurrentHitPoints + healed);

            rolls.Add(CombatRoll.Note(
                encounter.Round, CombatRoll.Player, $"Second Wind restores {healed} hit points."));
        }

        var drop = loot.RollDrop(userId, monster, perk == ClassPerk.FavouredQuarry);

        if (drop is not null)
        {
            db.InventoryItems.Add(drop);

            // The rolled name, not the catalog name. A log line that announced a plain
            // Silvered Blade for a drop that rolled Keen would read as the affix having been
            // lost between the roll and the row.
            rolls.Add(CombatRoll.Note(
                encounter.Round, CombatRoll.Player,
                $"{monster.Name} drops {RarityRules.Describe(drop.Rarity)} {drop.DisplayName}."));
        }

        // The kill counters ride the same transaction as the fight, the gold and the drop, so
        // the chronicle can never claim a win the encounters table does not also show. This is
        // the only place EncounterStatus.Won is set, so it is the complete kill site.
        await bestiary.RecordKillAsync(
            userId, monster.Key, encounter.Round, gold, cancellationToken);

        // Quest progress rides the same transaction as the fight it came from.
        var advances = new List<QuestAdvance>();
        advances.AddRange(await quests.RecordAsync(
            userId, ObjectiveKind.DefeatMonster, monster.Key, 1, cancellationToken));
        advances.AddRange(await quests.RecordAsync(
            userId, ObjectiveKind.EarnGold, string.Empty, gold, cancellationToken));

        if (drop is not null)
        {
            advances.AddRange(await quests.RecordAsync(
                userId, ObjectiveKind.AcquireItem, string.Empty, 1, cancellationToken));
        }

        // Note what is not here: character.TotalXp is never touched.
        return (gold, drop, Deduplicate(advances));
    }

    /// <summary>
    /// Runs the player's half of a round, whether that is a plain attack or an ability.
    /// </summary>
    /// <returns>True when the action forfeits the attack, as Healing Word does.</returns>
    private bool ResolvePlayerAction(
        Encounter encounter,
        Character character,
        Models.Rpg.CharacterSheet sheet,
        MonsterDefinition monster,
        ClassAbility? ability,
        List<CombatRoll> rolls)
    {
        var round = encounter.Round;

        // Healing forfeits the swing entirely.
        if (ability?.Kind == AbilityKind.HealingWord)
        {
            var heal = D20.Damage(
                roller,
                DiceExpression.Parse("1d8"),
                [new RollModifier("WIS", sheet.EffectiveScores.Modifier(Ability.Wisdom))]);

            var before = character.CurrentHitPoints;
            character.CurrentHitPoints = Math.Min(sheet.MaxHitPoints, before + heal.Total);
            var restored = character.CurrentHitPoints - before;

            rolls.Add(CombatRoll.From(
                round, CombatRoll.Player, heal,
                $"Healing Word restores {restored} hit points. You are on {character.CurrentHitPoints}."));

            return true;
        }

        // Magic Missile skips the attack roll: unerring force always lands.
        if (ability?.Kind == AbilityKind.MagicMissile)
        {
            var damage = D20.Damage(
                roller,
                DiceExpression.Parse("3d4"),
                [new RollModifier("INT", sheet.EffectiveScores.Modifier(Ability.Intelligence))]);

            encounter.MonsterHitPoints = Math.Max(0, encounter.MonsterHitPoints - damage.Total);

            rolls.Add(CombatRoll.Note(round, CombatRoll.Player, "Magic Missile streaks out. It cannot miss."));
            rolls.Add(CombatRoll.From(
                round, CombatRoll.Player, damage,
                $"{damage.Total} force damage. {monster.Name} has {encounter.MonsterHitPoints} hit points left."));

            return false;
        }

        var mode = ability?.Kind switch
        {
            AbilityKind.SneakStrike or AbilityKind.AimedShot => RollMode.Advantage,
            _ => RollMode.Normal
        };

        // Aimed Shot lowers the threshold to 19, it does not set it to 19. Assigning it flat
        // would make a Keen weapon or a completed Nightfall Vigil worse on the one attack the
        // player spent a use on.
        var criticalOn = ability?.Kind == AbilityKind.AimedShot
            ? Math.Min(19, sheet.CriticalOn)
            : sheet.CriticalOn;

        var modifiers = ability?.Kind == AbilityKind.PowerAttack
            ? [.. sheet.AttackModifiers, new RollModifier("power attack", ClassAbilities.PowerAttackPenalty)]
            : sheet.AttackModifiers;

        if (ability is not null)
        {
            rolls.Add(CombatRoll.Note(round, CombatRoll.Player, $"{ability.Name}."));
        }

        var attack = RollAttackWithBlessing(encounter, modifiers, monster.ArmourClass, criticalOn, mode);
        var attackFlavour = Flavour(MomentFor(attack), encounter.Id, round, monster);

        rolls.Add(CombatRoll.From(
            round, CombatRoll.Player, attack,
            CombatRoll.Compose(DescribeAttack(attack, monster.Name), attackFlavour),
            attackFlavour));

        if (attack.Outcome != RollOutcome.Hit)
        {
            return false;
        }

        var expression = ability?.Kind == AbilityKind.ViciousMockery
            ? DiceExpression.Parse("1d6")
            : sheet.DamageExpression;

        var damageModifiers = ability?.Kind == AbilityKind.ViciousMockery
            ? [new RollModifier("CHA", sheet.EffectiveScores.Modifier(Ability.Charisma))]
            : sheet.DamageModifiers;

        // Power Attack doubles the dice on any hit, the way a critical does.
        var doubleDice = attack.Critical || ability?.Kind == AbilityKind.PowerAttack;

        var hit = D20.Damage(roller, expression, damageModifiers, doubleDice);
        encounter.MonsterHitPoints = Math.Max(0, encounter.MonsterHitPoints - hit.Total);

        rolls.Add(CombatRoll.From(
            round, CombatRoll.Player, hit,
            $"{hit.Total} damage. {monster.Name} has {encounter.MonsterHitPoints} hit points left."));

        if (ability?.Kind == AbilityKind.ViciousMockery && encounter.MonsterHitPoints > 0)
        {
            // Applies to the counter-attack in this same round: the monster's next swing
            // is the one that goes wide, which is what mocking something should feel like.
            // The counter consumes it, so it does not linger into later rounds.
            encounter.MonsterDisadvantageRounds = ClassAbilities.MockeryRounds;

            rolls.Add(CombatRoll.Note(
                round, CombatRoll.Player,
                $"{monster.Name} is rattled. Its next swing goes wide."));
        }

        return false;
    }

    /// <summary>Applies the Cleric's Blessing, which rerolls the first natural 1 of a fight.</summary>
    private RollResult RollAttackWithBlessing(
        Encounter encounter,
        IReadOnlyList<RollModifier> modifiers,
        int armourClass,
        int criticalOn,
        RollMode mode = RollMode.Normal)
    {
        var result = D20.Attack(roller, modifiers, armourClass, mode, criticalOn);

        var character = db.Characters.Local.FirstOrDefault(c => c.UserId == encounter.UserId);
        var isCleric = character?.ClassKey == ClassCatalog.Cleric;

        if (!isCleric || encounter.BlessingUsed || !result.CriticalFailure)
        {
            return result;
        }

        encounter.BlessingUsed = true;

        return D20.Attack(roller, modifiers, armourClass, mode, criticalOn);
    }

    private static Dictionary<string, int> ReadUses(Encounter encounter)
    {
        if (string.IsNullOrWhiteSpace(encounter.AbilityUses))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(encounter.AbilityUses) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void WriteUses(Encounter encounter, Dictionary<string, int> uses) =>
        encounter.AbilityUses = JsonSerializer.Serialize(uses);

    /// <summary>Remaining uses of each ability for the caller's class this fight.</summary>
    public static IReadOnlyDictionary<string, int> RemainingUses(Encounter encounter, string? classKey)
    {
        var spent = ReadUses(encounter);

        return ClassAbilities.For(classKey)
            .ToDictionary(a => a.Key, a => Math.Max(0, a.UsesPerEncounter - spent.GetValueOrDefault(a.Key)));
    }

    private static string DescribeAttack(RollResult attack, string monsterName) => attack.Outcome switch
    {
        RollOutcome.Hit when attack.Critical => $"A critical hit on {monsterName}.",
        RollOutcome.Hit => $"You hit {monsterName}.",
        _ when attack.CriticalFailure => "You fumble the swing.",
        _ => $"You miss {monsterName}."
    };

    private static FlavourMoment MomentFor(RollResult attack) => attack.Outcome switch
    {
        RollOutcome.Hit when attack.Critical => FlavourMoment.PlayerCritical,
        RollOutcome.Hit => FlavourMoment.PlayerHit,
        _ when attack.CriticalFailure => FlavourMoment.PlayerFumble,
        _ => FlavourMoment.PlayerMiss
    };

    /// <summary>
    /// The narrative half of a log line, chosen from the encounter id, the round and the
    /// moment.
    /// </summary>
    /// <remarks>
    /// This consumes no <see cref="IDiceRoller"/> roll, and must never be changed so that it
    /// does. Every SequenceDiceRoller script in the test suite hard-codes how many rolls a
    /// round takes and in what order they arrive; a flavour line drawn from the roller would
    /// shift every later value in the script and silently change what dozens of existing
    /// tests are asserting. Keying off the id and the round instead also makes the choice
    /// stable, so a reloaded fight narrates itself exactly as it did the first time.
    /// </remarks>
    private static string Flavour(
        FlavourMoment moment,
        Guid encounterId,
        int round,
        MonsterDefinition? monster) =>
        FlavourCatalog.Pick(moment, encounterId, round, monster?.Name ?? "creature");

    /// <summary>
    /// Commits the fight, dropping the chronicle write rather than the fight when the
    /// chronicle is what the database refused.
    /// </summary>
    /// <remarks>
    /// A bestiary row is bookkeeping, not the point of the transaction. Two starts racing to
    /// insert the same user and monster pair would otherwise lose to
    /// IX_bestiary_entries_UserId_MonsterKey and return a 500 that had already taken the
    /// stamina without opening the fight. Concurrency failures are left alone so the existing
    /// handler still sees them.
    /// </remarks>
    private async Task SaveLettingBookkeepingGoAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception is not DbUpdateConcurrencyException && HasPendingChronicleWrite())
        {
            foreach (var entry in db.ChangeTracker.Entries<BestiaryEntry>())
            {
                entry.State = EntityState.Detached;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private bool HasPendingChronicleWrite() =>
        db.ChangeTracker.Entries<BestiaryEntry>()
            .Any(e => e.State is EntityState.Added or EntityState.Modified);

    private static IReadOnlyList<QuestAdvance> Deduplicate(IEnumerable<QuestAdvance> advances) =>
        advances
            .GroupBy(a => a.Key, StringComparer.Ordinal)
            .Select(g => g.FirstOrDefault(a => a.JustCompleted) ?? g.Last())
            .ToList();

    private static string Serialise(IReadOnlyList<CombatRoll> log) => JsonSerializer.Serialize(log);

    private static List<CombatRoll> Deserialise(string log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CombatRoll>>(log) ?? [];
        }
        catch (JsonException)
        {
            // A corrupt log must not make an in-progress fight unplayable.
            return [];
        }
    }
}
