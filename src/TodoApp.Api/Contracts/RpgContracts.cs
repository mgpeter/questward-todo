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

public sealed record EncounterDto(
    Guid Id,
    string MonsterKey,
    string MonsterName,
    int MonsterHitPoints,
    int MonsterMaxHitPoints,
    string Status,
    int Round,
    int GoldAwarded,
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
    int ReforgeCost);

public sealed record AttackResponse(
    EncounterDto Encounter,
    IReadOnlyList<CombatRollDto> Rolls,
    int PlayerHitPoints,
    int PlayerMaxHitPoints,
    int GoldAwarded,
    InventoryItemDto? Loot,
    IReadOnlyList<QuestAdvanceDto> QuestsAdvanced,
    CharacterSheetDto Sheet);

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

public sealed record ShopDto(IReadOnlyList<ShopOfferDto> Offers, DateTimeOffset RotatesAt, int Gold);

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
