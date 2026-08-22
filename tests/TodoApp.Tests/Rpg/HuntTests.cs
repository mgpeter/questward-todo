using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Dice;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// What a contract is, before any of it reaches a database or a die.
/// </summary>
public class HuntRuleTests
{
    /// <summary>
    /// DEC-013 in one assertion: a backlog is a carrot, and the carrot has a size.
    /// </summary>
    /// <remarks>
    /// The floor is the important half. A multiplier that could fall below 100 would be a debuff
    /// wearing a percentage sign, which is the exact rebuild a previous design agent was warned
    /// off. The ceiling is the other half: without it elapsed time is a free unlimited resource,
    /// and leaving one Epic task to rot outearns clearing the list.
    /// </remarks>
    [Fact]
    public void A_bounty_climbs_for_a_month_and_then_stops()
    {
        Assert.Equal(100, BountyRules.BountyPercent(0));
        Assert.Equal(103, BountyRules.BountyPercent(1));
        Assert.Equal(150, BountyRules.BountyPercent(15));
        Assert.Equal(196, BountyRules.BountyPercent(29));
        Assert.Equal(200, BountyRules.BountyPercent(30));
        Assert.Equal(200, BountyRules.BountyPercent(90));
        Assert.Equal(200, BountyRules.BountyPercent(400));

        // Nothing subtracts, at any age, including ages the calendar cannot produce.
        for (var days = -10; days <= 4000; days++)
        {
            var percent = BountyRules.BountyPercent(days);

            Assert.True(percent >= BountyRules.BasePercent, $"{days} days pays a penalty.");
            Assert.True(percent <= BountyRules.MaxPercent, $"{days} days pays past the cap.");
        }

        // Monotonic, so no single day of waiting ever costs the player money.
        for (var days = 0; days < 400; days++)
        {
            Assert.True(BountyRules.BountyPercent(days + 1) >= BountyRules.BountyPercent(days));
        }
    }

    /// <summary>
    /// The bounty reaches gold and nothing else, held against one fixed archetype.
    /// </summary>
    /// <remarks>
    /// Fixed on purpose: the promotion at thirty days moves hit points and armour, and comparing
    /// a promoted block against an unpromoted one would confuse "age changed the shape" with
    /// "age changed the numbers the shape was built from".
    /// </remarks>
    [Fact]
    public void A_bounty_multiplies_gold_and_reaches_nothing_else()
    {
        var drudge = HuntArchetypeCatalog.Find(HuntArchetypeCatalog.Drudge)!;

        var fresh = HuntRules.StatBlock(drudge, level: 6, daysOverdue: 0, subtaskCount: 0);
        var ancient = HuntRules.StatBlock(drudge, level: 6, daysOverdue: 400, subtaskCount: 0);

        Assert.Equal(fresh.Level, ancient.Level);
        Assert.Equal(fresh.ArmourClass, ancient.ArmourClass);
        Assert.Equal(fresh.MaxHitPoints, ancient.MaxHitPoints);
        Assert.Equal(fresh.AttackBonus, ancient.AttackBonus);
        Assert.Equal(fresh.DamageNotation, ancient.DamageNotation);
        Assert.Equal(fresh.DropChance, ancient.DropChance);

        Assert.Equal(fresh.MinGold * 2, ancient.MinGold);
        Assert.Equal(fresh.MaxGold * 2, ancient.MaxGold);

        // The cap has teeth: a year of neglect is worth exactly a month of it.
        var capped = HuntRules.StatBlock(drudge, level: 6, daysOverdue: 30, subtaskCount: 0);

        Assert.Equal(capped.MinGold, ancient.MinGold);
        Assert.Equal(capped.MaxGold, ancient.MaxGold);
    }

    /// <summary>
    /// Nothing on the whole derivation path pays a task less for being late (DEC-013).
    /// </summary>
    /// <remarks>
    /// Read across the promotion as well as within it, which is where a debuff would actually be
    /// smuggled in: a shape swapped at thirty days could quietly hand back a worse purse or a
    /// worse drop chance than the shape it replaced, and only a sweep across the boundary sees it.
    /// </remarks>
    [Theory]
    [InlineData(Difficulty.Easy, 0)]
    [InlineData(Difficulty.Medium, 0)]
    [InlineData(Difficulty.Hard, 0)]
    [InlineData(Difficulty.Epic, 0)]
    [InlineData(Difficulty.Medium, 2)]
    [InlineData(Difficulty.Hard, 7)]
    [InlineData(Difficulty.Epic, 40)]
    public void A_backlog_never_makes_a_contract_worth_less(Difficulty difficulty, int subtasks)
    {
        var level = HuntRules.LevelFor(characterLevel: 8, difficulty);
        var onTime = Block(difficulty, subtasks, 0, level);

        var previous = onTime;

        for (var days = 0; days <= 400; days++)
        {
            var block = Block(difficulty, subtasks, days, level);

            Assert.True(block.MinGold >= previous.MinGold, $"day {days} pays less at the bottom.");
            Assert.True(block.MaxGold >= previous.MaxGold, $"day {days} pays less at the top.");
            Assert.True(block.DropChance >= previous.DropChance, $"day {days} drops less often.");
            Assert.True(block.MinGold >= onTime.MinGold, $"day {days} is worse than on time.");

            previous = block;
        }

        return;

        static MonsterDefinition Block(Difficulty difficulty, int subtasks, int days, int level) =>
            HuntRules.StatBlock(
                HuntArchetypeCatalog.ShapeFor(difficulty, subtasks, days), level, days, subtasks);
    }

    /// <summary>Shape comes from the task; only the calendar promotes it, once, at a month.</summary>
    [Fact]
    public void A_task_takes_its_shape_from_its_own_two_axes()
    {
        Assert.Equal(HuntArchetypeCatalog.Drudge, Shape(Difficulty.Easy, 0, 0));
        Assert.Equal(HuntArchetypeCatalog.Drudge, Shape(Difficulty.Medium, 0, 0));
        Assert.Equal(HuntArchetypeCatalog.Bulwark, Shape(Difficulty.Hard, 0, 0));
        Assert.Equal(HuntArchetypeCatalog.Bulwark, Shape(Difficulty.Epic, 0, 0));
        Assert.Equal(HuntArchetypeCatalog.Tangle, Shape(Difficulty.Easy, 1, 0));
        Assert.Equal(HuntArchetypeCatalog.Tangle, Shape(Difficulty.Epic, 3, 0));
        Assert.Equal(HuntArchetypeCatalog.Hydra, Shape(Difficulty.Easy, 4, 0));

        // The promotion lands on the day the bounty caps, and one step is the whole ladder.
        Assert.Equal(HuntArchetypeCatalog.Drudge, Shape(Difficulty.Easy, 0, 29));
        Assert.Equal(HuntArchetypeCatalog.Bulwark, Shape(Difficulty.Easy, 0, 30));
        Assert.Equal(HuntArchetypeCatalog.Hydra, Shape(Difficulty.Easy, 2, 30));
        Assert.Equal(HuntArchetypeCatalog.Dread, Shape(Difficulty.Hard, 0, 30));
        Assert.Equal(HuntArchetypeCatalog.Dread, Shape(Difficulty.Easy, 9, 30));

        // Not a second step at a year. A task left to rot does not outrank a task left a month.
        Assert.Equal(HuntArchetypeCatalog.Bulwark, Shape(Difficulty.Easy, 0, 365));
        Assert.Equal(HuntArchetypeCatalog.Dread, Shape(Difficulty.Epic, 0, 3650));

        return;

        static string Shape(Difficulty difficulty, int subtasks, int days) =>
            HuntArchetypeCatalog.ShapeFor(difficulty, subtasks, days).Key;
    }

    /// <summary>
    /// Every rung a contract can be written at exists, and the board and the fight agree on it.
    /// </summary>
    /// <remarks>
    /// <c>HuntLadder.At</c> clamps rather than throwing, which keeps a live encounter finishable
    /// after a ladder is shortened but would also silently hide a ladder shorter than the level
    /// range <see cref="HuntRules.LevelFor"/> can return: the encounter would freeze
    /// <c>HuntLevel</c> at 15 while every stat block it derived reported level 14, and
    /// HuntOfferDto and HuntDto would quote two different rungs of the same contract.
    /// </remarks>
    [Fact]
    public void The_ladder_has_a_rung_for_every_level_a_contract_can_be_written_at()
    {
        Assert.Equal(MonsterCatalog.TopLevel, HuntLadder.All.Count);
        Assert.Equal(
            Enumerable.Range(1, HuntLadder.All.Count),
            HuntLadder.All.Select(r => r.Level));

        Assert.All(HuntLadder.All, rung => Assert.Equal(rung, HuntLadder.At(rung.Level)));

        // Clamped at both ends rather than throwing, because a stored level is history.
        Assert.Equal(HuntLadder.All[0], HuntLadder.At(0));
        Assert.Equal(HuntLadder.All[0], HuntLadder.At(-99));
        Assert.Equal(HuntLadder.All[^1], HuntLadder.At(HuntLadder.All.Count + 50));

        foreach (var difficulty in Enum.GetValues<Difficulty>())
        {
            foreach (var archetype in HuntArchetypeCatalog.All)
            {
                for (var characterLevel = 1; characterLevel <= 30; characterLevel++)
                {
                    var level = HuntRules.LevelFor(characterLevel, difficulty);
                    var block = HuntRules.StatBlock(archetype, level, 0, 0);

                    Assert.InRange(level, 1, HuntLadder.All.Count);

                    // The number frozen on the row and the number the block reports have to be
                    // the same number, or the board and the fight quote different rungs.
                    Assert.Equal(level, block.Level);

                    // Inside the band the tavern would already have offered, so a contract is
                    // never a fight the bestiary would have refused as certain death.
                    Assert.True(
                        block.IsAvailableAt(characterLevel),
                        $"{archetype.Key} at character level {characterLevel} is out of band.");
                }
            }
        }
    }

    /// <summary>
    /// The two key spaces share one varchar(60), so they must never collide.
    /// </summary>
    /// <remarks>
    /// A collision makes a single stored MonsterKey resolve to two different stat blocks
    /// depending on which catalog is asked first, and <see cref="Encounter.Monster"/> asks the
    /// archetypes first. The fight would change shape on the read that asked the other one.
    /// </remarks>
    [Fact]
    public void An_archetype_is_never_mistaken_for_a_bestiary_monster()
    {
        Assert.NotEmpty(HuntArchetypeCatalog.All);

        Assert.Equal(
            HuntArchetypeCatalog.All.Count,
            HuntArchetypeCatalog.All.Select(a => a.Key).Distinct(StringComparer.Ordinal).Count());

        foreach (var archetype in HuntArchetypeCatalog.All)
        {
            Assert.StartsWith("hunt-", archetype.Key, StringComparison.Ordinal);
            Assert.Null(MonsterCatalog.Find(archetype.Key));
            Assert.True(HuntArchetypeCatalog.Exists(archetype.Key));

            // Its own table, and every entry reachable and real.
            Assert.NotEmpty(archetype.LootTable);
            Assert.All(archetype.LootTable, entry =>
            {
                Assert.NotNull(ItemCatalog.Find(entry.ItemKey));
                Assert.True(entry.Weight > 0, $"{entry.ItemKey} can never be drawn.");
            });

            // Declared highest threshold down, the order PhaseAt counts in.
            if (archetype.Phases is { } phases)
            {
                Assert.Equal(
                    phases.Select(p => p.AtPercent).OrderByDescending(p => p),
                    phases.Select(p => p.AtPercent));

                Assert.All(phases, phase => Assert.InRange(phase.AtPercent, 1, 99));
            }
        }

        foreach (var monster in MonsterCatalog.All)
        {
            Assert.Null(HuntArchetypeCatalog.Find(monster.Key));
        }

        Assert.Null(HuntArchetypeCatalog.Find(null));
        Assert.Null(HuntArchetypeCatalog.Find("hunt-nothing"));
    }

    /// <summary>
    /// A contract's name carries its age and never the user's own words.
    /// </summary>
    /// <remarks>
    /// The name reaches the combat log, the chronicle and EncounterDto.MonsterName. A task title
    /// in it would put user text in all three, and would not fit the stored key's varchar(60)
    /// either. The title lives in exactly one line, composed once at the start.
    /// </remarks>
    [Fact]
    public void A_name_carries_the_age_and_the_opening_line_carries_the_title()
    {
        Assert.Null(HuntArchetypeCatalog.Epithet(0));
        Assert.Null(HuntArchetypeCatalog.Epithet(-5));
        Assert.Equal("Nagging", HuntArchetypeCatalog.Epithet(1));
        Assert.Equal("Nagging", HuntArchetypeCatalog.Epithet(2));
        Assert.Equal("Lingering", HuntArchetypeCatalog.Epithet(3));
        Assert.Equal("Festering", HuntArchetypeCatalog.Epithet(7));
        Assert.Equal("Entrenched", HuntArchetypeCatalog.Epithet(14));
        Assert.Equal("Ancient", HuntArchetypeCatalog.Epithet(30));
        Assert.Equal("Immemorial", HuntArchetypeCatalog.Epithet(90));

        var bulwark = HuntArchetypeCatalog.Find(HuntArchetypeCatalog.Bulwark)!;

        Assert.Equal("Bulwark", HuntRules.NameFor(bulwark, 0));
        Assert.Equal("Entrenched Bulwark", HuntRules.NameFor(bulwark, 14));

        // No article anywhere in a name: FlavourCatalog lines already read "The {monster} ...",
        // and a noun carrying its own would render "The The Bulwark".
        foreach (var archetype in HuntArchetypeCatalog.All)
        {
            foreach (var days in (int[])[0, 1, 5, 10, 20, 45, 200])
            {
                var name = HuntRules.NameFor(archetype, days);

                Assert.DoesNotContain("The ", name, StringComparison.OrdinalIgnoreCase);
                Assert.True(name.Length <= 60, $"{name} does not fit the stored key column.");
            }
        }

        Assert.Equal(
            "The Bulwark rises from \"file the tax return.\"",
            HuntRules.OpeningLine("Bulwark", "file the tax return"));

        // A title that brought its own stop does not get a second one.
        Assert.Equal(
            "The Bulwark rises from \"Is it done?\"",
            HuntRules.OpeningLine("Bulwark", "Is it done?"));

        // Blank falls back rather than composing a line that reads: rises from ".".
        Assert.Equal("The Bulwark rises.", HuntRules.OpeningLine("Bulwark", "   "));

        var quoted = HuntRules.OpeningLine("Bulwark", new string('x', 200));

        Assert.Contains("...", quoted, StringComparison.Ordinal);
        Assert.True(quoted.Length < 120, "A 200 character title reached the log whole.");
    }

    /// <summary>
    /// A checklist is a planning style, not a health bar, and the caps say so at both ends.
    /// </summary>
    [Fact]
    public void A_contract_is_capped_so_a_long_checklist_is_not_an_endless_fight()
    {
        var hydra = HuntArchetypeCatalog.Find(HuntArchetypeCatalog.Hydra)!;
        var rung = HuntLadder.At(6);

        var ten = HuntRules.StatBlock(hydra, 6, 0, HuntRules.CountedSubtaskCap);
        var forty = HuntRules.StatBlock(hydra, 6, 0, 40);

        Assert.Equal(ten.MaxHitPoints, forty.MaxHitPoints);

        Assert.All(
            HuntArchetypeCatalog.All,
            archetype =>
            {
                var block = HuntRules.StatBlock(archetype, 6, 400, 999);

                Assert.InRange(
                    block.MaxHitPoints, 1, rung.HitPoints * HuntRules.HitPointsCapMultiple);

                Assert.True(block.DropChance <= HuntRules.DropChanceCap, "A drop became certain.");
            });

        // A negative count cannot shrink a monster below the shape it was written as.
        Assert.Equal(
            HuntRules.StatBlock(hydra, 6, 0, 0).MaxHitPoints,
            HuntRules.StatBlock(hydra, 6, 0, -5).MaxHitPoints);
    }

    /// <summary>
    /// "Work", "work" and "WORK" are one faction, which the existing tag filter is not.
    /// </summary>
    /// <remarks>
    /// NormalizeTags preserves the case the user typed and dedupes case-insensitively within one
    /// task only, so all three casings genuinely exist across a list. The endpoint's own tag
    /// filter is byte-exact Postgres array containment; faction matching deliberately is not,
    /// and reusing that shape here would split one banner into three.
    /// </remarks>
    [Fact]
    public void A_faction_is_matched_from_a_tag_whatever_the_casing()
    {
        foreach (var typed in (string[])["work", "Work", "WORK", "wOrK", " work ", "work\t"])
        {
            Assert.Equal(FactionCatalog.TheLedger, FactionCatalog.FindByTag(typed)?.Key);
        }

        Assert.Null(FactionCatalog.FindByTag(null));
        Assert.Null(FactionCatalog.FindByTag("urgent"));
        Assert.Null(FactionCatalog.FindByTag(string.Empty));

        // The key is what travels, never the tag, so a user's casing never reaches a column.
        Assert.Equal(
            FactionCatalog.TheLedger,
            FactionCatalog.FactionFor(new TodoTask { Tags = ["WORK"] }));

        // Insertion order is the priority order, so a leading non-faction tag is skipped.
        Assert.Equal(
            FactionCatalog.TheLedger,
            FactionCatalog.FactionFor(new TodoTask { Tags = ["urgent", "Work", "home"] }));

        // First match wins when two banners are both named.
        Assert.Equal(
            FactionCatalog.TheHearth,
            FactionCatalog.FactionFor(new TodoTask { Tags = ["home", "work"] }));

        // A tag naming no banner falls to the Motley, and never beats one that does.
        Assert.Equal(
            FactionCatalog.TheMotley,
            FactionCatalog.FactionFor(new TodoTask { Tags = ["urgent"] }));

        Assert.Equal(
            FactionCatalog.TheMotley,
            FactionCatalog.FactionFor(new TodoTask { Tags = ["projects", "errands"] }));

        Assert.Equal(
            FactionCatalog.TheLedger,
            FactionCatalog.FactionFor(new TodoTask { Tags = ["projects", "work"] }));

        // The Motley is reached by falling through, never by being named, so it claims no word.
        Assert.Empty(FactionCatalog.Find(FactionCatalog.TheMotley)!.Aliases);
        Assert.Null(FactionCatalog.FindByTag("the-motley"));

        // An untagged task still musters nowhere: one tag is the price of entry.
        Assert.Null(FactionCatalog.FactionFor(new TodoTask()));
        Assert.Null(FactionCatalog.FactionFor(new TodoTask { Tags = [] }));
    }

    /// <summary>Every banner is reachable, distinct, and pays out of its own real table.</summary>
    [Fact]
    public void Every_banner_is_reachable_content_all_the_way_down()
    {
        Assert.NotEmpty(FactionCatalog.All);

        Assert.Equal(
            FactionCatalog.All.Count,
            FactionCatalog.All.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count());

        var aliases = FactionCatalog.All.SelectMany(f => f.Aliases).ToList();

        // A tag mustering under two banners has no defined winner.
        Assert.Equal(aliases.Count, aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Exactly one banner is reached by falling through rather than by being named, and it
        // is the Motley. Any other bannerless faction would be unreachable content.
        var wordless = FactionCatalog.All.Where(f => f.Aliases.Count == 0).ToList();

        Assert.Equal(FactionCatalog.TheMotley, Assert.Single(wordless).Key);

        foreach (var faction in FactionCatalog.All)
        {
            Assert.True(FactionCatalog.Exists(faction.Key));
            Assert.NotEmpty(faction.RewardTable);

            if (faction.Key != FactionCatalog.TheMotley)
            {
                Assert.NotEmpty(faction.Aliases);
            }

            Assert.All(faction.Aliases, alias =>
                Assert.Equal(faction.Key, FactionCatalog.FindByTag(alias)!.Key));

            Assert.All(faction.RewardTable, entry =>
            {
                Assert.NotNull(ItemCatalog.Find(entry.ItemKey));
                Assert.True(entry.Weight > 0, $"{entry.ItemKey} can never be drawn.");
            });

            // Every standing has a name, and an unnamed one falls back rather than throwing.
            foreach (var standing in Enum.GetValues<FactionStanding>())
            {
                Assert.False(string.IsNullOrWhiteSpace(faction.TitleAt(standing)));
            }

            Assert.Equal(faction.Name, faction.TitleAt((FactionStanding)99));
        }

        Assert.Null(FactionCatalog.Find(null));
        Assert.Null(FactionCatalog.Find("the-guild"));
    }

    /// <summary>Standing counts wins, and the only thing it buys is a floor.</summary>
    [Fact]
    public void Standing_is_a_count_of_wins_and_buys_only_a_floor()
    {
        Assert.Equal(FactionStanding.Unknown, FactionStandings.TierFor(0));
        Assert.Equal(FactionStanding.Unknown, FactionStandings.TierFor(-3));
        Assert.Equal(FactionStanding.Noticed, FactionStandings.TierFor(1));
        Assert.Equal(FactionStanding.Noticed, FactionStandings.TierFor(3));
        Assert.Equal(FactionStanding.Trusted, FactionStandings.TierFor(4));
        Assert.Equal(FactionStanding.Trusted, FactionStandings.TierFor(11));
        Assert.Equal(FactionStanding.Respected, FactionStandings.TierFor(12));
        Assert.Equal(FactionStanding.Respected, FactionStandings.TierFor(24));
        Assert.Equal(FactionStanding.Sworn, FactionStandings.TierFor(25));
        Assert.Equal(FactionStanding.Sworn, FactionStandings.TierFor(4000));

        Assert.Equal(Rarity.Common, FactionStandings.FloorFor(FactionStanding.Unknown));
        Assert.Equal(Rarity.Common, FactionStandings.FloorFor(FactionStanding.Noticed));
        Assert.Equal(Rarity.Uncommon, FactionStandings.FloorFor(FactionStanding.Trusted));
        Assert.Equal(Rarity.Rare, FactionStandings.FloorFor(FactionStanding.Respected));

        // Stopping at Rare is deliberate: a floor above it makes the rarity roll irrelevant.
        Assert.Equal(Rarity.Rare, FactionStandings.FloorFor(FactionStanding.Sworn));

        // Monotonic, so no win ever costs a hunter standing.
        var previous = FactionStanding.Unknown;

        for (var wins = 0; wins <= 200; wins++)
        {
            var tier = FactionStandings.TierFor(wins);

            Assert.True((int)tier >= (int)previous, $"{wins} wins is worth less than {wins - 1}.");
            previous = tier;
        }
    }

    /// <summary>
    /// A stat block spends no die, which is what keeps it out of every seeded script's blast
    /// radius, and every one of the four frozen facts moves something.
    /// </summary>
    /// <remarks>
    /// <see cref="HuntRules"/> takes no <c>IDiceRoller</c> at all, so a roll cannot appear here
    /// by accident, but a block is derived on every read of an encounter and a die spent there
    /// would land before the round's attack roll and change what dozens of passing tests assert
    /// without failing any of them.
    /// </remarks>
    [Fact]
    public void A_stat_block_is_a_pure_function_of_the_four_frozen_facts()
    {
        foreach (var archetype in HuntArchetypeCatalog.All)
        {
            var first = HuntRules.StatBlock(archetype, 7, 12, 4);
            var again = HuntRules.StatBlock(archetype, 7, 12, 4);

            Assert.Equal(first.Key, again.Key);
            Assert.Equal(first.Name, again.Name);
            Assert.Equal(first.MaxHitPoints, again.MaxHitPoints);
            Assert.Equal(first.ArmourClass, again.ArmourClass);
            Assert.Equal(first.AttackBonus, again.AttackBonus);
            Assert.Equal(first.MinGold, again.MinGold);
            Assert.Equal(first.MaxGold, again.MaxGold);
            Assert.Equal(first.DropChance, again.DropChance);

            // Each of the four moves something, so none of them is decorative.
            Assert.NotEqual(first.Name, HuntRules.StatBlock(archetype, 7, 0, 4).Name);
            Assert.NotEqual(first.MaxGold, HuntRules.StatBlock(archetype, 8, 12, 4).MaxGold);
            Assert.True(HuntRules.StatBlock(archetype, 7, 40, 4).MaxGold > first.MaxGold);
        }

        // The archetype's own table, never a monster's and never the rung's.
        var dread = HuntArchetypeCatalog.Find(HuntArchetypeCatalog.Dread)!;

        Assert.Same(dread.LootTable, HuntRules.StatBlock(dread, 9, 60, 2).LootTable);
        Assert.Same(dread.Phases, HuntRules.StatBlock(dread, 9, 60, 2).Phases);
    }
}
