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

    public static DungeonDto ToDto(this DungeonDefinition dungeon) => new(
        dungeon.Key,
        dungeon.Name,
        dungeon.Blurb,
        dungeon.Level,
        dungeon.Rooms,
        dungeon.BossKey,
        dungeon.Boss?.Name ?? dungeon.BossKey,
        dungeon.ClearGold,
        RarityRules.Describe(dungeon.RewardFloor),
        CombatService.StaminaPerEncounter,
        dungeon.Rooms * CombatService.StaminaPerEncounter);

    /// <summary>
    /// A run as the dungeon screen renders it, rooms and all.
    /// </summary>
    /// <remarks>
    /// The room names come from the catalog by key (DEC-004), so a retuned monster is renamed in
    /// every run in progress at once, and a key retired from the bestiary reads as its own key
    /// rather than as an empty label.
    /// </remarks>
    public static DungeonRunDto ToDto(this DungeonRunView view) => new(
        view.Run.Id,
        view.Run.DungeonKey,
        view.Run.Dungeon?.Name ?? view.Run.DungeonKey,
        view.Run.Status.ToString().ToLowerInvariant(),
        [.. view.Rooms.Select((key, index) => new DungeonRoomDto(
            index,
            key,
            MonsterCatalog.Find(key)?.Name ?? key,
            RoomState(index, view.Depth, view.Run.IsOver)))],
        view.Depth,
        view.Run.GoldAwarded,
        view.Encounter?.ToDto(),
        view.Run.StartedAt,
        view.Run.EndedAt);

    /// <summary>
    /// Where a room sits relative to the player.
    /// </summary>
    /// <remarks>
    /// A finished run has no current room, whichever way it finished. On a failed or abandoned run
    /// the room that ended it reads as ahead, which is the truth: it was never won.
    /// </remarks>
    private static string RoomState(int index, int depth, bool runIsOver) => index switch
    {
        _ when index < depth => "cleared",
        _ when index == depth && !runIsOver => "current",
        _ => "ahead"
    };

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
            encounter.Phase,
            // Looked up from the catalog by the stored number (DEC-004), which is also why a
            // phase retired from the catalog reads as no name rather than as a stale one.
            monster?.PhaseDefinition(encounter.Phase)?.Name,
            // Pruned on the way out, so an effect spent down to nothing in the round just
            // resolved cannot be rendered as still riding the fight.
            [.. StatusEffects.Prune(StatusEffects.Read(encounter)).Select(ToDto)],
            encounter.GoldAwarded,
            CombatService.ReadLog(encounter).Select(CombatRollDto.From).ToList(),
            encounter.StartedAt,
            encounter.EndedAt);
    }

    /// <summary>
    /// One effect as the strip renders it.
    /// </summary>
    /// <remarks>
    /// Both enums are lowercased here rather than sent as their member names, matching
    /// <see cref="EncounterDto.Status"/> and <see cref="InventoryItemDto.Slot"/>. The client
    /// keys its icons and its colours off these two strings, so one producer sending PascalCase
    /// would silently render an unlabelled chip rather than fail.
    /// </remarks>
    private static StatusEffectDto ToDto(StatusEffect effect) => new(
        effect.Kind.ToString().ToLowerInvariant(),
        effect.Target.ToString().ToLowerInvariant(),
        effect.Rounds,
        effect.Magnitude,
        effect.Source);

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
            ForgeRules.ReforgeCost(item.Rarity),
            item.Quantity,
            // At the rolled rarity, not at Common, so the card says what this row actually does.
            definition?.Use?.At(item.Rarity).Describe());
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

    public static BestiaryDto ToDto(this BestiaryCodex codex) => new(
        codex.Rows.Select(ToDto).ToList(),
        codex.Discovered,
        codex.Slain,
        codex.Total);

    private static BestiaryEntryDto ToDto(BestiaryRow row)
    {
        var entry = row.Entry;

        return new BestiaryEntryDto(
            row.Monster.Key,
            row.Monster.Name,
            entry is null ? null : row.Monster.Blurb,
            row.Monster.Level,
            IsDiscovered: entry is not null,
            IsSlain: entry?.IsSlain ?? false,
            entry?.Encounters ?? 0,
            entry?.Kills ?? 0,
            entry?.GoldTaken ?? 0,
            entry?.BestRound ?? 0,
            entry?.FirstSeenAt,
            entry?.LastSeenAt);
    }

    public static LoreDto ToDto(this LoreCollection collection) => new(
        collection.Places.Select(ToDto).ToList(),
        collection.Unlocked,
        collection.Total);

    private static LorePlaceDto ToDto(LorePlaceView place) => new(
        place.Place.Key,
        place.Place.Name,
        place.Place.Blurb,
        place.Fragments.Select(ToDto).ToList(),
        place.Fragments.Count(f => f.IsUnlocked),
        place.Fragments.Count);

    private static LoreFragmentDto ToDto(LoreFragmentView view) => new(
        view.Fragment.Key,
        view.Fragment.Title,
        view.IsUnlocked ? view.Fragment.Body : null,
        view.IsUnlocked,
        Requirement(view.Fragment));

    /// <summary>
    /// Says what a locked fragment wants, so the collection reads as a set of things to do
    /// rather than a wall of blanks.
    /// </summary>
    private static string Requirement(LoreFragment fragment)
    {
        var monster = MonsterCatalog.Find(fragment.Subject)?.Name ?? fragment.Subject;

        return fragment.Trigger switch
        {
            LoreTrigger.MonsterSeen when fragment.Threshold <= 1 => $"Meet the {monster}",
            LoreTrigger.MonsterSeen => $"Meet the {monster} {fragment.Threshold} times",
            LoreTrigger.MonsterSlain when fragment.Threshold <= 1 => $"Defeat the {monster}",
            LoreTrigger.MonsterSlain => $"Defeat the {monster} {fragment.Threshold} times",
            LoreTrigger.Level => $"Reach level {fragment.Threshold}",
            LoreTrigger.QuestClaimed =>
                $"Claim {QuestCatalog.Find(fragment.Subject)?.Name ?? fragment.Subject}",
            _ => "Not yet"
        };
    }

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
