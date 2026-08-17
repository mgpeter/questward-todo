using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;

namespace TodoApp.Tests.Rpg;

public class AbilityScoreTests
{
    [Theory]
    [InlineData(1, -5)]
    [InlineData(3, -4)]
    [InlineData(7, -2)]   // truncating division would wrongly give -1 here
    [InlineData(9, -1)]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 1)]
    [InlineData(15, 2)]
    [InlineData(18, 4)]
    [InlineData(20, 5)]
    public void Modifier_floors_rather_than_truncating(int score, int expected) =>
        Assert.Equal(expected, AbilityScores.ModifierFor(score));

    [Fact]
    public void Odd_scores_below_ten_round_down_not_toward_zero()
    {
        // The regression this guards: C# integer division rounds toward zero, so
        // (7 - 10) / 2 is -1 rather than the correct -2, quietly buffing weak characters.
        foreach (var (score, expected) in new[] { (9, -1), (7, -2), (5, -3), (3, -4) })
        {
            Assert.Equal(expected, AbilityScores.ModifierFor(score));
        }
    }

    [Fact]
    public void Default_really_is_an_average_human_and_not_a_pile_of_zeroes()
    {
        // The trap this guards: `new AbilityScores()` on a struct runs the implicit
        // parameterless constructor and ignores the primary constructor's `= 10` defaults,
        // silently producing a character with -5 to every ability.
        Assert.All(AbilityScores.All, a => Assert.Equal(10, AbilityScores.Default[a]));
        Assert.All(AbilityScores.All, a => Assert.Equal(0, AbilityScores.Default.Modifier(a)));

        Assert.All(AbilityScores.All, a => Assert.Equal(0, AbilityScores.Zero[a]));
    }

    [Fact]
    public void Zero_is_the_additive_identity()
    {
        var scores = new AbilityScores(12, 14, 13, 10, 11, 9);

        Assert.Equal(scores, scores.Plus(AbilityScores.Zero));
    }

    [Fact]
    public void Adds_two_sets_of_scores_component_wise()
    {
        var sum = new AbilityScores(10, 12, 14, 8, 11, 13).Plus(new AbilityScores(2, 0, 1, 0, 0, 0));

        Assert.Equal(12, sum.Strength);
        Assert.Equal(15, sum.Constitution);
        Assert.Equal(12, sum.Dexterity);
    }
}

public class CharacterSheetTests
{
    private static CharacterClass Fighter => ClassCatalog.Find(ClassCatalog.Fighter)!;
    private static CharacterClass Rogue => ClassCatalog.Find(ClassCatalog.Rogue)!;

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 4)]
    [InlineData(13, 5)]
    [InlineData(17, 6)]
    [InlineData(30, 6)]  // capped
    public void Proficiency_follows_the_tabletop_progression(int level, int expected) =>
        Assert.Equal(expected, CharacterSheet.ProficiencyFor(level));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(8, 2)]
    [InlineData(12, 3)]
    [InlineData(19, 5)]
    public void Ability_improvements_arrive_on_schedule(int level, int expected) =>
        Assert.Equal(expected, CharacterSheet.ImprovementsAt(level));

    [Fact]
    public void Armour_class_is_ten_plus_dexterity_plus_armour()
    {
        var sheet = CharacterSheet.Compute(
            Fighter,
            level: 1,
            new AbilityScores(Dexterity: 14),
            EquipmentEffects.None with { ArmourBonus = 3 });

        Assert.Equal(15, sheet.ArmourClass); // 10 + 2 + 3
    }

    [Fact]
    public void Item_bonuses_apply_to_scores_before_modifiers_are_derived()
    {
        // The bug this exists to prevent: adding the bonus to the modifier instead halves
        // it, so a +2 item silently becomes +1.
        var withoutItem = CharacterSheet.Compute(
            Fighter, level: 1, new AbilityScores(Dexterity: 14), EquipmentEffects.None);

        var withItem = CharacterSheet.Compute(
            Fighter,
            level: 1,
            new AbilityScores(Dexterity: 14),
            EquipmentEffects.None with { AbilityBonuses = ItemDefinition.Zero with { Dexterity = 2 } });

        Assert.Equal(16, withItem.EffectiveScores.Dexterity);
        Assert.Equal(3, withItem.EffectiveScores.Modifier(Ability.Dexterity));

        // 14 -> +2, 16 -> +3. A full point of armour class, not a half.
        Assert.Equal(withoutItem.ArmourClass + 1, withItem.ArmourClass);
    }

    [Fact]
    public void Attack_bonus_is_proficiency_plus_the_governing_ability()
    {
        var sheet = CharacterSheet.Compute(
            Fighter,
            level: 5,
            new AbilityScores(Strength: 18),
            EquipmentEffects.None with
            {
                WeaponDamage = DiceExpression.Parse("1d8"),
                WeaponAbility = Ability.Strength
            });

        // Level 5 gives proficiency 3; level 4 already added +1 to both Fighter primaries,
        // so Strength is 19 and still a +4 modifier.
        Assert.Equal(3, sheet.ProficiencyBonus);
        Assert.Equal(19, sheet.EffectiveScores.Strength);
        Assert.Equal(7, sheet.AttackBonus);
    }

    [Fact]
    public void A_finesse_weapon_uses_whichever_of_strength_or_dexterity_is_better()
    {
        var nimble = CharacterSheet.Compute(
            Rogue,
            level: 1,
            new AbilityScores(Strength: 8, Dexterity: 18),
            EquipmentEffects.None with
            {
                WeaponDamage = DiceExpression.Parse("1d4"),
                WeaponFinesse = true,
                WeaponAbility = Ability.Dexterity
            });

        Assert.Equal(Ability.Dexterity, nimble.AttackAbility);

        var burly = CharacterSheet.Compute(
            Rogue,
            level: 1,
            new AbilityScores(Strength: 18, Dexterity: 8),
            EquipmentEffects.None with
            {
                WeaponDamage = DiceExpression.Parse("1d4"),
                WeaponFinesse = true,
                WeaponAbility = Ability.Dexterity
            });

        Assert.Equal(Ability.Strength, burly.AttackAbility);
    }

    [Fact]
    public void A_non_finesse_weapon_always_uses_its_own_ability()
    {
        var sheet = CharacterSheet.Compute(
            Fighter,
            level: 1,
            new AbilityScores(Strength: 8, Dexterity: 18),
            EquipmentEffects.None with
            {
                WeaponDamage = DiceExpression.Parse("1d8"),
                WeaponFinesse = false,
                WeaponAbility = Ability.Strength
            });

        Assert.Equal(Ability.Strength, sheet.AttackAbility);
    }

    [Fact]
    public void Max_hit_points_take_the_full_die_at_first_level()
    {
        var sheet = CharacterSheet.Compute(
            Fighter, level: 1, new AbilityScores(Constitution: 14), EquipmentEffects.None);

        Assert.Equal(12, sheet.MaxHitPoints); // d10 max plus +2 Constitution
    }

    [Fact]
    public void Max_hit_points_grow_by_the_average_plus_constitution_each_level()
    {
        var level3 = CharacterSheet.Compute(
            Fighter, level: 3, new AbilityScores(Constitution: 14), EquipmentEffects.None);

        // 12 at level 1, then two levels of (6 average + 2 Constitution).
        Assert.Equal(28, level3.MaxHitPoints);
    }

    [Fact]
    public void Hit_points_never_drop_below_one_per_level()
    {
        var frail = CharacterSheet.Compute(
            ClassCatalog.Find(ClassCatalog.Wizard),
            level: 5,
            new AbilityScores(Constitution: 1),
            EquipmentEffects.None);

        Assert.True(frail.MaxHitPoints >= 5);
    }

    [Fact]
    public void A_character_without_a_class_is_still_a_valid_sheet()
    {
        // Existing characters predate class selection and must not crash the sheet.
        var sheet = CharacterSheet.Compute(null, level: 3, AbilityScores.Default, EquipmentEffects.None);

        Assert.Null(sheet.Class);
        Assert.Equal(10, sheet.ArmourClass);
        Assert.Equal(20, sheet.CriticalOn);
        Assert.True(sheet.MaxHitPoints > 0);
        Assert.Equal("1d4", sheet.DamageExpression.ToString()); // unarmed
    }

    [Fact]
    public void The_rogue_perk_lowers_the_critical_threshold()
    {
        var rogue = CharacterSheet.Compute(Rogue, 1, Rogue.StartingScores, EquipmentEffects.None);
        var fighter = CharacterSheet.Compute(Fighter, 1, Fighter.StartingScores, EquipmentEffects.None);

        Assert.Equal(19, rogue.CriticalOn);
        Assert.Equal(20, fighter.CriticalOn);
    }

    [Fact]
    public void Improvements_land_on_both_class_primaries()
    {
        var sheet = CharacterSheet.Compute(Fighter, level: 8, Fighter.StartingScores, EquipmentEffects.None);

        // Two improvements by level 8, on Strength and Constitution.
        Assert.Equal(Fighter.StartingScores.Strength + 2, sheet.EffectiveScores.Strength);
        Assert.Equal(Fighter.StartingScores.Constitution + 2, sheet.EffectiveScores.Constitution);
        Assert.Equal(Fighter.StartingScores.Charisma, sheet.EffectiveScores.Charisma);
    }
}

public class ClassCatalogTests
{
    [Fact]
    public void Every_class_key_is_unique() =>
        Assert.Equal(ClassCatalog.All.Count, ClassCatalog.All.Select(c => c.Key).Distinct().Count());

    [Fact]
    public void Every_class_references_real_starting_equipment()
    {
        // A typo here would hand a new character gear that silently does not exist.
        Assert.All(ClassCatalog.All, c =>
        {
            Assert.True(ItemCatalog.Exists(c.StartingWeaponKey), $"{c.Key} weapon: {c.StartingWeaponKey}");
            Assert.True(ItemCatalog.Exists(c.StartingArmourKey), $"{c.Key} armour: {c.StartingArmourKey}");
        });
    }

    [Fact]
    public void Every_class_is_playable()
    {
        Assert.All(ClassCatalog.All, c =>
        {
            Assert.NotEqual(c.Primary, c.Secondary);
            Assert.InRange(c.HitDieSides, 4, 12);
            Assert.NotEmpty(c.PerkName);
            Assert.NotEmpty(c.Blurb);

            // Nobody should start with a crippling score.
            foreach (var ability in AbilityScores.All)
            {
                Assert.InRange(c.StartingScores[ability], 8, 17);
            }
        });
    }

    [Fact]
    public void Each_class_leads_with_its_primary_ability()
    {
        Assert.All(ClassCatalog.All, c =>
            Assert.True(
                c.StartingScores[c.Primary] >= 15,
                $"{c.Key} should start strong in {c.Primary}"));
    }

    [Fact]
    public void Perks_are_distinct_so_classes_actually_play_differently() =>
        Assert.Equal(ClassCatalog.All.Count, ClassCatalog.All.Select(c => c.Perk).Distinct().Count());
}
