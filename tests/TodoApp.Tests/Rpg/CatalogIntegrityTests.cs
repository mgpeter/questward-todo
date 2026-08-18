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

    // ---- boss phases --------------------------------------------------------

    /// <summary>
    /// Highest threshold first, which is the order <see cref="MonsterDefinition.PhaseAt"/>
    /// counts in and the order the entry loop walks.
    /// </summary>
    /// <remarks>
    /// Declared the other way round, PhaseAt still counts the same number of thresholds crossed,
    /// so nothing throws: the fight would simply enter the wrong phase, name it after another
    /// one and apply its effects. A silent mis-tune rather than a failure, which is exactly the
    /// kind of thing a catalog test is for.
    /// </remarks>
    [Fact]
    public void Boss_phases_are_declared_from_the_highest_threshold_down()
    {
        Assert.All(MonsterCatalog.All.Where(m => m.Phases is not null), monster =>
        {
            var thresholds = monster.Phases!.Select(p => p.AtPercent).ToList();

            Assert.Equal(thresholds.OrderByDescending(t => t), thresholds);

            // Two phases at the same percent would both be entered by one blow and the second
            // would have no threshold of its own to mean anything.
            Assert.Equal(thresholds.Count, thresholds.Distinct().Count());
        });
    }

    /// <summary>
    /// A hundred percent would fire before a blow had landed, and zero only on a corpse.
    /// </summary>
    [Fact]
    public void Every_phase_threshold_sits_between_one_and_ninety_nine()
    {
        Assert.All(MonsterCatalog.All.Where(m => m.Phases is not null), monster =>
            Assert.All(monster.Phases!, phase =>
                Assert.InRange(phase.AtPercent, 1, 99)));
    }

    [Fact]
    public void Every_phase_says_something_and_does_something()
    {
        Assert.All(MonsterCatalog.All.Where(m => m.Phases is not null), monster =>
            Assert.All(monster.Phases!, phase =>
            {
                Assert.NotEmpty(phase.Name);
                Assert.NotEmpty(phase.Line);

                // A phase with no entry effect is narration wearing a mechanic's clothes.
                Assert.NotEmpty(phase.OnEntry);

                // Lasting rather than an arbitrary large number, and never zero: an effect
                // applied for no rounds is pruned before the monster ever reads it.
                Assert.All(phase.OnEntry, e => Assert.InRange(e.Rounds, 1, StatusEffects.Lasting));
            }));
    }

    /// <summary>
    /// The line is written out rather than templated, so it must not carry the flavour
    /// catalog's token: nothing substitutes it on this path and the player would read the
    /// braces.
    /// </summary>
    [Fact]
    public void No_phase_line_carries_the_flavour_token() =>
        Assert.All(MonsterCatalog.All.Where(m => m.Phases is not null), monster =>
            Assert.All(monster.Phases!, phase =>
                Assert.DoesNotContain(FlavourCatalog.MonsterToken, phase.Line, StringComparison.Ordinal)));

    // ---- consumables --------------------------------------------------------

    /// <summary>
    /// The slot and the use move together in both directions.
    /// </summary>
    /// <remarks>
    /// A consumable without a use is an item the use endpoint refuses and the shop still sells.
    /// A sword with one is worse: nothing would ever read it, so the effect would be paid for
    /// and never delivered.
    /// </remarks>
    [Fact]
    public void Every_consumable_has_a_use_and_nothing_else_does()
    {
        Assert.All(ItemCatalog.All, item => Assert.Equal(
            item.Slot == ItemSlot.Consumable,
            item.Use is not null));

        // The arm exists at all, so the rest of the phase's tests are testing something.
        Assert.Contains(ItemCatalog.All, i => i.Slot == ItemSlot.Consumable);
    }

    [Fact]
    public void Every_consumable_actually_does_something()
    {
        Assert.All(ItemCatalog.All.Where(i => i.Slot == ItemSlot.Consumable), item =>
        {
            var use = item.Use!;

            // Neither a heal nor an effect is a potion that costs a round and buys nothing.
            Assert.True(use.Heal > 0 || use.Kind is not null, item.Key);

            // An effect for zero rounds is pruned before anything reads it.
            if (use.Kind is not null)
            {
                Assert.True(use.Rounds > 0, item.Key);
            }

            Assert.NotEmpty(use.Describe());
        });
    }

    /// <summary>
    /// Consumables stay off every loot table, and this is the test that keeps them there.
    /// </summary>
    /// <remarks>
    /// Not a style rule. A table's summed weight is the die size LootService.PickWeighted rolls,
    /// so adding an entry changes which item an existing seeded script is handed, with no change
    /// in the roll count to make the break visible. There is a second reason as well: the drop
    /// path adds its item with a bare Add rather than through InventoryStack, so a consumable on
    /// a table would lose to the stacking index on the second one that dropped.
    /// </remarks>
    [Fact]
    public void No_loot_table_can_drop_a_consumable()
    {
        Assert.All(MonsterCatalog.All, monster =>
            Assert.All(monster.LootTable, entry =>
                Assert.NotEqual(ItemSlot.Consumable, ItemCatalog.Find(entry.ItemKey)!.Slot)));
    }

    /// <summary>The same rule, for the other unstacked grant path.</summary>
    [Fact]
    public void No_quest_rewards_a_consumable()
    {
        Assert.All(QuestCatalog.All.Where(q => q.RewardItemKey is not null), quest =>
            Assert.NotEqual(
                ItemSlot.Consumable,
                ItemCatalog.Find(quest.RewardItemKey)!.Slot));
    }
}
