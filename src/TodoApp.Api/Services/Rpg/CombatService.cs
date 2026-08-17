using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

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

        var encounter = new Encounter
        {
            UserId = userId,
            MonsterKey = monster.Key,
            MonsterHitPoints = monster.MaxHitPoints,
            Status = EncounterStatus.Active,
            Round = 0,
            Log = Serialise(free
                ? [CombatRoll.Note(0, CombatRoll.Player, $"Arcane Recovery: {monster.Name} approaches at no cost.")]
                : [CombatRoll.Note(0, CombatRoll.Player, $"{monster.Name} approaches.")])
        };

        db.Encounters.Add(encounter);

        // Spending the stamina and opening the fight commit together, so a failure can
        // never take the cost without producing the encounter.
        await db.SaveChangesAsync(cancellationToken);

        return RpgResult<Encounter>.Success(encounter);
    }

    public async Task<RpgResult<AttackOutcome>> AttackAsync(
        Guid userId,
        Guid encounterId,
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

        var log = Deserialise(encounter.Log);
        var rolls = new List<CombatRoll>();

        encounter.Round++;

        // --- the player swings ------------------------------------------------
        var attack = RollAttackWithBlessing(encounter, sheet.AttackModifiers, monster.ArmourClass, sheet.CriticalOn);
        rolls.Add(CombatRoll.From(encounter.Round, CombatRoll.Player, attack, DescribeAttack(attack, monster.Name)));

        if (attack.Outcome == RollOutcome.Hit)
        {
            var damage = D20.Damage(roller, sheet.DamageExpression, sheet.DamageModifiers, attack.Critical);
            encounter.MonsterHitPoints = Math.Max(0, encounter.MonsterHitPoints - damage.Total);

            rolls.Add(CombatRoll.From(
                encounter.Round, CombatRoll.Player, damage,
                $"{damage.Total} damage. {monster.Name} has {encounter.MonsterHitPoints} hit points left."));
        }

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
            var reply = D20.Attack(
                roller,
                [new RollModifier(monster.Name, monster.AttackBonus)],
                sheet.ArmourClass);

            rolls.Add(CombatRoll.From(
                encounter.Round, CombatRoll.Monster, reply,
                reply.Outcome == RollOutcome.Hit
                    ? $"{monster.Name} connects."
                    : $"{monster.Name} misses."));

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

                    rolls.Add(CombatRoll.Note(
                        encounter.Round, CombatRoll.Player,
                        "You are driven off, battered but breathing."));
                }
            }
        }

        log.AddRange(rolls);
        encounter.Log = Serialise(log);
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

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
        log.Add(CombatRoll.Note(encounter.Round, CombatRoll.Player, "You withdraw. The stamina is spent."));

        encounter.Status = EncounterStatus.Fled;
        encounter.EndedAt = DateTimeOffset.UtcNow;
        encounter.Log = Serialise(log);

        await db.SaveChangesAsync(cancellationToken);

        return RpgResult<Encounter>.Success(encounter);
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

        rolls.Add(CombatRoll.Note(encounter.Round, CombatRoll.Player, $"{monster.Name} falls."));

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

            var name = ItemCatalog.Find(drop.ItemKey)?.Name ?? drop.ItemKey;
            rolls.Add(CombatRoll.Note(
                encounter.Round, CombatRoll.Player,
                $"{monster.Name} drops {RarityRules.Describe(drop.Rarity)} {name}."));
        }

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

    /// <summary>Applies the Cleric's Blessing, which rerolls the first natural 1 of a fight.</summary>
    private RollResult RollAttackWithBlessing(
        Encounter encounter,
        IReadOnlyList<RollModifier> modifiers,
        int armourClass,
        int criticalOn)
    {
        var result = D20.Attack(roller, modifiers, armourClass, criticalOn: criticalOn);

        var character = db.Characters.Local.FirstOrDefault(c => c.UserId == encounter.UserId);
        var isCleric = character?.ClassKey == ClassCatalog.Cleric;

        if (!isCleric || encounter.BlessingUsed || !result.CriticalFailure)
        {
            return result;
        }

        encounter.BlessingUsed = true;

        return D20.Attack(roller, modifiers, armourClass, criticalOn: criticalOn);
    }

    private static string DescribeAttack(RollResult attack, string monsterName) => attack.Outcome switch
    {
        RollOutcome.Hit when attack.Critical => $"A critical hit on {monsterName}.",
        RollOutcome.Hit => $"You hit {monsterName}.",
        _ when attack.CriticalFailure => "You fumble the swing.",
        _ => $"You miss {monsterName}."
    };

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
