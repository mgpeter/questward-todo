using TodoApp.Models;
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
