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
        group.MapGet("/encounters", GetChronicle);
        group.MapPost("/encounters/{id:guid}/attack", Attack);
        group.MapPost("/encounters/{id:guid}/ability/{abilityKey}", UseAbility);
        group.MapPost("/encounters/{id:guid}/flee", Flee);

        group.MapPost("/rest", Rest);
        group.MapGet("/shop", GetShop);
        group.MapPost("/shop/{offerId}/buy", Buy);
        group.MapPost("/inventory/{id:guid}/upgrade", Upgrade);

        group.MapGet("/inventory", GetInventory);
        group.MapPost("/inventory/{id:guid}/equip", Equip);
        group.MapPost("/inventory/{id:guid}/unequip", Unequip);
        group.MapDelete("/inventory/{id:guid}", Sell);

        group.MapGet("/quests", GetQuests);
        group.MapPost("/quests/{key}/claim", ClaimQuest);

        return app;
    }

    // ------------------------------------------------------------------ sheet

    private static async Task<IResult> GetSheet(
        ICurrentUser currentUser,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken)
    {
        var (character, sheet) = await LoadAsync(currentUser, db, sheets, cancellationToken);

        return Results.Ok(sheet.ToDto(character));
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

        var sheet = await sheets.BuildAsync(result.Value!, cancellationToken);

        return Results.Ok(sheet.ToDto(result.Value!));
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
        var (character, sheet) = await LoadAsync(currentUser, db, sheets, cancellationToken);

        return Results.Ok(new AttackResponse(
            outcome.Encounter.ToDto(),
            outcome.Rolls.Select(CombatRollDto.From).ToList(),
            outcome.PlayerHitPoints,
            outcome.PlayerMaxHitPoints,
            outcome.GoldAwarded,
            outcome.Loot?.ToDto(),
            outcome.QuestsAdvanced.Select(q => q.ToDto()).ToList(),
            // Remaining ability uses come from the encounter the round just ran on.
            sheet.ToDto(character, outcome.Encounter)));
    }

    private static async Task<IResult> GetChronicle(
        ICurrentUser currentUser,
        CombatService combat,
        CancellationToken cancellationToken,
        int limit = 20,
        DateTimeOffset? before = null)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        var encounters = await combat.HistoryAsync(user.Id, limit, before, cancellationToken);
        var summary = await combat.SummaryAsync(user.Id, cancellationToken);

        return Results.Ok(new ChronicleDto(
            summary.ToDto(),
            encounters.Select(e => e.ToDto()).ToList()));
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
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        var gold = await db.Characters
            .Where(c => c.UserId == user.Id)
            .Select(c => c.Gold)
            .FirstOrDefaultAsync(cancellationToken);

        var stock = ShopService.StockFor(user.Id, DateTimeOffset.UtcNow);

        return Results.Ok(new ShopDto(
            stock.Offers.Select(o => o.ToDto(gold)).ToList(),
            stock.RotatesAt,
            gold));
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

    // ----------------------------------------------------------------- shared

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
        var (character, sheet) = await LoadAsync(currentUser, db, sheets, cancellationToken);
        var inventory = await adventurer.ListAsync(user.Id, cancellationToken);

        return Results.Ok(new EquipResponse(
            sheet.ToDto(character),
            inventory.Select(i => i.ToDto()).ToList()));
    }

    private static async Task<(Models.Character Character, CharacterSheet Sheet)> LoadAsync(
        ICurrentUser currentUser,
        TodoDbContext db,
        CharacterSheetService sheets,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id, cancellationToken);
        var sheet = await sheets.BuildAsync(character, cancellationToken);

        // Regeneration is applied on read rather than by a background job.
        if (CharacterSheetService.NormaliseHitPoints(character, sheet, DateTimeOffset.UtcNow))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return (character, sheet);
    }

    /// <summary>One place mapping domain failures to status codes.</summary>
    private static IResult Problem(RpgFailure failure, string? message) => failure switch
    {
        RpgFailure.NotFound => Results.NotFound(),
        RpgFailure.EncounterAlreadyActive => Results.Problem(message, statusCode: 409),
        RpgFailure.EncounterOver => Results.Problem(message, statusCode: 409),
        RpgFailure.ItemEquipped => Results.Problem(message, statusCode: 409),
        RpgFailure.QuestAlreadyClaimed => Results.Problem(message, statusCode: 409),
        RpgFailure.QuestNotComplete => Results.Problem(message, statusCode: 409),
        RpgFailure.AlreadyAtFullHealth => Results.Problem(message, statusCode: 409),
        RpgFailure.CannotUpgrade => Results.Problem(message, statusCode: 409),
        RpgFailure.NotEnoughStamina => Results.Problem(message, statusCode: 422),
        RpgFailure.NotEnoughGold => Results.Problem(message, statusCode: 422),
        RpgFailure.AbilityExhausted => Results.Problem(message, statusCode: 422),
        RpgFailure.MonsterOutOfRange => Results.Problem(message, statusCode: 400),
        RpgFailure.UnknownClass => Results.Problem(message, statusCode: 400),
        _ => Results.Problem(message, statusCode: 500)
    };
}
