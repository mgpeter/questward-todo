using TodoApp.Models;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// The catalogs are code-held (DEC-004), which means a typo in a key compiles fine and
/// fails silently at runtime: a loot table that drops nothing, or a quest that pays a
/// reward nobody receives. These tests are the referential integrity the database is not
/// providing.
/// </summary>
public class CatalogIntegrityTests
{
    [Fact]
    public void Item_keys_are_unique() =>
        Assert.Equal(ItemCatalog.All.Count, ItemCatalog.All.Select(i => i.Key).Distinct().Count());

    [Fact]
    public void Monster_keys_are_unique() =>
        Assert.Equal(MonsterCatalog.All.Count, MonsterCatalog.All.Select(m => m.Key).Distinct().Count());

    [Fact]
    public void Quest_keys_are_unique() =>
        Assert.Equal(QuestCatalog.All.Count, QuestCatalog.All.Select(q => q.Key).Distinct().Count());

    [Fact]
    public void Every_loot_table_entry_references_a_real_item()
    {
        Assert.All(MonsterCatalog.All, monster =>
            Assert.All(monster.LootTable, entry =>
                Assert.True(
                    ItemCatalog.Exists(entry.ItemKey),
                    $"{monster.Key} can drop '{entry.ItemKey}', which is not in the item catalog")));
    }

    [Fact]
    public void Every_quest_reward_item_is_a_real_item()
    {
        Assert.All(QuestCatalog.All.Where(q => q.RewardItemKey is not null), quest =>
            Assert.True(
                ItemCatalog.Exists(quest.RewardItemKey),
                $"{quest.Key} rewards '{quest.RewardItemKey}', which is not in the item catalog"));
    }

    [Fact]
    public void Every_monster_objective_references_a_real_monster()
    {
        var monsterObjectives = QuestCatalog.All
            .SelectMany(q => q.Objectives.Select(o => (q.Key, o)))
            .Where(x => x.o.Kind == ObjectiveKind.DefeatMonster && x.o.Target.Length > 0);

        Assert.All(monsterObjectives, x =>
            Assert.True(
                MonsterCatalog.Exists(x.o.Target),
                $"{x.Key} asks for '{x.o.Target}', which is not in the bestiary"));
    }

    [Fact]
    public void Every_task_objective_names_a_real_difficulty()
    {
        var taskObjectives = QuestCatalog.All
            .SelectMany(q => q.Objectives.Select(o => (q.Key, o)))
            .Where(x => x.o.Kind == ObjectiveKind.CompleteTask && x.o.Target.Length > 0);

        Assert.All(taskObjectives, x =>
            Assert.True(
                Enum.TryParse<Difficulty>(x.o.Target, ignoreCase: true, out _),
                $"{x.Key} asks for difficulty '{x.o.Target}', which is not a Difficulty"));
    }

    [Fact]
    public void Objective_ids_are_unique_within_their_quest()
    {
        // Counters are persisted keyed by objective id. Two objectives sharing an id would
        // silently share a counter, so one would complete the other.
        Assert.All(QuestCatalog.All, quest =>
            Assert.Equal(
                quest.Objectives.Count,
                quest.Objectives.Select(o => o.Id).Distinct().Count()));
    }

    [Fact]
    public void Every_quest_is_completable_and_pays_something()
    {
        Assert.All(QuestCatalog.All, quest =>
        {
            Assert.NotEmpty(quest.Objectives);
            Assert.All(quest.Objectives, o => Assert.True(o.Required > 0, $"{quest.Key}/{o.Id}"));
            Assert.True(quest.RewardGold > 0 || quest.RewardItemKey is not null, quest.Key);
            Assert.NotEmpty(quest.Description);
        });
    }

    [Fact]
    public void Every_monster_is_a_valid_opponent()
    {
        Assert.All(MonsterCatalog.All, monster =>
        {
            Assert.True(monster.MaxHitPoints > 0, monster.Key);
            Assert.InRange(monster.ArmourClass, 5, 25);
            Assert.InRange(monster.DropChance, 0, 100);
            Assert.True(monster.MinGold <= monster.MaxGold, monster.Key);
            Assert.NotEmpty(monster.LootTable);
            Assert.All(monster.LootTable, e => Assert.True(e.Weight > 0, $"{monster.Key}/{e.ItemKey}"));

            // Parsing here rather than at first use, so a malformed expression fails the
            // build rather than a fight already in progress.
            Assert.True(monster.Damage.Sides >= 2, monster.Key);
        });
    }

    [Fact]
    public void Every_weapon_has_damage_and_every_armour_has_a_bonus()
    {
        Assert.All(ItemCatalog.All, item =>
        {
            switch (item.Slot)
            {
                case ItemSlot.Weapon:
                    Assert.NotNull(item.Damage);
                    break;
                case ItemSlot.Armour:
                    Assert.True(item.ArmourBonus > 0, item.Key);
                    break;
                case ItemSlot.Trinket:
                    // A trinket with no ability bonus would do literally nothing.
                    Assert.NotNull(item.BonusAbility);
                    break;
            }

            Assert.True(item.BaseValue > 0, item.Key);
            Assert.NotEmpty(item.Blurb);
        });
    }

    [Fact]
    public void Rarity_makes_items_meaningfully_better()
    {
        var sword = ItemCatalog.Find(ItemCatalog.RustyLongsword)!;

        Assert.Equal(0, sword.AbilityBonusesAt(Rarity.Common).Strength);
        Assert.Equal(4, sword.AbilityBonusesAt(Rarity.Legendary).Strength);
        Assert.True(sword.ValueAt(Rarity.Legendary) > sword.ValueAt(Rarity.Common) * 10);
    }

    [Fact]
    public void Armour_rarity_raises_the_armour_bonus_too()
    {
        var mail = ItemCatalog.Find(ItemCatalog.ChainShirt)!;

        Assert.Equal(3, mail.ArmourBonusAt(Rarity.Common));
        Assert.Equal(5, mail.ArmourBonusAt(Rarity.Rare));
    }

    [Fact]
    public void A_weapon_is_not_given_an_armour_bonus_by_rarity()
    {
        var sword = ItemCatalog.Find(ItemCatalog.RustyLongsword)!;

        Assert.Equal(0, sword.ArmourBonusAt(Rarity.Legendary));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(11)]
    public void Every_level_has_something_to_fight(int level) =>
        Assert.NotEmpty(MonsterCatalog.AvailableAt(level));

    /// <summary>
    /// The old catalog sampled four levels and missed that 14 and above had no opponent at
    /// all, which left a character who reached it unable to start any fight. Every level the
    /// phase claims to cover is checked, and through the real predicate rather than a
    /// reimplementation of the band, so a change to <see cref="MonsterDefinition.IsAvailableAt"/>
    /// moves this test with it instead of past it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void Every_level_from_one_to_fourteen_has_an_opponent(int level)
    {
        var available = MonsterCatalog.AvailableAt(level);

        Assert.True(available.Count > 0, $"a level {level} character has nothing to fight");

        // The list and the predicate have to agree, or the tavern would offer a fight that
        // CombatService.StartAsync then refuses with a 400.
        Assert.All(available, m => Assert.True(
            m.IsAvailableAt(level),
            $"{m.Key} is offered at level {level} but the predicate refuses it"));
    }

    /// <summary>
    /// A single opponent is a level that plays itself. Three is the floor the phase set, and
    /// it is asserted rather than described so the next monster added cannot quietly undo it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void No_level_from_one_to_fourteen_is_down_to_a_single_opponent(int level) =>
        Assert.True(
            MonsterCatalog.AvailableAt(level).Count >= 3,
            $"level {level} offers only {MonsterCatalog.AvailableAt(level).Count} opponents");

    /// <summary>
    /// The bestiary stops at level 14 and the level curve runs to 9000, so the band has to
    /// stop climbing before the catalog runs out from under it. Unclamped it walked off the
    /// end: level 15 offered two opponents, level 16 one, and level 17 and above none at all,
    /// which emptied the tavern, refused every start as out of range and left stamina, gold,
    /// loot, the codex and every combat quest permanently inert. Levels never fall, so there
    /// was no way back out of it.
    /// </summary>
    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(25)]
    [InlineData(400)]
    [InlineData(LevelCurve.MaxLevel)]
    public void No_level_past_the_end_of_the_bestiary_runs_out_of_opponents(int level)
    {
        var available = MonsterCatalog.AvailableAt(level);

        Assert.True(
            available.Count >= 3,
            $"level {level} offers only {available.Count} opponents");

        // The same agreement the low levels are held to: the list and the predicate the
        // fight is gated on have to say the same thing.
        Assert.All(available, m => Assert.True(
            m.IsAvailableAt(level),
            $"{m.Key} is offered at level {level} but the predicate refuses it"));
    }

    /// <summary>
    /// The top of the catalog is the top of the band, and it is the whole of the reason the
    /// clamp above works. A monster added below the current top does not move it; one added
    /// above it does, and then the levels between have to be checked again.
    /// </summary>
    [Fact]
    public void The_band_stops_climbing_at_the_deepest_monster_in_the_catalog()
    {
        Assert.Equal(MonsterCatalog.All.Max(m => m.Level), MonsterCatalog.TopLevel);

        Assert.Equal(
            MonsterCatalog.AvailableAt(MonsterCatalog.TopLevel).Select(m => m.Key),
            MonsterCatalog.AvailableAt(MonsterCatalog.TopLevel + 9).Select(m => m.Key));
    }

    /// <summary>
    /// Every flavour line refers to the monster as "it" and conjugates for a singular
    /// subject: "The {monster} adjusts its footing without hurry." Selection is a hash of the
    /// encounter id and cannot be steered away from a bad pairing, so a single plural display
    /// name makes roughly ninety lines ungrammatical at once. A collective noun is the way
    /// round it, which is what "Drowned Crew" and "Carrion Flock" are.
    /// </summary>
    /// <remarks>
    /// A crude test for a crude failure: it can only see plurals that end in an s. A singular
    /// name that genuinely ends in one belongs on an exception list here rather than deleting
    /// the test, because the ninety lines are still the thing at stake.
    /// </remarks>
    [Fact]
    public void Every_monster_name_reads_as_a_singular_noun()
    {
        Assert.All(MonsterCatalog.All, m =>
            Assert.False(
                m.Name.EndsWith("s", StringComparison.OrdinalIgnoreCase),
                $"'{m.Name}' reads as a plural, and every flavour line says 'it' and 'its'"));
    }

    /// <summary>
    /// The sibling of <see cref="Every_monster_objective_references_a_real_monster"/> for the
    /// kind added this phase. Discovery objectives all carry an empty target today, so the
    /// only way this can start mattering is the day one names a key and gets it wrong.
    /// </summary>
    [Fact]
    public void Every_discovery_objective_references_a_real_monster()
    {
        var discoveries = QuestCatalog.All
            .SelectMany(q => q.Objectives.Select(o => (q.Key, o)))
            .Where(x => x.o.Kind == ObjectiveKind.DiscoverMonster && x.o.Target.Length > 0);

        Assert.All(discoveries, x =>
            Assert.True(
                MonsterCatalog.Exists(x.o.Target),
                $"{x.Key} asks to discover '{x.o.Target}', which is not in the bestiary"));
    }

    /// <summary>
    /// A discovery objective cannot ask for more kinds than exist, or the quest is a promise
    /// the catalog cannot keep no matter how much the player fights.
    /// </summary>
    [Fact]
    public void No_discovery_objective_asks_for_more_kinds_than_exist()
    {
        var discoveries = QuestCatalog.All
            .SelectMany(q => q.Objectives.Select(o => (q.Key, o)))
            .Where(x => x.o.Kind == ObjectiveKind.DiscoverMonster && x.o.Target.Length == 0);

        Assert.All(discoveries, x =>
            Assert.True(
                x.o.Required <= MonsterCatalog.All.Count,
                $"{x.Key} wants {x.o.Required} kinds discovered, and only " +
                $"{MonsterCatalog.All.Count} exist"));
    }

    /// <summary>
    /// The stronger reading of the same promise, and the one that catches what counting the
    /// catalog cannot. The availability band narrows as a character climbs: from level 8 the
    /// tavern will never again offer anything below monster level 6, so only ten kinds are
    /// still meetable from there on. Full Catalogue asks for twelve and Long Service for
    /// eighteen, and both were unwinnable for as long as progress only accrued while the
    /// quest was unlocked. They are honest now because progress is derived from the bestiary
    /// and so counts kinds met at any level, which is exactly the ladder measured here.
    /// </summary>
    [Fact]
    public void No_discovery_objective_asks_for_more_kinds_than_a_whole_career_can_meet()
    {
        // Every monster a character climbing one level at a time could ever have been offered.
        var reachable = Enumerable.Range(1, MonsterCatalog.TopLevel)
            .SelectMany(MonsterCatalog.AvailableAt)
            .Select(m => m.Key)
            .ToHashSet(StringComparer.Ordinal);

        var discoveries = QuestCatalog.All
            .SelectMany(q => q.Objectives.Select(o => (q.Key, o)))
            .Where(x => x.o.Kind == ObjectiveKind.DiscoverMonster && x.o.Target.Length == 0);

        Assert.All(discoveries, x =>
            Assert.True(
                x.o.Required <= reachable.Count,
                $"{x.Key} wants {x.o.Required} kinds discovered and only {reachable.Count} " +
                "can ever be met, at any level"));
    }

    // ---- lore -------------------------------------------------------------

    [Fact]
    public void Lore_fragment_keys_are_unique() =>
        Assert.Equal(LoreCatalog.All.Count, LoreCatalog.All.Select(f => f.Key).Distinct().Count());

    [Fact]
    public void Every_fragment_belongs_to_a_real_place()
    {
        var places = LoreCatalog.Places.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

        Assert.All(LoreCatalog.All, f =>
            Assert.True(
                places.Contains(f.PlaceKey),
                $"{f.Key} hangs off '{f.PlaceKey}', which is not a place"));
    }

    [Fact]
    public void Every_place_has_something_to_find_in_it() =>
        Assert.All(LoreCatalog.Places, p => Assert.NotEmpty(LoreCatalog.ForPlace(p.Key)));

    [Fact]
    public void Every_monster_fragment_names_a_real_monster()
    {
        var monsterFragments = LoreCatalog.All
            .Where(f => f.Trigger is LoreTrigger.MonsterSeen or LoreTrigger.MonsterSlain);

        Assert.All(monsterFragments, f =>
            Assert.True(
                MonsterCatalog.Exists(f.Subject),
                $"{f.Key} is about '{f.Subject}', which is not in the bestiary"));
    }

    [Fact]
    public void Every_quest_fragment_names_a_real_quest()
    {
        var questFragments = LoreCatalog.All.Where(f => f.Trigger == LoreTrigger.QuestClaimed);

        Assert.All(questFragments, f =>
            Assert.True(
                QuestCatalog.Exists(f.Subject),
                $"{f.Key} waits on '{f.Subject}', which is not a quest"));
    }

    /// <summary>
    /// Every monster is worth reading about. A monster with no ladder is a codex row that
    /// pays nothing for beating it, which is the whole reason the lore exists.
    /// </summary>
    [Fact]
    public void Every_monster_has_a_lore_ladder() =>
        Assert.All(MonsterCatalog.All, m =>
            Assert.True(
                LoreCatalog.ForMonster(m.Key).Count > 0,
                $"{m.Key} has no lore fragment of its own"));

    [Fact]
    public void Every_fragment_is_reachable_and_says_something()
    {
        Assert.All(LoreCatalog.All, f =>
        {
            Assert.NotEmpty(f.Title);
            Assert.NotEmpty(f.Body);

            // A counting trigger with a threshold of zero would already be unlocked before
            // the player had done anything, so the fragment would never read as a reward.
            if (f.Trigger is LoreTrigger.MonsterSeen or LoreTrigger.MonsterSlain or LoreTrigger.Level)
            {
                Assert.True(f.Threshold > 0, $"{f.Key} unlocks itself at a threshold of zero");
            }
        });
    }

    /// <summary>
    /// A level fragment beyond the level curve's ceiling could never open. Nothing enforces
    /// the relationship in code, so it is enforced here.
    /// </summary>
    [Fact]
    public void No_level_fragment_waits_past_the_end_of_the_curve() =>
        Assert.All(
            LoreCatalog.All.Where(f => f.Trigger == LoreTrigger.Level),
            f => Assert.InRange(f.Threshold, 1, LevelCurve.MaxLevel));

    [Fact]
    public void The_bestiary_does_not_offer_a_dragon_to_a_level_one_character()
    {
        var available = MonsterCatalog.AvailableAt(1).Select(m => m.Key);

        Assert.DoesNotContain(MonsterCatalog.YoungDragon, available);
        Assert.Contains(MonsterCatalog.Goblin, available);
    }

    [Fact]
    public void Every_class_starting_weapon_is_actually_a_weapon()
    {
        Assert.All(ClassCatalog.All, c =>
        {
            Assert.Equal(ItemSlot.Weapon, ItemCatalog.Find(c.StartingWeaponKey)!.Slot);
            Assert.Equal(ItemSlot.Armour, ItemCatalog.Find(c.StartingArmourKey)!.Slot);
        });
    }
}
