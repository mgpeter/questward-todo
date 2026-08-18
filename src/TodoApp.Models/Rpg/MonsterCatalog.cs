using TodoApp.Models.Dice;

namespace TodoApp.Models.Rpg;

/// <param name="Weight">Relative chance within the table. Not a percentage; weights are summed.</param>
public sealed record LootEntry(string ItemKey, int Weight);

/// <summary>
/// A gear a boss changes into partway through a fight.
/// </summary>
/// <remarks>
/// Code-held with everything else about a monster (DEC-004), but only the name is resolved live:
/// a row stores the number of the phase entered, and <see cref="MonsterDefinition.PhaseDefinition"/>
/// reads the name back from here on every request, so a rename reaches fights in progress.
/// <para>
/// The line and the entry effects do not work that way, and a balance change has to know it. The
/// line is composed into the combat log when the phase is entered and the log is history from
/// then on; the effects are applied once onto that fight's own effect board, where their rounds
/// are then spent down. So a retune reaches only fights that have not yet crossed the threshold.
/// Re-resolving either on the read path is not the fix: it would fight the spend-on-use counter
/// and the refresh-not-stack rule in <see cref="StatusEffects"/>.
/// </para>
/// </remarks>
/// <param name="AtPercent">
/// Entered when current hit points fall to or below this percent of the maximum. Declared from
/// the highest threshold down, which a catalog integrity test enforces.
/// </param>
/// <param name="Line">The mechanical clause the log carries when the phase is entered.</param>
/// <param name="OnEntry">
/// Applied once, on entry. Magnitudes are fixed here rather than rolled, which is what keeps a
/// phase change out of the blast radius of every hard-coded dice script in the suite.
/// </param>
public sealed record MonsterPhase(
    int AtPercent,
    string Name,
    string Line,
    IReadOnlyList<StatusEffect> OnEntry);

public sealed record MonsterDefinition(
    string Key,
    string Name,
    string Blurb,
    int Level,
    int ArmourClass,
    int MaxHitPoints,
    int AttackBonus,
    string DamageNotation,
    int MinGold,
    int MaxGold,
    /// <summary>Chance in 100 that a win drops anything at all.</summary>
    int DropChance,
    IReadOnlyList<LootEntry> LootTable,
    /// <summary>
    /// The gears this monster changes through, highest threshold first. Null for anything that
    /// fights the same way all the way down.
    /// </summary>
    /// <remarks>
    /// Trailing with a default on purpose, the precedent EquipmentEffects already set: every
    /// existing construction site, tests included, keeps compiling untouched.
    /// </remarks>
    IReadOnlyList<MonsterPhase>? Phases = null)
{
    public DiceExpression Damage => DiceExpression.Parse(DamageNotation);

    /// <summary>
    /// Which phase a monster on this many hit points belongs in. Zero means untouched by any
    /// threshold.
    /// </summary>
    /// <remarks>
    /// Cross-multiplied rather than dividing into a percentage, because integer division would
    /// put a 7 hit point Giant Rat into its first phase the moment it took a scratch, while a
    /// 132 hit point dragon rounded the other way. One rule has to hold at both ends of the
    /// bestiary.
    /// </remarks>
    public int PhaseAt(int currentHitPoints) =>
        Phases is null ? 0 : Phases.Count(p => currentHitPoints * 100 <= p.AtPercent * MaxHitPoints);

    /// <summary>The definition of a phase by its stored number, or null when there is none.</summary>
    public MonsterPhase? PhaseDefinition(int phase) =>
        Phases is not null && phase >= 1 && phase <= Phases.Count ? Phases[phase - 1] : null;

    /// <summary>
    /// Monsters from two levels below the character to one level above, so the tavern always
    /// has something to fight without offering a certain death. The band is deliberately
    /// asymmetric: an easy fight is a valid choice, an unwinnable one is not.
    /// </summary>
    /// <remarks>
    /// A monster of level N is therefore offered to characters N-1 through N+2, which is the
    /// arithmetic the level coverage test relies on. Other phases depend on the band as
    /// written; widening or narrowing it re-plans the whole bestiary.
    /// <para>
    /// The character level is clamped to the deepest level the bestiary actually goes to
    /// before the band is applied. Unclamped, the band walks off the end of the catalog: a
    /// character at level 17 or above has no opponent at all, so the tavern list is empty,
    /// every start is refused as out of range, and stamina, gold, loot, the codex and every
    /// combat quest go inert. Levels only ever rise, so there is no way back out of that.
    /// </para>
    /// </remarks>
    public bool IsAvailableAt(int characterLevel) => IsInBand(MonsterCatalog.BandLevel(characterLevel));

    private bool IsInBand(int level) => Level <= level + 1 && Level >= level - 2;
}

/// <summary>Code-held, following DEC-004. Only the key is ever persisted.</summary>
public static class MonsterCatalog
{
    public const string GiantRat = "giant-rat";
    public const string Goblin = "goblin";
    public const string Skeleton = "skeleton";
    public const string DireWolf = "dire-wolf";
    public const string Bandit = "bandit";
    public const string Ogre = "ogre";
    public const string Wraith = "wraith";
    public const string YoungDragon = "young-dragon";
    public const string CarrionCrows = "carrion-crows";
    public const string MireToad = "mire-toad";
    public const string Deserter = "deserter";
    public const string HedgeTroll = "hedge-troll";
    public const string StoneSentinel = "stone-sentinel";
    public const string FenHag = "fen-hag";
    public const string BarrowKnight = "barrow-knight";
    public const string DrownedCrew = "drowned-crew";
    public const string Basilisk = "basilisk";
    public const string Wyvern = "wyvern";
    public const string ElderDragon = "elder-dragon";

    // Filling the thin levels. Nine of the fourteen rungs carried a single opponent, so the
    // band was doing all the work and two characters a level apart met the same three things.
    // Nothing here is above level 14: adding one would move TopLevel and re-plan the whole
    // band, which the integrity tests would then make you check level by level.
    public const string TollBeetle = "toll-beetle";
    public const string CisternEel = "cistern-eel";
    public const string RustedSentry = "rusted-sentry";
    public const string GraveMoth = "grave-moth";
    public const string SaltWidow = "salt-widow";
    public const string PitForeman = "pit-foreman";
    public const string CairnWight = "cairn-wight";
    public const string LanternWraith = "lantern-wraith";
    public const string ScreeGiant = "scree-giant";
    public const string HollowAbbot = "hollow-abbot";

    public static IReadOnlyList<MonsterDefinition> All { get; } =
    [
        new(GiantRat, "Giant Rat",
            "Startlingly large. Deeply unbothered by you.",
            Level: 1, ArmourClass: 10, MaxHitPoints: 7, AttackBonus: 2, DamageNotation: "1d4",
            MinGold: 1, MaxGold: 5, DropChance: 15,
            [
                new LootEntry(ItemCatalog.WornDagger, 3),
                new LootEntry(ItemCatalog.ThrowingKnives, 3),
                new LootEntry(ItemCatalog.PaddedJerkin, 2),
                new LootEntry(ItemCatalog.OilskinCloak, 2),
                new LootEntry(ItemCatalog.LeatherArmour, 1),
                new LootEntry(ItemCatalog.HeraldsBaton, 1),
                new LootEntry(ItemCatalog.LuckyCoin, 1)
            ]),

        new(Goblin, "Goblin",
            "Small, mean, and better armed than it has any right to be.",
            Level: 1, ArmourClass: 12, MaxHitPoints: 10, AttackBonus: 3, DamageNotation: "1d6",
            MinGold: 3, MaxGold: 12, DropChance: 30,
            [
                new LootEntry(ItemCatalog.GoblinCleaver, 4),
                new LootEntry(ItemCatalog.LeatherArmour, 3),
                new LootEntry(ItemCatalog.MilitiaSpear, 3),
                new LootEntry(ItemCatalog.IronMace, 2),
                new LootEntry(ItemCatalog.GlovesOfTheThief, 2),
                new LootEntry(ItemCatalog.HideHarness, 2),
                new LootEntry(ItemCatalog.PoachersShortbow, 2),
                new LootEntry(ItemCatalog.BootsOfSpeed, 1),
                new LootEntry(ItemCatalog.GuildSignet, 1)
            ]),

        new(Skeleton, "Skeleton",
            "Rattles ominously. Does not appear to hold a grudge, exactly.",
            Level: 2, ArmourClass: 13, MaxHitPoints: 16, AttackBonus: 4, DamageNotation: "1d6+1",
            MinGold: 5, MaxGold: 18, DropChance: 35,
            [
                new LootEntry(ItemCatalog.RustyLongsword, 3),
                new LootEntry(ItemCatalog.ChainShirt, 3),
                new LootEntry(ItemCatalog.AcolytesVestment, 3),
                new LootEntry(ItemCatalog.OakenStaff, 2),
                new LootEntry(ItemCatalog.StuddedLeather, 2),
                new LootEntry(ItemCatalog.RingOfFocus, 2),
                new LootEntry(ItemCatalog.ApprenticeRod, 2),
                new LootEntry(ItemCatalog.PilgrimsCudgel, 2),
                new LootEntry(ItemCatalog.RingmailVest, 2),
                new LootEntry(ItemCatalog.AmuletOfInsight, 1),
                new LootEntry(ItemCatalog.AugursBeads, 1),
                new LootEntry(ItemCatalog.CartographersLens, 1)
            ]),

        // Phase 4 monsters carry their own loot tables rather than joining an existing one.
        // LootService.PickWeighted rolls once against a table's summed weight and walks it in
        // declaration order, so reweighting or extending a table an existing seeded test can
        // reach would change which item that test is handed, with no change to the roll count
        // to make the break visible.
        new(CarrionCrows, "Carrion Flock",
            "It arrives before there is anything to arrive for.",
            Level: 2, ArmourClass: 13, MaxHitPoints: 14, AttackBonus: 4, DamageNotation: "1d6+1",
            MinGold: 4, MaxGold: 16, DropChance: 35,
            [
                new LootEntry(ItemCatalog.ThrowingKnives, 3),
                new LootEntry(ItemCatalog.OilskinCloak, 3),
                new LootEntry(ItemCatalog.LuckyCoin, 2),
                new LootEntry(ItemCatalog.PaddedJerkin, 2),
                new LootEntry(ItemCatalog.HeraldsBaton, 2),
                new LootEntry(ItemCatalog.CartographersLens, 2),
                new LootEntry(ItemCatalog.GuildSignet, 2),
                new LootEntry(ItemCatalog.PoachersShortbow, 2),
                new LootEntry(ItemCatalog.RingmailVest, 1),
                new LootEntry(ItemCatalog.AugursBeads, 1)
            ]),

        new(Bandit, "Bandit",
            "Wants your gold. Has clearly done this before.",
            Level: 3, ArmourClass: 13, MaxHitPoints: 22, AttackBonus: 4, DamageNotation: "1d8",
            MinGold: 12, MaxGold: 35, DropChance: 40,
            [
                new LootEntry(ItemCatalog.DuellingRapier, 3),
                new LootEntry(ItemCatalog.StuddedLeather, 3),
                new LootEntry(ItemCatalog.WayfarersCoat, 3),
                new LootEntry(ItemCatalog.SilveredBlade, 2),
                new LootEntry(ItemCatalog.CavalrySabre, 2),
                new LootEntry(ItemCatalog.ChoirmastersLute, 2),
                new LootEntry(ItemCatalog.OratorsCane, 2),
                new LootEntry(ItemCatalog.Brigandine, 2),
                new LootEntry(ItemCatalog.CharmOfPresence, 2),
                new LootEntry(ItemCatalog.TumblersSash, 2),
                new LootEntry(ItemCatalog.EnvoysTorc, 1)
            ]),

        new(MireToad, "Mire Toad",
            "Still water, then a tongue. It never appears to have moved.",
            Level: 3, ArmourClass: 13, MaxHitPoints: 23, AttackBonus: 4, DamageNotation: "1d8",
            MinGold: 10, MaxGold: 30, DropChance: 40,
            [
                new LootEntry(ItemCatalog.BoarSpear, 3),
                new LootEntry(ItemCatalog.HideHarness, 3),
                new LootEntry(ItemCatalog.MilitiaSpear, 3),
                new LootEntry(ItemCatalog.OilskinCloak, 2),
                new LootEntry(ItemCatalog.WayfarersCoat, 2),
                new LootEntry(ItemCatalog.HuntingBow, 2),
                new LootEntry(ItemCatalog.HeartwoodToken, 2),
                new LootEntry(ItemCatalog.PoachersShortbow, 2),
                new LootEntry(ItemCatalog.IronMace, 2),
                new LootEntry(ItemCatalog.LuckyCoin, 1),
                new LootEntry(ItemCatalog.QuickstringBracer, 1)
            ]),

        new(DireWolf, "Dire Wolf",
            "Quick, patient, and entirely too interested in your ankles.",
            Level: 4, ArmourClass: 14, MaxHitPoints: 30, AttackBonus: 5, DamageNotation: "2d4+1",
            MinGold: 8, MaxGold: 25, DropChance: 40,
            [
                new LootEntry(ItemCatalog.BootsOfSpeed, 4),
                new LootEntry(ItemCatalog.HuntingBow, 3),
                new LootEntry(ItemCatalog.BoarSpear, 3),
                new LootEntry(ItemCatalog.PoachersShortbow, 3),
                new LootEntry(ItemCatalog.PendantOfTheBear, 2),
                new LootEntry(ItemCatalog.HideHarness, 2),
                new LootEntry(ItemCatalog.HeartwoodToken, 2),
                new LootEntry(ItemCatalog.ShadowweaveCloak, 1),
                new LootEntry(ItemCatalog.RingOfVigour, 1),
                new LootEntry(ItemCatalog.QuickstringBracer, 1)
            ]),

        new(Deserter, "Deserter",
            "Still turns out shaved and in step. Nobody is left to inspect him.",
            Level: 5, ArmourClass: 15, MaxHitPoints: 38, AttackBonus: 6, DamageNotation: "1d12",
            MinGold: 20, MaxGold: 50, DropChance: 45,
            [
                new LootEntry(ItemCatalog.CavalrySabre, 3),
                new LootEntry(ItemCatalog.Brigandine, 3),
                new LootEntry(ItemCatalog.MilitiaSpear, 3),
                new LootEntry(ItemCatalog.RingmailVest, 2),
                new LootEntry(ItemCatalog.IronMace, 2),
                new LootEntry(ItemCatalog.BannerSpear, 2),
                new LootEntry(ItemCatalog.ChainHauberk, 2),
                new LootEntry(ItemCatalog.OxhideBelt, 2),
                new LootEntry(ItemCatalog.WayfarersCoat, 2),
                new LootEntry(ItemCatalog.IronBand, 2),
                new LootEntry(ItemCatalog.DuellingRapier, 1),
                new LootEntry(ItemCatalog.GuildSignet, 1)
            ]),

        new(HedgeTroll, "Hedge Troll",
            "Collects a toll on a bridge nobody maintains. The toll has gone up.",
            Level: 5, ArmourClass: 14, MaxHitPoints: 40, AttackBonus: 5, DamageNotation: "2d6",
            MinGold: 18, MaxGold: 45, DropChance: 45,
            [
                new LootEntry(ItemCatalog.BeardedAxe, 3),
                new LootEntry(ItemCatalog.IronFlail, 3),
                new LootEntry(ItemCatalog.HideHarness, 3),
                new LootEntry(ItemCatalog.StuddedLeather, 2),
                new LootEntry(ItemCatalog.OxhideBelt, 2),
                new LootEntry(ItemCatalog.LuckyCoin, 2),
                new LootEntry(ItemCatalog.BoarSpear, 2),
                new LootEntry(ItemCatalog.QuarrymansGauntlets, 2),
                new LootEntry(ItemCatalog.IronBand, 2),
                new LootEntry(ItemCatalog.WayfarersCoat, 1),
                new LootEntry(ItemCatalog.GreatAxe, 1)
            ]),

        new(Ogre, "Ogre",
            "Slow to anger, slower to stop. Smells of wet rope.",
            Level: 6, ArmourClass: 15, MaxHitPoints: 48, AttackBonus: 6, DamageNotation: "2d6+2",
            MinGold: 25, MaxGold: 70, DropChance: 50,
            [
                new LootEntry(ItemCatalog.ScaleMail, 4),
                new LootEntry(ItemCatalog.GreatAxe, 3),
                new LootEntry(ItemCatalog.GoblinCleaver, 3),
                new LootEntry(ItemCatalog.BeardedAxe, 3),
                new LootEntry(ItemCatalog.ChainHauberk, 3),
                new LootEntry(ItemCatalog.PendantOfTheBear, 2),
                new LootEntry(ItemCatalog.RingOfVigour, 2),
                new LootEntry(ItemCatalog.IronFlail, 2),
                new LootEntry(ItemCatalog.OxhideBelt, 2),
                new LootEntry(ItemCatalog.IronBand, 2),
                new LootEntry(ItemCatalog.DuellistsHalfPlate, 1),
                new LootEntry(ItemCatalog.SiegeMaul, 1)
            ]),

        new(StoneSentinel, "Stone Sentinel",
            "The same stone as the wall it stands in. It is the part that moves.",
            Level: 7, ArmourClass: 16, MaxHitPoints: 54, AttackBonus: 7, DamageNotation: "2d6+3",
            MinGold: 35, MaxGold: 90, DropChance: 52,
            [
                new LootEntry(ItemCatalog.QuarrymansGauntlets, 3),
                new LootEntry(ItemCatalog.IronBand, 3),
                new LootEntry(ItemCatalog.ChainHauberk, 3),
                new LootEntry(ItemCatalog.CartographersLens, 3),
                new LootEntry(ItemCatalog.SiegeMaul, 2),
                new LootEntry(ItemCatalog.TowerShield, 2),
                new LootEntry(ItemCatalog.BulwarkHalberd, 2),
                new LootEntry(ItemCatalog.Brigandine, 2),
                new LootEntry(ItemCatalog.IronFlail, 2),
                new LootEntry(ItemCatalog.TemplarsCuirass, 1),
                new LootEntry(ItemCatalog.GravewatchPlate, 1)
            ]),

        new(FenHag, "Fen Hag",
            "Standing waist deep in the fen, and perfectly dry.",
            Level: 7, ArmourClass: 16, MaxHitPoints: 52, AttackBonus: 7, DamageNotation: "2d6+3",
            MinGold: 40, MaxGold: 95, DropChance: 52,
            [
                new LootEntry(ItemCatalog.ArcanistsWeave, 3),
                new LootEntry(ItemCatalog.RunedWand, 3),
                new LootEntry(ItemCatalog.AugursBeads, 3),
                new LootEntry(ItemCatalog.OrreryStaff, 2),
                new LootEntry(ItemCatalog.HermitsBell, 2),
                new LootEntry(ItemCatalog.CharmOfPresence, 2),
                new LootEntry(ItemCatalog.RingOfFocus, 2),
                new LootEntry(ItemCatalog.ShadowweaveCloak, 2),
                new LootEntry(ItemCatalog.AmuletOfInsight, 2),
                new LootEntry(ItemCatalog.EnvoysTorc, 2),
                new LootEntry(ItemCatalog.CircletOfClarity, 1),
                new LootEntry(ItemCatalog.PhilosophersInkstone, 1)
            ]),

        new(Wraith, "Wraith",
            "Cold where it passes. Does not seem to notice walls.",
            Level: 8, ArmourClass: 17, MaxHitPoints: 60, AttackBonus: 7, DamageNotation: "2d8",
            MinGold: 40, MaxGold: 110, DropChance: 55,
            [
                new LootEntry(ItemCatalog.SilveredBlade, 4),
                new LootEntry(ItemCatalog.RunedWand, 3),
                new LootEntry(ItemCatalog.CircletOfClarity, 3),
                new LootEntry(ItemCatalog.AmuletOfInsight, 3),
                new LootEntry(ItemCatalog.ArcanistsWeave, 3),
                new LootEntry(ItemCatalog.ShadowweaveCloak, 2),
                new LootEntry(ItemCatalog.WardingShield, 2),
                new LootEntry(ItemCatalog.OrreryStaff, 2),
                new LootEntry(ItemCatalog.CenserFlail, 2),
                new LootEntry(ItemCatalog.BannerSpear, 2),
                new LootEntry(ItemCatalog.TemplarsCuirass, 2),
                new LootEntry(ItemCatalog.HermitsBell, 2),
                new LootEntry(ItemCatalog.PhilosophersInkstone, 1)
            ]),

        new(BarrowKnight, "Barrow Knight",
            "Old plate, better kept than the man inside it.",
            Level: 9, ArmourClass: 17, MaxHitPoints: 70, AttackBonus: 8, DamageNotation: "2d8+2",
            MinGold: 60, MaxGold: 150, DropChance: 60,
            [
                new LootEntry(ItemCatalog.GravewatchPlate, 3),
                new LootEntry(ItemCatalog.TemplarsCuirass, 3),
                new LootEntry(ItemCatalog.BannerSpear, 3),
                new LootEntry(ItemCatalog.OathkeepersMaul, 2),
                new LootEntry(ItemCatalog.DuellistsHalfPlate, 2),
                new LootEntry(ItemCatalog.TowerShield, 2),
                new LootEntry(ItemCatalog.ReliquaryHammer, 2),
                new LootEntry(ItemCatalog.CavalrySabre, 2),
                new LootEntry(ItemCatalog.IronBand, 2),
                new LootEntry(ItemCatalog.PendantOfTheBear, 2),
                new LootEntry(ItemCatalog.EnvoysTorc, 1),
                new LootEntry(ItemCatalog.BreastplateOfDawn, 1)
            ]),

        new(DrownedCrew, "Drowned Crew",
            "Wet through, and none of it recent.",
            Level: 10, ArmourClass: 18, MaxHitPoints: 82, AttackBonus: 8, DamageNotation: "3d6",
            MinGold: 90, MaxGold: 210, DropChance: 65,
            [
                new LootEntry(ItemCatalog.ShadowweaveCloak, 3),
                new LootEntry(ItemCatalog.OilskinCloak, 3),
                new LootEntry(ItemCatalog.SilveredBlade, 3),
                new LootEntry(ItemCatalog.CartographersLens, 3),
                new LootEntry(ItemCatalog.LongbowOfTheVale, 2),
                new LootEntry(ItemCatalog.BoarSpear, 2),
                new LootEntry(ItemCatalog.QuickstringBracer, 2),
                new LootEntry(ItemCatalog.TumblersSash, 2),
                new LootEntry(ItemCatalog.WayfarersCoat, 2),
                new LootEntry(ItemCatalog.LodestoneSceptre, 2),
                new LootEntry(ItemCatalog.ChainHauberk, 2),
                new LootEntry(ItemCatalog.HermitsBell, 1)
            ]),

        new(YoungDragon, "Young Dragon",
            "Barely more than a hatchling. Still a dragon.",
            Level: 11, ArmourClass: 18, MaxHitPoints: 95, AttackBonus: 9, DamageNotation: "2d10+3",
            MinGold: 120, MaxGold: 300, DropChance: 75,
            [
                new LootEntry(ItemCatalog.DragonfangSpear, 3),
                new LootEntry(ItemCatalog.LongbowOfTheVale, 3),
                new LootEntry(ItemCatalog.WardingShield, 3),
                new LootEntry(ItemCatalog.ReliquaryHammer, 2),
                new LootEntry(ItemCatalog.BreastplateOfDawn, 2),
                new LootEntry(ItemCatalog.ScaleMail, 2),
                new LootEntry(ItemCatalog.SiegeMaul, 2),
                new LootEntry(ItemCatalog.OathkeepersMaul, 2),
                new LootEntry(ItemCatalog.BulwarkHalberd, 2),
                new LootEntry(ItemCatalog.LodestoneSceptre, 2),
                new LootEntry(ItemCatalog.TowerShield, 2),
                new LootEntry(ItemCatalog.GravewatchPlate, 2),
                new LootEntry(ItemCatalog.QuarrymansGauntlets, 1)
            ],
            // The first boss anyone meets with a second gear, and the plainest one: it simply
            // starts hitting harder. Level 11, so none of the exact-script tests can reach it.
            Phases:
            [
                new MonsterPhase(50, "Kindled",
                    "The Young Dragon stops testing you and draws a long breath.",
                    [new StatusEffect(
                        EffectKind.Empowered, EffectTarget.Monster, StatusEffects.Lasting, 2,
                        YoungDragon)])
            ]),

        new(Basilisk, "Basilisk",
            "Slow, incurious, and used to being given room.",
            Level: 12, ArmourClass: 19, MaxHitPoints: 105, AttackBonus: 10, DamageNotation: "2d10+4",
            MinGold: 150, MaxGold: 340, DropChance: 78,
            [
                new LootEntry(ItemCatalog.LodestoneSceptre, 3),
                new LootEntry(ItemCatalog.TowerShield, 3),
                new LootEntry(ItemCatalog.QuarrymansGauntlets, 3),
                new LootEntry(ItemCatalog.GravewatchPlate, 2),
                new LootEntry(ItemCatalog.SiegeMaul, 2),
                new LootEntry(ItemCatalog.BulwarkHalberd, 2),
                new LootEntry(ItemCatalog.ScaleMail, 2),
                new LootEntry(ItemCatalog.WardingShield, 2),
                new LootEntry(ItemCatalog.PhilosophersInkstone, 2),
                new LootEntry(ItemCatalog.OathkeepersMaul, 2),
                new LootEntry(ItemCatalog.BreastplateOfDawn, 2),
                new LootEntry(ItemCatalog.IronBand, 1)
            ],
            // Guarded rather than Empowered, so the fight gets longer rather than sharper. It
            // is the answer to a player who has decided every boss is a damage race.
            Phases:
            [
                new MonsterPhase(60, "Stone Skin",
                    "The Basilisk's hide sets like rock left out in the cold.",
                    [new StatusEffect(
                        EffectKind.Guarded, EffectTarget.Monster, StatusEffects.Lasting, 2,
                        Basilisk)])
            ]),

        new(Wyvern, "Wyvern",
            "A dragon's poorer relation. It knows.",
            Level: 13, ArmourClass: 19, MaxHitPoints: 118, AttackBonus: 10, DamageNotation: "3d8+2",
            MinGold: 180, MaxGold: 400, DropChance: 80,
            [
                new LootEntry(ItemCatalog.DragonfangSpear, 3),
                new LootEntry(ItemCatalog.LongbowOfTheVale, 3),
                new LootEntry(ItemCatalog.BulwarkHalberd, 3),
                new LootEntry(ItemCatalog.BoarSpear, 2),
                new LootEntry(ItemCatalog.ShadowweaveCloak, 2),
                new LootEntry(ItemCatalog.ScaleMail, 2),
                new LootEntry(ItemCatalog.QuickstringBracer, 2),
                new LootEntry(ItemCatalog.PendantOfTheBear, 2),
                new LootEntry(ItemCatalog.DuellistsHalfPlate, 2),
                new LootEntry(ItemCatalog.HeartwoodToken, 2),
                new LootEntry(ItemCatalog.SiegeMaul, 2),
                new LootEntry(ItemCatalog.GreatAxe, 1)
            ],
            // The only phase in the game that grows a round's roll count without the player
            // having chosen it: a Weakened player rolls two d20s where they rolled one. It is
            // called out here rather than left to be discovered, because it is the first thing
            // in the game that makes the player themselves roll at disadvantage.
            Phases:
            [
                new MonsterPhase(50, "Stooping",
                    "The Wyvern gets above you and comes down out of the light.",
                    [new StatusEffect(
                        EffectKind.Weakened, EffectTarget.Player, 3, 0, Wyvern)])
            ]),

        new(ElderDragon, "Elder Dragon",
            "The hatchling grew up. Nobody was watching at the time.",
            Level: 14, ArmourClass: 20, MaxHitPoints: 132, AttackBonus: 11, DamageNotation: "2d12+4",
            MinGold: 220, MaxGold: 480, DropChance: 85,
            [
                new LootEntry(ItemCatalog.DragonfangSpear, 3),
                new LootEntry(ItemCatalog.OathkeepersMaul, 3),
                new LootEntry(ItemCatalog.BreastplateOfDawn, 3),
                new LootEntry(ItemCatalog.TowerShield, 2),
                new LootEntry(ItemCatalog.GravewatchPlate, 2),
                new LootEntry(ItemCatalog.LodestoneSceptre, 2),
                new LootEntry(ItemCatalog.ReliquaryHammer, 2),
                new LootEntry(ItemCatalog.SiegeMaul, 2),
                new LootEntry(ItemCatalog.BulwarkHalberd, 2),
                new LootEntry(ItemCatalog.WardingShield, 2),
                new LootEntry(ItemCatalog.PhilosophersInkstone, 2),
                new LootEntry(ItemCatalog.HermitsBell, 1),
                new LootEntry(ItemCatalog.QuarrymansGauntlets, 1)
            ],
            // Two gears, declared highest threshold first, which is the order PhaseAt counts in
            // and the order the entry loop walks. The second is deliberately the only place in
            // the bestiary that heals: at four a round it does not outpace a hitting player, it
            // punishes a player who has stopped hitting.
            Phases:
            [
                new MonsterPhase(60, "Roused",
                    "The Elder Dragon stops treating this as an interruption.",
                    [new StatusEffect(
                        EffectKind.Empowered, EffectTarget.Monster, StatusEffects.Lasting, 2,
                        ElderDragon)]),

                new MonsterPhase(30, "Last Fire",
                    "Something older than the Elder Dragon opens its eyes behind them.",
                    [
                        new StatusEffect(
                            EffectKind.Regenerating, EffectTarget.Monster, StatusEffects.Lasting, 4,
                            ElderDragon),
                        new StatusEffect(
                            EffectKind.Empowered, EffectTarget.Monster, StatusEffects.Lasting, 3,
                            ElderDragon)
                    ])
            ]),

        // --- Filling out the middle -------------------------------------------------

        new(TollBeetle, "Toll Beetle",
            "Sits in the road. Will not be walked around, only over.",
            Level: 4, ArmourClass: 15, MaxHitPoints: 32, AttackBonus: 4, DamageNotation: "1d10",
            MinGold: 12, MaxGold: 34, DropChance: 40,
            [
                new LootEntry(ItemCatalog.StuddedLeather, 3),
                new LootEntry(ItemCatalog.IronBand, 3),
                new LootEntry(ItemCatalog.WorkmansGloves, 2),
                new LootEntry(ItemCatalog.OxhideBelt, 2),
                new LootEntry(ItemCatalog.BoarSpear, 1)
            ]),

        new(CisternEel, "Cistern Eel",
            "Lives in water nobody has looked at in years. It has been growing.",
            Level: 4, ArmourClass: 13, MaxHitPoints: 28, AttackBonus: 5, DamageNotation: "2d4",
            MinGold: 10, MaxGold: 30, DropChance: 40,
            [
                new LootEntry(ItemCatalog.LeatherArmour, 3),
                new LootEntry(ItemCatalog.PlainIronBand, 2),
                new LootEntry(ItemCatalog.LuckyCoin, 2),
                new LootEntry(ItemCatalog.HideHarness, 2),
                new LootEntry(ItemCatalog.WayfarersCoat, 1)
            ]),

        new(RustedSentry, "Rusted Sentry",
            "Still guarding. Nobody has told it what, or for how much longer.",
            Level: 6, ArmourClass: 17, MaxHitPoints: 46, AttackBonus: 5, DamageNotation: "2d6",
            MinGold: 22, MaxGold: 52, DropChance: 45,
            [
                new LootEntry(ItemCatalog.ChainShirt, 3),
                new LootEntry(ItemCatalog.QuartermastersTally, 2),
                new LootEntry(ItemCatalog.IronFlail, 2),
                new LootEntry(ItemCatalog.QuarrymansGauntlets, 2),
                new LootEntry(ItemCatalog.GreatAxe, 1)
            ]),

        new(GraveMoth, "Grave Moth",
            "Eats the cloth first. Waits, politely, for the rest.",
            Level: 6, ArmourClass: 14, MaxHitPoints: 38, AttackBonus: 6, DamageNotation: "1d12",
            MinGold: 20, MaxGold: 48, DropChance: 45,
            [
                new LootEntry(ItemCatalog.TravellersCloak, 3),
                new LootEntry(ItemCatalog.ClerksSpectacles, 2),
                new LootEntry(ItemCatalog.WayfarersCoat, 2),
                new LootEntry(ItemCatalog.LuckyCoin, 2),
                new LootEntry(ItemCatalog.RingOfFocus, 1)
            ]),

        new(SaltWidow, "Salt Widow",
            "Waits at the tideline for a boat that has been recorded lost for sixty years.",
            Level: 8, ArmourClass: 16, MaxHitPoints: 54, AttackBonus: 7, DamageNotation: "2d8",
            MinGold: 30, MaxGold: 70, DropChance: 50,
            [
                new LootEntry(ItemCatalog.PlainIronBand, 3),
                new LootEntry(ItemCatalog.ChainShirt, 2),
                new LootEntry(ItemCatalog.RingOfVigour, 2),
                new LootEntry(ItemCatalog.QuartermastersTally, 2),
                new LootEntry(ItemCatalog.EnvoysTorc, 1)
            ]),

        new(PitForeman, "Pit Foreman",
            "Keeps a tally of who is owed what. Includes itself in the reckoning.",
            Level: 9, ArmourClass: 17, MaxHitPoints: 60, AttackBonus: 7, DamageNotation: "2d8",
            MinGold: 34, MaxGold: 80, DropChance: 50,
            [
                new LootEntry(ItemCatalog.LedgerOfDebts, 2),
                new LootEntry(ItemCatalog.QuarrymansGauntlets, 3),
                new LootEntry(ItemCatalog.GreatAxe, 2),
                new LootEntry(ItemCatalog.ChainShirt, 2),
                new LootEntry(ItemCatalog.RingOfTheDiligent, 1)
            ]),

        new(CairnWight, "Cairn Wight",
            "The stones were put there to keep it in. They were stacked from the inside.",
            Level: 10, ArmourClass: 18, MaxHitPoints: 66, AttackBonus: 8, DamageNotation: "2d10",
            MinGold: 40, MaxGold: 92, DropChance: 52,
            [
                new LootEntry(ItemCatalog.RingOfTheDiligent, 2),
                new LootEntry(ItemCatalog.PlainIronBand, 3),
                new LootEntry(ItemCatalog.EnvoysTorc, 2),
                new LootEntry(ItemCatalog.ChainShirt, 2),
                new LootEntry(ItemCatalog.LedgerOfDebts, 1)
            ]),

        new(LanternWraith, "Lantern Wraith",
            "Carries a light out to the marsh every night. Comes back without it.",
            Level: 11, ArmourClass: 17, MaxHitPoints: 70, AttackBonus: 9, DamageNotation: "3d6",
            MinGold: 44, MaxGold: 100, DropChance: 52,
            [
                new LootEntry(ItemCatalog.ClerksSpectacles, 3),
                new LootEntry(ItemCatalog.LedgerOfDebts, 2),
                new LootEntry(ItemCatalog.RingOfFocus, 2),
                new LootEntry(ItemCatalog.AmuletOfInsight, 2),
                new LootEntry(ItemCatalog.BannerbearersTorc, 1)
            ]),

        new(ScreeGiant, "Scree Giant",
            "Comes down with the loose stone every spring, and goes back up with less of it.",
            Level: 12, ArmourClass: 19, MaxHitPoints: 84, AttackBonus: 9, DamageNotation: "3d8",
            MinGold: 52, MaxGold: 120, DropChance: 55,
            [
                new LootEntry(ItemCatalog.QuarrymansGauntlets, 3),
                new LootEntry(ItemCatalog.GreatAxe, 2),
                new LootEntry(ItemCatalog.RingOfTheDiligent, 2),
                new LootEntry(ItemCatalog.TravellersCloak, 2),
                new LootEntry(ItemCatalog.BannerbearersTorc, 1)
            ]),

        new(HollowAbbot, "Hollow Abbot",
            "Still keeps the hours. The bell is the only part of it with anything inside.",
            Level: 13, ArmourClass: 19, MaxHitPoints: 90, AttackBonus: 10, DamageNotation: "3d8",
            MinGold: 60, MaxGold: 135, DropChance: 55,
            [
                new LootEntry(ItemCatalog.LedgerOfDebts, 2),
                new LootEntry(ItemCatalog.BannerbearersTorc, 2),
                new LootEntry(ItemCatalog.ClerksSpectacles, 2),
                new LootEntry(ItemCatalog.AmuletOfInsight, 2),
                new LootEntry(ItemCatalog.RingOfTheDiligent, 2)
            ]),
    ];

    /// <summary>The deepest level the bestiary actually reaches.</summary>
    public static int TopLevel { get; } = All.Max(m => m.Level);

    /// <summary>
    /// The level <see cref="MonsterDefinition.IsAvailableAt"/> evaluates its band at. Capped
    /// at the top of the catalog so the last few opponents stay offered forever rather than
    /// the band sliding off the end and leaving a high level character nothing to fight.
    /// </summary>
    public static int BandLevel(int characterLevel) => Math.Min(characterLevel, TopLevel);

    private static readonly Dictionary<string, MonsterDefinition> ByKey =
        All.ToDictionary(m => m.Key, StringComparer.Ordinal);

    public static MonsterDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);

    public static IReadOnlyList<MonsterDefinition> AvailableAt(int characterLevel) =>
        All.Where(m => m.IsAvailableAt(characterLevel)).OrderBy(m => m.Level).ToList();
}
