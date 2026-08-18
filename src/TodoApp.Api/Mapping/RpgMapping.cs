using TodoApp.Api.Contracts;
using TodoApp.Api.Services.Rpg;
using TodoApp.Models;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Mapping;

public static class RpgMapping
{
    /// <param name="equipped">
    /// The rows the sheet was built from. Required rather than optional, so a new caller that
    /// forgets set progress fails to compile instead of quietly reporting no sets at all.
    /// </param>
    public static CharacterSheetDto ToDto(
        this CharacterSheet sheet,
        Character character,
        IReadOnlyList<InventoryItem> equipped,
        Encounter? activeEncounter = null)
    {
        var (nextPoint, fullyHealed) = CharacterSheetService.RegenerationForecast(
            character, sheet, DateTimeOffset.UtcNow);

        var remaining = activeEncounter is null
            ? null
            : CombatService.RemainingUses(activeEncounter, character.ClassKey);

        return new CharacterSheetDto(
            sheet.Class?.Key,
            sheet.Class?.Name,
            sheet.Level,
            AbilityDtos(sheet),
            sheet.ArmourClass,
            sheet.AttackBonus,
            sheet.DamageExpression.ToString(),
            AbilityScores.Abbreviate(sheet.AttackAbility),
            character.CurrentHitPoints,
            sheet.MaxHitPoints,
            sheet.ProficiencyBonus,
            sheet.CriticalOn,
            character.Stamina,
            character.Gold,
            sheet.Class is null
                ? null
                : new PerkDto(
                    sheet.Class.Perk.ToString(),
                    sheet.Class.PerkName,
                    sheet.Class.PerkDescription),
            Models.Rpg.ClassAbilities.For(character.ClassKey)
                .Select(a => new ClassAbilityDto(
                    a.Key,
                    a.Name,
                    a.Description,
                    a.UsesPerEncounter,
                    // Outside a fight, show a full bar rather than zero.
                    remaining?.GetValueOrDefault(a.Key) ?? a.UsesPerEncounter))
                .ToList(),
            nextPoint,
            fullyHealed,
            AdventurerService.RestCost(sheet.MaxHitPoints - character.CurrentHitPoints, sheet.Level),
            character.Essence,
            SetCatalog.ProgressFor(equipped).Select(ToDto).ToList());
    }

    private static SetProgressDto ToDto(SetProgress progress) => new(
        progress.Set.Key,
        progress.Set.Name,
        progress.Set.Blurb,
        progress.Equipped,
        progress.Set.Total,
        progress.Set.Bonuses
            .Select(b => new SetTierDto(b.Pieces, b.Description, progress.Active.Contains(b)))
            .ToList());

    private static IReadOnlyList<AbilityDto> AbilityDtos(CharacterSheet sheet) =>
        AbilityScores.All
            .Select(ability => new AbilityDto(
                ability.ToString(),
                AbilityScores.Abbreviate(ability),
                sheet.EffectiveScores[ability],
                sheet.EffectiveScores.Modifier(ability),
                sheet.ItemBonuses[ability]))
            .ToList();

    public static ClassOptionDto ToDto(this CharacterClass characterClass) => new(
        characterClass.Key,
        characterClass.Name,
        characterClass.Blurb,
        characterClass.HitDie.ToString(),
        AbilityScores.Abbreviate(characterClass.Primary),
        AbilityScores.Abbreviate(characterClass.Secondary),
        AbilityScores.All
            .Select(a => new AbilityDto(
                a.ToString(),
                AbilityScores.Abbreviate(a),
                characterClass.StartingScores[a],
                characterClass.StartingScores.Modifier(a),
                0))
            .ToList(),
        new PerkDto(
            characterClass.Perk.ToString(),
            characterClass.PerkName,
            characterClass.PerkDescription),
        ItemCatalog.Find(characterClass.StartingWeaponKey)?.Name ?? characterClass.StartingWeaponKey,
        ItemCatalog.Find(characterClass.StartingArmourKey)?.Name ?? characterClass.StartingArmourKey);

    public static MonsterDto ToDto(this MonsterDefinition monster) => new(
        monster.Key,
        monster.Name,
        monster.Blurb,
        monster.Level,
        monster.ArmourClass,
        monster.MaxHitPoints,
        monster.DamageNotation,
        monster.MinGold,
        monster.MaxGold,
        CombatService.StaminaPerEncounter);

    public static EncounterDto ToDto(this Encounter encounter)
    {
        var monster = encounter.Monster;

        return new EncounterDto(
            encounter.Id,
            encounter.MonsterKey,
            monster?.Name ?? encounter.MonsterKey,
            encounter.MonsterHitPoints,
            monster?.MaxHitPoints ?? encounter.MonsterHitPoints,
            encounter.Status.ToString().ToLowerInvariant(),
            encounter.Round,
            encounter.GoldAwarded,
            CombatService.ReadLog(encounter).Select(CombatRollDto.From).ToList(),
            encounter.StartedAt,
            encounter.EndedAt);
    }

    public static InventoryItemDto ToDto(this InventoryItem item)
    {
        var definition = ItemCatalog.Find(item.ItemKey);
        var (prefix, suffix) = AffixRules.InForce(item);

        // Read off the item, which already sums its intrinsic bonuses with its affixes. A
        // retired key contributes nothing rather than crashing the bag screen.
        var scores = item.AbilityBonuses;

        var bonuses = definition is null
            ? []
            : AbilityScores.All
                .Select(a => new RollModifierDto(AbilityScores.Abbreviate(a), scores[a]))
                .Where(m => m.Value != 0)
                .ToList();

        return new InventoryItemDto(
            item.Id,
            item.ItemKey,
            item.DisplayName,
            definition?.Blurb ?? string.Empty,
            item.Slot.ToString().ToLowerInvariant(),
            RarityRules.Describe(item.Rarity),
            item.IsEquipped,
            definition?.Damage?.ToString(),
            item.ArmourBonus,
            bonuses,
            definition is null ? 1 : Math.Max(1, definition.ValueAt(item.Rarity) / 2),
            item.AcquiredAt,
            prefix?.Word,
            suffix?.Word,
            item.Set?.Name,
            AffixRules.RollableFor(item.Slot, item.Rarity),
            ForgeRules.EssenceFor(item),
            ForgeRules.ImbueCost(item.Rarity),
            ForgeRules.ReforgeCost(item.Rarity));
    }

    public static QuestDto ToDto(this QuestView quest) => new(
        quest.Key,
        quest.Name,
        quest.Description,
        quest.Objectives
            .Select(o => new QuestObjectiveDto(o.Id, o.Description, o.Current, o.Required, o.IsComplete))
            .ToList(),
        quest.IsComplete,
        quest.ClaimedAt is not null,
        quest.ClaimedAt,
        quest.RewardGold,
        quest.RewardItemName,
        quest.IsLocked,
        quest.MinimumLevel);

    public static QuestAdvanceDto ToDto(this QuestAdvance advance) =>
        new(advance.Key, advance.Name, advance.Progress, advance.JustCompleted);

    public static ChronicleSummaryDto ToDto(this ChronicleSummary summary) => new(
        summary.Fought,
        summary.Won,
        summary.Lost,
        summary.Fled,
        summary.GoldEarned,
        summary.MostFoughtMonster,
        summary.MostFoughtCount);

    public static ShopOfferDto ToDto(this ShopOffer offer, int gold, IReadOnlyCollection<string> soldOut) => new(
        offer.OfferId,
        offer.Item.Key,
        offer.Item.Name,
        offer.Item.Blurb,
        offer.Item.Slot.ToString().ToLowerInvariant(),
        RarityRules.Describe(offer.Rarity),
        offer.Item.Damage?.ToString(),
        offer.Item.ArmourBonusAt(offer.Rarity),
        AbilityScores.All
            .Select(a => new RollModifierDto(
                AbilityScores.Abbreviate(a),
                offer.Item.AbilityBonusesAt(offer.Rarity)[a]))
            .Where(m => m.Value != 0)
            .ToList(),
        offer.Price,
        gold >= offer.Price,
        soldOut.Contains(offer.OfferId));
}
