using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// Flavour text narrates a fight. It must never cost a die to do it.
/// </summary>
/// <remarks>
/// Every SequenceDiceRoller script in this suite hard-codes how many rolls a round takes and
/// in what order they arrive. A flavour line drawn from the injected roller would shift every
/// later value in every one of those scripts, so dozens of tests would keep passing while
/// asserting something other than what they were written to assert. That failure is silent by
/// construction, which is why the roll stream is pinned here exactly rather than approximately.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class FlavourTests(PostgresFixture postgres)
{
    private sealed record Harness(TodoDbContext Db, CombatService Combat, Guid UserId);

    private async Task<Harness> ArrangeAsync(IDiceRoller roller, string classKey = ClassCatalog.Fighter)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|hero");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, roller);
        var quests = new QuestService(db, loot);
        var adventurer = new AdventurerService(db, sheets, loot);
        var combat = new CombatService(db, roller, sheets, loot, quests);

        // Class selection rolls nothing: starting gear is granted, never rolled for.
        await adventurer.ChooseClassAsync(user.Id, classKey, TestContext.Current.CancellationToken);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Stamina = 20;
        await db.SaveChangesAsync();

        return new Harness(db, combat, user.Id);
    }

    // -------------------------------------------------------------------------
    // The one that matters most.
    // -------------------------------------------------------------------------

    /// <summary>
    /// A Fighter kills a giant rat in one round. The dice that fight is entitled to are the
    /// attack, the damage, the gold and the drop chance, in that order, and nothing else.
    /// </summary>
    [Fact]
    public async Task Flavour_selection_consumes_no_dice_roll_on_a_winning_round()
    {
        // 15 hits (AC 10), 8 on the longsword kills a 7 hit point rat, 3 rolls the gold,
        // 99 fails the 15 in 100 drop chance so no loot dice follow.
        var roller = new RecordingDiceRoller(new SequenceDiceRoller(15, 8, 3, 99));
        var harness = await ArrangeAsync(roller);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        Assert.True(start.Ok);

        // Opening the fight narrates twice and rolls nothing: a Fighter has no Arcane
        // Recovery roll, so any die here would have to have come from the narration.
        Assert.Empty(roller.Sides);

        var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);

        Assert.True(attack.Ok);
        Assert.Equal(EncounterStatus.Won, attack.Value!.Encounter.Status);

        // d20 attack, d8 damage, d5 gold span, d100 drop chance. Nothing narrative in it.
        Assert.Equal([20, 8, 5, 100], roller.Sides);
    }

    /// <summary>
    /// The losing half of the round, where four separate moments are narrated: the player's
    /// fumble, the monster's hit, its damage line and the withdrawal.
    /// </summary>
    [Fact]
    public async Task Flavour_selection_consumes_no_dice_roll_on_a_missed_round_or_a_flight()
    {
        // 1 is a natural fumble, so no damage die follows. 15 lands the rat's answer against
        // armour class 14, and 4 is the damage it deals.
        var roller = new RecordingDiceRoller(new SequenceDiceRoller(1, 15, 4));
        var harness = await ArrangeAsync(roller);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);

        Assert.True(attack.Ok);
        Assert.Equal(EncounterStatus.Active, attack.Value!.Encounter.Status);
        Assert.Equal([20, 20, 4], roller.Sides);

        var fled = await harness.Combat.FleeAsync(harness.UserId, start.Value.Id, default);

        // Fleeing narrates and settles the encounter. It has never rolled a die and still
        // must not, even though it is now the only place the Flee moment is reached.
        Assert.True(fled.Ok);
        Assert.Equal([20, 20, 4], roller.Sides);
    }

    /// <summary>
    /// Defeat is the third ending, and the only one that narrates from inside the monster's
    /// half of the round.
    /// </summary>
    [Fact]
    public async Task Flavour_selection_consumes_no_dice_roll_when_the_fight_is_lost()
    {
        var roller = new RecordingDiceRoller(new SequenceDiceRoller(1, 15, 4));
        var harness = await ArrangeAsync(roller);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.CurrentHitPoints = 1;
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
        await harness.Db.SaveChangesAsync();

        var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);

        Assert.Equal(EncounterStatus.Lost, attack.Value!.Encounter.Status);
        Assert.Equal([20, 20, 4], roller.Sides);
    }

    /// <summary>
    /// The count as well as the shape. A line drawn from the roller would most likely take a
    /// die sized to the number of lines available, which is neither a 20 nor a 100 and would
    /// show up here first.
    /// </summary>
    [Fact]
    public async Task A_whole_fight_spends_exactly_the_dice_its_rules_call_for()
    {
        // Two rounds of trading misses, then a hit that kills, then the two loot dice.
        var script = new SequenceDiceRoller(1, 1, 1, 1, 10, 8, 1, 100);
        var roller = new RecordingDiceRoller(script);
        var harness = await ArrangeAsync(roller);

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);

        for (var round = 0; round < 3; round++)
        {
            var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);
            Assert.True(attack.Ok);
        }

        Assert.Equal(8, script.RollCount);
        Assert.Equal([20, 20, 20, 20, 20, 8, 5, 100], roller.Sides);
    }

    // -------------------------------------------------------------------------
    // Flavour actually arrives, and always says the same thing.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Pinning the roll stream is only half the guarantee. If narration were quietly dropped
    /// the stream would still be right, so the lines are checked against the catalog too, and
    /// against the exact id and round they claim to be keyed off.
    /// </summary>
    [Fact]
    public async Task Every_moment_of_a_fight_carries_the_line_the_catalog_chose_for_it()
    {
        var harness = await ArrangeAsync(new RecordingDiceRoller(new SequenceDiceRoller(15, 8, 3, 99)));

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        var encounterId = start.Value!.Id;
        var rat = MonsterCatalog.Find(MonsterCatalog.GiantRat)!;

        var opening = CombatService.ReadLog(start.Value);
        Assert.Contains(
            opening,
            r => r.Text == FlavourCatalog.Pick(FlavourMoment.Opening, encounterId, 0, rat.Name));

        var attack = await harness.Combat.AttackAsync(harness.UserId, encounterId, default);
        var log = CombatService.ReadLog(attack.Value!.Encounter);

        Assert.Contains(
            log,
            r => r.Text.EndsWith(
                FlavourCatalog.Pick(FlavourMoment.PlayerHit, encounterId, 1, rat.Name),
                StringComparison.Ordinal));

        Assert.Contains(
            log,
            r => r.Text.EndsWith(
                FlavourCatalog.Pick(FlavourMoment.Kill, encounterId, 1, rat.Name),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// A reloaded fight has to read exactly as it did the first time, which is the reason
    /// selection is a hash of the id and the round rather than anything ambient.
    /// </summary>
    [Fact]
    public async Task A_fight_narrates_itself_identically_after_a_reload()
    {
        var harness = await ArrangeAsync(new RecordingDiceRoller(new SequenceDiceRoller(1, 15, 4)));

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        var attack = await harness.Combat.AttackAsync(harness.UserId, start.Value!.Id, default);

        var written = CombatService.ReadLog(attack.Value!.Encounter).Select(r => r.Text).ToList();

        await using var reloaded = postgres.CreateContext();
        var fromDatabase = await reloaded.Encounters.AsNoTracking()
            .SingleAsync(e => e.Id == start.Value.Id);

        Assert.Equal(written, CombatService.ReadLog(fromDatabase).Select(r => r.Text).ToList());
    }

    /// <summary>
    /// Where the mechanical clause ends and the narration begins is stated by the server, not
    /// worked out by the client.
    /// </summary>
    /// <remarks>
    /// The client used to find the seam by cutting at the last sentence break, which is wrong
    /// for every mechanical line that already has two sentences: "6 damage. Goblin has 4 hit
    /// points left." handed the remaining hit points, the one number a player is tracking, to
    /// the faint style kept for decoration. Only this side knows whether a flavour line was
    /// appended, so both halves are asserted here: the line that has one says so, and the
    /// line that has none says that too.
    /// </remarks>
    [Fact]
    public async Task A_line_carries_the_flavour_it_was_given_and_a_mechanical_line_carries_none()
    {
        var harness = await ArrangeAsync(new RecordingDiceRoller(new SequenceDiceRoller(15, 8, 3, 99)));

        var start = await harness.Combat.StartAsync(harness.UserId, MonsterCatalog.GiantRat, default);
        var encounterId = start.Value!.Id;
        var rat = MonsterCatalog.Find(MonsterCatalog.GiantRat)!;

        // The opening narration is a line of its own, and all of it is flavour.
        var opening = CombatService.ReadLog(start.Value)[^1];
        Assert.Equal(
            FlavourCatalog.Pick(FlavourMoment.Opening, encounterId, 0, rat.Name),
            opening.Flavour);
        Assert.Equal(opening.Text, opening.Flavour);

        var attack = await harness.Combat.AttackAsync(harness.UserId, encounterId, default);
        var log = CombatService.ReadLog(attack.Value!.Encounter);

        var swing = log.First(r => r.Round == 1 && r.Kind == "attack");
        var swingFlavour = FlavourCatalog.Pick(FlavourMoment.PlayerHit, encounterId, 1, rat.Name);

        Assert.Equal(swingFlavour, swing.Flavour);
        Assert.Equal($"You hit {rat.Name}. {swingFlavour}", swing.Text);

        // Two mechanical sentences and no narration anywhere in it.
        var damage = log.First(r => r.Round == 1 && r.Kind == "damage");

        Assert.Null(damage.Flavour);
        Assert.EndsWith("hit points left.", damage.Text, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // The catalog itself.
    // -------------------------------------------------------------------------

    [Fact]
    public void Selection_is_a_pure_function_of_its_arguments()
    {
        var id = Guid.Parse("0192f2c0-0000-7000-8000-000000000001");

        foreach (var moment in Enum.GetValues<FlavourMoment>())
        {
            var first = FlavourCatalog.Pick(moment, id, 3, "Goblin");

            // Called again in the same process, and again as a fresh evaluation. Anything
            // ambient, a die, a clock or a randomised hash code, breaks one of the two.
            Assert.Equal(first, FlavourCatalog.Pick(moment, id, 3, "Goblin"));
            Assert.Contains(first.Replace("Goblin", FlavourCatalog.MonsterToken, StringComparison.Ordinal),
                FlavourCatalog.For(moment));
        }
    }

    /// <summary>
    /// A hash that ignored the round, or that folded in too little of its input, would collapse
    /// on to a handful of lines and every fight would read the same. Spread is the thing being
    /// bought by not using a die, so it is the thing worth asserting.
    /// </summary>
    [Fact]
    public void Selection_spreads_across_the_lines_available()
    {
        var id = Guid.Parse("0192f2c0-0000-7000-8000-000000000002");

        var used = Enumerable.Range(0, 200)
            .Select(round => FlavourCatalog.Pick(FlavourMoment.PlayerHit, id, round, "Goblin"))
            .Distinct(StringComparer.Ordinal)
            .Count();

        Assert.True(used >= 15, $"200 rounds only ever reached {used} of the available lines");
    }

    [Fact]
    public void Every_moment_has_lines_to_choose_from() =>
        Assert.All(Enum.GetValues<FlavourMoment>(), m => Assert.NotEmpty(FlavourCatalog.For(m)));

    /// <summary>
    /// House style, asserted rather than trusted. A line is game text and the same rules apply
    /// to it as to anything else in the repository.
    /// </summary>
    [Fact]
    public void Every_line_is_a_single_plain_sentence()
    {
        // Built from their codepoints rather than typed, so this file does not contain the
        // characters it is here to ban.
        var emDash = char.ConvertFromUtf32(0x2014);
        var enDash = char.ConvertFromUtf32(0x2013);
        var curlyApostrophe = char.ConvertFromUtf32(0x2019);

        foreach (var moment in Enum.GetValues<FlavourMoment>())
        {
            Assert.All(FlavourCatalog.For(moment), line =>
            {
                Assert.EndsWith(".", line, StringComparison.Ordinal);
                Assert.True(line.Length <= 90, line);

                // The dashes are named by codepoint so this assertion does not itself break
                // the rule it exists to enforce. The rest would each be the first of their
                // kind in the repository's prose.
                Assert.DoesNotContain(emDash, line, StringComparison.Ordinal);
                Assert.DoesNotContain(enDash, line, StringComparison.Ordinal);
                Assert.DoesNotContain(curlyApostrophe, line, StringComparison.Ordinal);
                Assert.DoesNotContain("!", line, StringComparison.Ordinal);
                Assert.DoesNotContain("?", line, StringComparison.Ordinal);
                Assert.DoesNotContain(";", line, StringComparison.Ordinal);
            });
        }
    }

    /// <summary>
    /// Any line has to compose with any monster, because selection is a hash and cannot be
    /// steered. A stray token would reach the player as literal braces.
    /// </summary>
    [Fact]
    public void The_monster_name_is_the_only_token_a_line_may_carry()
    {
        foreach (var moment in Enum.GetValues<FlavourMoment>())
        {
            Assert.All(FlavourCatalog.For(moment), line =>
                Assert.DoesNotContain(
                    "{",
                    line.Replace(FlavourCatalog.MonsterToken, string.Empty, StringComparison.Ordinal),
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public void No_moment_repeats_a_line()
    {
        foreach (var moment in Enum.GetValues<FlavourMoment>())
        {
            var lines = FlavourCatalog.For(moment);

            Assert.Equal(lines.Count, lines.Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>
    /// A line is printed inside a mechanical clause CombatService writes, and it may not
    /// repeat that clause's verb.
    /// </summary>
    /// <remarks>
    /// The clause is not visible from the catalog, which is how three lines came to be
    /// written against a prefix they always ship behind: "Goblin misses. The Goblin commits
    /// too early and misses." is one log entry saying the same thing twice, and at one draw
    /// in twenty two it is a routine sight rather than a curiosity. The words below are the
    /// verbs CombatService already spends; changing a clause there means changing one here.
    /// </remarks>
    [Theory]
    [InlineData(FlavourMoment.Opening, "approaches")]
    [InlineData(FlavourMoment.PlayerHit, "you hit")]
    [InlineData(FlavourMoment.PlayerMiss, "you miss")]
    [InlineData(FlavourMoment.PlayerCritical, "critical hit")]
    [InlineData(FlavourMoment.PlayerFumble, "fumble")]
    [InlineData(FlavourMoment.MonsterHit, "connects")]
    [InlineData(FlavourMoment.MonsterCritical, "connects")]
    [InlineData(FlavourMoment.MonsterMiss, "misses")]
    [InlineData(FlavourMoment.Kill, "falls")]
    [InlineData(FlavourMoment.Defeat, "driven off")]
    [InlineData(FlavourMoment.Flee, "withdraw")]
    // Vicious Mockery's clause is the only one an effect line ships behind today. It is two
    // sentences, so both of its verbs are named.
    [InlineData(FlavourMoment.EffectApplied, "rattled")]
    [InlineData(FlavourMoment.EffectApplied, "goes wide")]
    public void No_line_repeats_the_verb_of_the_clause_it_is_appended_to(
        FlavourMoment moment,
        string verb) =>
        Assert.All(FlavourCatalog.For(moment), line =>
            Assert.DoesNotContain(verb, line, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The three endings each state an outcome before the flavour is appended, and the
    /// flavour may not take it back in the same sentence.
    /// </summary>
    /// <remarks>
    /// Kill prints "{monster} falls." and had a line saying the monster kept its footing.
    /// Defeat prints "You are driven off, battered but breathing.", and CombatService floors
    /// the player at one hit point rather than killing them, which half the Defeat pool
    /// contradicted by putting the player on the floor or unconscious. The phrases below are
    /// the ones that were actually written; the list cannot be exhaustive, and a new line
    /// that says the fight ended some other way than the clause says still needs reading.
    /// </remarks>
    [Theory]
    [InlineData(FlavourMoment.Kill, "keeps its footing")]
    [InlineData(FlavourMoment.Kill, "stays upright")]
    [InlineData(FlavourMoment.Kill, "still standing")]
    [InlineData(FlavourMoment.Kill, "keeps coming")]
    [InlineData(FlavourMoment.Kill, "gets up")]
    [InlineData(FlavourMoment.Defeat, "goes out")]
    [InlineData(FlavourMoment.Defeat, "floor arrives")]
    [InlineData(FlavourMoment.Defeat, "you go down")]
    [InlineData(FlavourMoment.Defeat, "sit down")]
    [InlineData(FlavourMoment.Defeat, "one knee")]
    [InlineData(FlavourMoment.Defeat, "measure your length")]
    [InlineData(FlavourMoment.Defeat, "takes your weight")]
    [InlineData(FlavourMoment.Defeat, "above you")]
    public void No_ending_line_contradicts_the_ending_it_narrates(FlavourMoment moment, string phrase) =>
        Assert.All(FlavourCatalog.For(moment), line =>
            Assert.DoesNotContain(phrase, line, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A line has to read at round one, where nothing has happened yet. Selection is a hash,
    /// so a line that points at an earlier round is reachable as the first thing a player
    /// sees: a first round critical printed "It lands where the last one landed." with no
    /// earlier landed blow anywhere in the log.
    /// </summary>
    [Theory]
    [InlineData("the last one")]
    [InlineData("the same thing again")]
    [InlineData("as before")]
    [InlineData("once more")]
    [InlineData("like the last")]
    public void No_line_points_back_at_a_round_that_may_not_have_happened(string phrase)
    {
        foreach (var moment in Enum.GetValues<FlavourMoment>())
        {
            Assert.All(FlavourCatalog.For(moment), line =>
                Assert.DoesNotContain(phrase, line, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// A retired monster key leaves <c>Encounter.Monster</c> null, and the flee line is the
    /// one place that is read after the catalog may have moved on.
    /// </summary>
    [Fact]
    public void A_line_still_composes_when_the_monster_has_no_name() =>
        Assert.DoesNotContain(
            FlavourCatalog.MonsterToken,
            FlavourCatalog.Pick(FlavourMoment.Flee, Guid.CreateVersion7(), 0, "creature"));
}
