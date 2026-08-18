namespace TodoApp.Models.Rpg;

/// <summary>A point in a round that a narrative line can be attached to.</summary>
public enum FlavourMoment
{
    Opening = 0,
    PlayerHit = 1,
    PlayerMiss = 2,
    PlayerCritical = 3,
    PlayerFumble = 4,
    MonsterHit = 5,
    MonsterMiss = 6,
    MonsterCritical = 7,
    Kill = 8,
    Defeat = 9,
    Flee = 10
}

/// <summary>
/// Code-held per DEC-004, and deliberately not reachable from the dice. Selection is a hash
/// of the encounter id, the round and the moment, so the same fight always reads the same way
/// on reload and no line costs a roll.
/// </summary>
public static class FlavourCatalog
{
    /// <summary>The only token a line may contain. Anything else fails the lint test.</summary>
    public const string MonsterToken = "{monster}";

    private static readonly Dictionary<FlavourMoment, string[]> Lines = new()
    {
        [FlavourMoment.Opening] =
        [
            "The {monster} sees you and stays exactly where it is.",
            "The ground here is packed flat by earlier visits.",
            "The {monster} was here first and intends to remain.",
            "You take the measure of the {monster}, which takes yours.",
            "The {monster} stops what it was doing.",
            "Neither of you has anywhere else to be.",
            "The {monster} adjusts its footing without hurry.",
            "Somebody dropped a lantern here and never came back for it.",
            "Nothing here has been cleaned up in a while.",
            "The {monster} has done this before, by the look of it.",
            "You check your grip out of habit.",
            "The {monster} closes the distance at a walk.",
            "There is room here for one of you.",
            "The {monster} makes a sound you decide to ignore.",
            "Old bones lie about, arranged by nobody.",
            "The {monster} waits for you to move first.",
            "Your boots find level ground, which is something.",
            "A cold draught comes from somewhere behind the {monster}.",
            "Nothing about the {monster} suggests surprise.",
            "You have fought worse and remember most of it.",
            "The {monster} blocks the only clear way through.",
            "Whatever happened here last happened more than once."
        ],
        [FlavourMoment.PlayerHit] =
        [
            "The blow lands where you meant it to.",
            "It connects, and the {monster} takes note.",
            "Your swing goes through and the {monster} gives ground.",
            "A solid hit, nothing decorative about it.",
            "The {monster} absorbs it and stays upright.",
            "You land it clean and step back out.",
            "The strike arrives before the {monster} has finished moving.",
            "It lands with a flat, unremarkable sound.",
            "The {monster} shifts its weight to cover the damage.",
            "You find the gap and use it.",
            "The hit lands slightly off target and counts anyway.",
            "Your weapon does what it was made to do.",
            "The {monster} takes a step it did not choose.",
            "It lands and the {monster} keeps coming.",
            "The {monster} learns something about your reach.",
            "A short, economical hit, nothing wasted.",
            "You get through the guard and out again.",
            "The impact travels up your arm and settles.",
            "The {monster} takes it in a bad place.",
            "Contact, brief and to the point.",
            "The {monster} is slower afterwards, marginally.",
            "Your footing holds and the strike goes home."
        ],
        [FlavourMoment.PlayerMiss] =
        [
            "The {monster} is not where you left it.",
            "Your swing meets air and keeps going.",
            "Too slow by the width of a hand.",
            "The {monster} leans out of the way.",
            "You commit early and pay for it.",
            "The strike passes close enough to matter.",
            "Nothing there but the space it just used.",
            "The {monster} steps inside the arc and out.",
            "You misjudge the distance and correct afterwards.",
            "The blow goes wide and takes your balance with it.",
            "The {monster} moves first, which decides it.",
            "Your weapon finds the ground instead.",
            "It was a fair attempt at the wrong moment.",
            "The {monster} lets it pass and closes again.",
            "You swing through where it was standing.",
            "Close, and closeness counts for nothing here.",
            "The {monster} reads the swing before you finish it.",
            "Loose footing turns a good idea into a miss.",
            "The {monster} is unhurt and knows it.",
            "Your grip slips at the wrong end of the swing.",
            "The angle was wrong from the start.",
            "You recover the guard and lose the moment."
        ],
        [FlavourMoment.PlayerCritical] =
        [
            "Everything lines up and the {monster} is in the way.",
            "The hit goes deep and stays there.",
            "You find the seam and the {monster} finds the floor.",
            "That one changes the shape of the fight.",
            "The strike lands where armour was not.",
            "Clean through, and the {monster} has no answer.",
            "The {monster} was not expecting that angle.",
            "You put your whole weight behind it and connect.",
            "The blow arrives ahead of the guard.",
            "It lands in exactly the wrong place for the {monster}.",
            "The {monster} goes quiet for a moment.",
            "A single unhurried strike, perfectly placed.",
            "The gap opens and you are already through it.",
            "Your weapon rings and the {monster} folds around it.",
            "You catch the {monster} between one movement and the next.",
            "That will be felt for the rest of this.",
            "The {monster} loses its footing and most of its confidence.",
            "The strike goes in under the guard entirely.",
            "Nothing fancy, only very well aimed.",
            "The {monster} staggers and does not recover the ground.",
            "You are in the right place at the right instant.",
            "The impact goes somewhere important."
        ],
        [FlavourMoment.PlayerFumble] =
        [
            "Your foot finds the one loose stone.",
            "The swing goes badly wrong from the wrist.",
            "You overreach and the {monster} watches you do it.",
            "Your weapon turns in your hand at the worst moment.",
            "You do everything right and none of it at once.",
            "You lose the guard and have to buy it back.",
            "The strike goes wide and drags you after it.",
            "Your grip fails and the swing goes with it.",
            "The {monster} does not need to move at all.",
            "You catch your elbow on the way through.",
            "Something in the harness shifts at the wrong moment.",
            "You step where the ground was not.",
            "The attack ends before it properly starts.",
            "You spend the whole moment recovering your balance.",
            "The {monster} has time to consider its options.",
            "Your weapon fouls on your own cloak.",
            "The blow lands nowhere and costs you the stance.",
            "You swing hard at an empty piece of room.",
            "The strap gives, and the swing gives with it.",
            "You end up facing slightly the wrong way.",
            "That was worse than an ordinary miss.",
            "Your own momentum takes you off the line."
        ],
        [FlavourMoment.MonsterHit] =
        [
            "The {monster} lands one and takes the ground back.",
            "It gets through the guard without ceremony.",
            "The {monster} hits where the armour is thinnest.",
            "You take it on the shoulder and stay standing.",
            "The {monster} presses the advantage and does not gloat.",
            "That gets through and leaves a mark.",
            "The {monster} is stronger than it looks.",
            "The blow arrives while you are still moving.",
            "You catch it on the arm and keep your feet.",
            "The {monster} works on one side of you.",
            "It lands solidly and the {monster} withdraws.",
            "Your armour takes most of it, not all.",
            "The {monster} hits and steps out of reach.",
            "Something gives in the padding at your ribs.",
            "The {monster} takes the opening you left open.",
            "It hits, and the room tilts briefly.",
            "The {monster} is quicker at close range.",
            "You are moved further back than you intended.",
            "The impact folds you up a little.",
            "The {monster} does not waste the opportunity.",
            "Your guard holds and your footing does not.",
            "It lands square and the breath goes out."
        ],
        [FlavourMoment.MonsterMiss] =
        [
            "The {monster} commits too early and pays for it.",
            "It goes past your ear and hits nothing.",
            "You are already elsewhere by then.",
            "The {monster} strikes the ground where you were.",
            "The attempt comes close and stays an attempt.",
            "Your armour is never troubled by that one.",
            "The {monster} misjudges the reach entirely.",
            "It swings and the swing goes nowhere.",
            "You give ground and the blow follows badly.",
            "The {monster} finds only the space you left.",
            "Nothing lands, and the {monster} resets.",
            "The attack passes over your shoulder.",
            "The {monster} is slower than it intended.",
            "You step aside and let it go past.",
            "It comes in high and stays there.",
            "The {monster} tries an angle that was never there.",
            "You turn it away with the guard.",
            "The blow glances away and takes nothing with it.",
            "The {monster} overbalances slightly and recovers.",
            "That one was never going to land.",
            "You lean back and the {monster} finds air.",
            "The {monster} looks briefly annoyed with itself."
        ],
        [FlavourMoment.MonsterCritical] =
        [
            "The {monster} finds the gap and uses all of it.",
            "That one goes through everything you own.",
            "The {monster} hits hard enough to move you.",
            "You lose whatever you were braced against.",
            "The {monster} puts you on the back foot entirely.",
            "It lands where nothing was covering.",
            "The blow arrives with the whole {monster} behind it.",
            "Your guard is opened and stays opened.",
            "The {monster} strikes twice in the space of once.",
            "That will need attention afterwards.",
            "The {monster} finds the join in your armour.",
            "You go down to one knee and get up.",
            "The impact takes the sound out of everything.",
            "The {monster} was faster than the evidence suggested.",
            "It lands cleanly and the floor comes up.",
            "Your footing goes first and the rest follows.",
            "The {monster} hits with everything it has left.",
            "The blow gets under the guard and stays.",
            "You are turned around before you can answer.",
            "The {monster} makes the most of one opening.",
            "Something in your harness gives entirely.",
            "It lands where your attention was not."
        ],
        [FlavourMoment.Kill] =
        [
            "The {monster} stops, and then stays stopped.",
            "It goes down and does not get up.",
            "The {monster} has nothing further to offer.",
            "It is quieter than it was.",
            "The {monster} settles into the floor and stays.",
            "That is the end of the {monster}.",
            "It goes over without much ceremony.",
            "The {monster} is finished, and so is this.",
            "You lower the weapon and listen for a while.",
            "The {monster} goes still in an unremarkable way.",
            "Nothing moves except your own breathing.",
            "The {monster} runs out of fight entirely.",
            "It ends the way these things usually end.",
            "The {monster} folds up and stays that way.",
            "You step back and the {monster} does not.",
            "That was the last thing it had.",
            "The {monster} lies where it stood.",
            "The fight leaves before the {monster} does.",
            "You check twice and it stays down.",
            "The {monster} is out of the argument.",
            "It stops, and nothing takes its place.",
            "You clean the weapon and take stock."
        ],
        // Defeat is softened by design: CombatService floors the player at one hit point and
        // says "You are driven off, battered but breathing." A line here that has the player
        // black out or hit the floor contradicts the clause it is printed inside, in the same
        // sentence, so these narrate a fight lost on your feet rather than a body on the
        // ground. FlavourTests holds the line.
        [FlavourMoment.Defeat] =
        [
            "Your legs decide the matter and carry you out.",
            "The way out arrives sooner than expected.",
            "You give ground and the {monster} takes all of it.",
            "The light narrows to the way you came in.",
            "You lose the fight some time before you notice.",
            "Your grip opens and the weapon goes.",
            "The {monster} is still standing, which settles it.",
            "You back off without meaning to.",
            "That was the last thing you had.",
            "Everything ahead of you is further away than it was.",
            "You finish before the {monster} does.",
            "Your own weight is most of what you carry out.",
            "Your guard drops and stays down.",
            "You are done, and the {monster} is not.",
            "Everything goes quiet apart from the {monster}.",
            "You lose the thread of it entirely.",
            "The last blow was not the worst one.",
            "You take one step back too many.",
            "The {monster} steps back and lets you go.",
            "Your armour holds and you do not.",
            "The fight ends without your agreement.",
            "You are moved off the ground you were holding."
        ],
        [FlavourMoment.Flee] =
        [
            "You leave while leaving is still an option.",
            "The {monster} does not follow far.",
            "You take the first gap at speed.",
            "Leaving is a decision like any other.",
            "The {monster} watches you go and stays put.",
            "You put distance between the two of you.",
            "The way out is longer than the way in.",
            "You go back the way you came, faster.",
            "The {monster} keeps the ground and you keep breathing.",
            "Nothing here is worth the rest of it.",
            "You break off and do not look back.",
            "The {monster} is left with the place to itself.",
            "You leave the {monster} to its own company.",
            "The sound of the {monster} fades behind you.",
            "You give up the ground and keep the rest.",
            "Retreat, unhurried, and in one piece.",
            "The {monster} makes no effort to stop you.",
            "You take the long way out and live.",
            "There will be another one of those later.",
            "You disengage before it becomes something worse.",
            "The {monster} returns to whatever it was doing.",
            "You keep your back to the wall going out."
        ]
    };

    public static IReadOnlyList<string> For(FlavourMoment moment) => Lines[moment];

    /// <summary>
    /// The line for one moment of one round. Pure, so a reloaded encounter narrates
    /// identically to the round that produced it.
    /// </summary>
    public static string Pick(FlavourMoment moment, Guid encounterId, int round, string monsterName)
    {
        var lines = Lines[moment];
        var index = (int)(Mix(encounterId, round, (int)moment) % (uint)lines.Length);

        return lines[index].Replace(MonsterToken, monsterName, StringComparison.Ordinal);
    }

    // FNV-1a over the encounter id, the round and the moment. Deliberately not GetHashCode:
    // string and Guid hash codes are randomised per process in .NET, so the same fight would
    // narrate itself differently after a restart and no test could ever assert a line. The
    // moment ordinal is folded in so two moments in the same round cannot land on the same
    // index and read as an echo.
    private static uint Mix(Guid encounterId, int round, int moment)
    {
        const uint Offset = 2166136261;
        const uint Prime = 16777619;

        Span<byte> id = stackalloc byte[16];
        encounterId.TryWriteBytes(id);

        var hash = Offset;
        foreach (var b in id) hash = (hash ^ b) * Prime;
        foreach (var b in BitConverter.GetBytes(round)) hash = (hash ^ b) * Prime;
        foreach (var b in BitConverter.GetBytes(moment)) hash = (hash ^ b) * Prime;

        return hash;
    }
}
