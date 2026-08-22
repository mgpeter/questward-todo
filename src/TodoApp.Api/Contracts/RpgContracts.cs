using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Contracts;

public sealed record AbilityDto(string Key, string Abbreviation, int Score, int Modifier, int BonusFromItems);

public sealed record PerkDto(string Key, string Name, string Description);

public sealed record ClassAbilityDto(
    string Key,
    string Name,
    string Description,
    int UsesPerEncounter,
    int Remaining);

public sealed record CharacterSheetDto(
    string? ClassKey,
    string? ClassName,
    int Level,
    IReadOnlyList<AbilityDto> Abilities,
    int ArmourClass,
    int AttackBonus,
    string Damage,
    string AttackAbility,
    int CurrentHitPoints,
    int MaxHitPoints,
    int ProficiencyBonus,
    int CriticalOn,
    int Stamina,
    int Gold,
    PerkDto? Perk,
    /// <summary>Active class abilities. Distinct from Abilities, which are ability scores.</summary>
    IReadOnlyList<ClassAbilityDto> ClassAbilities,
    /// <summary>When the next hit point returns, so the UI can show a countdown.</summary>
    DateTimeOffset? NextRegenerationAt,
    DateTimeOffset? FullyHealedAt,
    /// <summary>Gold a full heal would cost right now. Zero when already whole.</summary>
    int RestCost,
    /// <summary>The forge's currency. Earned only by breaking items down.</summary>
    int Essence,
    /// <summary>
    /// Only sets the wearer has at least one piece equipped from. The rest are discovered
    /// through <see cref="InventoryItemDto.SetName"/> on the pieces themselves.
    /// </summary>
    IReadOnlyList<SetProgressDto> Sets);

/// <param name="Active">True when enough pieces are worn for this tier to be paying.</param>
public sealed record SetTierDto(int Pieces, string Description, bool Active);

public sealed record SetProgressDto(
    string Key,
    string Name,
    string Blurb,
    int Equipped,
    int Total,
    IReadOnlyList<SetTierDto> Tiers);

public sealed record ClassOptionDto(
    string Key,
    string Name,
    string Blurb,
    string HitDie,
    string PrimaryAbility,
    string SecondaryAbility,
    IReadOnlyList<AbilityDto> StartingScores,
    PerkDto Perk,
    string StartingWeapon,
    string StartingArmour);

public sealed record ChooseClassRequest(string ClassKey);

public sealed record MonsterDto(
    string Key,
    string Name,
    string Blurb,
    int Level,
    int ArmourClass,
    int MaxHitPoints,
    string Damage,
    int MinGold,
    int MaxGold,
    int StaminaCost);

public sealed record DieRollDto(int Sides, int Value, bool Kept);

public sealed record RollModifierDto(string Label, int Value);

/// <summary>
/// A roll as the client renders it. Carries the individual dice and every labelled
/// modifier, not just the total, so the arithmetic is visible on screen.
/// </summary>
public sealed record CombatRollDto(
    int Round,
    string Actor,
    string Kind,
    IReadOnlyList<DieRollDto> Dice,
    IReadOnlyList<RollModifierDto> Modifiers,
    int Total,
    int? Target,
    string Outcome,
    bool Critical,
    string Text,
    /// <summary>The narrative tail of <see cref="Text"/>, null when the line is purely mechanical.</summary>
    string? Flavour)
{
    public static CombatRollDto From(CombatRoll roll) => new(
        roll.Round,
        roll.Actor,
        roll.Kind,
        roll.Dice.Select(d => new DieRollDto(d.Sides, d.Value, d.Kept)).ToList(),
        roll.Modifiers.Select(m => new RollModifierDto(m.Label, m.Value)).ToList(),
        roll.Total,
        roll.Target,
        roll.Outcome,
        roll.Critical,
        roll.Text,
        roll.Flavour);
}

/// <summary>
/// One affliction or blessing riding a fight, as the strip renders it.
/// </summary>
/// <remarks>
/// <paramref name="Kind"/> and <paramref name="Target"/> are lowercased at the mapping site the
/// way <see cref="EncounterDto.Status"/> is, so the client reads one casing everywhere.
/// </remarks>
/// <param name="Rounds">
/// Applications remaining, not rounds elapsed, which is why a chip reading "2 left" can still be
/// spent twice inside one exchange.
/// </param>
/// <param name="Source">Key of whatever applied it: an ability, an item or a monster phase.</param>
public sealed record StatusEffectDto(
    string Kind,
    string Target,
    int Rounds,
    int Magnitude,
    string Source);

public sealed record EncounterDto(
    Guid Id,
    string MonsterKey,
    string MonsterName,
    int MonsterHitPoints,
    int MonsterMaxHitPoints,
    string Status,
    int Round,
    /// <summary>The highest boss phase this fight has entered. Zero for anything with none.</summary>
    int Phase,
    /// <param name="PhaseName">
    /// The catalog name of that phase, so the client can label the fight without knowing the
    /// thresholds. Null until a phase has been entered.
    /// </param>
    string? PhaseName,
    /// <param name="Effects">
    /// Both combatants' afflictions and blessings, in one array. Without it the client cannot
    /// show why a swing is suddenly rolled at disadvantage, and the rule change a boss phase
    /// announces would be invisible for the rest of the fight.
    /// </param>
    IReadOnlyList<StatusEffectDto> Effects,
    int GoldAwarded,
    /// <param name="Log">
    /// Every line of the fight, oldest first, in the order the events happened.
    ///
    /// Stated because the order is the only sequence record there is: a CombatRoll carries no
    /// ordinal and no timestamp, and one round stamps its attack, its damage, the monster's
    /// reply and the tick with the same Round. Position is what says which came first, so a
    /// consumer that wants the newest at the top reverses on the way to the screen and leaves
    /// this alone.
    /// </param>
    IReadOnlyList<CombatRollDto> Log,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

public sealed record StartEncounterRequest(string MonsterKey);

/// <param name="Name">
/// The rolled name, prefix and suffix included, never the bare catalog name. Every producer in
/// the app goes through <see cref="AffixRules.DisplayName(InventoryItem)"/>, because a screen
/// that showed the plain name would read to the player as the affix having been lost.
/// </param>
/// <param name="ArmourBonus">Item and affixes. Set bonuses belong to the wearer, not to a piece.</param>
/// <param name="AbilityBonuses">Item and affixes, for the same reason.</param>
/// <param name="AffixSlots">
/// How many words this item could hold at its rarity, so the client can tell a full Uncommon
/// from a half-filled Epic without knowing the rule.
/// </param>
/// <summary>What one step at the upgrade bench costs, and what it buys.</summary>
/// <remarks>
/// Sent on the row rather than fetched per item, and null rather than flagged: an item that
/// cannot be upgraded has no price and no preview to quote, so one nullable answers "is this
/// upgradeable" as well as "what would it do" and the client stops having to guess the rule.
/// It guessed wrong - `rarity !== 'legendary'` offered potions the bench refuses.
///
/// The numbers are the same pure functions the upgrade itself runs, so a preview cannot drift
/// from the outcome without a test noticing.
/// </remarks>
/// <param name="AffixesGrow">
/// Whether the words already on the item get stronger at the next rarity. Only true crossing
/// into Epic: magnitude is one at Uncommon and Rare, two at Epic and Legendary. The bench used
/// to promise growth on every step.
/// </param>
public sealed record UpgradePreviewDto(
    string ToRarity,
    int Cost,
    int ArmourBonus,
    IReadOnlyList<RollModifierDto> AbilityBonuses,
    int AffixSlots,
    bool AffixesGrow);

public sealed record InventoryItemDto(
    Guid Id,
    string ItemKey,
    string Name,
    string Blurb,
    string Slot,
    string Rarity,
    bool IsEquipped,
    string? Damage,
    int ArmourBonus,
    IReadOnlyList<RollModifierDto> AbilityBonuses,
    int SellValue,
    DateTimeOffset AcquiredAt,
    string? Prefix,
    string? Suffix,
    string? SetName,
    int AffixSlots,
    int SalvageValue,
    int ImbueCost,
    int ReforgeCost,
    /// <summary>
    /// How many of this item the row holds. One for everything worn; a consumable stacks.
    /// </summary>
    int Quantity,
    /// <summary>
    /// What using one does, at this row's rarity. Null for anything that is not a consumable, so
    /// the client can tell a usable item from a worn one without a slot lookup.
    /// </summary>
    string? UseDescription,
    /// <summary>The next step at the bench, or null when there is not one.</summary>
    UpgradePreviewDto? Upgrade);

/// <param name="Loot">What the monster itself dropped, or null when it dropped nothing.</param>
/// <param name="ClearReward">
/// The dungeon's guaranteed reward, on the round that cleared the last room. Its own member
/// rather than folded into <paramref name="Loot"/> because a clear round can hand over two
/// items, and the one that reported a single slot showed nothing at all on the common case
/// where the boss's own drop failed its roll while the run still paid out.
/// </param>
public sealed record AttackResponse(
    EncounterDto Encounter,
    IReadOnlyList<CombatRollDto> Rolls,
    int PlayerHitPoints,
    int PlayerMaxHitPoints,
    int GoldAwarded,
    InventoryItemDto? Loot,
    InventoryItemDto? ClearReward,
    IReadOnlyList<QuestAdvanceDto> QuestsAdvanced,
    CharacterSheetDto Sheet);

/// <param name="StaminaPerRoom">One fight's worth, because a room is one fight.</param>
/// <param name="TotalStaminaCost">
/// What the whole run costs, spelled out rather than left to the client to multiply. A five room
/// run is five units of real work (DEC-012), and the screen that sells the run should say so
/// before it is started rather than after the fourth room refuses to open.
/// </param>
public sealed record DungeonDto(
    string Key,
    string Name,
    string Blurb,
    int Level,
    int Rooms,
    string BossKey,
    string BossName,
    int ClearGold,
    string RewardFloor,
    int StaminaPerRoom,
    int TotalStaminaCost);

/// <param name="State">"cleared", "current" for the room to enter next, or "ahead".</param>
public sealed record DungeonRoomDto(int Index, string MonsterKey, string MonsterName, string State);

/// <param name="Depth">
/// Rooms won, derived from the encounters rather than stored (DEC-002). Also the index of the
/// room to enter next, which is all a reloaded client needs to pick the run back up.
/// </param>
/// <param name="Encounter">The fight in progress, or null when the next room is unopened.</param>
public sealed record DungeonRunDto(
    Guid Id,
    string DungeonKey,
    string Name,
    string Status,
    IReadOnlyList<DungeonRoomDto> Rooms,
    int Depth,
    int GoldAwarded,
    EncounterDto? Encounter,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

public sealed record StartDungeonRequest(string DungeonKey);

/// <summary>
/// One line of the contract board: a task, written up and priced, before it has been taken.
/// </summary>
/// <remarks>
/// Everything here is derived on the read and stored nowhere, so opening the board twice quotes
/// the same purse twice. The whole block is what the fight will be opened against, which is why
/// it is quoted in full rather than summarised: the decision the player is making is which of
/// their own chores to promise to finish, and what finishing it will be worth.
/// </remarks>
/// <param name="DaysOverdue">
/// Measured from the recurrence gate for a recurring task rather than its due date, which is
/// never advanced by completion. A daily task done faithfully every day is zero here, not the
/// age of the due date it was first given.
/// </param>
/// <param name="BountyPercent">
/// The gold multiplier this contract's age has earned, as a percentage. Never below 100 and
/// never above 200 (DEC-013): an overdue task is a bounty, and nothing on this path subtracts.
/// </param>
/// <param name="PaysContractReward">
/// Whether winning would also hand over a guaranteed item. False for a task that is not overdue
/// and false for one flying no banner, which is what keeps a fresh contract strictly worse per
/// stamina than a band-appropriate tavern fight.
/// </param>
/// <param name="StaminaCost">
/// What the fight will cost, once the work has unlocked it. Accepting costs nothing at all, and
/// this is quoted so the board can say so honestly rather than looking like a price of entry.
/// </param>
public sealed record HuntOfferDto(
    Guid TaskId,
    string Title,
    string Difficulty,
    DateTimeOffset? DueDate,
    int DaysOverdue,
    int Subtasks,
    string ArchetypeKey,
    string MonsterName,
    string Blurb,
    int Level,
    int ArmourClass,
    int MaxHitPoints,
    string Damage,
    int MinGold,
    int MaxGold,
    int DropChance,
    int BountyPercent,
    string? FactionKey,
    string? FactionName,
    string? FactionTitle,
    string Standing,
    string RewardFloor,
    bool PaysContractReward,
    int StaminaCost);

/// <param name="WonHunts">
/// Contracts won under this banner, counted from the encounters rather than stored (DEC-002).
/// Wins, not contracts taken, so a hunt fled is worth no standing.
/// </param>
/// <param name="RewardFloor">
/// The worst a contract reward from this banner can roll at this standing. It lifts a poor roll
/// and never caps a good one, and it is the only mechanical thing standing buys.
/// </param>
public sealed record FactionStandingDto(
    string Key,
    string Name,
    string Blurb,
    string Standing,
    string Title,
    int WonHunts,
    string RewardFloor);

/// <param name="Offers">
/// Every task that could be written up, worst first, and deliberately not trimmed. How many the
/// board shows at once is a display decision and is made on the display: a task card reads its
/// own contract out of this same list, and a list cut off at twenty tells the twenty-first task
/// it has nothing to offer.
/// </param>
/// <param name="Contracts">The contracts already taken: what is promised, and what is owed.</param>
public sealed record HuntBoardDto(
    IReadOnlyList<HuntOfferDto> Offers,
    IReadOnlyList<HuntContractDto> Contracts,
    IReadOnlyList<FactionStandingDto> Factions,
    int Stamina,
    int StaminaPerHunt);

public sealed record AcceptHuntRequest(Guid TaskId);

/// <summary>
/// A contract taken: the promise, and where it is in its three steps.
/// </summary>
/// <remarks>
/// Every number here was frozen when the contract was accepted, which is why a contract whose
/// task has since been re-dated, retagged, re-graded, split or deleted still reports exactly what
/// it was written as.
/// </remarks>
/// <param name="Status">
/// "accepted" while the work is outstanding, "discharged" once it is done. The fight is offered
/// on the second and refused on the first: there is no route from an unfinished task to bounty
/// gold, loot or standing (DEC-013).
/// </param>
/// <param name="TaskId">
/// Null once the task has been deleted. A discharged contract survives that and stays fightable,
/// because doing the work is what earned it.
/// </param>
/// <param name="StaminaCost">One fight's worth, and only once the fight is unlocked (DEC-012).</param>
public sealed record HuntContractDto(
    Guid Id,
    string Status,
    Guid? TaskId,
    string TaskTitle,
    string ArchetypeKey,
    string MonsterName,
    string Blurb,
    int Level,
    int ArmourClass,
    int MaxHitPoints,
    string Damage,
    int MinGold,
    int MaxGold,
    int DropChance,
    int DaysOverdue,
    int Subtasks,
    int BountyPercent,
    string? FactionKey,
    string? FactionName,
    string? FactionTitle,
    string Standing,
    string RewardFloor,
    bool PaysContractReward,
    int StaminaCost,
    DateTimeOffset AcceptedAt,
    DateTimeOffset? DischargedAt);

/// <summary>
/// A contract's fight, live or finished.
/// </summary>
/// <remarks>
/// Wrapped around <see cref="EncounterDto"/> rather than folded into it, because a hunt's fight is
/// an ordinary encounter row and every existing screen that renders one should keep working
/// without learning what a contract is. The fight is driven by the ordinary attack routes.
/// </remarks>
/// <param name="TaskId">
/// Null once the task has been deleted. The fight survives that and stays fully renderable and
/// fully fightable, because every number below was frozen onto the encounter when it was opened.
/// </param>
public sealed record HuntDto(
    Guid EncounterId,
    Guid? ContractId,
    Guid? TaskId,
    string? TaskTitle,
    string ArchetypeKey,
    string MonsterName,
    int Level,
    int DaysOverdue,
    int Subtasks,
    int BountyPercent,
    string? FactionKey,
    string? FactionName,
    string? FactionTitle,
    string Standing,
    EncounterDto Encounter);

public sealed record QuestAdvanceDto(string Key, string Name, string Progress, bool JustCompleted);

public sealed record QuestObjectiveDto(string Id, string Description, int Current, int Required, bool IsComplete);

public sealed record QuestDto(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<QuestObjectiveDto> Objectives,
    bool IsComplete,
    bool IsClaimed,
    DateTimeOffset? ClaimedAt,
    int RewardGold,
    string? RewardItemName,
    bool IsLocked,
    int MinimumLevel);

public sealed record QuestClaimResponse(int GoldGained, int Gold, InventoryItemDto? Item);

public sealed record ChronicleSummaryDto(
    int Fought,
    int Won,
    int Lost,
    int Fled,
    int GoldEarned,
    string? MostFoughtMonster,
    int MostFoughtCount);

public sealed record ChronicleDto(ChronicleSummaryDto Summary, IReadOnlyList<EncounterDto> Encounters);

/// <param name="Blurb">
/// Null until the monster has been met. The description is the reward for the first sighting,
/// so sending it for an undiscovered row would give away what the codex is there to earn.
/// </param>
/// <param name="BestRound">Fewest rounds to a kill. Zero means never killed.</param>
public sealed record BestiaryEntryDto(
    string Key,
    string Name,
    string? Blurb,
    int Level,
    bool IsDiscovered,
    bool IsSlain,
    int Encounters,
    int Kills,
    int GoldTaken,
    int BestRound,
    DateTimeOffset? FirstSeenAt,
    DateTimeOffset? LastSeenAt);

public sealed record BestiaryDto(
    IReadOnlyList<BestiaryEntryDto> Entries,
    int Discovered,
    int Slain,
    int Total);

/// <param name="Body">Null until unlocked. The body is the whole of the reward.</param>
/// <param name="Requirement">
/// What would unlock it, in words. Derived from the trigger rather than stored, so a fragment
/// that changes its ladder cannot start describing itself wrongly.
/// </param>
public sealed record LoreFragmentDto(
    string Key,
    string Title,
    string? Body,
    bool IsUnlocked,
    string Requirement);

public sealed record LorePlaceDto(
    string Key,
    string Name,
    string Blurb,
    IReadOnlyList<LoreFragmentDto> Fragments,
    int Unlocked,
    int Total);

public sealed record LoreDto(IReadOnlyList<LorePlaceDto> Places, int Unlocked, int Total);

/// <param name="SoldOut">
/// Already bought today. Each offer sells once, so without this the card would invite a click
/// that can only come back a 409.
/// </param>
public sealed record ShopOfferDto(
    string OfferId,
    string ItemKey,
    string Name,
    string Blurb,
    string Slot,
    string Rarity,
    string? Damage,
    int ArmourBonus,
    IReadOnlyList<RollModifierDto> AbilityBonuses,
    int Price,
    bool Affordable,
    bool SoldOut);

/// <param name="NextRerollCost">Stamina the next reroll costs, or null once the day is spent.</param>
/// <param name="RerollsLeft">How many restocks the trader will still do today.</param>
public sealed record ShopDto(
    IReadOnlyList<ShopOfferDto> Offers,
    DateTimeOffset RotatesAt,
    int Gold,
    int Stamina,
    int? NextRerollCost,
    int RerollsLeft);

public sealed record PurchaseResponse(InventoryItemDto Item, int GoldSpent, int Gold);

public sealed record UpgradeResponse(
    InventoryItemDto Item,
    string From,
    string To,
    int GoldSpent,
    int Gold);

public sealed record RestResponse(int GoldSpent, int Gold, int HitPoints, int MaxHitPoints);

public sealed record SellResponse(int GoldGained, int Gold);

public sealed record SalvageResponse(int EssenceGained, int Essence);

public sealed record CraftResponse(InventoryItemDto Item, int EssenceSpent, int Essence);

public sealed record EquipResponse(CharacterSheetDto Sheet, IReadOnlyList<InventoryItemDto> Inventory);
