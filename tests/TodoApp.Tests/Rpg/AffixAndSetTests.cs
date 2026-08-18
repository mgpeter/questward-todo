using TodoApp.Api.Mapping;
using TodoApp.Api.Services.Rpg;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// Shared arrangement for the pure affix and set tests: an inventory row built by hand, with
/// its slot taken from the catalog so a test can never accidentally assert against a weapon
/// that thinks it is armour.
/// </summary>
internal static class Gear
{
    public static InventoryItem Item(
        string key,
        Rarity rarity = Rarity.Uncommon,
        string? prefix = null,
        string? suffix = null,
        bool equipped = true) =>
        new()
        {
            UserId = Guid.Empty,
            ItemKey = key,
            Slot = ItemCatalog.Find(key)?.Slot ?? ItemSlot.Weapon,
            Rarity = rarity,
            PrefixKey = prefix,
            SuffixKey = suffix,
            IsEquipped = equipped
        };
}

/// <summary>
/// The affix catalog is code-held (DEC-004), so a suffix that grants armour class or a
/// weapon-only word left open to every slot compiles fine and only shows up as gear whose
/// name does not describe what it does. These are the integrity the compiler is not providing.
/// </summary>
public class AffixCatalogTests
{
    [Fact]
    public void Affix_keys_are_unique() =>
        Assert.Equal(AffixCatalog.All.Count, AffixCatalog.All.Select(a => a.Key).Distinct().Count());

    [Fact]
    public void Every_affix_is_worth_rolling() =>
        Assert.All(AffixCatalog.All, affix =>
        {
            Assert.NotEmpty(affix.Word);
            Assert.NotEmpty(affix.Blurb);
            Assert.False(affix.EffectAt(Rarity.Uncommon).IsNothing, affix.Key);
        });

    [Fact]
    public void Prefixes_never_grant_ability_scores_and_suffixes_only_grant_them()
    {
        // The split is what lets a rolled name read like real gear. A suffix that raised
        // armour class would make "of the Fox" a claim about dexterity that is not true.
        foreach (var affix in AffixCatalog.All)
        {
            var effect = affix.EffectAt(Rarity.Epic);

            var grantsScores = effect.Abilities != AbilityScores.Zero;
            var grantsCombat = effect.ArmourBonus != 0
                || effect.AttackBonus != 0
                || effect.DamageBonus != 0
                || effect.CriticalRangeBonus != 0;

            Assert.Equal(affix.Kind == AffixKind.Suffix, grantsScores);
            Assert.Equal(affix.Kind == AffixKind.Prefix, grantsCombat);
        }
    }

    [Fact]
    public void Critical_range_is_flat_and_never_scales_with_rarity()
    {
        // A second point of critical range is worth far more than the first, so tiering it
        // would make an Epic weapon crit on something close to a coin toss.
        var keen = AffixCatalog.Find(AffixCatalog.Keen)!;

        Assert.Equal(1, keen.EffectAt(Rarity.Rare).CriticalRangeBonus);
        Assert.Equal(1, keen.EffectAt(Rarity.Legendary).CriticalRangeBonus);
    }

    [Theory]
    [InlineData(Rarity.Common, 0)]
    [InlineData(Rarity.Uncommon, 1)]
    [InlineData(Rarity.Rare, 1)]
    [InlineData(Rarity.Epic, 2)]
    [InlineData(Rarity.Legendary, 2)]
    public void Magnitude_doubles_at_epic_and_not_before(Rarity rarity, int expected)
    {
        Assert.Equal(expected, AffixRules.TierAt(rarity));
        Assert.Equal(expected, AffixCatalog.Find(AffixCatalog.Vicious)!.EffectAt(rarity).DamageBonus);
        Assert.Equal(expected, AffixCatalog.Find(AffixCatalog.OfTheBear)!.EffectAt(rarity).Abilities.Strength);
    }

    [Fact]
    public void A_two_ability_suffix_pays_both_at_the_same_tier()
    {
        var titan = AffixCatalog.Find(AffixCatalog.OfTheTitan)!.EffectAt(Rarity.Epic).Abilities;

        Assert.Equal(2, titan.Strength);
        Assert.Equal(2, titan.Constitution);
        Assert.Equal(0, titan.Dexterity);
    }

    [Fact]
    public void A_weapon_bound_prefix_does_not_fit_armour_or_a_trinket()
    {
        var vicious = AffixCatalog.Find(AffixCatalog.Vicious)!;

        Assert.True(vicious.FitsOn(ItemSlot.Weapon));
        Assert.False(vicious.FitsOn(ItemSlot.Armour));
        Assert.False(vicious.FitsOn(ItemSlot.Trinket));
    }

    [Fact]
    public void Every_suffix_fits_every_slot_that_can_hold_a_word() =>
        Assert.All(AffixCatalog.All.Where(a => a.Kind == AffixKind.Suffix), affix =>
            Assert.All(
                new[] { ItemSlot.Weapon, ItemSlot.Armour, ItemSlot.Trinket },
                slot => Assert.True(affix.FitsOn(slot), $"{affix.Key} on {slot}")));

    [Fact]
    public void An_unknown_key_reads_as_nothing()
    {
        // Retiring an affix must be a catalog edit, not a migration and not a crash for
        // whoever happened to be holding one.
        Assert.Null(AffixCatalog.Find("gilded"));
        Assert.Null(AffixCatalog.Find(null));
        Assert.False(AffixCatalog.Exists("gilded"));
    }
}

/// <summary>
/// Rolling, driven by a scripted die so the outcome is an assertion rather than a hope.
/// </summary>
public class AffixRollTests
{
    [Theory]
    [InlineData(Rarity.Common, 0)]
    [InlineData(Rarity.Uncommon, 1)]
    [InlineData(Rarity.Rare, 1)]
    [InlineData(Rarity.Epic, 2)]
    [InlineData(Rarity.Legendary, 2)]
    public void Slot_count_is_a_pure_function_of_rarity(Rarity rarity, int expected) =>
        Assert.Equal(expected, AffixRules.RollableFor(ItemSlot.Weapon, rarity));

    [Fact]
    public void A_common_drop_spends_no_dice_at_all()
    {
        // Half of all drops are Common. A variable slot count there would shift every seeded
        // script in the suite, which is why the ruling is zero rather than "usually none".
        var roller = new SequenceDiceRoller();

        var (prefix, suffix) = AffixRules.Roll(ItemSlot.Weapon, Rarity.Common, roller);

        Assert.Null(prefix);
        Assert.Null(suffix);
        Assert.Equal(0, roller.RollCount);
    }

    [Theory]
    [InlineData(Rarity.Common)]
    [InlineData(Rarity.Uncommon)]
    [InlineData(Rarity.Rare)]
    [InlineData(Rarity.Epic)]
    [InlineData(Rarity.Legendary)]
    public void A_consumable_never_rolls_an_affix(Rarity rarity)
    {
        // Bound now, before any item claims the slot: a stacking index that merges consumable
        // rows by key must never find two that differ only by a word nobody can see.
        var roller = new SequenceDiceRoller();

        var (prefix, suffix) = AffixRules.Roll(ItemSlot.Consumable, rarity, roller);

        Assert.Equal(0, AffixRules.RollableFor(ItemSlot.Consumable, rarity));
        Assert.Empty(AffixRules.EligibleFor(ItemSlot.Consumable, rarity));
        Assert.Null(prefix);
        Assert.Null(suffix);
        Assert.Equal(0, roller.RollCount);
    }

    [Fact]
    public void One_slot_costs_one_die_and_two_slots_cost_two()
    {
        var single = new SequenceDiceRoller(1);
        AffixRules.Roll(ItemSlot.Weapon, Rarity.Uncommon, single);
        Assert.Equal(1, single.RollCount);

        var pair = new SequenceDiceRoller(1, 1);
        AffixRules.Roll(ItemSlot.Weapon, Rarity.Epic, pair);
        Assert.Equal(2, pair.RollCount);
    }

    [Fact]
    public void One_slot_can_land_on_either_kind_from_a_single_die()
    {
        // The pool is prefixes then suffixes in catalog order, so one die reaches both kinds
        // and the assignment follows the winner's own kind rather than a second roll.
        var prefixed = AffixRules.Roll(ItemSlot.Weapon, Rarity.Uncommon, new SequenceDiceRoller(1));

        Assert.Equal(AffixCatalog.Balanced, prefixed.Prefix!.Key);
        Assert.Null(prefixed.Suffix);

        var suffixed = AffixRules.Roll(ItemSlot.Weapon, Rarity.Uncommon, new SequenceDiceRoller(5));

        Assert.Null(suffixed.Prefix);
        Assert.Equal(AffixCatalog.OfTheFox, suffixed.Suffix!.Key);
    }

    [Fact]
    public void Two_slots_always_pay_one_of_each_kind()
    {
        // Prefix first, then suffix, so Epic reads as a jackpot rather than as two prefixes.
        var rolled = AffixRules.Roll(ItemSlot.Weapon, Rarity.Epic, new SequenceDiceRoller(4, 2));

        Assert.Equal(AffixCatalog.Keen, rolled.Prefix!.Key);
        Assert.Equal(AffixCatalog.OfTheFox, rolled.Suffix!.Key);
    }

    [Fact]
    public void The_same_script_always_produces_the_same_words()
    {
        var first = AffixRules.Roll(ItemSlot.Weapon, Rarity.Epic, new SequenceDiceRoller(3, 7));
        var second = AffixRules.Roll(ItemSlot.Weapon, Rarity.Epic, new SequenceDiceRoller(3, 7));

        Assert.Equal(first, second);
        Assert.Equal(AffixCatalog.Warded, first.Prefix!.Key);
        Assert.Equal(AffixCatalog.OfTheTitan, first.Suffix!.Key);
    }

    [Fact]
    public void A_rare_only_word_is_unreachable_below_its_tier()
    {
        // MinimumRarity is enforced in the pool, so it holds for a drop and for a paid imbue
        // alike: essence can never buy a Rare-only word onto an Uncommon item.
        var uncommon = AffixRules.EligibleFor(ItemSlot.Weapon, Rarity.Uncommon).Select(a => a.Key).ToList();
        var rare = AffixRules.EligibleFor(ItemSlot.Weapon, Rarity.Rare).Select(a => a.Key).ToList();

        Assert.DoesNotContain(AffixCatalog.Keen, uncommon);
        Assert.DoesNotContain(AffixCatalog.Masterwork, uncommon);
        Assert.DoesNotContain(AffixCatalog.OfTheTitan, uncommon);

        Assert.Contains(AffixCatalog.Keen, rare);
        Assert.Contains(AffixCatalog.OfTheTitan, rare);
    }

    [Fact]
    public void Armour_and_trinkets_never_roll_a_weapon_bound_word()
    {
        foreach (var slot in new[] { ItemSlot.Armour, ItemSlot.Trinket })
        {
            var pool = AffixRules.EligibleFor(slot, Rarity.Legendary).Select(a => a.Key).ToList();

            Assert.DoesNotContain(AffixCatalog.Vicious, pool);
            Assert.DoesNotContain(AffixCatalog.Keen, pool);

            // Attack bonus and armour class are global once they are sheet fields, so the
            // any-slot prefixes are what keeps the armour pool from being three words wide.
            Assert.Contains(AffixCatalog.Warded, pool);
            Assert.Contains(AffixCatalog.Balanced, pool);
        }
    }

    [Fact]
    public void A_reforge_can_never_hand_back_the_word_it_replaced()
    {
        // Asserted over every face rather than one, because paying twice the imbue price to
        // be given the same word reads as the forge having taken the essence and done nothing.
        var pool = AffixRules.EligibleFor(ItemSlot.Weapon, Rarity.Epic, AffixKind.Prefix);

        for (var face = 1; face <= pool.Count; face++)
        {
            var rolled = AffixRules.RollOne(
                ItemSlot.Weapon,
                Rarity.Epic,
                AffixKind.Prefix,
                new SequenceDiceRoller(face),
                excluding: AffixCatalog.Keen);

            Assert.NotNull(rolled);
            Assert.NotEqual(AffixCatalog.Keen, rolled.Key);
        }
    }

    [Fact]
    public void An_empty_pool_rolls_nothing_rather_than_reaching_for_a_die()
    {
        var roller = new SequenceDiceRoller();

        Assert.Null(AffixRules.RollOne(ItemSlot.Weapon, Rarity.Common, AffixKind.Prefix, roller));
        Assert.Null(AffixRules.RollOne(ItemSlot.Consumable, Rarity.Legendary, null, roller));
        Assert.Equal(0, roller.RollCount);
    }
}

/// <summary>
/// What a stored row reads back as, which is where a retired key or a hand-edited pair has to
/// fail safely rather than loudly.
/// </summary>
public class AffixReadSideTests
{
    [Fact]
    public void A_rolled_item_reads_as_prefix_then_name_then_suffix()
    {
        var blade = Gear.Item(ItemCatalog.SilveredBlade, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheFox);

        Assert.Equal("Keen Silvered Blade of the Fox", blade.DisplayName);
    }

    [Fact]
    public void An_unaffixed_item_reads_as_its_catalog_name() =>
        Assert.Equal("Silvered Blade", Gear.Item(ItemCatalog.SilveredBlade, Rarity.Common).DisplayName);

    [Fact]
    public void Half_a_pair_still_composes_cleanly()
    {
        Assert.Equal(
            "Vicious Silvered Blade",
            Gear.Item(ItemCatalog.SilveredBlade, Rarity.Rare, prefix: AffixCatalog.Vicious).DisplayName);

        Assert.Equal(
            "Silvered Blade of the Ox",
            Gear.Item(ItemCatalog.SilveredBlade, Rarity.Rare, suffix: AffixCatalog.OfTheOx).DisplayName);
    }

    [Fact]
    public void A_retired_affix_key_reads_as_nothing_rather_than_crashing_the_bag()
    {
        var item = Gear.Item(ItemCatalog.SilveredBlade, Rarity.Epic, "gilded", "of-the-tax-collector");

        Assert.Equal("Silvered Blade", item.DisplayName);
        Assert.Equal(0, AffixRules.CountInForce(item));
        Assert.True(item.AffixEffects.IsNothing);
    }

    [Fact]
    public void A_key_stored_in_the_wrong_slot_is_ignored()
    {
        // Nothing in the app writes a suffix into PrefixKey, but a row that says so must not
        // pay a prefix's bonus under a suffix's name.
        var item = Gear.Item(ItemCatalog.SilveredBlade, Rarity.Epic, AffixCatalog.OfTheBear, AffixCatalog.Vicious);

        Assert.Equal("Silvered Blade", item.DisplayName);
        Assert.Equal(0, AffixRules.CountInForce(item));
    }

    [Fact]
    public void A_stored_pair_never_out_performs_the_rarity_on_the_label()
    {
        // Defensive rather than reachable. Whatever wrote the row, an Uncommon item carries
        // one word and a Common one carries none.
        var uncommon = Gear.Item(
            ItemCatalog.SilveredBlade, Rarity.Uncommon, AffixCatalog.Vicious, AffixCatalog.OfTheFox);

        Assert.Equal(1, AffixRules.CountInForce(uncommon));
        Assert.Equal(AffixCatalog.Vicious, AffixRules.InForce(uncommon).Prefix!.Key);
        Assert.Null(AffixRules.InForce(uncommon).Suffix);

        var common = Gear.Item(
            ItemCatalog.SilveredBlade, Rarity.Common, AffixCatalog.Vicious, AffixCatalog.OfTheFox);

        Assert.Equal(0, AffixRules.CountInForce(common));
        Assert.Equal("Silvered Blade", common.DisplayName);
    }

    [Fact]
    public void Upgrading_keeps_a_word_the_new_rarity_could_have_rolled_and_one_it_could_not()
    {
        // MinimumRarity is a roll-time filter only. Gold buys tiers, so a Rare Keen weapon
        // upgraded to Epic keeps its Keen and its Vicious goes from plus one to plus two.
        var epic = Gear.Item(ItemCatalog.SilveredBlade, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheTitan);

        Assert.Equal(2, AffixRules.CountInForce(epic));
        Assert.Equal(AffixCatalog.Keen, AffixRules.InForce(epic).Prefix!.Key);

        var rare = Gear.Item(ItemCatalog.SilveredBlade, Rarity.Rare, prefix: AffixCatalog.Vicious);
        var upgraded = Gear.Item(ItemCatalog.SilveredBlade, Rarity.Epic, prefix: AffixCatalog.Vicious);

        Assert.Equal(1, rare.AffixEffects.DamageBonus);
        Assert.Equal(2, upgraded.AffixEffects.DamageBonus);
    }

    [Fact]
    public void An_affix_on_a_trinket_still_grants_armour_class()
    {
        // Reading armour off the base item's slot is how a Warded trinket silently does
        // nothing while looking perfectly correct on the item card.
        var charm = Gear.Item(ItemCatalog.LuckyCoin, Rarity.Uncommon, prefix: AffixCatalog.Warded);

        Assert.Equal(1, charm.ArmourBonus);
    }

    [Fact]
    public void An_items_own_bonus_and_its_affix_add_up_rather_than_replacing_each_other()
    {
        // Scale Mail already grants Constitution, and of the Ox grants it again. The card has
        // to show the sum, not whichever was written last.
        var mail = Gear.Item(ItemCatalog.ScaleMail, Rarity.Rare, suffix: AffixCatalog.OfTheOx);

        Assert.Equal(3, mail.AbilityBonuses.Constitution);  // 2 from Rare, 1 from the suffix
        Assert.Equal(6, mail.ArmourBonus);                  // 4 intrinsic, 2 from Rare
    }

    [Fact]
    public void Set_membership_is_read_back_from_the_item_key()
    {
        Assert.Equal(SetCatalog.BearsDue, Gear.Item(ItemCatalog.ScaleMail, Rarity.Common).Set!.Key);
        Assert.Null(Gear.Item(ItemCatalog.WornDagger, Rarity.Common).Set);
    }
}

/// <summary>
/// The item card. Every display name in the app is supposed to come from one place, and a
/// producer that composes the catalog name itself reads to the player as a lost affix.
/// </summary>
public class InventoryItemCardTests
{
    [Fact]
    public void The_card_carries_the_rolled_name_the_words_and_the_forge_prices()
    {
        var dto = Gear.Item(
            ItemCatalog.SilveredBlade, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheFox).ToDto();

        Assert.Equal("Keen Silvered Blade of the Fox", dto.Name);
        Assert.Equal("Keen", dto.Prefix);
        Assert.Equal("of the Fox", dto.Suffix);
        Assert.Equal("The Nightfall Vigil", dto.SetName);

        // A full Uncommon and a half-filled Epic are otherwise indistinguishable on the wire.
        Assert.Equal(2, dto.AffixSlots);
        Assert.Equal(ForgeRules.EssenceFor(Rarity.Epic, 2), dto.SalvageValue);
        Assert.Equal(ForgeRules.ImbueCost(Rarity.Epic), dto.ImbueCost);
        Assert.Equal(ForgeRules.ReforgeCost(Rarity.Epic), dto.ReforgeCost);
    }

    [Fact]
    public void A_plain_item_advertises_no_words_and_no_set()
    {
        var dto = Gear.Item(ItemCatalog.WornDagger, Rarity.Common).ToDto();

        Assert.Equal("Worn Dagger", dto.Name);
        Assert.Null(dto.Prefix);
        Assert.Null(dto.Suffix);
        Assert.Null(dto.SetName);
        Assert.Equal(0, dto.AffixSlots);
        Assert.Equal(0, dto.ImbueCost);
    }

    [Fact]
    public void An_affix_never_raises_what_an_item_sells_for()
    {
        // The Gilded proposal, refused and then asserted. A rolled affix that raised an item's
        // value would mint gold outside the stamina chain (DEC-014), which is the same bug as
        // pricing salvage off BaseValue.
        var plain = Gear.Item(ItemCatalog.SilveredBlade, Rarity.Epic).ToDto();
        var rolled = Gear.Item(
            ItemCatalog.SilveredBlade, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheFox).ToDto();

        Assert.Equal(plain.SellValue, rolled.SellValue);
        Assert.True(rolled.SalvageValue > plain.SalvageValue);
    }

    [Fact]
    public void The_card_reports_armour_and_scores_including_its_affixes()
    {
        var warded = Gear.Item(ItemCatalog.LuckyCoin, Rarity.Uncommon, prefix: AffixCatalog.Warded).ToDto();

        Assert.Equal(1, warded.ArmourBonus);

        var oxen = Gear.Item(ItemCatalog.ScaleMail, Rarity.Rare, suffix: AffixCatalog.OfTheOx).ToDto();

        Assert.Contains(oxen.AbilityBonuses, b => b is { Label: "CON", Value: 3 });
    }
}

/// <summary>
/// The five sets, all composed from items that already existed, and all derived rather than
/// stored (DEC-002).
/// </summary>
public class SetCatalogTests
{
    [Fact]
    public void Every_set_item_is_a_real_item() =>
        Assert.All(SetCatalog.All, set =>
            Assert.All(set.ItemKeys, key =>
                Assert.True(ItemCatalog.Exists(key), $"{set.Key} lists '{key}'")));

    [Fact]
    public void No_item_belongs_to_two_sets()
    {
        // The key dictionary would throw at startup on a duplicate, which is the point. This
        // says so out loud so the failure is a named test rather than a type initialiser.
        var keys = SetCatalog.All.SelectMany(s => s.ItemKeys).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Set_keys_are_unique() =>
        Assert.Equal(SetCatalog.All.Count, SetCatalog.All.Select(s => s.Key).Distinct().Count());

    [Fact]
    public void Every_set_is_one_weapon_one_armour_and_one_trinket() =>
        Assert.All(SetCatalog.All, set =>
        {
            // There are exactly three equip slots, so any other composition is uncompletable
            // and its capstone would be a bonus nobody can ever be paid.
            var slots = set.ItemKeys.Select(k => ItemCatalog.Find(k)!.Slot).ToList();

            Assert.Equal(3, set.Total);
            Assert.Equal(1, slots.Count(s => s == ItemSlot.Weapon));
            Assert.Equal(1, slots.Count(s => s == ItemSlot.Armour));
            Assert.Equal(1, slots.Count(s => s == ItemSlot.Trinket));
        });

    [Fact]
    public void Every_tier_is_reachable_and_worth_wearing() =>
        Assert.All(SetCatalog.All, set =>
        {
            Assert.NotEmpty(set.Name);
            Assert.NotEmpty(set.Blurb);
            Assert.NotEmpty(set.Bonuses);

            Assert.All(set.Bonuses, bonus =>
            {
                Assert.InRange(bonus.Pieces, 2, set.Total);
                Assert.NotEmpty(bonus.Description);
                Assert.False(bonus.Effect.IsNothing, $"{set.Key} at {bonus.Pieces}");
            });
        });

    [Fact]
    public void A_weapon_bound_capstone_always_has_a_weapon_to_act_on() =>
        Assert.All(SetCatalog.All, set =>
            // Damage and critical range only mean anything on a weapon. They are safe here
            // precisely because a full set is a full set, weapon included.
            Assert.All(
                set.Bonuses.Where(b => b.Effect.DamageBonus != 0 || b.Effect.CriticalRangeBonus != 0),
                bonus => Assert.Equal(set.Total, bonus.Pieces)));

    [Fact]
    public void Two_pieces_pay_the_lower_tier_only()
    {
        var pair = new[]
        {
            Gear.Item(ItemCatalog.LongbowOfTheVale, Rarity.Common),
            Gear.Item(ItemCatalog.LeatherArmour, Rarity.Common)
        };

        var bonus = SetCatalog.BonusesFor(pair);

        Assert.Equal(1, bonus.ArmourBonus);
        Assert.Equal(0, bonus.AttackBonus);
    }

    [Fact]
    public void Three_pieces_pay_both_tiers()
    {
        var full = new[]
        {
            Gear.Item(ItemCatalog.LongbowOfTheVale, Rarity.Common),
            Gear.Item(ItemCatalog.LeatherArmour, Rarity.Common),
            Gear.Item(ItemCatalog.BootsOfSpeed, Rarity.Common)
        };

        var bonus = SetCatalog.BonusesFor(full);

        // Cumulative: the two-piece armour class is still being paid at three.
        Assert.Equal(1, bonus.ArmourBonus);
        Assert.Equal(1, bonus.AttackBonus);
    }

    [Fact]
    public void An_unequipped_piece_does_not_count()
    {
        // Bag contents are not a wearing decision, so the capstone has to come off with the
        // trinket rather than surviving in the backpack.
        var items = new[]
        {
            Gear.Item(ItemCatalog.LongbowOfTheVale, Rarity.Common),
            Gear.Item(ItemCatalog.LeatherArmour, Rarity.Common),
            Gear.Item(ItemCatalog.BootsOfSpeed, Rarity.Common, equipped: false)
        };

        var progress = SetCatalog.ProgressFor(items).Single(p => p.Set.Key == SetCatalog.Valewarden);

        Assert.Equal(2, progress.Equipped);
        Assert.Equal(0, SetCatalog.BonusesFor(items).AttackBonus);
    }

    [Fact]
    public void Set_bonuses_do_not_change_with_rarity()
    {
        // Rarity already scales the base item and the affix tier. A third multiplier on the
        // same score is where the numbers run away.
        static InventoryItem[] Valewarden(Rarity rarity) =>
        [
            Gear.Item(ItemCatalog.LongbowOfTheVale, rarity),
            Gear.Item(ItemCatalog.LeatherArmour, rarity),
            Gear.Item(ItemCatalog.BootsOfSpeed, rarity)
        ];

        Assert.Equal(
            SetCatalog.BonusesFor(Valewarden(Rarity.Common)),
            SetCatalog.BonusesFor(Valewarden(Rarity.Legendary)));
    }

    [Fact]
    public void Progress_is_reported_only_for_sets_you_have_a_piece_of()
    {
        var progress = SetCatalog.ProgressFor([Gear.Item(ItemCatalog.LeatherArmour, Rarity.Common)]);

        Assert.Single(progress);
        Assert.Equal(SetCatalog.Valewarden, progress[0].Set.Key);
        Assert.Equal(1, progress[0].Equipped);
        Assert.Empty(progress[0].Active);
    }

    [Fact]
    public void A_bag_full_of_the_same_piece_is_still_one_piece()
    {
        // Counted by distinct key. The partial unique index already guarantees it in the
        // database, but this is a pure static that tests hand arbitrary lists to.
        InventoryItem[] hoard =
        [
            Gear.Item(ItemCatalog.LeatherArmour, Rarity.Common),
            Gear.Item(ItemCatalog.LeatherArmour, Rarity.Rare),
            Gear.Item(ItemCatalog.LeatherArmour, Rarity.Legendary)
        ];

        Assert.Equal(1, SetCatalog.ProgressFor(hoard).Single().Equipped);
        Assert.Empty(SetCatalog.ProgressFor(hoard).Single().Active);
    }

    [Fact]
    public void An_item_outside_every_set_reports_nothing()
    {
        Assert.Null(SetCatalog.ForItem(ItemCatalog.HuntingBow));
        Assert.Null(SetCatalog.ForItem("a-key-from-a-later-patch"));
        Assert.Null(SetCatalog.ForItem(null));
        Assert.Empty(SetCatalog.ProgressFor([Gear.Item(ItemCatalog.HuntingBow, Rarity.Legendary)]));
    }

    [Fact]
    public void A_new_character_already_holds_a_piece()
    {
        // Two starting armours are set pieces on purpose, so the mechanic is discoverable
        // rather than something you only meet after a hundred fights.
        var starters = ClassCatalog.All
            .SelectMany(c => new[] { c.StartingWeaponKey, c.StartingArmourKey })
            .Distinct();

        Assert.Contains(starters, key => SetCatalog.ForItem(key) is not null);
    }
}

/// <summary>
/// Where affixes and set bonuses become numbers a fight can read. Every test here uses Common
/// pieces where it can, so anything that moves is the affix or the set and only that.
/// </summary>
public class GearOnTheSheetTests
{
    private static CharacterClass Fighter => ClassCatalog.Find(ClassCatalog.Fighter)!;
    private static CharacterClass Rogue => ClassCatalog.Find(ClassCatalog.Rogue)!;
    private static CharacterClass Ranger => ClassCatalog.Find(ClassCatalog.Ranger)!;

    private static CharacterSheet SheetOf(CharacterClass characterClass, params InventoryItem[] equipped) =>
        CharacterSheet.Compute(
            characterClass,
            level: 1,
            characterClass.StartingScores,
            CharacterSheetService.EffectsOf(equipped));

    [Fact]
    public void A_balanced_weapon_reaches_the_modifier_list_and_not_just_the_total()
    {
        // The single most likely silent failure in the phase: the combat service rolls
        // AttackModifiers, so a bonus that only reached AttackBonus would look right on the
        // sheet and do nothing at all in a fight.
        var plain = SheetOf(Fighter, Gear.Item(ItemCatalog.RustyLongsword));
        var balanced = SheetOf(Fighter, Gear.Item(ItemCatalog.RustyLongsword, prefix: AffixCatalog.Balanced));

        Assert.Equal(plain.AttackBonus + 1, balanced.AttackBonus);
        Assert.DoesNotContain(plain.AttackModifiers, m => m.Label == "gear");
        Assert.Contains(balanced.AttackModifiers, m => m is { Label: "gear", Value: 1 });

        // The breakdown has to add up to the number printed beside it.
        Assert.Equal(balanced.AttackBonus, balanced.AttackModifiers.Sum(m => m.Value));
    }

    [Fact]
    public void A_vicious_weapon_shows_its_damage_in_the_breakdown_rather_than_hiding_in_the_flat()
    {
        var sheet = SheetOf(
            Fighter, Gear.Item(ItemCatalog.RustyLongsword, Rarity.Epic, prefix: AffixCatalog.Vicious));

        Assert.Equal(2, sheet.GearDamageBonus);
        Assert.Contains(sheet.DamageModifiers, m => m is { Label: "gear", Value: 2 });

        // The damage roll counts DiceExpression.Flat in its total but never lists it, so a
        // Flat-based bonus produces a combat log whose own arithmetic does not add up.
        Assert.Equal(0, sheet.DamageExpression.Flat);
    }

    [Fact]
    public void A_warded_trinket_raises_armour_class()
    {
        // Accumulating affix armour inside the Slot == Armour branch is how this one fails.
        var bare = SheetOf(Fighter, Gear.Item(ItemCatalog.LuckyCoin, Rarity.Common));
        var warded = SheetOf(
            Fighter, Gear.Item(ItemCatalog.LuckyCoin, Rarity.Uncommon, prefix: AffixCatalog.Warded));

        Assert.Equal(bare.ArmourClass + 1, warded.ArmourClass);
    }

    [Fact]
    public void An_ability_suffix_lands_on_the_score_before_the_modifier_is_derived()
    {
        var plain = SheetOf(Fighter, Gear.Item(ItemCatalog.ChainShirt, Rarity.Epic));
        var oxen = SheetOf(
            Fighter, Gear.Item(ItemCatalog.ChainShirt, Rarity.Epic, suffix: AffixCatalog.OfTheOx));

        // Adding it to the modifier instead would silently halve every suffix in the catalog.
        Assert.Equal(plain.EffectiveScores.Constitution + 2, oxen.EffectiveScores.Constitution);
        Assert.True(oxen.MaxHitPoints > plain.MaxHitPoints);
    }

    [Fact]
    public void Masterwork_pays_attack_and_armour_at_the_same_time()
    {
        var plain = SheetOf(Fighter, Gear.Item(ItemCatalog.ChainShirt, Rarity.Rare));
        var fine = SheetOf(
            Fighter, Gear.Item(ItemCatalog.ChainShirt, Rarity.Rare, prefix: AffixCatalog.Masterwork));

        Assert.Equal(plain.ArmourClass + 1, fine.ArmourClass);
        Assert.Equal(plain.AttackBonus + 1, fine.AttackBonus);
    }

    [Fact]
    public void Keen_lowers_the_critical_threshold_by_exactly_one()
    {
        var plain = SheetOf(Fighter, Gear.Item(ItemCatalog.RustyLongsword, Rarity.Rare));
        var keen = SheetOf(
            Fighter, Gear.Item(ItemCatalog.RustyLongsword, Rarity.Rare, prefix: AffixCatalog.Keen));

        Assert.Equal(20, plain.CriticalOn);
        Assert.Equal(19, keen.CriticalOn);

        // Flat, so an Epic Keen is still one point rather than two.
        var epic = SheetOf(
            Fighter, Gear.Item(ItemCatalog.RustyLongsword, Rarity.Epic, prefix: AffixCatalog.Keen));

        Assert.Equal(19, epic.CriticalOn);
    }

    [Fact]
    public void Critical_range_never_falls_below_eighteen_however_it_stacks()
    {
        // A Rogue in the Nightfall Vigil holding a Keen blade: 20 minus one for Sneak Attack,
        // one for Keen and one for the capstone is 17, at which point the arithmetic of an
        // attack roll stops mattering. The floor is applied once, at the sum.
        var sheet = SheetOf(
            Rogue,
            Gear.Item(ItemCatalog.SilveredBlade, Rarity.Epic, prefix: AffixCatalog.Keen),
            Gear.Item(ItemCatalog.ShadowweaveCloak, Rarity.Epic),
            Gear.Item(ItemCatalog.GlovesOfTheThief, Rarity.Epic));

        Assert.Equal(2, sheet.CriticalRangeBonus);
        Assert.Equal(AffixRules.MinimumCriticalOn, sheet.CriticalOn);
        Assert.Equal(18, sheet.CriticalOn);
    }

    [Fact]
    public void A_set_bonus_reaches_the_sheet_and_leaves_again_when_a_piece_comes_off()
    {
        // Common pieces on purpose: at Common the items contribute no rarity bonus of their
        // own, so every point that moves here belongs to the set.
        var bow = Gear.Item(ItemCatalog.LongbowOfTheVale, Rarity.Common);
        var armour = Gear.Item(ItemCatalog.LeatherArmour, Rarity.Common);
        var boots = Gear.Item(ItemCatalog.BootsOfSpeed, Rarity.Common);

        var one = SheetOf(Ranger, bow);
        var two = SheetOf(Ranger, bow, armour);
        var three = SheetOf(Ranger, bow, armour, boots);

        // Leather is worth two armour class, and the second Valewarden piece one more.
        Assert.Equal(one.ArmourClass + 3, two.ArmourClass);

        // The capstone adds attack without paying the first tier a second time.
        Assert.Equal(two.ArmourClass, three.ArmourClass);
        Assert.Equal(two.AttackBonus + 1, three.AttackBonus);
        Assert.Contains(three.AttackModifiers, m => m is { Label: "gear", Value: 1 });

        // Nothing is stored: dropping the trinket from the list is the whole of taking it back.
        Assert.Equal(two.AttackBonus, SheetOf(Ranger, bow, armour).AttackBonus);
        Assert.DoesNotContain(two.AttackModifiers, m => m.Label == "gear");
    }

    [Fact]
    public void A_set_and_an_affix_stack_on_the_same_score()
    {
        var plain = SheetOf(
            Rogue,
            Gear.Item(ItemCatalog.SilveredBlade, Rarity.Common),
            Gear.Item(ItemCatalog.ShadowweaveCloak, Rarity.Common));

        var withSuffix = SheetOf(
            Rogue,
            Gear.Item(ItemCatalog.SilveredBlade, Rarity.Common),
            Gear.Item(ItemCatalog.ShadowweaveCloak, Rarity.Uncommon, suffix: AffixCatalog.OfTheFox));

        // Nightfall pays +1 Dexterity at two pieces and the suffix another, on the same score.
        Assert.Equal(1, plain.ItemBonuses.Dexterity);
        Assert.Equal(3, withSuffix.ItemBonuses.Dexterity);
    }

    [Fact]
    public void A_retired_item_key_contributes_nothing_rather_than_crashing_the_sheet()
    {
        var effects = CharacterSheetService.EffectsOf([Gear.Item("axe-of-a-forgotten-patch", Rarity.Legendary)]);

        Assert.Equal(0, effects.ArmourBonus);
        Assert.Equal(0, effects.AttackBonus);
        Assert.Null(effects.WeaponDamage);
    }

    [Fact]
    public void A_word_on_a_retired_item_key_reads_as_nothing_on_the_card_too()
    {
        // The sheet skips the whole item, so a card that still counted the word would promise
        // armour class and ability scores that wearing it does not grant, and the forge would
        // be selling that promise.
        var lost = Gear.Item(
            "axe-of-a-forgotten-patch", Rarity.Epic, AffixCatalog.Warded, AffixCatalog.OfTheOx);

        var worn = CharacterSheetService.EffectsOf([lost]);

        Assert.True(lost.AffixEffects.IsNothing);
        Assert.Equal(0, lost.ArmourBonus);
        Assert.Equal(0, lost.AbilityBonuses.Constitution);
        Assert.Equal(worn.ArmourBonus, lost.ArmourBonus);
    }

    [Fact]
    public void An_empty_loadout_still_computes()
    {
        var sheet = SheetOf(Fighter);

        Assert.Equal(0, sheet.CriticalRangeBonus);
        Assert.Equal(20, sheet.CriticalOn);
        Assert.Equal("1d4", sheet.DamageExpression.ToString());
    }
}

/// <summary>
/// The essence economy, asserted as arithmetic rather than trusted as a table.
/// </summary>
public class ForgeRuleTests
{
    [Theory]
    [InlineData(Rarity.Common, 0, 1)]
    [InlineData(Rarity.Uncommon, 0, 2)]
    [InlineData(Rarity.Uncommon, 1, 4)]
    [InlineData(Rarity.Rare, 1, 7)]
    [InlineData(Rarity.Epic, 2, 16)]
    [InlineData(Rarity.Legendary, 2, 34)]
    public void Salvage_yield_rises_with_rarity_and_with_affixes(Rarity rarity, int affixes, int expected) =>
        Assert.Equal(expected, ForgeRules.EssenceFor(rarity, affixes));

    [Fact]
    public void No_item_can_pay_for_its_own_affix()
    {
        // The loop-closing invariant. Without it, break-and-imbue is a treadmill that turns
        // inventory churn into power at no cost.
        foreach (var rarity in Enum.GetValues<Rarity>())
        {
            Assert.False(ForgeRules.PaysForItsOwnAffix(rarity), rarity.ToString());
        }

        foreach (var rarity in new[] { Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary })
        {
            Assert.True(
                ForgeRules.EssenceFor(rarity, ForgeRules.MaxAffixes(rarity)) < ForgeRules.ImbueCost(rarity),
                $"{rarity}: a fully affixed one pays for a word on another");
        }
    }

    [Fact]
    public void A_reforge_costs_twice_an_imbue_at_every_rarity() =>
        Assert.All(Enum.GetValues<Rarity>(), rarity =>
            Assert.Equal(ForgeRules.ImbueCost(rarity) * 2, ForgeRules.ReforgeCost(rarity)));

    [Fact]
    public void Common_holds_no_words_and_therefore_has_no_price()
    {
        Assert.Equal(0, ForgeRules.MaxAffixes(Rarity.Common));
        Assert.Equal(0, ForgeRules.ImbueCost(Rarity.Common));
        Assert.Equal(0, ForgeRules.ReforgeCost(Rarity.Common));
    }

    [Fact]
    public void The_price_of_a_word_rises_faster_than_the_yield_of_breaking_one()
    {
        var rarities = new[] { Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary };

        for (var i = 1; i < rarities.Length; i++)
        {
            Assert.True(ForgeRules.ImbueCost(rarities[i]) > ForgeRules.ImbueCost(rarities[i - 1]));
            Assert.True(ForgeRules.EssenceFor(rarities[i], 0) > ForgeRules.EssenceFor(rarities[i - 1], 0));
        }
    }

    [Fact]
    public void Yield_tracks_rarity_and_affixes_rather_than_what_the_item_is_worth()
    {
        // Pricing salvage off BaseValue would make the shop a converter: buy the cheapest
        // item of a tier, break it, repeat.
        var cheap = Gear.Item(ItemCatalog.PaddedJerkin, Rarity.Rare);
        var dear = Gear.Item(ItemCatalog.DragonfangSpear, Rarity.Rare);

        Assert.Equal(ForgeRules.EssenceFor(cheap), ForgeRules.EssenceFor(dear));
        Assert.True(dear.Definition!.ValueAt(Rarity.Rare) > cheap.Definition!.ValueAt(Rarity.Rare) * 10);
    }

    [Fact]
    public void A_retired_item_key_still_pays_the_floor()
    {
        // A key that has left the catalog must not strand a row in someone's bag forever,
        // which is the same ruling the sell path already makes.
        Assert.Equal(1, ForgeRules.EssenceFor(Gear.Item("axe-of-a-forgotten-patch", Rarity.Legendary)));
    }

    [Fact]
    public void A_dead_affix_key_pays_nothing_because_it_does_nothing()
    {
        var live = Gear.Item(ItemCatalog.SilveredBlade, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheFox);
        var dead = Gear.Item(ItemCatalog.SilveredBlade, Rarity.Epic, "gilded", "of-the-tax-collector");

        Assert.Equal(16, ForgeRules.EssenceFor(live));
        Assert.Equal(12, ForgeRules.EssenceFor(dead));
    }

    [Fact]
    public void The_shelf_is_the_only_route_from_gold_to_essence_and_it_is_capped()
    {
        // Buy, break, repeat is the real inflation route. What caps it is structural: six
        // offers a day, sampled without replacement, none above Rare, and each one sells once.
        // The constants are checked here; that the shop actually honours them is walked end to
        // end by ForgeEndpointTests.A_whole_day_of_buying_and_breaking_pays_less_than_two_epic_words,
        // because arithmetic over two constants would pass just as happily against a shop that
        // resold the same offer all day.
        var bestCase = ShopService.OfferCount * ForgeRules.EssenceFor(ShopService.MaxStockRarity, 0);

        Assert.True(ShopService.MaxStockRarity <= Rarity.Rare);
        Assert.Equal(6, ShopService.OfferCount);

        // A day of buying the whole shelf and breaking all of it still does not buy two Epic
        // words, and the shelf will not refill until tomorrow.
        Assert.True(bestCase < 2 * ForgeRules.ImbueCost(Rarity.Epic));

        // What this does not claim: paying to upgrade a purchase before breaking it raises what
        // it yields. That route is bounded by the same six items and by the gold behind them,
        // and gold is only earned by spending stamina, which only real work produces.

        // Shop stock carries no affixes, so nothing bought pays the affix premium.
        var stock = ShopService.StockFor(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.All(stock.Offers, offer =>
            Assert.Equal(
                ForgeRules.EssenceFor(offer.Rarity, 0),
                ForgeRules.EssenceFor(Gear.Item(offer.Item.Key, offer.Rarity))));
    }
}
