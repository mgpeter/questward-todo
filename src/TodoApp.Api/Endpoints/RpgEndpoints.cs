using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Auth;
using TodoApp.Api.Contracts;
using TodoApp.Api.Mapping;
using TodoApp.Api.Services;
using TodoApp.Api.Services.Rpg;
using TodoApp.Api.Validation;
using TodoApp.Data;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Endpoints;

public static class RpgEndpoints
{
    public static IEndpointRouteBuilder MapRpgEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rpg")
            .WithTags("Adventure")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PerUser);

        group.MapGet("/sheet", GetSheet);
        group.MapGet("/classes", GetClasses);
        group.MapPut("/class", ChooseClass).ValidateBody<ChooseClassRequest>();

        group.MapGet("/monsters", GetMonsters);
        group.MapPost("/encounters", StartEncounter).ValidateBody<StartEncounterRequest>();
        group.MapGet("/encounters/active", GetActiveEncounter);
        group.MapGet("/encounters", GetEncounterHistory);

        // The journal, and a different thing from the fight history above it. /encounters answers
        // "which fights have I had"; /chronicle answers "what has happened", which includes
        // quests claimed, contracts taken and settled, dungeons ended, levels reached and
        // ascensions. Both stay: the first is what the encounter tests are written against, and
        // the second is what the Chronicle panel reads.
        group.MapGet("/chronicle", GetChronicle);
        group.MapPost("/encounters/{id:guid}/attack", Attack);
        group.MapPost("/encounters/{id:guid}/ability/{abilityKey}", UseAbility);
        group.MapPost("/encounters/{id:guid}/use/{itemId:guid}", UseItem);
        group.MapPost("/encounters/{id:guid}/flee", Flee);

        group.MapGet("/dungeons", GetDungeons);
        group.MapPost("/dungeons", StartDungeon).ValidateBody<StartDungeonRequest>();
        group.MapGet("/dungeons/active", GetActiveDungeon);
        group.MapPost("/dungeons/{id:guid}/enter", EnterRoom);
        group.MapPost("/dungeons/{id:guid}/abandon", AbandonDungeon);

        // The three steps of a contract, plus the board and the fight in progress. No attack
        // route among them: a contract's fight is an ordinary encounter row, so
        // /encounters/{id}/attack drives it exactly as it drives a dungeon room, and
        // /encounters/{id}/flee ends it the one way fights end.
        //
        // Note which verb costs what. Accepting is a POST that charges nothing at all, and the
        // fight is a separate call: charging to accept would be a toll for having a backlog, and
        // DEC-013 replaced every such toll with a bounty.
        group.MapGet("/hunts", GetHunts);
        group.MapPost("/hunts", AcceptHunt).ValidateBody<AcceptHuntRequest>();
        group.MapGet("/hunts/active", GetActiveHunt);
        group.MapPost("/hunts/{id:guid}/fight", FightHunt);
        group.MapDelete("/hunts/{id:guid}", AbandonHunt);

        group.MapPost("/rest", Rest);

        // No body and no confirmation token. The confirmation belongs in the client, where the
        // player can be shown what they are about to lose; an endpoint that asked for a magic
        // word would be pretending to a safety it cannot provide.
        group.MapPost("/ascend", Ascend);
        group.MapGet("/shop", GetShop);
        group.MapPost("/shop/{offerId}/buy", Buy);
        group.MapPost("/shop/reroll", RerollShop);
        group.MapPost("/inventory/{id:guid}/upgrade", Upgrade);

        group.MapGet("/inventory", GetInventory);
        group.MapPost("/inventory/{id:guid}/equip", Equip);
        group.MapPost("/inventory/{id:guid}/unequip", Unequip);
        group.MapDelete("/inventory/{id:guid}", Sell);

        group.MapPost("/inventory/{id:guid}/salvage", Salvage);
        group.MapPost("/inventory/{id:guid}/imbue", Imbue);
        group.MapPost("/inventory/{id:guid}/reforge", Reforge);

        group.MapGet("/quests", GetQuests);
        group.MapPost("/quests/{key}/claim", ClaimQuest);

        group.MapGet("/bestiary", GetBestiary);
        group.MapGet("/lore", GetLore);

        return app;
    }

    // ------------------------------------------------------------------ sheet

    private static async Task<IResult> GetSheet(
        ICurrentUser currentUser,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(currentUser, db, sheets, cancellationToken);

        return Results.Ok(loaded.Sheet.ToDto(loaded.Character, loaded.Equipped));
    }

    private static async Task<IResult> Ascend(
        ICurrentUser currentUser,
        AscendService ascend,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await ascend.AscendAsync(user.Id, cancellationToken);

        if (!result.Ok)
        {
            return Problem(result.Failure, result.Message);
        }

        // The sheet is reloaded rather than carried out of the service, because almost everything
        // on it has just changed and half of it is derived from rows the service deleted.
        var loaded = await LoadAsync(currentUser, db, sheets, cancellationToken);

        return Results.Ok(new AscendResponse(
            result.Value!.EssenceGained,
            result.Value.Essence,
            result.Value.Ascensions,
            result.Value.LevelReached,
            result.Value.GoldConverted,
            result.Value.StaminaConverted,
            loaded.Sheet.ToDto(loaded.Character, loaded.Equipped)));
    }

    private static IResult GetClasses() =>
        Results.Ok(ClassCatalog.All.Select(c => c.ToDto()).ToList());

    private static async Task<IResult> ChooseClass(
        ChooseClassRequest request,
        ICurrentUser currentUser,
        AdventurerService adventurer,
        CharacterSheetService sheets,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await adventurer.ChooseClassAsync(user.Id, request.ClassKey, cancellationToken);

        if (!result.Ok)
        {
            return Problem(result.Failure, result.Message);
        }

        var (sheet, equipped) = await sheets.BuildWithEquipmentAsync(result.Value!, cancellationToken);

        return Results.Ok(sheet.ToDto(result.Value!, equipped));
    }

    // ----------------------------------------------------------------- combat

    private static async Task<IResult> GetMonsters(
        ICurrentUser currentUser,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        var totalXp = await db.Characters
            .Where(c => c.UserId == user.Id)
            .Select(c => c.TotalXp)
            .FirstOrDefaultAsync(cancellationToken);

        var level = LevelCurve.LevelForXp(totalXp);

        return Results.Ok(MonsterCatalog.AvailableAt(level).Select(m => m.ToDto()).ToList());
    }

    private static async Task<IResult> StartEncounter(
        StartEncounterRequest request,
        ICurrentUser currentUser,
        CombatService combat,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await combat.StartAsync(user.Id, request.MonsterKey, cancellationToken);

        return result.Ok
            ? Results.Created($"/api/rpg/encounters/{result.Value!.Id}", result.Value.ToDto())
            : Problem(result.Failure, result.Message);
    }

    private static async Task<IResult> GetActiveEncounter(
        ICurrentUser currentUser,
        CombatService combat,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var encounter = await combat.ActiveAsync(user.Id, cancellationToken);

        return encounter is null ? Results.NoContent() : Results.Ok(encounter.ToDto());
    }

    private static Task<IResult> Attack(
        Guid id,
        ICurrentUser currentUser,
        CombatService combat,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken) =>
        ResolveRoundAsync(
            currentUser, combat, db, sheets, cancellationToken,
            (service, userId) => service.AttackAsync(userId, id, cancellationToken));

    private static Task<IResult> UseAbility(
        Guid id,
        string abilityKey,
        ICurrentUser currentUser,
        CombatService combat,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken) =>
        ResolveRoundAsync(
            currentUser, combat, db, sheets, cancellationToken,
            (service, userId) => service.UseAbilityAsync(userId, id, abilityKey, cancellationToken));

    /// <summary>
    /// Spends a consumable as the player's half of a round.
    /// </summary>
    /// <remarks>
    /// Through the same helper as an attack and returning the same response, because it is a
    /// round: the player forfeits the swing and the monster still answers.
    /// </remarks>
    private static Task<IResult> UseItem(
        Guid id,
        Guid itemId,
        ICurrentUser currentUser,
        CombatService combat,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken) =>
        ResolveRoundAsync(
            currentUser, combat, db, sheets, cancellationToken,
            (service, userId) => service.UseItemAsync(userId, id, itemId, cancellationToken));

    private static async Task<IResult> ResolveRoundAsync(
        ICurrentUser currentUser,
        CombatService combat,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken,
        Func<CombatService, Guid, Task<RpgResult<AttackOutcome>>> resolve)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await resolve(combat, user.Id);

        if (!result.Ok)
        {
            return Problem(result.Failure, result.Message);
        }

        var outcome = result.Value!;
        var loaded = await LoadAsync(currentUser, db, sheets, cancellationToken);

        return Results.Ok(new AttackResponse(
            outcome.Encounter.ToDto(),
            outcome.Rolls.Select(CombatRollDto.From).ToList(),
            outcome.PlayerHitPoints,
            outcome.PlayerMaxHitPoints,
            outcome.GoldAwarded,
            outcome.Loot?.ToDto(),
            outcome.ClearReward?.ToDto(),
            outcome.QuestsAdvanced.Select(q => q.ToDto()).ToList(),
            // Remaining ability uses come from the encounter the round just ran on.
            loaded.Sheet.ToDto(loaded.Character, loaded.Equipped, outcome.Encounter)));
    }

    private static async Task<IResult> GetEncounterHistory(
        ICurrentUser currentUser,
        CombatService combat,
        CancellationToken cancellationToken,
        int limit = 20,
        DateTimeOffset? before = null)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        var encounters = await combat.HistoryAsync(user.Id, limit, before, cancellationToken);
        var summary = await combat.SummaryAsync(user.Id, cancellationToken);

        return Results.Ok(new EncounterHistoryDto(
            summary.ToDto(),
            encounters.Select(e => e.ToDto()).ToList()));
    }

    /// <summary>
    /// The journal: everything that happened, newest first, paged with a keyset on the timestamp.
    /// </summary>
    /// <remarks>
    /// The fights among the entries are hydrated in one query rather than one per row, and only
    /// the ones still on the table: an ascension deletes the encounters and leaves the entries,
    /// so a missing fight is expected rather than an error. The line still reads; what is gone is
    /// the log it could expand into.
    /// </remarks>
    private static async Task<IResult> GetChronicle(
        ICurrentUser currentUser,
        ChronicleService chronicle,
        CombatService combat,
        TodoDbContext db,
        CancellationToken cancellationToken,
        int limit = 20,
        DateTimeOffset? before = null,
        string? kind = null)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        ChronicleKind? filter =
            kind is not null && Enum.TryParse<ChronicleKind>(kind, true, out var parsed)
                ? parsed
                : null;

        var entries = await chronicle.HistoryAsync(user.Id, limit, before, filter, cancellationToken);

        var encounterIds = entries
            .Where(e => e.EncounterId is not null)
            .Select(e => e.EncounterId!.Value)
            .ToList();

        var encounters = encounterIds.Count == 0
            ? []
            : await db.Encounters
                .AsNoTracking()
                .Where(e => e.UserId == user.Id && encounterIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, cancellationToken);

        var summary = await combat.SummaryAsync(user.Id, cancellationToken);

        return Results.Ok(new ChronicleDto(
            summary.ToDto(),
            entries
                .Select(e => e.ToDto(
                    e.EncounterId is { } id && encounters.TryGetValue(id, out var found) ? found : null))
                .ToList()));
    }

    private static async Task<IResult> Rest(
        ICurrentUser currentUser,
        AdventurerService adventurer,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await adventurer.RestAsync(user.Id, cancellationToken);

        return result.Ok
            ? Results.Ok(new RestResponse(
                result.Value.GoldSpent, result.Value.Gold,
                result.Value.HitPoints, result.Value.MaxHitPoints))
            : Problem(result.Failure, result.Message);
    }

    private static async Task<IResult> GetShop(
        ICurrentUser currentUser,
        TodoDbContext db,
        ShopService shop,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        var gold = await db.Characters
            .Where(c => c.UserId == user.Id)
            .Select(c => c.Gold)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var generation = await shop.GenerationAsync(user.Id, now, cancellationToken);
        var stock = ShopService.StockFor(user.Id, now, generation);
        var soldOut = await shop.SoldOutAsync(user.Id, now, cancellationToken);

        var stamina = await db.Characters
            .Where(c => c.UserId == user.Id)
            .Select(c => c.Stamina)
            .FirstOrDefaultAsync(cancellationToken);

        return Results.Ok(new ShopDto(
            stock.Offers.Select(o => o.ToDto(gold, soldOut)).ToList(),
            stock.RotatesAt,
            gold,
            stamina,
            ShopRerolls.CostOf(generation),
            ShopRerolls.MaxPerDay - generation));
    }

    private static async Task<IResult> RerollShop(
        ICurrentUser currentUser,
        ShopService shop,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await shop.RerollAsync(user.Id, cancellationToken);

        if (!result.Ok)
        {
            return Problem(result.Failure, result.Message);
        }

        var gold = await db.Characters
            .Where(c => c.UserId == user.Id)
            .Select(c => c.Gold)
            .FirstOrDefaultAsync(cancellationToken);

        var reroll = result.Value!;

        return Results.Ok(new ShopDto(
            reroll.Stock.Offers.Select(o => o.ToDto(gold, reroll.SoldOut)).ToList(),
            reroll.Stock.RotatesAt,
            gold,
            reroll.Stamina,
            reroll.NextCost,
            reroll.RerollsLeft));
    }

    private static async Task<IResult> Buy(
        string offerId,
        ICurrentUser currentUser,
        ShopService shop,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await shop.BuyAsync(user.Id, offerId, cancellationToken);

        return result.Ok
            ? Results.Ok(new PurchaseResponse(
                result.Value!.Item.ToDto(), result.Value.GoldSpent, result.Value.Gold))
            : Problem(result.Failure, result.Message);
    }

    private static async Task<IResult> Upgrade(
        Guid id,
        ICurrentUser currentUser,
        ShopService shop,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await shop.UpgradeAsync(user.Id, id, cancellationToken);

        return result.Ok
            ? Results.Ok(new UpgradeResponse(
                result.Value!.Item.ToDto(),
                RarityRules.Describe(result.Value.From),
                RarityRules.Describe(result.Value.To),
                result.Value.GoldSpent,
                result.Value.Gold))
            : Problem(result.Failure, result.Message);
    }

    private static async Task<IResult> Flee(
        Guid id,
        ICurrentUser currentUser,
        CombatService combat,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await combat.FleeAsync(user.Id, id, cancellationToken);

        return result.Ok ? Results.Ok(result.Value!.ToDto()) : Problem(result.Failure, result.Message);
    }

    // --------------------------------------------------------------- dungeons

    private static async Task<IResult> GetDungeons(
        ICurrentUser currentUser,
        DungeonService dungeons,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var available = await dungeons.AvailableAsync(user.Id, cancellationToken);

        return Results.Ok(available.Select(d => d.ToDto()).ToList());
    }

    private static async Task<IResult> StartDungeon(
        StartDungeonRequest request,
        ICurrentUser currentUser,
        DungeonService dungeons,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await dungeons.StartAsync(user.Id, request.DungeonKey, cancellationToken);

        return result.Ok
            ? Results.Created($"/api/rpg/dungeons/{result.Value!.Run.Id}", result.Value.ToDto())
            : Problem(result.Failure, result.Message);
    }

    /// <summary>
    /// The whole of what a reloaded client needs to pick a run back up.
    /// </summary>
    /// <remarks>
    /// The client holds nothing between requests. This answers 204 when there is no run, and
    /// otherwise returns the run with the fight in progress attached, if one is open. A run with
    /// no open fight is resumed by entering room <c>Depth</c>; one with an open fight is resumed
    /// through the ordinary attack routes.
    /// </remarks>
    private static async Task<IResult> GetActiveDungeon(
        ICurrentUser currentUser,
        DungeonService dungeons,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var run = await dungeons.ActiveAsync(user.Id, cancellationToken);

        return run is null ? Results.NoContent() : Results.Ok(run.ToDto());
    }

    private static async Task<IResult> EnterRoom(
        Guid id,
        ICurrentUser currentUser,
        DungeonService dungeons,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await dungeons.EnterAsync(user.Id, id, cancellationToken);

        return result.Ok ? Results.Ok(result.Value!.ToDto()) : Problem(result.Failure, result.Message);
    }

    private static async Task<IResult> AbandonDungeon(
        Guid id,
        ICurrentUser currentUser,
        DungeonService dungeons,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await dungeons.AbandonAsync(user.Id, id, cancellationToken);

        return result.Ok ? Results.Ok(result.Value!.ToDto()) : Problem(result.Failure, result.Message);
    }

    // ------------------------------------------------------------------ hunts

    /// <summary>The contract board: which open tasks are huntable, and what each is worth.</summary>
    /// <remarks>Derived on every read. Rolls nothing, writes nothing, costs no stamina.</remarks>
    private static async Task<IResult> GetHunts(
        ICurrentUser currentUser,
        HuntService hunts,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var board = await hunts.BoardAsync(user.Id, cancellationToken);

        return Results.Ok(board.ToDto());
    }

    /// <summary>
    /// Takes a contract on a task. Free: no stamina, no fight, no die.
    /// </summary>
    /// <remarks>
    /// Created rather than Ok, and it points at the contract rather than at an encounter, because
    /// what this makes is a promise and not a fight. The fight is a second, separate call that
    /// only opens once the work is done, which is the whole of how the bounty stays attached to
    /// finishing the task rather than to avoiding it (DEC-013).
    /// </remarks>
    private static async Task<IResult> AcceptHunt(
        AcceptHuntRequest request,
        ICurrentUser currentUser,
        HuntService hunts,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await hunts.AcceptAsync(user.Id, request.TaskId, cancellationToken);

        return result.Ok
            ? Results.Created($"/api/rpg/hunts/{result.Value!.Contract.Id}", result.Value.ToDto())
            : Problem(result.Failure, result.Message);
    }

    private static async Task<IResult> GetActiveHunt(
        ICurrentUser currentUser,
        HuntService hunts,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var hunt = await hunts.ActiveAsync(user.Id, cancellationToken);

        return hunt is null ? Results.NoContent() : Results.Ok(hunt.ToDto());
    }

    /// <summary>
    /// Opens the fight a discharged contract earned. One stamina, like every other fight.
    /// </summary>
    /// <remarks>
    /// Refused with 409 while the task is still outstanding, and there is deliberately no way
    /// round that: paying bounty gold, loot or standing for an unfinished task is exactly what
    /// DEC-013 forbids. Created rather than Ok, pointing at the encounter, because from here on
    /// it is the ordinary combat surface.
    /// </remarks>
    private static async Task<IResult> FightHunt(
        Guid id,
        ICurrentUser currentUser,
        HuntService hunts,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await hunts.FightAsync(user.Id, id, cancellationToken);

        return result.Ok
            ? Results.Created(
                $"/api/rpg/encounters/{result.Value!.Encounter.Id}", result.Value.ToDto())
            : Problem(result.Failure, result.Message);
    }

    /// <summary>
    /// Tears up a contract. Free, and it takes nothing back that was paid for.
    /// </summary>
    /// <remarks>
    /// The way out, and the only way to have a contract re-priced after the task under it has
    /// genuinely changed shape: what was frozen at acceptance stays frozen. It cannot be turned
    /// into a gain, because a fresh contract can only be discharged by a completion that postdates
    /// it, so tearing up a discharged one forfeits the fight rather than banking it.
    /// </remarks>
    private static async Task<IResult> AbandonHunt(
        Guid id,
        ICurrentUser currentUser,
        HuntService hunts,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await hunts.AbandonAsync(user.Id, id, cancellationToken);

        return result.Ok ? Results.Ok(result.Value!.ToDto()) : Problem(result.Failure, result.Message);
    }

    // -------------------------------------------------------------- inventory

    private static async Task<IResult> GetInventory(
        ICurrentUser currentUser,
        AdventurerService adventurer,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var items = await adventurer.ListAsync(user.Id, cancellationToken);

        return Results.Ok(items.Select(i => i.ToDto()).ToList());
    }

    private static Task<IResult> Equip(
        Guid id,
        ICurrentUser currentUser,
        AdventurerService adventurer,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken) =>
        ChangeEquipmentAsync(
            currentUser, adventurer, db, sheets, cancellationToken,
            (service, userId) => service.EquipAsync(userId, id, cancellationToken));

    private static Task<IResult> Unequip(
        Guid id,
        ICurrentUser currentUser,
        AdventurerService adventurer,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken) =>
        ChangeEquipmentAsync(
            currentUser, adventurer, db, sheets, cancellationToken,
            (service, userId) => service.UnequipAsync(userId, id, cancellationToken));

    private static async Task<IResult> Sell(
        Guid id,
        ICurrentUser currentUser,
        AdventurerService adventurer,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await adventurer.SellAsync(user.Id, id, cancellationToken);

        return result.Ok
            ? Results.Ok(new SellResponse(result.Value.GoldGained, result.Value.Gold))
            : Problem(result.Failure, result.Message);
    }

    // ------------------------------------------------------------------ forge

    private static async Task<IResult> Salvage(
        Guid id,
        ICurrentUser currentUser,
        ForgeService forge,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await forge.SalvageAsync(user.Id, id, cancellationToken);

        return result.Ok
            ? Results.Ok(new SalvageResponse(result.Value!.EssenceGained, result.Value.Essence))
            : Problem(result.Failure, result.Message);
    }

    private static Task<IResult> Imbue(
        Guid id,
        ICurrentUser currentUser,
        ForgeService forge,
        CancellationToken cancellationToken) =>
        CraftAsync(
            currentUser, cancellationToken,
            userId => forge.ImbueAsync(userId, id, cancellationToken));

    private static Task<IResult> Reforge(
        Guid id,
        ICurrentUser currentUser,
        ForgeService forge,
        CancellationToken cancellationToken) =>
        CraftAsync(
            currentUser, cancellationToken,
            userId => forge.ReforgeAsync(userId, id, cancellationToken));

    // ----------------------------------------------------------------- quests

    private static async Task<IResult> GetQuests(
        ICurrentUser currentUser,
        QuestService quests,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var views = await quests.ListAsync(user.Id, cancellationToken);

        return Results.Ok(views.Select(q => q.ToDto()).ToList());
    }

    private static async Task<IResult> ClaimQuest(
        string key,
        ICurrentUser currentUser,
        QuestService quests,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await quests.ClaimAsync(user.Id, key, cancellationToken);

        return result.Ok
            ? Results.Ok(new QuestClaimResponse(
                result.Value.GoldGained, result.Value.Gold, result.Value.Item?.ToDto()))
            : Problem(result.Failure, result.Message);
    }

    // -------------------------------------------------------- bestiary and lore

    private static async Task<IResult> GetBestiary(
        ICurrentUser currentUser,
        BestiaryService bestiary,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var codex = await bestiary.CodexAsync(user.Id, cancellationToken);

        return Results.Ok(codex.ToDto());
    }

    private static async Task<IResult> GetLore(
        ICurrentUser currentUser,
        BestiaryService bestiary,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var collection = await bestiary.LoreAsync(user.Id, cancellationToken);

        return Results.Ok(collection.ToDto());
    }

    // ----------------------------------------------------------------- shared

    /// <summary>Imbue and reforge differ only in which service call they make.</summary>
    private static async Task<IResult> CraftAsync(
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        Func<Guid, Task<RpgResult<CraftResult>>> craft)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await craft(user.Id);

        return result.Ok
            ? Results.Ok(new CraftResponse(
                result.Value!.Item.ToDto(), result.Value.EssenceSpent, result.Value.Essence))
            : Problem(result.Failure, result.Message);
    }

    private static async Task<IResult> ChangeEquipmentAsync(
        ICurrentUser currentUser,
        AdventurerService adventurer,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken,
        Func<AdventurerService, Guid, Task<RpgResult<InventoryItem>>> change)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var result = await change(adventurer, user.Id);

        if (!result.Ok)
        {
            return Problem(result.Failure, result.Message);
        }

        // Both the sheet and the inventory come back, so one round trip refreshes
        // everything the equipment screen shows.
        var loaded = await LoadAsync(currentUser, db, sheets, cancellationToken);
        var inventory = await adventurer.ListAsync(user.Id, cancellationToken);

        return Results.Ok(new EquipResponse(
            loaded.Sheet.ToDto(loaded.Character, loaded.Equipped),
            inventory.Select(i => i.ToDto()).ToList()));
    }

    /// <summary>
    /// The equipped rows come back alongside the sheet because the sheet DTO reports set
    /// progress, which is derived from exactly those rows rather than stored (DEC-002).
    /// </summary>
    private static async Task<Loaded> LoadAsync(
        ICurrentUser currentUser,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id, cancellationToken);
        var (sheet, equipped) = await sheets.BuildWithEquipmentAsync(character, cancellationToken);

        // Regeneration is applied on read rather than by a background job.
        if (CharacterSheetService.NormaliseHitPoints(character, sheet, DateTimeOffset.UtcNow))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new Loaded(character, sheet, equipped);
    }

    private readonly record struct Loaded(
        Models.Character Character,
        CharacterSheet Sheet,
        IReadOnlyList<InventoryItem> Equipped);

    /// <summary>One place mapping domain failures to status codes.</summary>
    private static IResult Problem(RpgFailure failure, string? message) => failure switch
    {
        // Carries its sentence, like NoDungeonRun below and for the same reason: every caller
        // raising this writes the same message whether the row is missing or belongs to
        // somebody else, so nothing is disclosed by saying so. An empty 404 left the client
        // with no body to read and printing "Request failed with 404".
        RpgFailure.NotFound => Results.Problem(message, statusCode: 404),
        RpgFailure.EncounterAlreadyActive => Results.Problem(message, statusCode: 409),
        RpgFailure.EncounterOver => Results.Problem(message, statusCode: 409),
        RpgFailure.ItemEquipped => Results.Problem(message, statusCode: 409),
        RpgFailure.QuestAlreadyClaimed => Results.Problem(message, statusCode: 409),
        RpgFailure.QuestNotComplete => Results.Problem(message, statusCode: 409),
        RpgFailure.AlreadyAtFullHealth => Results.Problem(message, statusCode: 409),
        RpgFailure.CannotUpgrade => Results.Problem(message, statusCode: 409),
        RpgFailure.OfferSoldOut => Results.Problem(message, statusCode: 409),
        RpgFailure.NoneLeft => Results.Problem(message, statusCode: 409),
        RpgFailure.DungeonInProgress => Results.Problem(message, statusCode: 409),
        RpgFailure.DungeonOver => Results.Problem(message, statusCode: 409),
        RpgFailure.HuntAlreadyTaken => Results.Problem(message, statusCode: 409),
        RpgFailure.HuntNotDischarged => Results.Problem(message, statusCode: 409),
        RpgFailure.HuntAlreadyFought => Results.Problem(message, statusCode: 409),
        // 404 beside NotFound rather than folded into it, so a run that is genuinely missing and
        // one that belongs to somebody else stay one answer while keeping their own message.
        // Carrying the sentence gives nothing away: both cases produce this same failure with
        // this same message, which is what keeps run ids unprobeable.
        RpgFailure.NoDungeonRun => Results.Problem(message, statusCode: 404),
        RpgFailure.NotEnoughStamina => Results.Problem(message, statusCode: 422),

        // A state rather than a bad request: the ladder is spent until tomorrow.
        RpgFailure.RerollsSpent => Results.Problem(message, statusCode: 409),
        RpgFailure.NotEnoughGold => Results.Problem(message, statusCode: 422),
        RpgFailure.NotEnoughEssence => Results.Problem(message, statusCode: 422),
        RpgFailure.AbilityExhausted => Results.Problem(message, statusCode: 422),
        RpgFailure.MonsterOutOfRange => Results.Problem(message, statusCode: 400),
        RpgFailure.UnknownClass => Results.Problem(message, statusCode: 400),
        RpgFailure.ItemNotUsable => Results.Problem(message, statusCode: 400),
        RpgFailure.NotHuntable => Results.Problem(message, statusCode: 400),

        // 422 rather than 403: the request is well formed and the character is allowed to
        // ascend, just not yet. The message names the level, so the client needs no rule of
        // its own to explain the refusal.
        RpgFailure.NotReadyToAscend => Results.Problem(message, statusCode: 422),
        _ => Results.Problem(message, statusCode: 500)
    };
}
