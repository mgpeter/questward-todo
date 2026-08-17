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
    int RestCost);

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
    string Text)
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
        roll.Text);
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
    DateTimeOffset AcquiredAt);

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
    bool Affordable);

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

public sealed record EquipResponse(CharacterSheetDto Sheet, IReadOnlyList<InventoryItemDto> Inventory);
