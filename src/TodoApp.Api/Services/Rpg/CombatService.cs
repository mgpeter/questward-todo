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

/// <param name="Loot">What the monster itself dropped, or null when it dropped nothing.</param>
/// <param name="ClearReward">
/// The dungeon's guaranteed reward, on the round that cleared its last room. Carried beside the
/// drop rather than instead of it: a clear round hands over two items, the quest advance already
/// counts two, and a single slot reported one of them or, when the boss's own drop failed its
/// roll, neither.
/// </param>
public sealed record AttackOutcome(
    Encounter Encounter,
    IReadOnlyList<CombatRoll> Rolls,
    int PlayerHitPoints,
    int PlayerMaxHitPoints,
    int GoldAwarded,
    InventoryItem? Loot,
    InventoryItem? ClearReward,
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

    public Task<RpgResult<Encounter>> StartAsync(
        Guid userId,
        string monsterKey,
        CancellationToken cancellationToken) =>
        StartAsync(userId, monsterKey, run: null, cancellationToken);

    /// <summary>
    /// Opens a fight, either at the tavern or as one room of a dungeon run.
    /// </summary>
    /// <remarks>
    /// One method rather than two, because DEC-012's gate is the thing that must not be
    /// duplicated. A room charges its stamina here, on the same line, through the same check, as
    /// a tavern fight; a second path that opened dungeon fights would be one edit away from
    /// letting a five room run cost one unit of real work and pay five fights' loot.
    /// </remarks>
    /// <param name="run">
    /// The run this fight is a room of, or null for a fight taken at the tavern. Tracked by this
    /// context, so ending it commits with the fight.
    /// </param>
    public async Task<RpgResult<Encounter>> StartAsync(
        Guid userId,
        string monsterKey,
        DungeonRun? run,
        CancellationToken cancellationToken)
    {
        var monster = MonsterCatalog.Find(monsterKey);

        if (monster is null)
        {
            return RpgResult<Encounter>.Fail(RpgFailure.NotFound, $"No monster called '{monsterKey}'.");
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var sheet = await sheets.BuildAsync(character, cancellationToken);

        // The tavern's band, and only the tavern's. A dungeon is gated once, at its own door, and
        // its boss deliberately sits above the band its rooms were drawn from: the Sunken Warren
        // opens at level two and ends on a level five Hedge Troll. Applying the band per room
        // would make the last room of every dungeon unreachable, and applying it to a run that
        // levelled up mid-way would strand the player inside one.
        if (run is null && !monster.IsAvailableAt(sheet.Level))
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

        // A tavern fight while a run is open would spend the one encounter slot outside the run
        // and strand it: the dungeon could never open its next room, and abandoning would be the
        // only way out of a run that had already been paid for.
        if (run is null && await db.DungeonRuns.AnyAsync(
                r => r.UserId == userId && r.Status == DungeonRunStatus.Active, cancellationToken))
        {
            return RpgResult<Encounter>.Fail(
                RpgFailure.DungeonInProgress,
                "You are in a dungeon. Finish the run or abandon it before fighting elsewhere.");
        }

        // The run is re-read rather than trusted, because the instance handed in was checked
        // several awaits ago and a concurrent abandon commits inside that window. Without this,
        // a room opens on a finished run: the stamina is spent, the encounter points at a run
        // that can never pay its clear reward, and the dungeon screen loses the fight entirely.
        if (run is not null && !await db.DungeonRuns.AnyAsync(
                r => r.Id == run.Id && r.Status == DungeonRunStatus.Active, cancellationToken))
        {
            return RpgResult<Encounter>.Fail(RpgFailure.DungeonOver, "That run is already over.");
        }

        // The gate that keeps the game a sink for real work rather than a substitute.
        //
        // Asked before the perk is rolled, and that order is load-bearing. Arcane Recovery
        // refunds a stamina, and there is nothing to refund at zero: rolling first meant a
        // refused attempt still drew a fresh d20, wrote nothing, and could simply be retried
        // until one came up 16 or better, which turns a one-in-four perk into a free dungeon
        // for anyone willing to press the button.
        if (character.Stamina < StaminaPerEncounter)
        {
            return RpgResult<Encounter>.Fail(
                RpgFailure.NotEnoughStamina,
                "You need stamina to fight. Complete a task to earn some.");
        }

        var free = character.ClassKey == ClassCatalog.Wizard && roller.Roll(20) >= ArcaneRecoveryThreshold;

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
            // The encounter points at the run, never the reverse, which is what keeps a room's
            // fight an ordinary row governed by the existing one-fight-at-a-time index.
            DungeonRunId = run?.Id,
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
        ResolveRoundAsync(userId, encounterId, abilityKey: null, itemId: null, cancellationToken);

    /// <summary>
    /// Resolves one round using a class ability instead of a plain attack. The monster
    /// answers exactly as it would otherwise, so the turn structure is unchanged.
    /// </summary>
    public Task<RpgResult<AttackOutcome>> UseAbilityAsync(
        Guid userId,
        Guid encounterId,
        string abilityKey,
        CancellationToken cancellationToken) =>
        ResolveRoundAsync(userId, encounterId, abilityKey, itemId: null, cancellationToken);

    /// <summary>
    /// Resolves one round by spending a consumable instead of swinging.
    /// </summary>
    /// <remarks>
    /// A round, not a free action, and it goes through the same path for that reason. Using a
    /// draught takes the player's half exactly as Healing Word does and the monster still
    /// answers. That price is load-bearing: healing that cost no turn would let any losing fight
    /// be won without opening another one, which is inflation of the DEC-003 family wearing a
    /// different hat. A consumable is not an ability and neither reads nor writes AbilityUses.
    /// </remarks>
    public Task<RpgResult<AttackOutcome>> UseItemAsync(
        Guid userId,
        Guid encounterId,
        Guid itemId,
        CancellationToken cancellationToken) =>
        ResolveRoundAsync(userId, encounterId, abilityKey: null, itemId, cancellationToken);

    private async Task<RpgResult<AttackOutcome>> ResolveRoundAsync(
        Guid userId,
        Guid encounterId,
        string? abilityKey,
        Guid? itemId,
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

        var spend = SpendAbilityUse(encounter, character.ClassKey, abilityKey);

        if (!spend.Ok)
        {
            return RpgResult<AttackOutcome>.Fail(spend.Failure, spend.Message ?? string.Empty);
        }

        var ability = spend.Value;

        // Found and checked before the round starts, so a refused use costs neither a round
        // number nor a unit off the stack. Nothing here reads the roller either, so a refusal
        // cannot shift the sequence a retry would see.
        var draught = await TakeConsumableAsync(userId, itemId, cancellationToken);

        if (!draught.Ok)
        {
            return RpgResult<AttackOutcome>.Fail(draught.Failure, draught.Message ?? string.Empty);
        }

        var log = Deserialise(encounter.Log);
        var rolls = new List<CombatRoll>();

        // Read once and threaded through the whole round. Reading it again per half would let
        // the monster see an effect the player's half has already spent.
        var effects = StatusEffects.Read(encounter);

        // Loaded tracked, so ending the run commits in the same SaveChanges as the fight that
        // ended it. A run written in a second unit of work could survive a fight that did not,
        // and the player would be looking at a cleared dungeon whose last room is still open.
        var run = await LoadRunAsync(encounter, cancellationToken);

        // Both halves of the round share this number, so a player line and the answer it
        // provoked read as one exchange rather than as two rounds.
        encounter.Round++;

        // --- the player acts --------------------------------------------------
        ResolvePlayerAction(
            encounter, character, sheet, monster, new PlayerAction(ability, draught.Value), effects, rolls);

        var goldAwarded = 0;
        InventoryItem? drop = null;
        InventoryItem? clearReward = null;
        IReadOnlyList<QuestAdvance> advances = [];

        if (encounter.MonsterHitPoints <= 0)
        {
            (goldAwarded, drop, clearReward, advances) = await ResolveVictoryAsync(
                userId, character, sheet, encounter, monster, run, rolls, cancellationToken);
        }
        else
        {
            // --- the boss changes gear ----------------------------------------
            // Between the two halves, so a phase entered by the player's blow is already in
            // force for the answer it provoked. Costs no die.
            ResolvePhaseChange(encounter, monster, effects, rolls);

            // --- the monster answers ------------------------------------------
            ResolveMonsterReply(encounter, character, sheet, monster, run, effects, rolls);
        }

        // --- the end of the round ---------------------------------------------
        // Only while the fight is still live, and after both halves. That placement is not
        // taste: every one-round dice script in the suite ends its fight in the round it
        // scripts, so a tick at the start of a round, or one that ran on the round that ended
        // the fight, would land inside those scripts rather than after them. Here it costs
        // nothing at all on a round with no effect in force.
        if (encounter.Status == EncounterStatus.Active)
        {
            ResolveTick(encounter, character, sheet, monster, effects, rolls);

            // A poison can finish a fight the swings did not. The victory tail runs here, with
            // its own dice, rather than being skipped because the monster died out of turn.
            if (encounter.MonsterHitPoints <= 0)
            {
                (goldAwarded, drop, clearReward, advances) = await ResolveVictoryAsync(
                    userId, character, sheet, encounter, monster, run, rolls, cancellationToken);
            }
        }

        StatusEffects.Write(encounter, effects);

        log.AddRange(rolls);
        encounter.Log = Serialise(log);
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;

        await SaveLettingBookkeepingGoAsync(cancellationToken);

        return RpgResult<AttackOutcome>.Success(new AttackOutcome(
            encounter, rolls, character.CurrentHitPoints, sheet.MaxHitPoints, goldAwarded, drop,
            clearReward, advances));
    }

    /// <summary>
    /// Charges one use of the named ability, or succeeds with no ability for a plain attack.
    /// </summary>
    /// <remarks>
    /// The use is spent before the round runs, so an ability that fails later in the round
    /// still costs its use. Nothing here reads the roller, so a refused ability leaves the
    /// dice untouched and cannot shift the sequence a retry would see.
    /// </remarks>
    private static RpgResult<ClassAbility?> SpendAbilityUse(
        Encounter encounter,
        string? classKey,
        string? abilityKey)
    {
        if (abilityKey is null)
        {
            return RpgResult<ClassAbility?>.Success(null);
        }

        var ability = ClassAbilities.Find(classKey, abilityKey);

        if (ability is null)
        {
            return RpgResult<ClassAbility?>.Fail(
                RpgFailure.NotFound, "Your class does not have that ability.");
        }

        var uses = ReadUses(encounter);

        if (uses.GetValueOrDefault(ability.Key) >= ability.UsesPerEncounter)
        {
            return RpgResult<ClassAbility?>.Fail(
                RpgFailure.AbilityExhausted,
                $"{ability.Name} is spent for this fight.");
        }

        uses[ability.Key] = uses.GetValueOrDefault(ability.Key) + 1;
        WriteUses(encounter, uses);

        return RpgResult<ClassAbility?>.Success(ability);
    }

    /// <summary>
    /// Runs the monster's half of the round. Reached only while the monster is still standing,
    /// so a killing blow is never answered.
    /// </summary>
    private void ResolveMonsterReply(
        Encounter encounter,
        Character character,
        Models.Rpg.CharacterSheet sheet,
        MonsterDefinition monster,
        DungeonRun? run,
        List<StatusEffect> effects,
        List<CombatRoll> rolls)
    {
        // Read, then spent, before the roll. That is exactly where the old
        // MonsterDisadvantageRounds counter was decremented, and keeping it there is what makes
        // the Bard's remark land on the counter-attack it provoked instead of on the following
        // round's. Spending at the one site that applies an effect is the whole lifecycle rule,
        // so these three reads and the three spends under them move together or not at all.
        var weakened = StatusEffects.Find(effects, EffectKind.Weakened, EffectTarget.Monster);
        var empowered = StatusEffects.MagnitudeOf(effects, EffectKind.Empowered, EffectTarget.Monster);
        var guarded = StatusEffects.MagnitudeOf(effects, EffectKind.Guarded, EffectTarget.Player);

        StatusEffects.Spend(effects, EffectKind.Weakened, EffectTarget.Monster);
        StatusEffects.Spend(effects, EffectKind.Empowered, EffectTarget.Monster);
        StatusEffects.Spend(effects, EffectKind.Guarded, EffectTarget.Player);

        // Empowered is a labelled modifier on both the swing and its damage, never an extra
        // die: the client shows the arithmetic, and the dice scripts stay put.
        IReadOnlyList<RollModifier> empowerment =
            empowered == 0 ? [] : [new RollModifier("empowered", empowered)];

        var reply = D20.Attack(
            roller,
            [new RollModifier(monster.Name, monster.AttackBonus), .. empowerment],
            sheet.ArmourClass + guarded,
            weakened is null ? RollMode.Normal : RollMode.Disadvantage);

        var replyLine = reply.Outcome == RollOutcome.Hit
            ? $"{monster.Name} connects."
            : DescribeMonsterMiss(monster, weakened);

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

        if (reply.Outcome != RollOutcome.Hit)
        {
            return;
        }

        var damage = D20.Damage(roller, monster.Damage, empowerment, reply.Critical);
        character.CurrentHitPoints = Math.Max(0, character.CurrentHitPoints - damage.Total);

        rolls.Add(CombatRoll.From(
            encounter.Round, CombatRoll.Monster, damage,
            $"{damage.Total} damage. You have {character.CurrentHitPoints} hit points left."));

        if (character.CurrentHitPoints <= 0)
        {
            RecordDefeat(encounter, character, monster, run, rolls);
        }
    }

    /// <summary>
    /// How a monster's failed swing reads when something was working against it.
    /// </summary>
    /// <remarks>
    /// Keyed off the source rather than the mere presence of Weakened. A remark is not a
    /// poison and should not read as one: the day something other than the Bard weakens a
    /// monster, "still stung by the remark" would be describing a line nobody said.
    /// </remarks>
    private static string DescribeMonsterMiss(MonsterDefinition monster, StatusEffect? weakened) =>
        weakened switch
        {
            null => $"{monster.Name} misses.",
            { Source: ClassAbilities.ViciousMockery } => $"{monster.Name} misses, still stung by the remark.",
            _ => $"{monster.Name} misses, off balance."
        };

    /// <summary>
    /// The end of the round: poison bites, regeneration knits, and neither spends a die.
    /// </summary>
    /// <remarks>
    /// Magnitudes were fixed when the effect was applied, so nothing here reaches for the
    /// roller. That is the decision the whole engine rests on: a magnitude rolled at tick time
    /// would insert a die into the middle of every round and shift every hard-coded
    /// SequenceDiceRoller script in the suite at once.
    /// <para>
    /// Lines are emitted as notes rather than damage rolls. Several tests reach for the first
    /// roll of a given kind in a round, and a tick wearing the damage kind would hijack them.
    /// </para>
    /// </remarks>
    private static void ResolveTick(
        Encounter encounter,
        Character character,
        Models.Rpg.CharacterSheet sheet,
        MonsterDefinition monster,
        List<StatusEffect> effects,
        List<CombatRoll> rolls)
    {
        foreach (var tick in StatusEffects.Tick(effects))
        {
            var line = tick.Target == EffectTarget.Player
                ? TickPlayer(character, sheet, tick)
                : TickMonster(encounter, monster, tick);

            if (line is null)
            {
                continue;
            }

            rolls.Add(CombatRoll.Note(
                encounter.Round,
                tick.Target == EffectTarget.Player ? CombatRoll.Player : CombatRoll.Monster,
                line));
        }
    }

    /// <summary>
    /// Applies one tick to the player, or returns null when it changed nothing. A line
    /// reporting that no hit points moved is noise in a log someone is reading to follow a fight.
    /// </summary>
    /// <remarks>
    /// The floor is one hit point and a tick never sets Lost. Losing a fight to damage over
    /// time in a round where nothing went wrong reads as arbitrary, and <see cref="RecordDefeat"/>
    /// already refuses to kill for the same reason.
    /// </remarks>
    private static string? TickPlayer(
        Character character,
        Models.Rpg.CharacterSheet sheet,
        StatusEffect tick)
    {
        var before = character.CurrentHitPoints;

        character.CurrentHitPoints = tick.Kind == EffectKind.Poisoned
            ? Math.Max(1, before - tick.Magnitude)
            : Math.Min(sheet.MaxHitPoints, before + tick.Magnitude);

        var change = Math.Abs(character.CurrentHitPoints - before);

        return change == 0
            ? null
            : tick.Kind == EffectKind.Poisoned
                ? $"Poison takes {change}. You have {character.CurrentHitPoints} hit points left."
                : $"You knit back together for {change}. You are on {character.CurrentHitPoints}.";
    }

    /// <summary>Applies one tick to the monster, or returns null when it changed nothing.</summary>
    private static string? TickMonster(
        Encounter encounter,
        MonsterDefinition monster,
        StatusEffect tick)
    {
        var before = encounter.MonsterHitPoints;

        // A corpse does not regenerate. Poison fires first, so without this guard a heal later
        // in the same tick could undo a kill the tick had already dealt and the fight would
        // carry on past it.
        if (tick.Kind == EffectKind.Regenerating && before <= 0)
        {
            return null;
        }

        encounter.MonsterHitPoints = tick.Kind == EffectKind.Poisoned
            ? Math.Max(0, before - tick.Magnitude)
            : Math.Min(monster.MaxHitPoints, before + tick.Magnitude);

        var change = Math.Abs(encounter.MonsterHitPoints - before);

        return change == 0
            ? null
            : tick.Kind == EffectKind.Poisoned
                ? $"Poison takes {change}. {monster.Name} has {encounter.MonsterHitPoints} hit points left."
                : $"{monster.Name} knits back together for {change}. It has {encounter.MonsterHitPoints} left.";
    }

    /// <summary>Ends a fight the player lost, and the run it was a room of. Costs no roll.</summary>
    private static void RecordDefeat(
        Encounter encounter,
        Character character,
        MonsterDefinition monster,
        DungeonRun? run,
        List<CombatRoll> rolls)
    {
        encounter.Status = EncounterStatus.Lost;
        encounter.EndedAt = DateTimeOffset.UtcNow;

        // Left standing on one hit point rather than killed. A todo app has no
        // business punishing someone for losing a dice roll.
        character.CurrentHitPoints = 1;
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;

        // Here, at the one site that sets Lost, rather than at a second check somewhere after the
        // round. A run left Active behind a lost room would hold the encounter slot forever and
        // no route would ever close it.
        EndRun(run, DungeonRunStatus.Failed);

        var defeatFlavour = Flavour(
            FlavourMoment.Defeat, encounter.Id, encounter.Round, monster);

        rolls.Add(CombatRoll.Note(
            encounter.Round, CombatRoll.Player,
            CombatRoll.Compose(
                "You are driven off, battered but breathing.", defeatFlavour),
            defeatFlavour));

        if (run is not null)
        {
            rolls.Add(CombatRoll.Note(
                encounter.Round, CombatRoll.Player,
                "The run ends here. What is left of the dungeon keeps it."));
        }
    }

    /// <summary>The run a fight belongs to, tracked, or null when it was taken at the tavern.</summary>
    private Task<DungeonRun?> LoadRunAsync(Encounter encounter, CancellationToken cancellationToken) =>
        encounter.DungeonRunId is not { } runId
            ? Task.FromResult<DungeonRun?>(null)
            : db.DungeonRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    /// <summary>
    /// Closes a run. Does nothing when there is none, so every ending can call it unconditionally.
    /// </summary>
    /// <remarks>
    /// Idempotent on an already finished run, because the three endings are reached from three
    /// different places and only one of them can be first. Costs no roll.
    /// </remarks>
    private static void EndRun(DungeonRun? run, DungeonRunStatus status)
    {
        if (run is null || run.IsOver)
        {
            return;
        }

        run.Status = status;
        run.EndedAt = DateTimeOffset.UtcNow;
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

        // Walking out of a room is walking out of the dungeon. Abandoned rather than Failed:
        // nothing beat the player, they left, and the two read differently in a history.
        var run = await LoadRunAsync(encounter, cancellationToken);

        EndRun(run, DungeonRunStatus.Abandoned);

        if (run is not null)
        {
            log.Add(CombatRoll.Note(
                encounter.Round, CombatRoll.Player, "You leave the dungeon the way you came in."));
        }

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

    /// <summary>Everything a winning round produced, carried out whole.</summary>
    /// <remarks>
    /// A record rather than a tuple because it grew a fourth member the day the dungeon reward
    /// stopped being discarded, and a positional tuple that long stops saying which slot is
    /// which at the call site.
    /// </remarks>
    private sealed record VictorySpoils(
        int Gold,
        InventoryItem? Drop,
        InventoryItem? ClearReward,
        IReadOnlyList<QuestAdvance> Advances);

    private async Task<VictorySpoils> ResolveVictoryAsync(
        Guid userId,
        Character character,
        Models.Rpg.CharacterSheet sheet,
        Encounter encounter,
        MonsterDefinition monster,
        DungeonRun? run,
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

        // Riding the boss's death rather than waiting to be claimed, so there is no window in
        // which a dungeon is cleared and its reward is not yet paid. Its dice append to this
        // round's tail, after the monster's own loot.
        var (clearGold, reward) = await ResolveDungeonClearAsync(
            userId, character, sheet, encounter, run, rolls, cancellationToken);

        // The kill counters ride the same transaction as the fight, the gold and the drop, so
        // the chronicle can never claim a win the encounters table does not also show. This is
        // the only place EncounterStatus.Won is set, so it is the complete kill site.
        // Deliberately the monster's own gold, not the clear bonus: the bonus was paid by the
        // dungeon and crediting it to the boss would make one Barrow Knight look like four.
        await bestiary.RecordKillAsync(
            userId, monster.Key, encounter.Round, gold, cancellationToken);

        // Quest progress rides the same transaction as the fight it came from.
        var advances = new List<QuestAdvance>();
        advances.AddRange(await quests.RecordAsync(
            userId, ObjectiveKind.DefeatMonster, monster.Key, 1, cancellationToken));
        advances.AddRange(await quests.RecordAsync(
            userId, ObjectiveKind.EarnGold, string.Empty, gold + clearGold, cancellationToken));

        // Counted together, because a clear round can hand over two items and an objective that
        // says "acquire three items" should see both of them.
        var acquired = (drop is null ? 0 : 1) + (reward is null ? 0 : 1);

        if (acquired > 0)
        {
            advances.AddRange(await quests.RecordAsync(
                userId, ObjectiveKind.AcquireItem, string.Empty, acquired, cancellationToken));
        }

        // Note what is not here: character.TotalXp is never touched.
        // The reward is carried out rather than dropped: it was counted towards AcquireItem two
        // lines above, and a response that reported one item while its own quest chip said two
        // left the second one discoverable only in the prose log.
        return new VictorySpoils(gold + clearGold, drop, reward, Deduplicate(advances));
    }

    /// <summary>
    /// Pays the clear reward when the room just won was the last one, in the same transaction.
    /// </summary>
    /// <remarks>
    /// Nothing to claim afterwards, so nothing can be lost between the kill and the payout, and
    /// there is no second endpoint that has to be defended against being called twice.
    /// <para>
    /// The count deliberately excludes this encounter and adds one, rather than relying on the
    /// row not having been flushed yet. <c>CountAsync</c> runs in the database and cannot see a
    /// tracked change; the day something ahead of this saves, counting naively would read the
    /// last room as the second to last and the run would never close.
    /// </para>
    /// </remarks>
    private async Task<(int Gold, InventoryItem? Reward)> ResolveDungeonClearAsync(
        Guid userId,
        Character character,
        Models.Rpg.CharacterSheet sheet,
        Encounter encounter,
        DungeonRun? run,
        List<CombatRoll> rolls,
        CancellationToken cancellationToken)
    {
        if (run is null || run.IsOver)
        {
            return (0, null);
        }

        // The rolled chain is the authority on how many rooms this run has, not the catalog. A
        // dungeon retuned from three rooms to four mid-run must not move the finish line of a run
        // that only ever rolled three. A chain that failed to deserialise reads as no rooms and
        // pays nothing, because a corrupt blob must not be a way to mint a clear reward.
        var rooms = DungeonRuns.Read(run).Count;

        var cleared = 1 + await db.Encounters.CountAsync(
            e => e.DungeonRunId == run.Id
                && e.Status == EncounterStatus.Won
                && e.Id != encounter.Id,
            cancellationToken);

        if (rooms == 0 || cleared < rooms)
        {
            return (0, null);
        }

        // Closed before the catalog is consulted, and deliberately. Whether the run is finished is
        // a fact about the rooms that were fought; what it pays is content. A dungeon retired from
        // the catalog mid-run would otherwise leave a run that has won every one of its rooms
        // stuck Active forever, holding the one encounter slot, with abandoning the only way out.
        EndRun(run, DungeonRunStatus.Cleared);

        if (run.Dungeon is not { } dungeon)
        {
            return (0, null);
        }

        run.GoldAwarded = dungeon.ClearGold;
        character.Gold += dungeon.ClearGold;

        rolls.Add(CombatRoll.Note(
            encounter.Round, CombatRoll.Player,
            $"{dungeon.Name} is cleared. You recover {dungeon.ClearGold} gold from the deepest room."));

        var reward = await loot.RollRewardAsync(
            userId,
            dungeon.RewardTable,
            dungeon.RewardFloor,
            sheet.Class?.Perk == ClassPerk.FavouredQuarry,
            cancellationToken);

        if (reward is not null)
        {
            rolls.Add(CombatRoll.Note(
                encounter.Round, CombatRoll.Player,
                $"The dungeon yields {RarityRules.Describe(reward.Rarity)} {reward.DisplayName}."));
        }

        return (dungeon.ClearGold, reward);
    }

    /// <summary>
    /// Runs the player's half of a round, whether that is a plain attack or an ability.
    /// </summary>
    /// <remarks>
    /// A dispatcher rather than one method that switches on the ability six separate times.
    /// Each shape of action owns its own dice in its own order, so adding a shape cannot
    /// quietly reorder the rolls of the shapes beside it.
    /// <para>
    /// Deliberately returns nothing. It used to report "this action forfeits the attack" and
    /// no caller ever read it: Healing Word forfeits the swing by never rolling one, not by
    /// the caller honouring a flag. A branch that trusted that return value would have done
    /// nothing at all.
    /// </para>
    /// </remarks>
    private void ResolvePlayerAction(
        Encounter encounter,
        Character character,
        Models.Rpg.CharacterSheet sheet,
        MonsterDefinition monster,
        PlayerAction action,
        List<StatusEffect> effects,
        List<CombatRoll> rolls)
    {
        // First, and on its own: a draught is not an ability, so it can never arrive alongside
        // one and never reaches the switch below.
        if (action.Item is { } draught)
        {
            ResolveConsumable(encounter, character, sheet, monster, draught, effects, rolls);

            return;
        }

        switch (action.Ability?.Kind)
        {
            case AbilityKind.HealingWord:
                ResolveHealingWord(encounter.Round, character, sheet, rolls);
                break;

            case AbilityKind.MagicMissile:
                ResolveMagicMissile(encounter, sheet, monster, rolls);
                break;

            default:
                ResolveWeaponAction(encounter, sheet, monster, action.Ability, effects, rolls);
                break;
        }
    }

    /// <summary>What the player spends their half of the round on. At most one of the two.</summary>
    private readonly record struct PlayerAction(ClassAbility? Ability, InventoryItem? Item);

    /// <summary>
    /// Finds the draught a round is about to spend, and refuses everything that is not one.
    /// </summary>
    /// <remarks>
    /// Scoped to the owner in the query itself, so another user's item id is indistinguishable
    /// from one that never existed and ids cannot be probed for. Succeeds with no item when the
    /// round is an ordinary attack, so the caller has one shape to handle rather than two.
    /// </remarks>
    private async Task<RpgResult<InventoryItem?>> TakeConsumableAsync(
        Guid userId,
        Guid? itemId,
        CancellationToken cancellationToken)
    {
        if (itemId is not { } id)
        {
            return RpgResult<InventoryItem?>.Success(null);
        }

        var item = await db.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken);

        if (item is null)
        {
            return RpgResult<InventoryItem?>.Fail(RpgFailure.NotFound, "No such item.");
        }

        if (item.Slot != ItemSlot.Consumable)
        {
            return RpgResult<InventoryItem?>.Fail(RpgFailure.ItemNotUsable, "That is worn, not used.");
        }

        if (item.Quantity < 1)
        {
            return RpgResult<InventoryItem?>.Fail(RpgFailure.NoneLeft, "You have none of those left.");
        }

        // A key retired from the catalog leaves the row sellable and salvageable but not usable:
        // there is no longer anything that says what using it would do.
        if (ItemCatalog.Find(item.ItemKey)?.Use is null)
        {
            return RpgResult<InventoryItem?>.Fail(
                RpgFailure.ItemNotUsable, "That does nothing when used.");
        }

        return RpgResult<InventoryItem?>.Success(item);
    }

    /// <summary>
    /// Drinks, oils or throws one unit of a consumable, and spends the player's half of the
    /// round doing it.
    /// </summary>
    /// <remarks>
    /// Zero dice, deliberately. The heal and the magnitude are both fixed by the catalog and the
    /// rolled rarity, so nothing here reaches for the roller: a potion that healed 1d8 would drag
    /// consumables into the blast radius of every hard-coded SequenceDiceRoller script in the
    /// suite for no gain, and a value the player can read before deciding is a better decision
    /// than a gamble.
    /// <para>
    /// Two lines, the shape every other effect in the round takes: what the player did, and what
    /// it changed. The second carries the flavour, so a draught reads like the rest of the log
    /// rather than like a receipt.
    /// </para>
    /// </remarks>
    private void ResolveConsumable(
        Encounter encounter,
        Character character,
        Models.Rpg.CharacterSheet sheet,
        MonsterDefinition monster,
        InventoryItem item,
        List<StatusEffect> effects,
        List<CombatRoll> rolls)
    {
        // Both non-null: TakeConsumableAsync refused the round otherwise.
        var definition = ItemCatalog.Find(item.ItemKey)!;
        var use = definition.Use!.At(item.Rarity);

        InventoryStack.ConsumeOne(db, item);

        rolls.Add(CombatRoll.Note(
            encounter.Round, CombatRoll.Player, $"You reach for the {definition.Name}."));

        var clauses = new List<string>(2);

        if (use.Heal > 0)
        {
            var before = character.CurrentHitPoints;
            character.CurrentHitPoints = Math.Min(sheet.MaxHitPoints, before + use.Heal);
            var restored = character.CurrentHitPoints - before;

            // A draught drunk at full health is still drunk. Saying so is kinder than a line
            // claiming a heal that did not happen.
            clauses.Add(restored == 0
                ? "There was nothing left in you to mend."
                : $"It restores {restored} hit points. You are on {character.CurrentHitPoints}.");
        }

        if (use.Kind is { } kind)
        {
            var incoming = new StatusEffect(kind, use.Target, use.Rounds, use.Magnitude, definition.Key);

            StatusEffects.Apply(effects, incoming);
            clauses.Add(DescribeEffect(incoming, monster));
        }

        NoteWithFlavour(
            encounter, FlavourMoment.EffectApplied, CombatRoll.Player,
            string.Join(" ", clauses), monster, rolls);
    }

    /// <summary>
    /// Moves a boss into every phase its hit points have fallen past, once each.
    /// </summary>
    /// <remarks>
    /// Every phase crossed, not only the last one reached. One critical can take a dragon past
    /// two thresholds at once, and skipping the middle one would make a big hit the way to dodge
    /// the mechanic it was supposed to trigger.
    /// <para>
    /// Compared against <see cref="Encounter.Phase"/> rather than against the hit points alone,
    /// because that field is a high-water mark. A regenerating boss crosses back and forth over
    /// its own threshold, and re-entering would re-apply its entry effects every round for the
    /// rest of the fight.
    /// </para>
    /// <para>
    /// Costs no die, and is deliberately not run at <see cref="StartAsync"/>: a monster opens on
    /// full hit points, so PhaseAt is always zero there and nothing is appended to the opening
    /// log. A tick that takes a boss past a threshold at the end of a round is therefore read at
    /// the start of the next exchange rather than lost.
    /// </para>
    /// </remarks>
    private static void ResolvePhaseChange(
        Encounter encounter,
        MonsterDefinition monster,
        List<StatusEffect> effects,
        List<CombatRoll> rolls)
    {
        var reached = monster.PhaseAt(encounter.MonsterHitPoints);

        if (reached <= encounter.Phase)
        {
            return;
        }

        for (var phase = encounter.Phase + 1; phase <= reached; phase++)
        {
            if (monster.PhaseDefinition(phase) is not { } definition)
            {
                continue;
            }

            NoteWithFlavour(
                encounter, FlavourMoment.PhaseChange, CombatRoll.Monster,
                definition.Line, monster, rolls);

            foreach (var effect in definition.OnEntry)
            {
                StatusEffects.Apply(effects, effect);

                rolls.Add(CombatRoll.Note(
                    encounter.Round, CombatRoll.Monster, DescribeEffect(effect, monster)));
            }
        }

        encounter.Phase = reached;
    }

    /// <summary>
    /// What an effect landing reads as.
    /// </summary>
    /// <remarks>
    /// Written per kind and per side rather than from one template, because "the Wyvern cannot
    /// find a swing" and "you cannot find a swing" are different sentences, and a template that
    /// produced both from one string would produce neither well.
    /// </remarks>
    private static string DescribeEffect(StatusEffect effect, MonsterDefinition monster) =>
        (effect.Kind, effect.Target) switch
        {
            (EffectKind.Weakened, EffectTarget.Player) => "You have lost the line of the fight.",
            (EffectKind.Weakened, EffectTarget.Monster) => $"{monster.Name} cannot find a clear swing.",
            (EffectKind.Empowered, EffectTarget.Player) => $"Your swing carries {effect.Magnitude} more.",
            (EffectKind.Empowered, EffectTarget.Monster) => $"{monster.Name} hits {effect.Magnitude} harder.",
            (EffectKind.Guarded, EffectTarget.Player) => $"You are {effect.Magnitude} harder to hit.",
            (EffectKind.Guarded, EffectTarget.Monster) => $"{monster.Name} is {effect.Magnitude} harder to hit.",
            (EffectKind.Poisoned, EffectTarget.Player) => $"Poison works on you for {effect.Magnitude} a round.",
            (EffectKind.Poisoned, EffectTarget.Monster) => $"{monster.Name} takes {effect.Magnitude} a round.",
            (EffectKind.Regenerating, EffectTarget.Player) => $"You knit back {effect.Magnitude} a round.",
            _ => $"{monster.Name} knits back {effect.Magnitude} a round."
        };

    /// <summary>
    /// Heals instead of attacking. Forfeits the swing by never rolling one, which is also why
    /// a Cleric spending it cannot burn the Blessing reroll that round.
    /// </summary>
    private void ResolveHealingWord(
        int round,
        Character character,
        Models.Rpg.CharacterSheet sheet,
        List<CombatRoll> rolls)
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
    }

    /// <summary>
    /// Unerring force. No attack roll at all, so it can never critical and never doubles.
    /// </summary>
    private void ResolveMagicMissile(
        Encounter encounter,
        Models.Rpg.CharacterSheet sheet,
        MonsterDefinition monster,
        List<CombatRoll> rolls)
    {
        var damage = D20.Damage(
            roller,
            DiceExpression.Parse("3d4"),
            [new RollModifier("INT", sheet.EffectiveScores.Modifier(Ability.Intelligence))]);

        encounter.MonsterHitPoints = Math.Max(0, encounter.MonsterHitPoints - damage.Total);

        rolls.Add(CombatRoll.Note(
            encounter.Round, CombatRoll.Player, "Magic Missile streaks out. It cannot miss."));

        rolls.Add(CombatRoll.From(
            encounter.Round, CombatRoll.Player, damage,
            $"{damage.Total} force damage. {monster.Name} has {encounter.MonsterHitPoints} hit points left."));
    }

    /// <summary>
    /// Everything an ability changes about a swing, settled before any die leaves the cup.
    /// </summary>
    /// <param name="DoubleDiceOnHit">
    /// Doubles the damage dice on a plain hit, as Power Attack does. A critical doubles them
    /// too, and the two do not compound.
    /// </param>
    /// <param name="ArmourClassBonus">
    /// Added to the number the swing has to beat, from a Guarded effect on the monster.
    /// </param>
    private readonly record struct AttackShape(
        RollMode Mode,
        int CriticalOn,
        IReadOnlyList<RollModifier> AttackModifiers,
        DiceExpression Damage,
        IReadOnlyList<RollModifier> DamageModifiers,
        bool DoubleDiceOnHit,
        int ArmourClassBonus);

    /// <summary>Reads the swing an ability and the effects in force ask for.</summary>
    /// <remarks>
    /// Pure on purpose, in the one sense that matters here: it never touches the roller. Every
    /// SequenceDiceRoller script in the suite hard-codes the order a round consumes its dice, so
    /// a shape that rolled while being computed would insert a die before the attack roll and
    /// silently change what those scripts assert. It does not spend the effects it reads either;
    /// <see cref="ResolveWeaponAction"/> does that, and the two lists move together.
    /// </remarks>
    private static AttackShape ShapeFor(
        ClassAbility? ability,
        Models.Rpg.CharacterSheet sheet,
        IReadOnlyList<StatusEffect> effects)
    {
        var weakened = StatusEffects.Find(effects, EffectKind.Weakened, EffectTarget.Player) is not null;
        var empowered = StatusEffects.MagnitudeOf(effects, EffectKind.Empowered, EffectTarget.Player);
        var advantage = ability?.Kind is AbilityKind.SneakStrike or AbilityKind.AimedShot;

        // Empowered is a labelled modifier on the swing and on its damage, never another die.
        IReadOnlyList<RollModifier> empowerment =
            empowered == 0 ? [] : [new RollModifier("empowered", empowered)];

        return new(
            // Advantage and disadvantage cancel to Normal rather than compounding. Ruled that
            // way because it is what the two terms mean, and because compounding would make one
            // Aimed Shot by a Weakened Ranger cost four d20s.
            Mode: (advantage, weakened) switch
            {
                (true, false) => RollMode.Advantage,
                (false, true) => RollMode.Disadvantage,
                _ => RollMode.Normal
            },

            // Aimed Shot lowers the threshold to 19, it does not set it to 19. Assigning it flat
            // would make a Keen weapon or a completed Nightfall Vigil worse on the one attack the
            // player spent a use on.
            CriticalOn: ability?.Kind == AbilityKind.AimedShot
                ? Math.Min(19, sheet.CriticalOn)
                : sheet.CriticalOn,

            AttackModifiers: ability?.Kind == AbilityKind.PowerAttack
                ?
                [
                    .. sheet.AttackModifiers,
                    new RollModifier("power attack", ClassAbilities.PowerAttackPenalty),
                    .. empowerment
                ]
                : [.. sheet.AttackModifiers, .. empowerment],

            Damage: ability?.Kind == AbilityKind.ViciousMockery
                ? DiceExpression.Parse("1d6")
                : sheet.DamageExpression,

            DamageModifiers: ability?.Kind == AbilityKind.ViciousMockery
                ? [new RollModifier("CHA", sheet.EffectiveScores.Modifier(Ability.Charisma)), .. empowerment]
                : [.. sheet.DamageModifiers, .. empowerment],

            // Power Attack doubles the dice on any hit, the way a critical does.
            DoubleDiceOnHit: ability?.Kind == AbilityKind.PowerAttack,

            ArmourClassBonus: StatusEffects.MagnitudeOf(
                effects, EffectKind.Guarded, EffectTarget.Monster));
    }

    /// <summary>
    /// The shared swing: roll to hit, then roll damage. Every action that keeps an attack roll
    /// arrives here, differing only in the <see cref="AttackShape"/> it brings.
    /// </summary>
    private void ResolveWeaponAction(
        Encounter encounter,
        Models.Rpg.CharacterSheet sheet,
        MonsterDefinition monster,
        ClassAbility? ability,
        List<StatusEffect> effects,
        List<CombatRoll> rolls)
    {
        var round = encounter.Round;
        var shape = ShapeFor(ability, sheet, effects);

        if (ability is not null)
        {
            rolls.Add(CombatRoll.Note(round, CombatRoll.Player, $"{ability.Name}."));
        }

        // Spent before the roll, at the one site that applies them, and these three pair with
        // the three ShapeFor reads above. An effect read here and spent somewhere else would
        // last a swing longer than it says it does.
        StatusEffects.Spend(effects, EffectKind.Weakened, EffectTarget.Player);
        StatusEffects.Spend(effects, EffectKind.Empowered, EffectTarget.Player);
        StatusEffects.Spend(effects, EffectKind.Guarded, EffectTarget.Monster);

        var attack = RollAttackWithBlessing(
            encounter,
            sheet,
            shape.AttackModifiers,
            monster.ArmourClass + shape.ArmourClassBonus,
            shape.CriticalOn,
            shape.Mode);

        var attackFlavour = Flavour(MomentFor(attack), encounter.Id, round, monster);

        rolls.Add(CombatRoll.From(
            round, CombatRoll.Player, attack,
            CombatRoll.Compose(DescribeAttack(attack, monster.Name), attackFlavour),
            attackFlavour));

        if (attack.Outcome != RollOutcome.Hit)
        {
            return;
        }

        var hit = D20.Damage(
            roller, shape.Damage, shape.DamageModifiers, attack.Critical || shape.DoubleDiceOnHit);

        encounter.MonsterHitPoints = Math.Max(0, encounter.MonsterHitPoints - hit.Total);

        rolls.Add(CombatRoll.From(
            round, CombatRoll.Player, hit,
            $"{hit.Total} damage. {monster.Name} has {encounter.MonsterHitPoints} hit points left."));

        // Gated on the monster surviving, so the remark is never spent on a corpse. The effect
        // is consumed by the counter-attack further down this same round, which is what mocking
        // something should feel like.
        if (ability?.Kind == AbilityKind.ViciousMockery && encounter.MonsterHitPoints > 0)
        {
            ApplyEffect(
                encounter,
                effects,
                new StatusEffect(
                    EffectKind.Weakened,
                    EffectTarget.Monster,
                    ClassAbilities.MockeryRounds,
                    Magnitude: 0,
                    ClassAbilities.ViciousMockery),
                CombatRoll.Player,
                $"{monster.Name} is rattled. Its next swing goes wide.",
                monster,
                rolls);
        }
    }

    /// <summary>Puts an effect on the board and says so. Costs no roll.</summary>
    /// <remarks>
    /// The magnitude arrives already fixed by whatever applied it and is never rolled here, and
    /// the line comes from the flavour catalog, which is a hash rather than a die. Those two
    /// together are why an effect landing cannot move a single dice script in the suite.
    /// <para>
    /// Emitted as a note, never as a damage or attack roll. Tests reach for the first roll of a
    /// given kind in a round, and an effect line wearing the wrong kind would hijack them.
    /// </para>
    /// </remarks>
    private static void ApplyEffect(
        Encounter encounter,
        List<StatusEffect> effects,
        StatusEffect incoming,
        string actor,
        string clause,
        MonsterDefinition? monster,
        List<CombatRoll> rolls)
    {
        StatusEffects.Apply(effects, incoming);

        NoteWithFlavour(encounter, FlavourMoment.EffectApplied, actor, clause, monster, rolls);
    }

    /// <summary>A mechanical clause with its narrative tail, recorded as one line.</summary>
    /// <remarks>
    /// The flavour comes from the catalog, which is a hash of the encounter, the round and the
    /// moment rather than a die. That is what lets a phase change and a draught narrate
    /// themselves without moving a single dice script in the suite.
    /// </remarks>
    private static void NoteWithFlavour(
        Encounter encounter,
        FlavourMoment moment,
        string actor,
        string clause,
        MonsterDefinition? monster,
        List<CombatRoll> rolls)
    {
        var flavour = Flavour(moment, encounter.Id, encounter.Round, monster);

        rolls.Add(CombatRoll.Note(
            encounter.Round, actor, CombatRoll.Compose(clause, flavour), flavour));
    }

    /// <summary>Applies the Cleric's Blessing, which rerolls the first natural 1 of a fight.</summary>
    /// <remarks>
    /// Identified by the perk on the sheet the round already built, not by the class key. The
    /// perk is what the rule is about, so a second class given Blessing gets it and a renamed
    /// Cleric key cannot switch it off silently.
    /// <para>
    /// The reroll re-enters <see cref="D20.Attack"/> with the same mode, so a Weakened Cleric
    /// spends two more d20s on it rather than one. That is the only place in the round where
    /// one effect and one perk multiply, and it is scripted in a test rather than reasoned about.
    /// </para>
    /// </remarks>
    private RollResult RollAttackWithBlessing(
        Encounter encounter,
        Models.Rpg.CharacterSheet sheet,
        IReadOnlyList<RollModifier> modifiers,
        int armourClass,
        int criticalOn,
        RollMode mode = RollMode.Normal)
    {
        var result = D20.Attack(roller, modifiers, armourClass, mode, criticalOn);

        if (sheet.Class?.Perk != ClassPerk.Blessing || encounter.BlessingUsed || !result.CriticalFailure)
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
