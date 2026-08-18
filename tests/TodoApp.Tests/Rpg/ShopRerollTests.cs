using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services.Rpg;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// Paying stamina for a fresh shelf.
/// </summary>
/// <remarks>
/// The reroll hands back the six purchases the day's cap had already spent, and that cap exists
/// because the shop was once an uncapped route from gold to essence: buy six, break them at the
/// forge, buy six more. The escalating ladder is the whole defence, so most of what is asserted
/// here is the price going up and the day running out.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class ShopRerollTests(PostgresFixture postgres) : IAsyncLifetime
{
    private QuestwardAppFactory _factory = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _factory = new QuestwardAppFactory(postgres.ConnectionString);
        _alice = _factory.CreateClientAs("auth0|alice");
        _bob = _factory.CreateClientAs("auth0|bob");
    }

    public ValueTask DisposeAsync()
    {
        _alice.Dispose();
        _bob.Dispose();
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record OfferView(string OfferId, string Name, int Price, bool SoldOut, bool Affordable);

    private sealed record ShopView(
        OfferView[] Offers,
        DateTimeOffset RotatesAt,
        int Gold,
        int Stamina,
        int? NextRerollCost,
        int RerollsLeft);

    /// <summary>Grants stamina directly. Earning 1861 through the endpoint would be absurd.</summary>
    private async Task GrantAsync(HttpClient client, string subject, int stamina, int gold = 0)
    {
        // The user and character are provisioned by the first authenticated request, so the
        // row does not exist until the client has touched something.
        (await client.GetAsync("/api/rpg/shop")).EnsureSuccessStatusCode();

        await using var db = postgres.CreateContext();
        var user = await db.Users.SingleAsync(u => u.Auth0Sub == subject);
        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);

        character.Stamina = stamina;
        character.Gold = gold;
        await db.SaveChangesAsync();
    }

    private async Task<ShopView> ShopAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ShopView>("/api/rpg/shop"))!;

    // ------------------------------------------------------------------ the ladder

    [Fact]
    public void The_ladder_is_the_prices_the_owner_chose()
    {
        Assert.Equal([1, 10, 50, 100, 200, 500, 1000], ShopRerolls.Ladder);
        Assert.Equal(7, ShopRerolls.MaxPerDay);
        Assert.Equal(1861, ShopRerolls.WholeLadder);
    }

    [Fact]
    public void The_price_climbs_and_then_the_day_is_spent()
    {
        Assert.Equal(1, ShopRerolls.CostOf(0));
        Assert.Equal(10, ShopRerolls.CostOf(1));
        Assert.Equal(1000, ShopRerolls.CostOf(6));

        // Not an exception and not a zero: null is what "no more today" reads as everywhere.
        Assert.Null(ShopRerolls.CostOf(7));
        Assert.Null(ShopRerolls.CostOf(100));
    }

    [Fact]
    public async Task Each_reroll_costs_more_than_the_last_until_the_trader_refuses()
    {
        await GrantAsync(_alice, "auth0|alice", ShopRerolls.WholeLadder);

        var spent = 0;

        foreach (var expected in ShopRerolls.Ladder)
        {
            var before = await ShopAsync(_alice);
            Assert.Equal(expected, before.NextRerollCost);

            var response = await _alice.PostAsync("/api/rpg/shop/reroll", null);
            response.EnsureSuccessStatusCode();

            spent += expected;

            var after = (await response.Content.ReadFromJsonAsync<ShopView>())!;
            Assert.Equal(ShopRerolls.WholeLadder - spent, after.Stamina);
        }

        // The whole ladder walked, and the stamina exactly consumed.
        Assert.Equal(0, (await ShopAsync(_alice)).Stamina);

        var refused = await _alice.PostAsync("/api/rpg/shop/reroll", null);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var exhausted = await ShopAsync(_alice);
        Assert.Null(exhausted.NextRerollCost);
        Assert.Equal(0, exhausted.RerollsLeft);
    }

    [Fact]
    public async Task A_reroll_nobody_can_pay_for_is_refused_and_costs_nothing()
    {
        await GrantAsync(_alice, "auth0|alice", 0);

        var response = await _alice.PostAsync("/api/rpg/shop/reroll", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var shop = await ShopAsync(_alice);

        Assert.Equal(0, shop.Stamina);
        Assert.Equal(ShopRerolls.Ladder[0], shop.NextRerollCost);
        Assert.Equal(ShopRerolls.MaxPerDay, shop.RerollsLeft);
    }

    // ------------------------------------------------------------------- the shelf

    [Fact]
    public async Task A_reroll_actually_changes_the_shelf()
    {
        await GrantAsync(_alice, "auth0|alice", 100);

        var before = await ShopAsync(_alice);

        (await _alice.PostAsync("/api/rpg/shop/reroll", null)).EnsureSuccessStatusCode();

        var after = await ShopAsync(_alice);

        // Every id differs, because the generation is part of it. That is what makes the new
        // shelf buyable rather than inheriting the old one's sold-out marks.
        Assert.Empty(after.Offers.Select(o => o.OfferId).Intersect(before.Offers.Select(o => o.OfferId)));

        // And the contents move too, rather than the same six items under new ids.
        Assert.NotEqual(
            before.Offers.Select(o => o.Name).OrderBy(n => n),
            after.Offers.Select(o => o.Name).OrderBy(n => n));
    }

    [Fact]
    public void The_opening_shelf_is_byte_identical_to_the_one_before_rerolls_existed()
    {
        // Generation 0 writes its seed and its offer ids without the suffix, deliberately.
        // Appending ":r0" would have silently reshuffled every user's stock the day this
        // shipped, and invalidated every purchase row already written.
        var monday = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
        var user = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Assert.Equal(
            $"shop:{user:N}:2026-08-17",
            SeededDiceRoller.DailySeed(user, DateOnly.FromDateTime(monday.UtcDateTime)));

        var opening = ShopService.StockFor(user, monday);

        // The leading segment carries the generation, so it is the one that must stay a bare
        // date. Item keys are full of letters and a naive search for the marker finds
        // "goblin-cleaver".
        Assert.All(opening.Offers, offer => Assert.Equal("20260817", offer.OfferId.Split('-')[0]));

        var rerolled = ShopService.StockFor(user, monday, generation: 1);

        Assert.All(rerolled.Offers, offer => Assert.Equal("20260817r1", offer.OfferId.Split('-')[0]));
    }

    [Fact]
    public async Task A_rerolled_shelf_is_fully_buyable_even_after_the_day_is_spent()
    {
        // The deliberate consequence of the owner's choice: a reroll gives back the purchases
        // the cap had spent. What stops it being the old essence pump is the price of the next
        // one, not a limit on this one.
        await GrantAsync(_alice, "auth0|alice", 100, gold: 100_000);

        var before = await ShopAsync(_alice);

        foreach (var offer in before.Offers)
        {
            (await _alice.PostAsync($"/api/rpg/shop/{offer.OfferId}/buy", null)).EnsureSuccessStatusCode();
        }

        Assert.All((await ShopAsync(_alice)).Offers, o => Assert.True(o.SoldOut));

        (await _alice.PostAsync("/api/rpg/shop/reroll", null)).EnsureSuccessStatusCode();

        var after = await ShopAsync(_alice);

        Assert.All(after.Offers, o => Assert.False(o.SoldOut));
    }

    [Fact]
    public async Task An_offer_from_a_shelf_that_has_been_rerolled_away_can_no_longer_be_bought()
    {
        // Otherwise every previous shelf stays alive beside the new one, and one payment of the
        // ladder buys unlimited stock rather than a replacement for it.
        await GrantAsync(_alice, "auth0|alice", 100, gold: 100_000);

        var stale = (await ShopAsync(_alice)).Offers[0].OfferId;

        (await _alice.PostAsync("/api/rpg/shop/reroll", null)).EnsureSuccessStatusCode();

        var response = await _alice.PostAsync($"/api/rpg/shop/{stale}/buy", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------- the rules

    [Fact]
    public async Task One_shoppers_reroll_leaves_another_shopper_alone()
    {
        await GrantAsync(_alice, "auth0|alice", 100);
        await GrantAsync(_bob, "auth0|bob", 100);

        var bobBefore = await ShopAsync(_bob);

        (await _alice.PostAsync("/api/rpg/shop/reroll", null)).EnsureSuccessStatusCode();

        var bobAfter = await ShopAsync(_bob);

        Assert.Equal(
            bobBefore.Offers.Select(o => o.OfferId),
            bobAfter.Offers.Select(o => o.OfferId));
        Assert.Equal(100, bobAfter.Stamina);
        Assert.Equal(ShopRerolls.MaxPerDay, bobAfter.RerollsLeft);
    }

    [Fact]
    public async Task Rerolling_the_market_never_moves_experience()
    {
        await GrantAsync(_alice, "auth0|alice", 100);

        var before = await _alice.GetFromJsonAsync<CharacterProgress>("/api/character");

        for (var i = 0; i < 3; i++)
        {
            (await _alice.PostAsync("/api/rpg/shop/reroll", null)).EnsureSuccessStatusCode();
        }

        var after = await _alice.GetFromJsonAsync<CharacterProgress>("/api/character");

        Assert.Equal(before!.TotalXp, after!.TotalXp);
        Assert.Equal(before.Level, after.Level);
    }

    [Fact]
    public async Task The_reroll_route_requires_authentication()
    {
        using var anonymous = _factory.CreateAnonymousClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync("/api/rpg/shop/reroll", null)).StatusCode);
    }

    private sealed record CharacterProgress(int Level, int TotalXp);
}
