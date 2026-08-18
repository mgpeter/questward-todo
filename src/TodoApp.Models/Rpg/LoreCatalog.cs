namespace TodoApp.Models.Rpg;

/// <summary>How a fragment is earned. Never persisted, so a new trigger costs no migration.</summary>
public enum LoreTrigger
{
    /// <summary>Subject is a monster key. Unlocked once it has been sighted Threshold times.</summary>
    MonsterSeen = 0,

    /// <summary>Subject is a monster key. Unlocked once it has been killed Threshold times.</summary>
    MonsterSlain = 1,

    /// <summary>Subject is ignored. Unlocked at character level Threshold.</summary>
    Level = 2,

    /// <summary>Subject is a quest key. Unlocked once that quest has been claimed.</summary>
    QuestClaimed = 3
}

public sealed record LorePlace(string Key, string Name, string Blurb);

public sealed record LoreFragment(
    string Key,
    string Title,
    string Body,
    string PlaceKey,
    LoreTrigger Trigger,
    string Subject,
    int Threshold)
{
    public bool IsUnlockedBy(LoreState state) => Trigger switch
    {
        LoreTrigger.MonsterSeen => state.Sightings(Subject) >= Threshold,
        LoreTrigger.MonsterSlain => state.Kills(Subject) >= Threshold,
        LoreTrigger.Level => state.Level >= Threshold,
        LoreTrigger.QuestClaimed => state.ClaimedQuests.Contains(Subject),
        _ => false
    };
}

/// <summary>What a fragment is allowed to know. Built once per request from rows already stored.</summary>
/// <remarks>
/// There is no lore_unlocks table on purpose. An unlock is a pure function of the level, the
/// bestiary and the claimed quests, all of which are already persisted, so storing it again
/// would create a second copy that can disagree with the first (DEC-002).
/// </remarks>
public sealed record LoreState(
    int Level,
    IReadOnlyDictionary<string, (int Seen, int Slain)> Bestiary,
    IReadOnlySet<string> ClaimedQuests)
{
    public int Sightings(string monsterKey) => Bestiary.TryGetValue(monsterKey, out var e) ? e.Seen : 0;
    public int Kills(string monsterKey) => Bestiary.TryGetValue(monsterKey, out var e) ? e.Slain : 0;
}

/// <summary>
/// Code-held, following DEC-004. Fragment keys are never written to the database, so adding a
/// fragment stays a one-line change with no migration behind it.
/// </summary>
/// <remarks>
/// The monster ladder is deliberate. A first sighting gives the field note, three kills give the
/// habit or the history, ten kills give the thing nobody who met it once could know. Place
/// fragments hang off level and claimed quests instead, so the map opens for a player who
/// progresses rather than only for one who grinds.
/// </remarks>
public static class LoreCatalog
{
    public const string TheTavern = "the-tavern";
    public const string TheOldRoad = "the-old-road";
    public const string TheCrypt = "the-crypt";
    public const string TheFen = "the-fen";
    public const string TheQuarry = "the-quarry";
    public const string TheDrownedCoast = "the-drowned-coast";
    public const string TheHighPasses = "the-high-passes";
    public const string TheForge = "the-forge";

    public static IReadOnlyList<LorePlace> Places { get; } =
    [
        new(TheTavern, "The Tavern", "Warm, loud, and the only unlocked door in the valley."),
        new(TheOldRoad, "The Old Road", "Older than the county that maintains it. Maintained accordingly."),
        new(TheCrypt, "The Crypt", "Dry, orderly, and fuller than the burial register accounts for."),
        new(TheFen, "The Fen", "Water where the map says ground. Slow about giving things back."),
        new(TheQuarry, "The Quarry", "Cut out and abandoned in one generation. Nobody has filled it in."),
        new(TheDrownedCoast, "The Drowned Coast", "Two villages under the water, and both still on the charts."),
        new(TheHighPasses, "The High Passes", "Open four months a year. Crossed all twelve."),
        new(TheForge, "The Forge", "Hot, methodical, and unimpressed by whatever is brought in.")
    ];

    public static IReadOnlyList<LoreFragment> All { get; } =
    [
        // Giant Rat, at the tavern.
        new("giant-rat-sighted", "Cellar Measurements",
            "The cellar rats are measured against the brickwork, because nobody believes a description. Most of them reach the second course. The one behind the barrels reached the third, and the barrels were moved.",
            TheTavern, LoreTrigger.MonsterSeen, MonsterCatalog.GiantRat, 1),
        new("giant-rat-known", "On Their Diet",
            "They eat what the kitchen throws out, and lately the kitchen throws out less. Nobody behind the bar has adjusted for that. The rats have.",
            TheTavern, LoreTrigger.MonsterSlain, MonsterCatalog.GiantRat, 3),
        new("giant-rat-studied", "The Ratcatcher's Rates",
            "The ratcatcher charges by the tail and has never once been asked to prove the tail was attached to anything. He raised his rates the spring the cellar flooded, and has not lowered them since.",
            TheTavern, LoreTrigger.MonsterSlain, MonsterCatalog.GiantRat, 10),

        // Goblin, on the old road.
        new("goblin-sighted", "A Quartermaster's Complaint",
            "Every goblin blade was somebody else's blade first. They regrind them on the wrong side, which ruins the edge and does not appear to slow anybody down.",
            TheOldRoad, LoreTrigger.MonsterSeen, MonsterCatalog.Goblin, 1),
        new("goblin-known", "Camp Etiquette",
            "They post a watch, and the watch faces inward. Whatever they are guarding against, it is not the road. The road they treat as a source of supply.",
            TheOldRoad, LoreTrigger.MonsterSlain, MonsterCatalog.Goblin, 3),
        new("goblin-studied", "The Third Cull",
            "The cull has been ordered three times in living memory and worked twice. The second time it was ordered by the same man who ordered the first, which is the part the road remembers.",
            TheOldRoad, LoreTrigger.MonsterSlain, MonsterCatalog.Goblin, 10),

        // Carrion Crows, on the old road.
        new("carrion-crows-sighted", "Following Behaviour",
            "They do not follow the wounded. They follow the armed, which is a longer wait and a better one. The distinction took somebody a lifetime to notice and an afternoon to write down.",
            TheOldRoad, LoreTrigger.MonsterSeen, MonsterCatalog.CarrionCrows, 1),
        new("carrion-crows-known", "Bow Shapes",
            "They learned the shape of a drawn bow and stopped landing near archers. Then they learned which archers were out of arrows.",
            TheOldRoad, LoreTrigger.MonsterSlain, MonsterCatalog.CarrionCrows, 3),
        new("carrion-crows-studied", "A Debt of Carrion",
            "Crows on this road are fed by nobody and have never gone hungry. The carters used to offer that as proof the road was busy. The carters have stopped offering it.",
            TheOldRoad, LoreTrigger.MonsterSlain, MonsterCatalog.CarrionCrows, 10),

        // Bandit, on the old road.
        new("bandit-sighted", "Roadside Arithmetic",
            "He asks for the purse before the fight, because the fight is expensive for him as well. Most people hand it over. He has priced the rest correctly.",
            TheOldRoad, LoreTrigger.MonsterSeen, MonsterCatalog.Bandit, 1),
        new("bandit-known", "A Toll By Habit",
            "The demand is always the same words in the same order, worn smooth by use. Somebody taught it to him. That somebody is not on the road any more.",
            TheOldRoad, LoreTrigger.MonsterSlain, MonsterCatalog.Bandit, 3),
        new("bandit-studied", "The Amnesty",
            "An amnesty was offered once, and a fair number came in for it. The ones who did not come in are the ones still out there, which is how the road ended up with the patient sort.",
            TheOldRoad, LoreTrigger.MonsterSlain, MonsterCatalog.Bandit, 10),

        // Stone Sentinel, on the old road.
        new("stone-sentinel-sighted", "Boundary Marker",
            "It stands where a boundary used to be. The boundary is on no map still in use. It has not been told.",
            TheOldRoad, LoreTrigger.MonsterSeen, MonsterCatalog.StoneSentinel, 1),
        new("stone-sentinel-known", "The Survey Party",
            "A survey party moved the line half a mile east and left the marker where it was, judging it too heavy and not worth the trouble. Their note records the matter as settled.",
            TheOldRoad, LoreTrigger.MonsterSlain, MonsterCatalog.StoneSentinel, 3),
        new("stone-sentinel-studied", "What It Guards",
            "There is nothing behind it, and there has been nothing behind it for a long time. This makes no difference at all to how it stands. A post outlasts its reason more often than anybody expects.",
            TheOldRoad, LoreTrigger.MonsterSlain, MonsterCatalog.StoneSentinel, 10),

        // Skeleton, in the crypt.
        new("skeleton-sighted", "Grave Goods, Missing",
            "Nothing down here was buried with a sword, and half of them are carrying one now. The registers were kept properly. Somebody has been adding.",
            TheCrypt, LoreTrigger.MonsterSeen, MonsterCatalog.Skeleton, 1),
        new("skeleton-known", "On Rattling",
            "The rattling is neither warning nor grievance. It is the sound of a body walking without the parts that used to keep it quiet.",
            TheCrypt, LoreTrigger.MonsterSlain, MonsterCatalog.Skeleton, 3),
        new("skeleton-studied", "The Sexton's Note",
            "The sexton stopped resetting the lids in his third year and started numbering them instead. He wrote that a lid put back wrong is a lid somebody has to lift again. The numbers are still legible.",
            TheCrypt, LoreTrigger.MonsterSlain, MonsterCatalog.Skeleton, 10),

        // Wraith, in the crypt.
        new("wraith-sighted", "The Marked Strip",
            "The cold arrives before it does and stays after, in a strip about the width of a corridor. Masons have marked that strip in three houses. The marks agree.",
            TheCrypt, LoreTrigger.MonsterSeen, MonsterCatalog.Wraith, 1),
        new("wraith-known", "Walls, Ignored",
            "It does not pass through a wall so much as decline to acknowledge one. Everybody who has watched it happen reports the same thing, which is that the wall looked briefly unconvincing.",
            TheCrypt, LoreTrigger.MonsterSlain, MonsterCatalog.Wraith, 3),
        new("wraith-studied", "The Sealed Wing",
            "The east wing was sealed and the keys melted down, an expense the house accounts enter under building repairs. The wing is still sealed. The entry was never queried, because the sum was small.",
            TheCrypt, LoreTrigger.MonsterSlain, MonsterCatalog.Wraith, 10),

        // Barrow Knight, in the crypt.
        new("barrow-knight-sighted", "Buried in Harness",
            "He was buried in his armour, which was unusual then and expensive always. Somebody wanted him ready. Nobody left a note saying what for.",
            TheCrypt, LoreTrigger.MonsterSeen, MonsterCatalog.BarrowKnight, 1),
        new("barrow-knight-known", "The Barrow Ditch",
            "He does not follow past the barrow ditch. Whatever he was set to hold, he is still holding it, and the ditch is where it ends.",
            TheCrypt, LoreTrigger.MonsterSlain, MonsterCatalog.BarrowKnight, 3),
        new("barrow-knight-studied", "The Armour Still Fits",
            "The plate was made to measure and has been neither let out nor taken in. Everything under it has changed a great deal. The fit has not, and the straps are on their original holes.",
            TheCrypt, LoreTrigger.MonsterSlain, MonsterCatalog.BarrowKnight, 10),

        // Mire Toad, in the fen.
        new("mire-toad-sighted", "Still Water Notes",
            "The water it sits in is the only still water in the fen. Everything else moves a little. Nobody has explained why that is the restful thing to look at.",
            TheFen, LoreTrigger.MonsterSeen, MonsterCatalog.MireToad, 1),
        new("mire-toad-known", "On the Tongue",
            "The tongue arrives before the sound of it. Fen guides teach the sound anyway, on the theory that anybody who hears it once will stand differently ever after.",
            TheFen, LoreTrigger.MonsterSlain, MonsterCatalog.MireToad, 3),
        new("mire-toad-studied", "The Dredging Return",
            "When the fen was dredged, the crews logged everything they brought up, including boots in pairs and boots not in pairs. The unpaired column is the longer one. The dredging was not repeated.",
            TheFen, LoreTrigger.MonsterSlain, MonsterCatalog.MireToad, 10),

        // Hedge Troll, in the fen.
        new("hedge-troll-sighted", "The Toll Board",
            "The board at the bridge gives the toll in a careful hand, corrected upward four times in a much less careful one. The bridge has not been mended in any of those years.",
            TheFen, LoreTrigger.MonsterSeen, MonsterCatalog.HedgeTroll, 1),
        new("hedge-troll-known", "Terms of Passage",
            "It will take coin, or a boot, or a hat. It will not take a promise, having taken one once. Whoever made that promise is not discussed at the crossing.",
            TheFen, LoreTrigger.MonsterSlain, MonsterCatalog.HedgeTroll, 3),
        new("hedge-troll-studied", "Who Built the Bridge",
            "The county built the bridge and then stopped sending anybody to look at it, which is when the toll began. The arrangement predates the records that should describe it. Both sides now treat it as ordinary.",
            TheFen, LoreTrigger.MonsterSlain, MonsterCatalog.HedgeTroll, 10),

        // Fen Hag, in the fen.
        new("fen-hag-sighted", "Before You Ask",
            "She trades fairly and says so before you ask, which is true and does not help. The price is always slightly more than you brought.",
            TheFen, LoreTrigger.MonsterSeen, MonsterCatalog.FenHag, 1),
        new("fen-hag-known", "Things Taken In Trade",
            "The shelves hold hair, teeth, a wedding ring and a name written on slate. Every item was handed over willingly and priced correctly. Nobody has ever come back to buy one out.",
            TheFen, LoreTrigger.MonsterSlain, MonsterCatalog.FenHag, 3),
        new("fen-hag-studied", "The Miller's Wife",
            "The miller's wife went out to the Fen Hag twice. The first time she came back with what she went for. The second time she went to renegotiate, and the fen keeps no record of that.",
            TheFen, LoreTrigger.MonsterSlain, MonsterCatalog.FenHag, 10),

        // Deserter, at the quarry.
        new("deserter-sighted", "Half a Uniform",
            "The tunic is regimental and the boots are not. He kept the half that is hard to replace and sold the half that is not. The drill he kept entirely.",
            TheQuarry, LoreTrigger.MonsterSeen, MonsterCatalog.Deserter, 1),
        new("deserter-known", "Standing Orders",
            "He still forms up against a single opponent as though a line stood beside him. The line is not there. The habit is better than most people's training regardless.",
            TheQuarry, LoreTrigger.MonsterSlain, MonsterCatalog.Deserter, 3),
        new("deserter-studied", "The Muster Roll",
            "His name is on the roll with no mark against it, because the clerk who kept that column died the same season. Nobody has been paid to finish the entry. So the roll still says he is owed.",
            TheQuarry, LoreTrigger.MonsterSlain, MonsterCatalog.Deserter, 10),

        // Ogre, at the quarry.
        new("ogre-sighted", "Wet Rope",
            "The smell arrives first, and it is not the animal itself. It is what the animal drags. Nobody who followed the smell to the far end has offered a description afterwards.",
            TheQuarry, LoreTrigger.MonsterSeen, MonsterCatalog.Ogre, 1),
        new("ogre-known", "Slow to Stop",
            "It takes a long time to decide and no time at all to continue. Fighting one is mostly a matter of choosing where you would like the wall to be.",
            TheQuarry, LoreTrigger.MonsterSlain, MonsterCatalog.Ogre, 3),
        new("ogre-studied", "The Valley Fields",
            "Two fields below the quarry have lain unploughed for a generation, and the reason entered in the parish record is subsidence. The record was written by a man who had seen the tracks and preferred subsidence.",
            TheQuarry, LoreTrigger.MonsterSlain, MonsterCatalog.Ogre, 10),

        // Basilisk, at the quarry.
        new("basilisk-sighted", "The Den Inventory",
            "Everything in the den is intact, which is what is wrong with it. A goat, a dog and a man, all in good condition and all upright. Nothing in there has fallen over.",
            TheQuarry, LoreTrigger.MonsterSeen, MonsterCatalog.Basilisk, 1),
        new("basilisk-known", "On Not Looking",
            "Guides go in with a mirror and come out with a mirror, and the ones who come out say the mirror is not the difficult part. The difficult part is the walking backwards.",
            TheQuarry, LoreTrigger.MonsterSlain, MonsterCatalog.Basilisk, 3),
        new("basilisk-studied", "Weight of the Preserved",
            "The quarry men were hired once to shift what was in a den, and charged by weight as they do for stone. They did not ask what they were shifting. The bill survives, and the receipt is signed.",
            TheQuarry, LoreTrigger.MonsterSlain, MonsterCatalog.Basilisk, 10),

        // Drowned Crew, on the drowned coast.
        new("drowned-crew-sighted", "Still Rowing",
            "They keep the stroke. Whatever they are rowing has not been under them for years, and the stroke has not slipped once.",
            TheDrownedCoast, LoreTrigger.MonsterSeen, MonsterCatalog.DrownedCrew, 1),
        new("drowned-crew-known", "Salt in Everything",
            "Salt comes off them onto rope, timber and skin, and none of it dries again. Coast people replace their rope far oftener than the rope needs it. They do not say why.",
            TheDrownedCoast, LoreTrigger.MonsterSlain, MonsterCatalog.DrownedCrew, 3),
        new("drowned-crew-studied", "The Owner's Claim",
            "The owner claimed the loss and was paid, and the entry notes that no bodies were recovered, which was the quickest way to close the file. The file is closed. The crew was not consulted.",
            TheDrownedCoast, LoreTrigger.MonsterSlain, MonsterCatalog.DrownedCrew, 10),

        // Dire Wolf, in the high passes.
        new("dire-wolf-sighted", "Ankle Height",
            "Trappers set their lines low up here, because the wolves have learned what a knee is for. Nothing about the animal is careless. It simply prefers the parts that fold.",
            TheHighPasses, LoreTrigger.MonsterSeen, MonsterCatalog.DireWolf, 1),
        new("dire-wolf-known", "Pack Discipline",
            "The pack does not rush. It puts one animal in front of you and the rest behind the ridge, then waits to see which way you are willing to turn.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.DireWolf, 3),
        new("dire-wolf-studied", "A Shepherd's Ledger",
            "The ledger for the high field runs forty years and shows losses every winter but one. That winter is circled twice, and beside it somebody has written that the pack had moved on and would be back.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.DireWolf, 10),

        // Young Dragon, in the high passes.
        new("young-dragon-sighted", "Barely a Hatchling",
            "Small for the kind, and every part of it is finished. Nothing about it is unformed. It is simply not yet as large as it intends to be.",
            TheHighPasses, LoreTrigger.MonsterSeen, MonsterCatalog.YoungDragon, 1),
        new("young-dragon-known", "The Hoard, Started",
            "The hoard is a saucepan, two candlesticks and a good bridle, arranged by size and kept free of dust. It is a small hoard. It is kept exactly as a large one would be.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.YoungDragon, 3),
        new("young-dragon-studied", "Feeding Range",
            "It takes from farms a day's walk out and never from the nearest one. The nearest farm has noticed this and says nothing about it, which is the arrangement working as intended.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.YoungDragon, 10),

        // Wyvern, in the high passes.
        new("wyvern-sighted", "The Poorer Relation",
            "Two legs, one temper, and no hoard worth the name. It is built along the same lines as a dragon and comes in cheaper on every count.",
            TheHighPasses, LoreTrigger.MonsterSeen, MonsterCatalog.Wyvern, 1),
        new("wyvern-known", "Nesting Sites",
            "It nests below the dragon ledges and above everything else, on shelves nothing else wants. The position is deliberate. It has been driven off higher ground and remembers by whom.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.Wyvern, 3),
        new("wyvern-studied", "The Falconer's Attempt",
            "A falconer in the passes took one from the nest and kept it eleven months, which is longer than anybody expected. His notes end with the observation that it had begun to look down at him.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.Wyvern, 10),

        // Elder Dragon, in the high passes.
        new("elder-dragon-sighted", "The Hatchling, Later",
            "It is the same animal that used to take bridles. The change took a lifetime and was noticed by nobody, because the people who would have noticed had other years to get through.",
            TheHighPasses, LoreTrigger.MonsterSeen, MonsterCatalog.ElderDragon, 1),
        new("elder-dragon-known", "The Hoard, Later",
            "The saucepan is still in it, near the middle, arranged with everything acquired since. Nothing has been discarded. That is the part worth worrying about, rather than the size.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.ElderDragon, 3),
        new("elder-dragon-studied", "What Is Not Burned",
            "The valley burns in bands, with clean ground between them, and the clean ground follows the old rights of way. It has not forgotten which land was granted and which was taken. Somebody's grandfather signed something.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.ElderDragon, 10),

        // The Tavern.
        new("tavern-house-rules", "House Rules",
            "The board by the door says no blades drawn indoors, no credit past the second week, and no sleeping in the settle. Two of the three are enforced. The settle has a blanket on it.",
            TheTavern, LoreTrigger.Level, "", 1),
        new("tavern-long-table", "The Long Table",
            "The long table is scored with names cut by people who were sitting there a good while. Some names have a second cut through them, done later and more carefully. The landlord does not sand it.",
            TheTavern, LoreTrigger.Level, "", 4),
        new("tavern-slate", "The Slate",
            "Every name on the slate comes off eventually, some by paying and some by leaving. The landlord keeps a rag for the first sort and a longer memory for the second.",
            TheTavern, LoreTrigger.QuestClaimed, QuestCatalog.HonestWork, 0),

        // The Old Road.
        new("old-road-milestones", "The Old Milestones",
            "The stones give distances to a town that burned and was rebuilt half a mile south. Nobody has recut them. Carters still measure by them and arrive in the right place regardless.",
            TheOldRoad, LoreTrigger.Level, "", 2),
        new("old-road-verge", "Where the Verge Widens",
            "The verge is wider outside the villages, cut back by hand every spring. It is cut back to a bowshot exactly, and the one spring it was skipped is still talked about.",
            TheOldRoad, LoreTrigger.Level, "", 6),
        new("old-road-first-mile", "The First Mile",
            "The first mile out of the valley is walked more than any other and mended less. It is where people find out what they brought with them, and where a fair number turn round.",
            TheOldRoad, LoreTrigger.QuestClaimed, QuestCatalog.FirstBlood, 0),

        // The Crypt.
        new("crypt-register", "The Burial Register",
            "The register is complete and in one hand for sixty years. After that it is in three hands and the columns stop agreeing with each other. The count of lids has been correct throughout.",
            TheCrypt, LoreTrigger.Level, "", 3),
        new("crypt-lower-course", "The Lower Course",
            "There is a lower course beneath the one people visit, reached by a stair the masons built and then bricked up. The brick is a century newer than the stair, which means somebody went down first.",
            TheCrypt, LoreTrigger.Level, "", 10),
        new("crypt-goods-returned", "Grave Goods, Returned",
            "Anything carried out is expected back, and most of it does come back, delivered at night and left inside the gate. Nobody has been thanked for this. Nobody has stopped doing it either.",
            TheCrypt, LoreTrigger.QuestClaimed, QuestCatalog.BoneCollector, 0),

        // The Fen.
        new("fen-causeway", "The Causeway",
            "The causeway is laid on hurdles that must be relaid every second year. The men who relay it are paid in advance, which is unusual, and paid well, which is not discussed.",
            TheFen, LoreTrigger.Level, "", 5),
        new("fen-what-it-keeps", "What the Fen Keeps",
            "It gives back leather and keeps iron, or so the dredgers judge from what comes up. The rule holds for boots and buckles. It has never once held for the owners of the buckles.",
            TheFen, LoreTrigger.Level, "", 11),
        new("fen-coin", "Coin From the Water",
            "Coins come up from the fen in fair condition and spend perfectly well, and the tavern takes them without comment. The Fen Hag will not take them back. She says they are already paid for.",
            TheFen, LoreTrigger.QuestClaimed, QuestCatalog.Treasurer, 0),

        // The Quarry.
        new("quarry-tools", "Tools Left Standing",
            "The tools were left where they were set down, upright and in order, which is not how a place is abandoned in a hurry. Whoever left was expecting to be back inside the week.",
            TheQuarry, LoreTrigger.Level, "", 7),
        new("quarry-deep-cut", "The Deep Cut",
            "The deepest cut was worked from the top down until the face stopped being stone and started being something the foreman declined to name in the ledger. He wrote hard ground and moved the men.",
            TheQuarry, LoreTrigger.Level, "", 12),
        new("quarry-paid-by-weight", "Paid by Weight",
            "Quarry men are paid by weight shifted, which makes them honest about how much they lifted and vague about what it was. The rates have not changed in two generations. The lifting has.",
            TheQuarry, LoreTrigger.QuestClaimed, QuestCatalog.HeavyLifting, 0),

        // The Drowned Coast.
        new("coast-tide-book", "The Tide Book",
            "The tide book for this stretch is kept in ink that runs, so every page has been copied twice over. The copies disagree about one night in autumn. Both copies were kept.",
            TheDrownedCoast, LoreTrigger.Level, "", 8),
        new("coast-bell", "Bells Under Water",
            "A bell sounds off the point when the swell is running, and the harbour keeps the hour by it in bad weather. There has been no bell tower on the point for a hundred years.",
            TheDrownedCoast, LoreTrigger.Level, "", 9),
        new("coast-salvage", "The Salvage Roll",
            "Salvage is logged by what washes in and by who took it up, and the column for owners is mostly empty. That empty column is the reason the roll is kept at all. Somebody may still ask.",
            TheDrownedCoast, LoreTrigger.Level, "", 13),

        // The High Passes.
        new("passes-four-months", "Two Sets of Figures",
            "The toll house records every crossing plainly, and the coroner records some of them separately. The two sets of figures have never been compared in public. Both books are kept in the same room.",
            TheHighPasses, LoreTrigger.Level, "", 10),
        new("passes-cairns", "The Cairns",
            "Cairns mark the route and also mark where people stopped, and there is no telling the two kinds apart. Travellers add a stone to every cairn they pass. The custom removes the difference on purpose.",
            TheHighPasses, LoreTrigger.Level, "", 14),
        new("passes-upper-ledges", "The Upper Ledges",
            "Nothing nests on the highest ledges now, and everything below them has moved down a shelf. The shift happened inside one season. Shepherds noticed it well before anybody with a map did.",
            TheHighPasses, LoreTrigger.QuestClaimed, QuestCatalog.ApexPredator, 0),

        // The Forge.
        new("forge-order-of-arrival", "The Smith's Terms",
            "Pieces are taken in the order they arrive, never in the order of who brought them, and the smith has held that line against better men than most. It costs him custom every year. He has priced it in.",
            TheForge, LoreTrigger.Level, "", 6),
        new("forge-rack", "The Rack at the Back",
            "The rack at the back holds pieces nobody collected, each tagged with a name and a date. Some tags are older than the smith. He will sell them, at a price that assumes the owner might still walk in.",
            TheForge, LoreTrigger.QuestClaimed, QuestCatalog.WellEquipped, 0),
        new("forge-reground", "Reground Steel",
            "Blades come back off the road with the edge put on the wrong side, and the smith knows that grind on sight. He charges to set it right and does not ask where the blade has been.",
            TheForge, LoreTrigger.QuestClaimed, QuestCatalog.GoblinCull, 0),

        // --- The ten that filled out the middle -------------------------------------
        //
        // The same ladder as everything else: the sighting is what an outsider notices, three
        // kills is the habit, ten is the thing nobody who met it once could know.

        // Toll Beetle, on the old road.
        new("toll-beetle-sighted", "Not Around",
            "Carters will tell you the road is clear and mean that the beetle is on the far side of it. The distinction matters to them and to nobody else.",
            TheOldRoad, LoreTrigger.MonsterSeen, MonsterCatalog.TollBeetle, 1),
        new("toll-beetle-known", "The Wheel Ruts",
            "Two grooves run over the crown of the road, deeper than any cart makes, and they do not deviate. Whatever went over went over in a straight line and was in no hurry.",
            TheOldRoad, LoreTrigger.MonsterSlain, MonsterCatalog.TollBeetle, 3),
        new("toll-beetle-studied", "The Surveyor's Note",
            "A surveyor once proposed rerouting the road by forty yards. The estimate survives; the reroute does not. In the margin, in the same hand: it moved too.",
            TheOldRoad, LoreTrigger.MonsterSlain, MonsterCatalog.TollBeetle, 10),

        // Cistern Eel, in the fen.
        new("cistern-eel-sighted", "Water Nobody Draws",
            "The cistern is marked on the plans and struck through on the copies. Nobody who works the fen can say when it was struck through, only that it was correct to do it.",
            TheFen, LoreTrigger.MonsterSeen, MonsterCatalog.CisternEel, 1),
        new("cistern-eel-known", "By Length",
            "It is measured in hands, and the number has gone up every time somebody has been brave enough to measure it. There have been four measurements.",
            TheFen, LoreTrigger.MonsterSlain, MonsterCatalog.CisternEel, 3),
        new("cistern-eel-studied", "The Fourth Measurement",
            "The fourth was taken from the rim with a weighted line rather than from the water. This was recorded as a refinement of method.",
            TheFen, LoreTrigger.MonsterSlain, MonsterCatalog.CisternEel, 10),

        // Rusted Sentry, at the forge.
        new("rusted-sentry-sighted", "Still At It",
            "It stands where a gate used to be. The gate was taken for its iron a long time ago, and the standing has not been affected.",
            TheForge, LoreTrigger.MonsterSeen, MonsterCatalog.RustedSentry, 1),
        new("rusted-sentry-known", "The Watch Order",
            "There is an order pinned inside the breastplate, rusted illegible except for the hours. The hours are still being kept.",
            TheForge, LoreTrigger.MonsterSlain, MonsterCatalog.RustedSentry, 3),
        new("rusted-sentry-studied", "Relieved",
            "Nobody came to relieve it. The relief was posted, and rode out, and there is a second sentry somewhere on that road doing the same thing.",
            TheForge, LoreTrigger.MonsterSlain, MonsterCatalog.RustedSentry, 10),

        // Grave Moth, in the crypt.
        new("grave-moth-sighted", "Cloth First",
            "Undertakers in the district budget for replacement shrouds twice a year. It is a line item, and it is never questioned.",
            TheCrypt, LoreTrigger.MonsterSeen, MonsterCatalog.GraveMoth, 1),
        new("grave-moth-known", "In Order",
            "It works from the outside in and never skips. Whether this is method or manners is the sort of question that gets asked once in a crypt and not again.",
            TheCrypt, LoreTrigger.MonsterSlain, MonsterCatalog.GraveMoth, 3),
        new("grave-moth-studied", "The Patient Kind",
            "One was kept in a case for study. It ate the label, then the card the label was pinned to, and then waited eleven months without apparent inconvenience.",
            TheCrypt, LoreTrigger.MonsterSlain, MonsterCatalog.GraveMoth, 10),

        // Salt Widow, on the drowned coast.
        new("salt-widow-sighted", "At the Tideline",
            "She is only ever seen facing the water. Coastal folk consider walking behind her rude rather than dangerous, which is its own kind of statement.",
            TheDrownedCoast, LoreTrigger.MonsterSeen, MonsterCatalog.SaltWidow, 1),
        new("salt-widow-known", "The Register",
            "The harbour register lists the boat as lost with all hands, sixty years back, in a hand that pressed hard enough to go through the page.",
            TheDrownedCoast, LoreTrigger.MonsterSlain, MonsterCatalog.SaltWidow, 3),
        new("salt-widow-studied", "All Hands",
            "The crew list has one name fewer than the muster. The missing name is hers, and she was never aboard.",
            TheDrownedCoast, LoreTrigger.MonsterSlain, MonsterCatalog.SaltWidow, 10),

        // Pit Foreman, at the quarry.
        new("pit-foreman-sighted", "The Tally",
            "It carries a slate and adds to it. Quarrymen who have seen the slate say the columns are wages, and that the arithmetic is correct.",
            TheQuarry, LoreTrigger.MonsterSeen, MonsterCatalog.PitForeman, 1),
        new("pit-foreman-known", "Owed",
            "The pay chest went into the flooded lower gallery with the roof. Every name on the slate is a name from that shift.",
            TheQuarry, LoreTrigger.MonsterSlain, MonsterCatalog.PitForeman, 3),
        new("pit-foreman-studied", "The Last Column",
            "There is a column at the bottom for the foreman's own wage, and it is the only one crossed through. It was crossed through first.",
            TheQuarry, LoreTrigger.MonsterSlain, MonsterCatalog.PitForeman, 10),

        // Cairn Wight, in the high passes.
        new("cairn-wight-sighted", "Stones Enough",
            "The cairn is larger than a burial needs and has been added to by every party that has passed it. Nobody adds a stone twice.",
            TheHighPasses, LoreTrigger.MonsterSeen, MonsterCatalog.CairnWight, 1),
        new("cairn-wight-known", "Stacked From Within",
            "The stones nearest the ground are laid with their flat faces inward. Anybody who has built a wall will tell you what that means about which side the builder stood on.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.CairnWight, 3),
        new("cairn-wight-studied", "The Custom",
            "The custom of adding a stone is older than the cairn. It was practised on the pass before there was anything there to add to.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.CairnWight, 10),

        // Lantern Wraith, in the fen.
        new("lantern-wraith-sighted", "Out and Back",
            "The light goes out across the marsh at dusk at a walking pace. What comes back comes back faster and without it.",
            TheFen, LoreTrigger.MonsterSeen, MonsterCatalog.LanternWraith, 1),
        new("lantern-wraith-known", "The Lantern Count",
            "The chandler on the fen road sells more lanterns than the village has households, and has done for as long as the accounts run.",
            TheFen, LoreTrigger.MonsterSlain, MonsterCatalog.LanternWraith, 3),
        new("lantern-wraith-studied", "Where They Go",
            "Dredging turned up forty of them in a line, all facing the same way, all still shut. The line points at nothing anybody has been able to find.",
            TheFen, LoreTrigger.MonsterSlain, MonsterCatalog.LanternWraith, 10),

        // Scree Giant, in the high passes.
        new("scree-giant-sighted", "Spring Thaw",
            "It comes down with the loose stone and goes back up carrying less than it brought. Where the difference goes is a matter of local opinion.",
            TheHighPasses, LoreTrigger.MonsterSeen, MonsterCatalog.ScreeGiant, 1),
        new("scree-giant-known", "The Shorter Pass",
            "The pass is measurably shorter than it was a century ago. Surveyors have blamed the survey four times and the mountain none.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.ScreeGiant, 3),
        new("scree-giant-studied", "What It Takes Up",
            "It is building something at the top. Nobody has been high enough to say what, and everybody who has been high enough to guess has guessed the same thing.",
            TheHighPasses, LoreTrigger.MonsterSlain, MonsterCatalog.ScreeGiant, 10),

        // Hollow Abbot, in the crypt.
        new("hollow-abbot-sighted", "The Hours",
            "The bell is rung at matins and at vespers and has not been late. The house has stood empty for ninety years.",
            TheCrypt, LoreTrigger.MonsterSeen, MonsterCatalog.HollowAbbot, 1),
        new("hollow-abbot-known", "The Rule",
            "The rule of the house required the abbot to keep the hours whatever befell. It did not say for how long, an omission the order considered too obvious to correct.",
            TheCrypt, LoreTrigger.MonsterSlain, MonsterCatalog.HollowAbbot, 3),
        new("hollow-abbot-studied", "Nothing Inside",
            "The habit is empty. The bell is not, and what is in the bell is the only part of the abbot that answered when the plague took the house.",
            TheCrypt, LoreTrigger.MonsterSlain, MonsterCatalog.HollowAbbot, 10),
    ];

    private static readonly Dictionary<string, LoreFragment> ByKey =
        All.ToDictionary(f => f.Key, StringComparer.Ordinal);

    public static LoreFragment? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);

    public static IReadOnlyList<LoreFragment> ForPlace(string placeKey) =>
        [.. All.Where(f => f.PlaceKey == placeKey)];

    public static IReadOnlyList<LoreFragment> ForMonster(string monsterKey) =>
        [.. All.Where(f => f.Subject == monsterKey &&
                           f.Trigger is LoreTrigger.MonsterSeen or LoreTrigger.MonsterSlain)];
}
