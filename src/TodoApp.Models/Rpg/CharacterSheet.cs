using TodoApp.Models.Dice;

namespace TodoApp.Models.Rpg;

/// <summary>What the equipped items contribute, flattened so the sheet stays a pure function.</summary>
/// <param name="AttackBonus">Affix and set contributions to attack rolls.</param>
/// <param name="DamageBonus">Affix and set contributions to weapon damage.</param>
/// <param name="CriticalRangeBonus">How far the critical threshold moves down.</param>
/// <remarks>
/// The last three are trailing with defaults on purpose: every existing construction site,
/// tests included, keeps compiling untouched, so a widening of the effect vocabulary is not
/// also a rewrite of the suite.
/// </remarks>
public sealed record EquipmentEffects(
    AbilityScores AbilityBonuses,
    int ArmourBonus,
    DiceExpression? WeaponDamage,
    bool WeaponFinesse,
    Ability? WeaponAbility,
    int AttackBonus = 0,
    int DamageBonus = 0,
    int CriticalRangeBonus = 0)
{
    public static EquipmentEffects None { get; } =
        new(ItemDefinition.Zero, 0, null, false, null);

    /// <summary>
    /// Folds an affix or set contribution in. Armour is added outside any slot test, because
    /// a Warded trinket that only counted on armour would silently do nothing.
    /// </summary>
    public EquipmentEffects Plus(BonusEffects bonus) => this with
    {
        AbilityBonuses = AbilityBonuses.Plus(bonus.Abilities),
        ArmourBonus = ArmourBonus + bonus.ArmourBonus,
        AttackBonus = AttackBonus + bonus.AttackBonus,
        DamageBonus = DamageBonus + bonus.DamageBonus,
        CriticalRangeBonus = CriticalRangeBonus + bonus.CriticalRangeBonus
    };
}

/// <summary>
/// Every derived number on the character, computed from class, level and equipment.
/// </summary>
/// <remarks>
/// Nothing here is stored. Max hit points, armour class and attack bonus are all
/// recomputed on read for the same reason level is (DEC-002): two copies of a derived
/// value eventually disagree, and the stored one is always the wrong one.
/// </remarks>
public sealed record CharacterSheet(
    CharacterClass? Class,
    int Level,
    AbilityScores BaseScores,
    AbilityScores EffectiveScores,
    AbilityScores ItemBonuses,
    int ProficiencyBonus,
    int ArmourClass,
    int AttackBonus,
    DiceExpression DamageExpression,
    Ability AttackAbility,
    int MaxHitPoints,
    int GearAttackBonus = 0,
    int GearDamageBonus = 0,
    int CriticalRangeBonus = 0)
{
    /// <summary>Levels at which the two primary abilities each gain a point.</summary>
    private static readonly int[] ImprovementLevels = [4, 8, 12, 16, 19];

    /// <summary>Bare hands, for a character with no weapon equipped.</summary>
    private static readonly DiceExpression Unarmed = new(1, 4, 0);

    public static CharacterSheet Compute(
        CharacterClass? characterClass,
        int level,
        AbilityScores baseScores,
        EquipmentEffects equipment)
    {
        level = Math.Max(1, level);

        var withImprovements = ApplyAbilityImprovements(baseScores, characterClass, level);

        // Item bonuses apply to the raw scores, before modifiers are derived. Adding them
        // to the modifiers instead would silently halve every one of them.
        var effective = withImprovements.Plus(equipment.AbilityBonuses);

        var proficiency = ProficiencyFor(level);
        var attackAbility = ChooseAttackAbility(equipment, effective);

        var damage = equipment.WeaponDamage ?? Unarmed;

        return new CharacterSheet(
            Class: characterClass,
            Level: level,
            BaseScores: withImprovements,
            EffectiveScores: effective,
            ItemBonuses: equipment.AbilityBonuses,
            ProficiencyBonus: proficiency,
            ArmourClass: 10 + effective.Modifier(Ability.Dexterity) + equipment.ArmourBonus,
            AttackBonus: proficiency + effective.Modifier(attackAbility) + equipment.AttackBonus,
            DamageExpression: damage,
            AttackAbility: attackAbility,
            MaxHitPoints: MaxHitPointsFor(characterClass, level, effective),
            GearAttackBonus: equipment.AttackBonus,
            GearDamageBonus: equipment.DamageBonus,
            CriticalRangeBonus: equipment.CriticalRangeBonus);
    }

    public int DamageModifier => EffectiveScores.Modifier(AttackAbility);

    /// <summary>
    /// The Rogue crits on 19, everyone else needs a 20, and gear moves that down further.
    /// </summary>
    /// <remarks>
    /// The floor is applied once, here at the sum, rather than by each contributor. A Rogue in
    /// the Nightfall Vigil holding a Keen blade otherwise reaches 17, at which point the
    /// arithmetic of an attack roll stops mattering.
    /// </remarks>
    public int CriticalOn => Math.Max(
        AffixRules.MinimumCriticalOn,
        (Class?.Perk == ClassPerk.SneakAttack ? 19 : 20) - CriticalRangeBonus);

    /// <remarks>
    /// The combat service rolls this list, not <see cref="AttackBonus"/>. A gear bonus that
    /// only reached the displayed total would look right on the sheet and do nothing in a
    /// fight, which is the quietest possible way for an affix to be broken.
    /// </remarks>
    public IReadOnlyList<RollModifier> AttackModifiers =>
        GearAttackBonus == 0
            ?
            [
                new(AbilityScores.Abbreviate(AttackAbility), EffectiveScores.Modifier(AttackAbility)),
                new("proficiency", ProficiencyBonus)
            ]
            :
            [
                new(AbilityScores.Abbreviate(AttackAbility), EffectiveScores.Modifier(AttackAbility)),
                new("proficiency", ProficiencyBonus),
                new("gear", GearAttackBonus)
            ];

    /// <remarks>
    /// Gear damage is a labelled modifier rather than <c>DiceExpression.Flat</c>. The damage
    /// roll counts Flat in the total but never shows it in the breakdown, so a Flat-based
    /// bonus produces a combat log whose own arithmetic does not add up.
    /// </remarks>
    public IReadOnlyList<RollModifier> DamageModifiers
    {
        get
        {
            List<RollModifier> modifiers = [];

            if (DamageModifier != 0)
            {
                modifiers.Add(new RollModifier(AbilityScores.Abbreviate(AttackAbility), DamageModifier));
            }

            if (GearDamageBonus != 0)
            {
                modifiers.Add(new RollModifier("gear", GearDamageBonus));
            }

            return modifiers;
        }
    }

    public static int ProficiencyFor(int level) => Math.Min(6, 2 + ((Math.Max(1, level) - 1) / 4));

    public static int ImprovementsAt(int level) => ImprovementLevels.Count(l => level >= l);

    private static AbilityScores ApplyAbilityImprovements(
        AbilityScores scores,
        CharacterClass? characterClass,
        int level)
    {
        if (characterClass is null)
        {
            return scores;
        }

        // Applied automatically to the class's two primaries rather than prompting. A todo
        // app is the wrong place to make someone plan a build.
        var improvements = ImprovementsAt(level);

        return scores
            .Plus(characterClass.Primary, improvements)
            .Plus(characterClass.Secondary, improvements);
    }

    private static Ability ChooseAttackAbility(EquipmentEffects equipment, AbilityScores scores)
    {
        if (equipment.WeaponAbility is null)
        {
            return Ability.Strength;
        }

        if (!equipment.WeaponFinesse)
        {
            return equipment.WeaponAbility.Value;
        }

        // Finesse weapons use whichever of Strength or Dexterity is better right now.
        return scores.Modifier(Ability.Dexterity) >= scores.Modifier(Ability.Strength)
            ? Ability.Dexterity
            : Ability.Strength;
    }

    private static int MaxHitPointsFor(CharacterClass? characterClass, int level, AbilityScores scores)
    {
        var die = characterClass?.HitDie ?? new DiceExpression(1, 6, 0);
        var constitution = scores.Modifier(Ability.Constitution);

        // Level 1 takes the maximum roll, later levels take the average. Both then add
        // the Constitution modifier, which is why a tough character scales with level.
        var total = die.Max + constitution + ((level - 1) * (die.Average + constitution));

        // A punishing Constitution should make you fragile, never negative.
        return Math.Max(level, total);
    }
}
