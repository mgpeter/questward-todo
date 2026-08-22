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

    public static HuntBoardDto ToDto(this HuntBoard board) => new(
        board.Offers.Select(ToDto).ToList(),
        board.Contracts.Select(ToDto).ToList(),
        board.Factions.Select(ToDto).ToList(),
        board.Stamina,
        CombatService.StaminaPerEncounter);

    /// <summary>
    /// One offer as the board renders it, block and all.
    /// </summary>
    /// <remarks>
    /// The stat block is quoted from the same <see cref="HuntRules.StatBlock"/> the fight will be
    /// opened against, not from a second summary written for the screen. A board that priced a
    /// contract differently from the fight it sells would be lying in the one place the player is
    /// making a decision.
    /// </remarks>
    private static HuntOfferDto ToDto(HuntOffer offer) => new(
        offer.Task.Id,
        offer.Task.Title,
        offer.Task.Difficulty.ToString().ToLowerInvariant(),
        offer.Task.DueDate,
        offer.DaysOverdue,
        offer.Subtasks,
        offer.Monster.Key,
        offer.Monster.Name,
        offer.Monster.Blurb,
        offer.Monster.Level,
        offer.Monster.ArmourClass,
        offer.Monster.MaxHitPoints,
        offer.Monster.DamageNotation,

        // Already bounty-scaled, because the multiplier is baked into the derived block's range
        // rather than applied to the roll. What the board quotes is what RollGold will draw from.
        offer.Monster.MinGold,
        offer.Monster.MaxGold,
        offer.Monster.DropChance,
        BountyRules.BountyPercent(offer.DaysOverdue),
        offer.Faction?.Key,
        offer.Faction?.Name,
        offer.Faction?.TitleAt(offer.Standing),
        offer.Standing.ToString().ToLowerInvariant(),
        RarityRules.Describe(FactionStandings.FloorFor(offer.Standing)),

        // An on-time task is not a bounty and pays no item, and one flying no banner has no table
        // to draw from. Said here so the card can show it rather than promising a reward the win
        // will not hand over.
        offer.DaysOverdue > 0 && offer.Faction is not null,
        CombatService.StaminaPerEncounter);

    private static FactionStandingDto ToDto(FactionRecord record) => new(
        record.Faction.Key,
        record.Faction.Name,
        record.Faction.Blurb,
        record.Standing.ToString().ToLowerInvariant(),
        record.Faction.TitleAt(record.Standing),
        record.WonHunts,
        RarityRules.Describe(FactionStandings.FloorFor(record.Standing)));

    /// <summary>
    /// A contract as every screen renders it: what was promised, and how far along it is.
    /// </summary>
    /// <remarks>
    /// Every number is read back off the contract row, never off the task, which is what lets a
    /// contract whose task has been re-dated, retagged, re-graded, split or deleted still report
    /// exactly what it was written as. The faction's name and title come from the catalog by the
    /// frozen key (DEC-004), so a renamed banner renames itself in contracts already taken.
    /// <para>
    /// The status is the whole gate, spelled out for the client: "accepted" offers no fight and
    /// "discharged" does. There is deliberately no field saying whether the task looks finished,
    /// because that is the question whose two stale answers used to let a completion from another
    /// window collect a bounty. What the screen reads is what the server recorded.
    /// </para>
    /// </remarks>
    public static HuntContractDto ToDto(this HuntContractView view)
    {
        var contract = view.Contract;

        // Coalesced rather than banged, matching EncounterDto: an archetype retired from the
        // catalog leaves a contract that renders as its key instead of throwing over it (DEC-004).
        var monster = view.Monster;

        return new HuntContractDto(
            contract.Id,
            contract.Status.ToString().ToLowerInvariant(),
            contract.TaskId,
            contract.TaskTitle,
            contract.ArchetypeKey,
            monster?.Name ?? contract.ArchetypeKey,
            monster?.Blurb ?? string.Empty,
            monster?.Level ?? contract.Level,
            monster?.ArmourClass ?? 0,
            monster?.MaxHitPoints ?? 0,
            monster?.DamageNotation ?? string.Empty,
            monster?.MinGold ?? 0,
            monster?.MaxGold ?? 0,
            monster?.DropChance ?? 0,
            contract.DaysOverdue,
            contract.Subtasks,
            BountyRules.BountyPercent(contract.DaysOverdue),
            view.Faction?.Key,
            view.Faction?.Name,
            view.Faction?.TitleAt(view.Standing),
            view.Standing.ToString().ToLowerInvariant(),
            RarityRules.Describe(FactionStandings.FloorFor(view.Standing)),
            contract.DaysOverdue > 0 && view.Faction is not null,
            CombatService.StaminaPerEncounter,
            contract.AcceptedAt,
            contract.DischargedAt);
    }

    /// <summary>
    /// A contract's fight as the adventure screen renders it.
    /// </summary>
    /// <remarks>
    /// Every number here is read back off the encounter, never off the task, which is what lets a
    /// fight whose task has been edited, retagged, completed or deleted still report exactly what
    /// it was opened against.
    /// </remarks>
    public static HuntDto ToDto(this HuntView view)
    {
        var encounter = view.Encounter;
        var daysOverdue = encounter.HuntDaysOverdue ?? 0;

        return new HuntDto(
            encounter.Id,
            view.Contract?.Id,
            encounter.TaskId,
            view.Task?.Title ?? view.Contract?.TaskTitle,
            encounter.MonsterKey,
            encounter.Monster?.Name ?? encounter.MonsterKey,
            encounter.HuntLevel ?? 0,
            daysOverdue,
            encounter.HuntSubtasks ?? 0,
            BountyRules.BountyPercent(daysOverdue),
            view.Faction?.Key,
            view.Faction?.Name,
            view.Faction?.TitleAt(view.Standing),
            view.Standing.ToString().ToLowerInvariant(),
            encounter.ToDto());
    }

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
            definition?.Use?.At(item.Rarity).Describe(),
            UpgradePreview(item, definition));
    }

    /// <summary>
    /// What the bench would charge for this row, and what the row would become.
    /// </summary>
    /// <remarks>
    /// The refusals mirror ShopService.UpgradeAsync exactly - Legendary is the ceiling, the bench
    /// does not work on what you drink, and a retired key has no catalogue entry to price. Null
    /// for all three, so the client filters on "is there a next step" instead of restating the
    /// rule and getting it wrong.
    ///
    /// Every number comes from the same function the upgrade itself calls, so the only way this
    /// can lie is if one of them changes and the other does not.
    /// </remarks>
    private static UpgradePreviewDto? UpgradePreview(InventoryItem item, ItemDefinition? definition)
    {
        if (definition is null || item.Slot == ItemSlot.Consumable || item.Rarity >= Rarity.Legendary)
        {
            return null;
        }

        var target = item.Rarity + 1;

        // Intrinsic bonus at the new rarity plus the affixes at their new tier, which is what
        // InventoryItem.AbilityBonuses and .ArmourBonus sum for the current one.
        var affixes = AffixRules.EffectsOf(item, target);
        var scores = definition.AbilityBonusesAt(target).Plus(affixes.Abilities);

        var bonuses = AbilityScores.All
            .Select(a => new RollModifierDto(AbilityScores.Abbreviate(a), scores[a]))
            .Where(m => m.Value != 0)
            .ToList();

        return new UpgradePreviewDto(
            RarityRules.Describe(target),
            definition.UpgradeCostTo(target),
            definition.ArmourBonusAt(target) + affixes.ArmourBonus,
            bonuses,
            AffixRules.RollableFor(item.Slot, target),
            AffixRules.TierAt(target) > AffixRules.TierAt(item.Rarity));
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
