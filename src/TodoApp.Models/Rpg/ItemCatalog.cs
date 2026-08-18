using TodoApp.Models.Dice;

namespace TodoApp.Models.Rpg;

public enum ItemSlot
{
    Weapon = 0,
    Armour = 1,
    Trinket = 2,

    /// <summary>
    /// Used up rather than worn, and therefore never affixed (see <see cref="AffixRules.RollableFor"/>).
    /// </summary>
    /// <remarks>
    /// Declared before any item claims it, because the value 3 is already pinned by the
    /// stacking rule that consumables will be counted with <c>WHERE Slot = 3</c>. Left
    /// unassigned it invites a different number later, and renumbering a persisted enum
    /// silently re-slots every row already written.
    /// </remarks>
    Consumable = 3
}

public enum Rarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4
}

public static class RarityRules
{
    /// <summary>
    /// The bonus a rarity adds on top of the item's intrinsic power. Rarity is rolled per
    /// drop, so one catalog entry covers the whole range from junk to trophy.
    /// </summary>
    public static int BonusFor(Rarity rarity) => (int)rarity;

    public static int ValueMultiplier(Rarity rarity) => rarity switch
    {
        Rarity.Common => 1,
        Rarity.Uncommon => 3,
        Rarity.Rare => 8,
        Rarity.Epic => 20,
        Rarity.Legendary => 50,
        _ => 1
    };

    public static string Describe(Rarity rarity) => rarity.ToString().ToLowerInvariant();
}

/// <summary>
/// What one unit of a consumable does when it is used in a fight.
/// </summary>
/// <remarks>
/// Every number here is fixed rather than rolled. A potion that healed 1d8 would cost a die and
/// drag consumables into the blast radius of every hard-coded SequenceDiceRoller script in the
/// suite, and a value the player can read before deciding is a better decision than a gamble.
/// </remarks>
/// <param name="Heal">Hit points restored on use, before any effect is applied.</param>
/// <param name="Kind">Null when the item only heals.</param>
/// <param name="Rounds">Applications the effect lasts. Deliberately not scaled by rarity.</param>
/// <param name="Magnitude">The magnitude at Common. Rarity adds to it through <see cref="At"/>.</param>
public sealed record ConsumableUse(
    int Heal,
    EffectKind? Kind,
    EffectTarget Target,
    int Rounds,
    int Magnitude)
{
    /// <summary>This use as a given rarity rolls it.</summary>
    /// <remarks>
    /// Rarity buys size, never duration, so one catalog entry covers the whole range and a Rare
    /// potion is better than a Common one with no new rule. A zero stays zero on purpose: a
    /// Smoke Pellet's magnitude is unused and a Rare one must not start healing, and Weakened
    /// carries no magnitude at all.
    /// </remarks>
    public ConsumableUse At(Rarity rarity)
    {
        var bonus = RarityRules.BonusFor(rarity);

        return this with
        {
            Heal = Heal == 0 ? 0 : Heal + bonus,
            Magnitude = Magnitude == 0 ? 0 : Magnitude + bonus
        };
    }

    /// <summary>What the item card says this does, at the rarity that was rolled.</summary>
    public string Describe(string monsterWord = "the monster")
    {
        var parts = new List<string>(2);

        if (Heal > 0)
        {
            parts.Add($"Restores {Heal} hit points.");
        }

        if (Kind is { } kind)
        {
            var subject = Target == EffectTarget.Player ? "you" : monsterWord;

            parts.Add(kind switch
            {
                EffectKind.Weakened => $"Leaves {subject} swinging at disadvantage for {Rounds} rounds.",
                EffectKind.Empowered => $"Adds {Magnitude} to {subject} attacks and damage for {Rounds} rounds.",
                EffectKind.Guarded => $"Makes {subject} {Magnitude} harder to hit for {Rounds} rounds.",
                EffectKind.Poisoned => $"Poisons {subject} for {Magnitude} a round, {Rounds} rounds.",
                _ => $"Knits {subject} back {Magnitude} a round, {Rounds} rounds."
            });
        }

        return string.Join(" ", parts);
    }
}

/// <param name="BonusAbility">
/// Which ability the rarity bonus lands on. For a weapon this raises attack and damage
/// together, which is why a rare sword feels different from a common one.
/// </param>
public sealed record ItemDefinition(
    string Key,
    string Name,
    ItemSlot Slot,
    string Blurb,
    string? DamageNotation = null,
    bool Finesse = false,
    int ArmourBonus = 0,
    Ability? BonusAbility = null,
    int BaseValue = 10,
    /// <summary>
    /// What using one does, for a <see cref="ItemSlot.Consumable"/>. Null for everything worn.
    /// </summary>
    /// <remarks>
    /// Trailing with a default on purpose, the same precedent MonsterDefinition.Phases follows:
    /// every existing construction site, tests included, keeps compiling untouched. A catalog
    /// integrity test holds the two in step, so a consumable can never be added without one and
    /// a sword can never be given one.
    /// </remarks>
    ConsumableUse? Use = null)
{
    public DiceExpression? Damage =>
        DamageNotation is null ? null : DiceExpression.Parse(DamageNotation);

    public AbilityScores AbilityBonusesAt(Rarity rarity)
    {
        var bonus = RarityRules.BonusFor(rarity);

        return BonusAbility is null || bonus == 0 ? Zero : Zero.With(BonusAbility.Value, bonus);
    }

    public int ArmourBonusAt(Rarity rarity) =>
        Slot == ItemSlot.Armour ? ArmourBonus + RarityRules.BonusFor(rarity) : ArmourBonus;

    public int ValueAt(Rarity rarity) => BaseValue * RarityRules.ValueMultiplier(rarity);

    /// <summary>All six bonuses at zero, the additive identity for ability bonuses.</summary>
    public static AbilityScores Zero => AbilityScores.Zero;
}

/// <summary>Code-held, following DEC-004. Only the key and rolled rarity are persisted.</summary>
public static class ItemCatalog
{
    // Starting gear
    public const string RustyLongsword = "rusty-longsword";
    public const string WornDagger = "worn-dagger";
    public const string CrackedQuarterstaff = "cracked-quarterstaff";
    public const string PlainMace = "plain-mace";
    public const string HuntingBow = "hunting-bow";
    public const string LeatherArmour = "leather-armour";
    public const string ChainShirt = "chain-shirt";
    public const string TravellersRobes = "travellers-robes";

    // Drops
    public const string GoblinCleaver = "goblin-cleaver";
    public const string SilveredBlade = "silvered-blade";
    public const string WardingShield = "warding-shield";
    public const string ScaleMail = "scale-mail";
    public const string BootsOfSpeed = "boots-of-speed";
    public const string AmuletOfInsight = "amulet-of-insight";
    public const string RingOfVigour = "ring-of-vigour";
    public const string CharmOfPresence = "charm-of-presence";
    public const string DragonfangSpear = "dragonfang-spear";

    // Expanded gear
    public const string IronMace = "iron-mace";
    public const string OakenStaff = "oaken-staff";
    public const string DuellingRapier = "duelling-rapier";
    public const string GreatAxe = "great-axe";
    public const string RunedWand = "runed-wand";
    public const string LongbowOfTheVale = "longbow-of-the-vale";
    public const string ChoirmastersLute = "choirmasters-lute";
    public const string ReliquaryHammer = "reliquary-hammer";
    public const string PaddedJerkin = "padded-jerkin";
    public const string StuddedLeather = "studded-leather";
    public const string BreastplateOfDawn = "breastplate-of-dawn";
    public const string ShadowweaveCloak = "shadowweave-cloak";
    public const string RingOfFocus = "ring-of-focus";
    public const string PendantOfTheBear = "pendant-of-the-bear";
    public const string GlovesOfTheThief = "gloves-of-the-thief";
    public const string CircletOfClarity = "circlet-of-clarity";
    public const string LuckyCoin = "lucky-coin";

    // Phase 3 weapons. Every ability governs at least three now, at a low, a middle and a
    // high price, so no class is ever offered a shelf it cannot use.
    public const string MilitiaSpear = "militia-spear";
    public const string BeardedAxe = "bearded-axe";
    public const string SiegeMaul = "siege-maul";
    public const string ThrowingKnives = "throwing-knives";
    public const string PoachersShortbow = "poachers-shortbow";
    public const string CavalrySabre = "cavalry-sabre";
    public const string BoarSpear = "boar-spear";
    public const string IronFlail = "iron-flail";
    public const string BulwarkHalberd = "bulwark-halberd";
    public const string ApprenticeRod = "apprentice-rod";
    public const string OrreryStaff = "orrery-staff";
    public const string LodestoneSceptre = "lodestone-sceptre";
    public const string PilgrimsCudgel = "pilgrims-cudgel";
    public const string CenserFlail = "censer-flail";
    public const string OathkeepersMaul = "oathkeepers-maul";
    public const string HeraldsBaton = "heralds-baton";
    public const string OratorsCane = "orators-cane";
    public const string BannerSpear = "banner-spear";

    // Phase 3 armour
    public const string OilskinCloak = "oilskin-cloak";
    public const string AcolytesVestment = "acolytes-vestment";
    public const string HideHarness = "hide-harness";
    public const string RingmailVest = "ringmail-vest";
    public const string WayfarersCoat = "wayfarers-coat";
    public const string Brigandine = "brigandine";
    public const string ChainHauberk = "chain-hauberk";
    public const string ArcanistsWeave = "arcanists-weave";
    public const string TemplarsCuirass = "templars-cuirass";
    public const string DuellistsHalfPlate = "duellists-half-plate";
    public const string TowerShield = "tower-shield";
    public const string GravewatchPlate = "gravewatch-plate";

    // Phase 3 trinkets
    public const string IronBand = "iron-band";
    public const string QuarrymansGauntlets = "quarrymans-gauntlets";
    public const string TumblersSash = "tumblers-sash";
    public const string QuickstringBracer = "quickstring-bracer";
    public const string HeartwoodToken = "heartwood-token";
    public const string OxhideBelt = "oxhide-belt";
    public const string CartographersLens = "cartographers-lens";
    public const string PhilosophersInkstone = "philosophers-inkstone";
    public const string AugursBeads = "augurs-beads";
    public const string HermitsBell = "hermits-bell";
    public const string GuildSignet = "guild-signet";
    public const string EnvoysTorc = "envoys-torc";

    // Phase 5 consumables. Used up rather than worn, so they never roll an affix and they
    // stack: one row per key per rarity with a count, enforced by a partial unique index.
    public const string DraughtOfMending = "draught-of-mending";
    public const string VialOfSerpentsKiss = "vial-of-serpents-kiss";
    public const string WhetstoneOil = "whetstone-oil";
    public const string ElixirOfStone = "elixir-of-stone";
    public const string SmokePellet = "smoke-pellet";

    // Quest rewards for the task-facing quests. Named for the work rather than the fight,
    // because that is what earns them.
    public const string TravellersCloak = "travellers-cloak";
    public const string RingOfTheDiligent = "ring-of-the-diligent";
    public const string LedgerOfDebts = "ledger-of-debts";
    public const string BannerbearersTorc = "bannerbearers-torc";
    public const string QuartermastersTally = "quartermasters-tally";
    public const string PlainIronBand = "plain-iron-band";
    public const string ClerksSpectacles = "clerks-spectacles";
    public const string WorkmansGloves = "workmans-gloves";

    public static IReadOnlyList<ItemDefinition> All { get; } =
    [
        // --- Weapons ---------------------------------------------------------
        new(RustyLongsword, "Rusty Longsword", ItemSlot.Weapon,
            "Serviceable, if you do not look too closely at the edge.",
            DamageNotation: "1d8", BonusAbility: Ability.Strength, BaseValue: 12),

        new(WornDagger, "Worn Dagger", ItemSlot.Weapon,
            "Small, quick, and honest about what it is.",
            DamageNotation: "1d4", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 8),

        new(CrackedQuarterstaff, "Cracked Quarterstaff", ItemSlot.Weapon,
            "More walking stick than weapon, but it swings.",
            DamageNotation: "1d6", BonusAbility: Ability.Intelligence, BaseValue: 8),

        new(PlainMace, "Plain Mace", ItemSlot.Weapon,
            "Blunt instrument, blunt purpose.",
            DamageNotation: "1d6", BonusAbility: Ability.Wisdom, BaseValue: 10),

        new(HuntingBow, "Hunting Bow", ItemSlot.Weapon,
            "Draws smoothly. Smells faintly of pine.",
            DamageNotation: "1d8", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 14),

        new(GoblinCleaver, "Goblin Cleaver", ItemSlot.Weapon,
            "Notched from use rather than neglect.",
            DamageNotation: "1d8", BonusAbility: Ability.Strength, BaseValue: 25),

        new(SilveredBlade, "Silvered Blade", ItemSlot.Weapon,
            "Cold to the touch, and quicker than it looks.",
            DamageNotation: "1d8", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 40),

        new(DragonfangSpear, "Dragonfang Spear", ItemSlot.Weapon,
            "The tooth is real. Nobody asks where it came from.",
            DamageNotation: "1d10", BonusAbility: Ability.Strength, BaseValue: 90),

        // --- Armour ----------------------------------------------------------
        new(TravellersRobes, "Traveller's Robes", ItemSlot.Armour,
            "Comfortable. Not, strictly speaking, protective.",
            ArmourBonus: 1, BonusAbility: Ability.Intelligence, BaseValue: 8),

        new(LeatherArmour, "Leather Armour", ItemSlot.Armour,
            "Quiet, flexible, and better than nothing.",
            ArmourBonus: 2, BonusAbility: Ability.Dexterity, BaseValue: 12),

        new(ChainShirt, "Chain Shirt", ItemSlot.Armour,
            "Heavy on the shoulders, reassuring everywhere else.",
            ArmourBonus: 3, BonusAbility: Ability.Constitution, BaseValue: 20),

        new(ScaleMail, "Scale Mail", ItemSlot.Armour,
            "Overlapping plates that rattle when you run.",
            ArmourBonus: 4, BonusAbility: Ability.Constitution, BaseValue: 45),

        new(WardingShield, "Warding Shield", ItemSlot.Armour,
            "Someone painted a sigil on it. It may even help.",
            ArmourBonus: 5, BonusAbility: Ability.Wisdom, BaseValue: 70),

        // --- Trinkets ---------------------------------------------------------
        new(BootsOfSpeed, "Boots of Speed", ItemSlot.Trinket,
            "You arrive slightly before you expect to.",
            BonusAbility: Ability.Dexterity, BaseValue: 30),

        new(AmuletOfInsight, "Amulet of Insight", ItemSlot.Trinket,
            "Problems look smaller while you wear it.",
            BonusAbility: Ability.Intelligence, BaseValue: 30),

        new(RingOfVigour, "Ring of Vigour", ItemSlot.Trinket,
            "A steady warmth, somewhere behind the sternum.",
            BonusAbility: Ability.Constitution, BaseValue: 35),

        new(CharmOfPresence, "Charm of Presence", ItemSlot.Trinket,
            "People finish their sentences more generously around you.",
            BonusAbility: Ability.Charisma, BaseValue: 30),

        // --- Expanded weapons -------------------------------------------------
        new(IronMace, "Iron Mace", ItemSlot.Weapon,
            "No edge to dull, which is rather the point.",
            DamageNotation: "1d8", BonusAbility: Ability.Wisdom, BaseValue: 22),

        new(OakenStaff, "Oaken Staff", ItemSlot.Weapon,
            "Worn smooth where a hand has gripped it for years.",
            DamageNotation: "1d8", BonusAbility: Ability.Intelligence, BaseValue: 26),

        new(DuellingRapier, "Duelling Rapier", ItemSlot.Weapon,
            "Balanced for someone who intends to be precise about this.",
            DamageNotation: "1d8", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 35),

        new(GreatAxe, "Great Axe", ItemSlot.Weapon,
            "Two hands, one purpose.",
            DamageNotation: "1d12", BonusAbility: Ability.Strength, BaseValue: 55),

        new(RunedWand, "Runed Wand", ItemSlot.Weapon,
            "The runes shift when you are not looking directly at them.",
            DamageNotation: "1d6", Finesse: true, BonusAbility: Ability.Intelligence, BaseValue: 48),

        new(LongbowOfTheVale, "Longbow of the Vale", ItemSlot.Weapon,
            "Draws like it wants to be drawn.",
            DamageNotation: "1d10", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 65),

        new(ChoirmastersLute, "Choirmaster's Lute", ItemSlot.Weapon,
            "Surprisingly solid. The strings are almost incidental.",
            DamageNotation: "1d6", Finesse: true, BonusAbility: Ability.Charisma, BaseValue: 40),

        new(ReliquaryHammer, "Reliquary Hammer", ItemSlot.Weapon,
            "Something small and holy rattles in the head of it.",
            DamageNotation: "1d10", BonusAbility: Ability.Wisdom, BaseValue: 72),

        // --- Expanded armour --------------------------------------------------
        new(PaddedJerkin, "Padded Jerkin", ItemSlot.Armour,
            "Warm, at least.",
            ArmourBonus: 1, BonusAbility: Ability.Constitution, BaseValue: 6),

        new(StuddedLeather, "Studded Leather", ItemSlot.Armour,
            "The studs are more useful than they look.",
            ArmourBonus: 3, BonusAbility: Ability.Dexterity, BaseValue: 28),

        new(BreastplateOfDawn, "Breastplate of Dawn", ItemSlot.Armour,
            "Catches the light even indoors, which is either holy or a nuisance.",
            ArmourBonus: 5, BonusAbility: Ability.Charisma, BaseValue: 80),

        new(ShadowweaveCloak, "Shadowweave Cloak", ItemSlot.Armour,
            "You keep losing track of your own sleeves.",
            ArmourBonus: 4, BonusAbility: Ability.Dexterity, BaseValue: 68),

        // --- Expanded trinkets -------------------------------------------------
        new(RingOfFocus, "Ring of Focus", ItemSlot.Trinket,
            "The noise recedes a little while you wear it.",
            BonusAbility: Ability.Wisdom, BaseValue: 32),

        new(PendantOfTheBear, "Pendant of the Bear", ItemSlot.Trinket,
            "Heavy, and you find you do not mind carrying it.",
            BonusAbility: Ability.Strength, BaseValue: 34),

        new(GlovesOfTheThief, "Gloves of the Thief", ItemSlot.Trinket,
            "Fingertips worn thin from honest work, allegedly.",
            BonusAbility: Ability.Dexterity, BaseValue: 38),

        new(CircletOfClarity, "Circlet of Clarity", ItemSlot.Trinket,
            "Thoughts arrive already in order.",
            BonusAbility: Ability.Intelligence, BaseValue: 42),

        new(LuckyCoin, "Lucky Coin", ItemSlot.Trinket,
            "It has come up heads every time so far. Every single time.",
            BonusAbility: Ability.Charisma, BaseValue: 26),

        // --- Phase 3 weapons ---------------------------------------------------
        // Added under DEC-004, which is why this is a list edit and not a migration: the
        // key is the only part of an item that was ever written to a row.
        new(MilitiaSpear, "Militia Spear", ItemSlot.Weapon,
            "Issued by the hundred, and the finish says so.",
            DamageNotation: "1d6", BonusAbility: Ability.Strength, BaseValue: 9),

        new(BeardedAxe, "Bearded Axe", ItemSlot.Weapon,
            "The hook below the blade is for pulling shields aside.",
            DamageNotation: "1d8", BonusAbility: Ability.Strength, BaseValue: 30),

        new(SiegeMaul, "Siege Maul", ItemSlot.Weapon,
            "Built for doors. It has never been asked to stop there.",
            DamageNotation: "1d12", BonusAbility: Ability.Strength, BaseValue: 78),

        new(ThrowingKnives, "Throwing Knives", ItemSlot.Weapon,
            "Sold in threes, on the assumption you will not get two back.",
            DamageNotation: "1d4", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 11),

        new(PoachersShortbow, "Poacher's Shortbow", ItemSlot.Weapon,
            "Unstrung it passes for a walking stick, which is the point.",
            DamageNotation: "1d6", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 16),

        new(CavalrySabre, "Cavalry Sabre", ItemSlot.Weapon,
            "Curved so the draw and the cut are the same motion.",
            DamageNotation: "1d8", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 46),

        // The first weapons to govern with Constitution. ChooseAttackAbility already reads
        // whatever the item names, so the hole was a missing item rather than a missing branch.
        new(BoarSpear, "Boar Spear", ItemSlot.Weapon,
            "The crossbar stops the boar reaching you. Usually in time.",
            DamageNotation: "1d6", BonusAbility: Ability.Constitution, BaseValue: 13),

        new(IronFlail, "Iron Flail", ItemSlot.Weapon,
            "The chain does half the work and none of the aiming.",
            DamageNotation: "1d8", BonusAbility: Ability.Constitution, BaseValue: 33),

        new(BulwarkHalberd, "Bulwark Halberd", ItemSlot.Weapon,
            "Long enough to keep the fight where you decided to have it.",
            DamageNotation: "1d10", BonusAbility: Ability.Constitution, BaseValue: 74),

        new(ApprenticeRod, "Apprentice Rod", ItemSlot.Weapon,
            "Plain ash, with a term of notes pencilled down the shaft.",
            DamageNotation: "1d6", BonusAbility: Ability.Intelligence, BaseValue: 11),

        new(OrreryStaff, "Orrery Staff", ItemSlot.Weapon,
            "The brass rings turn whether or not anyone winds them.",
            DamageNotation: "1d8", BonusAbility: Ability.Intelligence, BaseValue: 38),

        new(LodestoneSceptre, "Lodestone Sceptre", ItemSlot.Weapon,
            "Every nail in the room leans a little toward it.",
            DamageNotation: "1d10", BonusAbility: Ability.Intelligence, BaseValue: 76),

        new(PilgrimsCudgel, "Pilgrim's Cudgel", ItemSlot.Weapon,
            "Carried the whole road and used twice.",
            DamageNotation: "1d6", BonusAbility: Ability.Wisdom, BaseValue: 11),

        new(CenserFlail, "Censer Flail", ItemSlot.Weapon,
            "Swung in procession first and in anger second.",
            DamageNotation: "1d8", BonusAbility: Ability.Wisdom, BaseValue: 34),

        new(OathkeepersMaul, "Oathkeeper's Maul", ItemSlot.Weapon,
            "Heavy enough that you finish the thought before you lift it.",
            DamageNotation: "1d12", BonusAbility: Ability.Wisdom, BaseValue: 84),

        new(HeraldsBaton, "Herald's Baton", ItemSlot.Weapon,
            "Made for pointing at things. Adequate for the rest.",
            DamageNotation: "1d4", Finesse: true, BonusAbility: Ability.Charisma, BaseValue: 9),

        new(OratorsCane, "Orator's Cane", ItemSlot.Weapon,
            "Weighted at the head, for emphasis.",
            DamageNotation: "1d6", Finesse: true, BonusAbility: Ability.Charisma, BaseValue: 24),

        new(BannerSpear, "Banner Spear", ItemSlot.Weapon,
            "People follow the flag, which is hard on whoever holds it.",
            DamageNotation: "1d8", BonusAbility: Ability.Charisma, BaseValue: 50),

        // --- Phase 3 armour ----------------------------------------------------
        new(OilskinCloak, "Oilskin Cloak", ItemSlot.Armour,
            "Keeps the rain out. Keeps very little else out.",
            ArmourBonus: 1, BonusAbility: Ability.Dexterity, BaseValue: 7),

        new(AcolytesVestment, "Acolyte's Vestment", ItemSlot.Armour,
            "Laundered far more often than it is mended.",
            ArmourBonus: 1, BonusAbility: Ability.Wisdom, BaseValue: 9),

        new(HideHarness, "Hide Harness", ItemSlot.Armour,
            "Cut from something that objected at the time.",
            ArmourBonus: 2, BonusAbility: Ability.Strength, BaseValue: 14),

        new(RingmailVest, "Ring Mail Vest", ItemSlot.Armour,
            "Rings sewn flat to leather, and you can hear every one.",
            ArmourBonus: 2, BonusAbility: Ability.Constitution, BaseValue: 16),

        new(WayfarersCoat, "Wayfarer's Coat", ItemSlot.Armour,
            "Cut well enough that people assume you were expected.",
            ArmourBonus: 2, BonusAbility: Ability.Charisma, BaseValue: 18),

        new(Brigandine, "Brigandine", ItemSlot.Armour,
            "The plates are riveted inside the cloth, where you cannot check them.",
            ArmourBonus: 3, BonusAbility: Ability.Constitution, BaseValue: 30),

        new(ChainHauberk, "Chain Hauberk", ItemSlot.Armour,
            "Down to the knee, and you feel every step of it.",
            ArmourBonus: 3, BonusAbility: Ability.Strength, BaseValue: 34),

        new(ArcanistsWeave, "Arcanist's Weave", ItemSlot.Armour,
            "The threads hum when the weather is about to turn.",
            ArmourBonus: 3, BonusAbility: Ability.Intelligence, BaseValue: 36),

        new(TemplarsCuirass, "Templar's Cuirass", ItemSlot.Armour,
            "Dented in the same place three times and repaired twice.",
            ArmourBonus: 4, BonusAbility: Ability.Wisdom, BaseValue: 58),

        new(DuellistsHalfPlate, "Duellist's Half Plate", ItemSlot.Armour,
            "Articulated at the elbow, because the arm matters more than the ribs.",
            ArmourBonus: 4, BonusAbility: Ability.Dexterity, BaseValue: 62),

        new(TowerShield, "Tower Shield", ItemSlot.Armour,
            "You can set it down and stand behind it, which is most of the idea.",
            ArmourBonus: 5, BonusAbility: Ability.Strength, BaseValue: 84),

        new(GravewatchPlate, "Gravewatch Plate", ItemSlot.Armour,
            "Issued to whoever stands the night watch, and returned less often than issued.",
            ArmourBonus: 5, BonusAbility: Ability.Constitution, BaseValue: 88),

        // --- Phase 3 trinkets --------------------------------------------------
        // Priced inside the band the existing trinkets already occupy. A trinket's whole
        // effect is its rarity bonus, so a cheaper one is not a weaker item, it is the same
        // item with a shorter road to Legendary.
        new(IronBand, "Iron Band", ItemSlot.Trinket,
            "Plain, heavy, and not inclined to come off.",
            BonusAbility: Ability.Strength, BaseValue: 28),

        new(QuarrymansGauntlets, "Quarryman's Gauntlets", ItemSlot.Trinket,
            "The grip stays shut a moment after you decide to open it.",
            BonusAbility: Ability.Strength, BaseValue: 46),

        new(TumblersSash, "Tumbler's Sash", ItemSlot.Trinket,
            "You find your footing half a step earlier than you used to.",
            BonusAbility: Ability.Dexterity, BaseValue: 29),

        new(QuickstringBracer, "Quickstring Bracer", ItemSlot.Trinket,
            "The string comes back before the arm does.",
            BonusAbility: Ability.Dexterity, BaseValue: 44),

        new(HeartwoodToken, "Heartwood Token", ItemSlot.Trinket,
            "Cut from the middle of the tree, where the years are tightest.",
            BonusAbility: Ability.Constitution, BaseValue: 27),

        new(OxhideBelt, "Oxhide Belt", ItemSlot.Trinket,
            "Broad and plain, and it takes the weight off the back.",
            BonusAbility: Ability.Constitution, BaseValue: 40),

        new(CartographersLens, "Cartographer's Lens", ItemSlot.Trinket,
            "Distances stop lying to you.",
            BonusAbility: Ability.Intelligence, BaseValue: 31),

        new(PhilosophersInkstone, "Philosopher's Inkstone", ItemSlot.Trinket,
            "The argument arranges itself while you grind the ink.",
            BonusAbility: Ability.Intelligence, BaseValue: 50),

        new(AugursBeads, "Augur's Beads", ItemSlot.Trinket,
            "You count them without deciding to.",
            BonusAbility: Ability.Wisdom, BaseValue: 28),

        new(HermitsBell, "Hermit's Bell", ItemSlot.Trinket,
            "It rings just before something happens. Usually nothing does.",
            BonusAbility: Ability.Wisdom, BaseValue: 48),

        new(GuildSignet, "Guild Signet", ItemSlot.Trinket,
            "Doors that were shut turn out to be merely closed.",
            BonusAbility: Ability.Charisma, BaseValue: 26),

        new(EnvoysTorc, "Envoy's Torc", ItemSlot.Trinket,
            "Worn so it can be read from the far end of a hall.",
            BonusAbility: Ability.Charisma, BaseValue: 46),

        // --- Phase 5 consumables ------------------------------------------------
        // Priced above a trinket of comparable effect on purpose: a trinket is bought once and
        // worn forever, and one of these is gone the moment it works. Deliberately absent from
        // every monster's loot table, and a catalog test holds them out of one: a loot table's
        // summed weight is the die size PickWeighted rolls, so an added entry would change
        // which item an existing seeded script is handed with no change in the roll count to
        // make the break visible.
        new(DraughtOfMending, "Draught of Mending", ItemSlot.Consumable,
            "Tastes of iron filings and someone else's garden.",
            BaseValue: 30,
            Use: new ConsumableUse(Heal: 8, Kind: null, EffectTarget.Player, Rounds: 0, Magnitude: 0)),

        new(VialOfSerpentsKiss, "Vial of Serpent's Kiss", ItemSlot.Consumable,
            "Milked from something that objected, and sold by someone who did not.",
            BaseValue: 45,
            Use: new ConsumableUse(
                Heal: 0, EffectKind.Poisoned, EffectTarget.Monster, Rounds: 3, Magnitude: 3)),

        new(WhetstoneOil, "Whetstone Oil", ItemSlot.Consumable,
            "Half a minute's work and the edge remembers what it was for.",
            BaseValue: 40,
            Use: new ConsumableUse(
                Heal: 0, EffectKind.Empowered, EffectTarget.Player, Rounds: 3, Magnitude: 1)),

        new(ElixirOfStone, "Elixir of Stone", ItemSlot.Consumable,
            "Thick, grey, and it settles before you can finish it.",
            BaseValue: 40,
            Use: new ConsumableUse(
                Heal: 0, EffectKind.Guarded, EffectTarget.Player, Rounds: 3, Magnitude: 1)),

        new(SmokePellet, "Smoke Pellet", ItemSlot.Consumable,
            "Throw it down and be somewhere else by the time it clears.",
            BaseValue: 35,
            Use: new ConsumableUse(
                Heal: 0, EffectKind.Weakened, EffectTarget.Monster, Rounds: 2, Magnitude: 0)),

        // --- Earned by working rather than by winning -------------------------------
        //
        // Every one of these is a quest reward on the task-facing quests, so the only way to
        // hold one is to have finished things. They sit deliberately mid-table: the point is
        // that they exist, not that they beat what a dragon drops.

        new(TravellersCloak, "Traveller's Cloak", ItemSlot.Armour,
            "Worn thin at the shoulders from being put on early.",
            ArmourBonus: 2, BonusAbility: Ability.Constitution, BaseValue: 34),

        new(WorkmansGloves, "Workman's Gloves", ItemSlot.Armour,
            "Shaped to one pair of hands, and not yours, but they will do.",
            ArmourBonus: 1, BonusAbility: Ability.Strength, BaseValue: 26),

        new(RingOfTheDiligent, "Ring of the Diligent", ItemSlot.Trinket,
            "Plain, and heavier than it looks.",
            BonusAbility: Ability.Constitution, BaseValue: 48),

        new(PlainIronBand, "Plain Iron Band", ItemSlot.Trinket,
            "No maker's mark. Somebody wore it every day for a very long time.",
            BonusAbility: Ability.Wisdom, BaseValue: 28),

        new(LedgerOfDebts, "Ledger of Debts", ItemSlot.Trinket,
            "Every entry crossed through. The crossing out took longer than the writing.",
            BonusAbility: Ability.Intelligence, BaseValue: 62),

        new(BannerbearersTorc, "Bannerbearer's Torc", ItemSlot.Trinket,
            "Five marks around the band, none of them decorative.",
            BonusAbility: Ability.Charisma, BaseValue: 70),

        new(QuartermastersTally, "Quartermaster's Tally", ItemSlot.Trinket,
            "Counts what is left rather than what was promised.",
            BonusAbility: Ability.Wisdom, BaseValue: 44),

        new(ClerksSpectacles, "Clerk's Spectacles", ItemSlot.Trinket,
            "The small print stops being an argument.",
            BonusAbility: Ability.Intelligence, BaseValue: 36),
    ];

    private static readonly Dictionary<string, ItemDefinition> ByKey =
        All.ToDictionary(i => i.Key, StringComparer.Ordinal);

    public static ItemDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);
}
