using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services.Rpg;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// The three routes the forge adds, plus set progress on the sheet and the words on the item
/// card. Endpoint tests run against the real dice roller, so nothing here asserts which word
/// was rolled: only that one arrived, that it cost what the card said, and that the account
/// boundary holds.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class ForgeEndpointTests(PostgresFixture postgres) : IAsyncLifetime
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

    private static async Task ChooseAsync(HttpClient client, string classKey = ClassCatalog.Fighter) =>
        (await client.PutAsJsonAsync("/api/rpg/class", new { classKey })).EnsureSuccessStatusCode();

    /// <summary>
    /// Items and essence are placed through the database, the way gold already is: there is no
    /// route that hands out a specific item, and inventing one for the tests would be testing
    /// something the application does not do.
    /// </summary>
    private async Task<Guid> GiveAsync(
        string subject,
        string itemKey,
        Rarity rarity,
        string? prefix = null,
        string? suffix = null)
    {
        await using var db = postgres.CreateContext();

        var user = await db.Users.SingleAsync(u => u.Auth0Sub == subject);

        var item = new InventoryItem
        {
            UserId = user.Id,
            ItemKey = itemKey,
            Slot = ItemCatalog.Find(itemKey)!.Slot,
            Rarity = rarity,
            PrefixKey = prefix,
            SuffixKey = suffix,
            IsEquipped = false
        };

        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        return item.Id;
    }

    private async Task GrantGoldAsync(string subject, int gold)
    {
        await using var db = postgres.CreateContext();

        var user = await db.Users.SingleAsync(u => u.Auth0Sub == subject);
        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);

        character.Gold = gold;
        await db.SaveChangesAsync();
    }

    private async Task GrantEssenceAsync(string subject, int essence)
    {
        await using var db = postgres.CreateContext();

        var user = await db.Users.SingleAsync(u => u.Auth0Sub == subject);
        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);

        character.Essence = essence;
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("salvage")]
    [InlineData("imbue")]
    [InlineData("reforge")]
    public async Task Every_forge_route_requires_authentication(string verb)
    {
        using var anonymous = _factory.CreateAnonymousClient();

        var response = await anonymous.PostAsync($"/api/rpg/inventory/{Guid.NewGuid()}/{verb}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("salvage")]
    [InlineData("imbue")]
    [InlineData("reforge")]
    public async Task An_item_that_does_not_exist_is_a_bare_404(string verb)
    {
        await ChooseAsync(_alice);
        await GrantEssenceAsync("auth0|alice", 1000);

        var response = await _alice.PostAsync($"/api/rpg/inventory/{Guid.NewGuid()}/{verb}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- salvage

    [Fact]
    public async Task Salvaging_breaks_the_item_down_for_essence()
    {
        await ChooseAsync(_alice);
        var id = await GiveAsync("auth0|alice", ItemCatalog.SilveredBlade, Rarity.Rare, AffixCatalog.Keen);

        var response = await _alice.PostAsync($"/api/rpg/inventory/{id}/salvage", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SalvageDto>();

        Assert.Equal(7, result!.EssenceGained);   // 5 for Rare, 2 for the word
        Assert.Equal(7, result.Essence);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        Assert.DoesNotContain(inventory!, i => i.Id == id);

        // The sheet carries the balance, so the forge screen needs no second call.
        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        Assert.Equal(7, sheet!.Essence);
    }

    [Fact]
    public async Task Salvaging_pays_essence_and_never_gold()
    {
        await ChooseAsync(_alice);
        var id = await GiveAsync("auth0|alice", ItemCatalog.DragonfangSpear, Rarity.Epic);

        var before = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        (await _alice.PostAsync($"/api/rpg/inventory/{id}/salvage", null)).EnsureSuccessStatusCode();

        var after = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        Assert.Equal(before!.Gold, after!.Gold);
        Assert.Equal(12, after.Essence);
    }

    [Fact]
    public async Task An_equipped_item_cannot_be_broken_down()
    {
        await ChooseAsync(_alice);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var worn = inventory!.First(i => i.IsEquipped);

        var response = await _alice.PostAsync($"/api/rpg/inventory/{worn.Id}/salvage", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var after = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        Assert.Contains(after!, i => i.Id == worn.Id);
    }

    [Fact]
    public async Task Another_users_item_cannot_be_salvaged_imbued_or_reforged()
    {
        // 404 rather than 403, so item ids cannot be probed for existence.
        await ChooseAsync(_alice);
        await ChooseAsync(_bob);
        await GrantEssenceAsync("auth0|bob", 10_000);

        var id = await GiveAsync(
            "auth0|alice", ItemCatalog.SilveredBlade, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheFox);

        foreach (var verb in new[] { "salvage", "imbue", "reforge" })
        {
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await _bob.PostAsync($"/api/rpg/inventory/{id}/{verb}", null)).StatusCode);
        }

        // Alice still has it, unchanged, and Bob paid nothing.
        var alices = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var survivor = alices!.Single(i => i.Id == id);

        Assert.Equal("Keen", survivor.Prefix);
        Assert.Equal("of the Fox", survivor.Suffix);

        var bobs = await _bob.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        Assert.Equal(10_000, bobs!.Essence);
    }

    // ------------------------------------------------------------ the forge

    [Fact]
    public async Task Imbuing_puts_a_word_on_an_empty_slot_for_the_price_on_the_card()
    {
        await ChooseAsync(_alice);
        await GrantEssenceAsync("auth0|alice", 100);

        var id = await GiveAsync("auth0|alice", ItemCatalog.SilveredBlade, Rarity.Rare);

        var card = (await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory"))!.Single(i => i.Id == id);

        Assert.Equal(1, card.AffixSlots);
        Assert.Equal(12, card.ImbueCost);
        Assert.Equal("Silvered Blade", card.Name);

        var response = await _alice.PostAsync($"/api/rpg/inventory/{id}/imbue", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CraftDto>();

        Assert.Equal(card.ImbueCost, result!.EssenceSpent);
        Assert.Equal(100 - card.ImbueCost, result.Essence);

        // One word, either kind, and the name it produces is the one the player now owns.
        Assert.True(result.Item.Prefix is not null ^ result.Item.Suffix is not null);
        Assert.NotEqual("Silvered Blade", result.Item.Name);
        Assert.Contains("Silvered Blade", result.Item.Name);
    }

    [Fact]
    public async Task Imbuing_a_common_item_is_refused()
    {
        await ChooseAsync(_alice);
        await GrantEssenceAsync("auth0|alice", 1000);

        var id = await GiveAsync("auth0|alice", ItemCatalog.SilveredBlade, Rarity.Common);

        var response = await _alice.PostAsync($"/api/rpg/inventory/{id}/imbue", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        Assert.Equal(1000, sheet!.Essence);
    }

    [Fact]
    public async Task Imbuing_a_full_item_is_refused()
    {
        await ChooseAsync(_alice);
        await GrantEssenceAsync("auth0|alice", 1000);

        var id = await GiveAsync("auth0|alice", ItemCatalog.SilveredBlade, Rarity.Uncommon, AffixCatalog.Vicious);

        var response = await _alice.PostAsync($"/api/rpg/inventory/{id}/imbue", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Crafting_without_the_essence_is_refused_with_a_clear_reason()
    {
        // 422 rather than 400: the request was well formed, the character simply has not
        // broken enough down yet.
        await ChooseAsync(_alice);
        await GrantEssenceAsync("auth0|alice", 5);

        var id = await GiveAsync("auth0|alice", ItemCatalog.SilveredBlade, Rarity.Rare);

        var response = await _alice.PostAsync($"/api/rpg/inventory/{id}/imbue", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("essence", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        Assert.Equal(5, sheet!.Essence);
    }

    [Fact]
    public async Task Reforging_swaps_the_word_for_a_different_one()
    {
        await ChooseAsync(_alice);
        await GrantEssenceAsync("auth0|alice", 100);

        var id = await GiveAsync("auth0|alice", ItemCatalog.SilveredBlade, Rarity.Rare, AffixCatalog.Keen);

        var response = await _alice.PostAsync($"/api/rpg/inventory/{id}/reforge", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CraftDto>();

        Assert.Equal(24, result!.EssenceSpent);   // twice the imbue price
        Assert.Equal(76, result.Essence);
        Assert.NotNull(result.Item.Prefix);
        Assert.NotEqual("Keen", result.Item.Prefix);
    }

    [Fact]
    public async Task Reforging_an_unaffixed_item_is_refused()
    {
        await ChooseAsync(_alice);
        await GrantEssenceAsync("auth0|alice", 1000);

        var id = await GiveAsync("auth0|alice", ItemCatalog.SilveredBlade, Rarity.Epic);

        var response = await _alice.PostAsync($"/api/rpg/inventory/{id}/reforge", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Crafting_costs_essence_and_never_gold()
    {
        await ChooseAsync(_alice);
        await GrantEssenceAsync("auth0|alice", 200);

        var id = await GiveAsync("auth0|alice", ItemCatalog.SilveredBlade, Rarity.Rare);

        var before = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        (await _alice.PostAsync($"/api/rpg/inventory/{id}/imbue", null)).EnsureSuccessStatusCode();
        (await _alice.PostAsync($"/api/rpg/inventory/{id}/reforge", null)).EnsureSuccessStatusCode();

        var after = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        Assert.Equal(before!.Gold, after!.Gold);
        Assert.Equal(200 - 12 - 24, after.Essence);
    }

    // ------------------------------------------------------------------ sets

    [Fact]
    public async Task A_set_piece_advertises_its_set_on_the_item_card()
    {
        // Discovery of the sets you are not wearing rides on this field, which is why every
        // piece carries it whether it is worn or not.
        await ChooseAsync(_alice, ClassCatalog.Ranger);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");

        Assert.Contains(inventory!, i => i.ItemKey == ItemCatalog.LeatherArmour && i.SetName == "Valewarden");
        Assert.All(
            inventory!.Where(i => i.ItemKey == ItemCatalog.HuntingBow),
            i => Assert.Null(i.SetName));
    }

    [Fact]
    public async Task Set_progress_is_computed_from_what_is_worn_and_vanishes_when_a_piece_comes_off()
    {
        // Nothing stores this. The only write in the whole test is IsEquipped, and the sheet
        // derives progress, the active tiers and the attack bonus from the rows it loaded.
        await ChooseAsync(_alice, ClassCatalog.Ranger);

        var start = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        var oneWorn = start!.Sets.Single(s => s.Key == SetCatalog.Valewarden);

        // Leather armour comes with the class, so the mechanic is discoverable from minute one.
        Assert.Equal(1, oneWorn.Equipped);
        Assert.Equal(3, oneWorn.Total);
        Assert.All(oneWorn.Tiers, t => Assert.False(t.Active));

        // Sets you own no piece of are not listed at all.
        Assert.DoesNotContain(start.Sets, s => s.Key == SetCatalog.DawnwardOath);

        var bow = await GiveAsync("auth0|alice", ItemCatalog.LongbowOfTheVale, Rarity.Common);
        var boots = await GiveAsync("auth0|alice", ItemCatalog.BootsOfSpeed, Rarity.Common);

        var withTwo = await (await _alice.PostAsync($"/api/rpg/inventory/{bow}/equip", null))
            .Content.ReadFromJsonAsync<EquipDto>();

        var pair = withTwo!.Sheet.Sets.Single(s => s.Key == SetCatalog.Valewarden);
        Assert.Equal(2, pair.Equipped);
        Assert.Contains(pair.Tiers, t => t is { Pieces: 2, Active: true });
        Assert.Contains(pair.Tiers, t => t is { Pieces: 3, Active: false });

        var withThree = await (await _alice.PostAsync($"/api/rpg/inventory/{boots}/equip", null))
            .Content.ReadFromJsonAsync<EquipDto>();

        var full = withThree!.Sheet.Sets.Single(s => s.Key == SetCatalog.Valewarden);
        Assert.Equal(3, full.Equipped);
        Assert.All(full.Tiers, t => Assert.True(t.Active));

        // The capstone is worth exactly one point of attack, and the armour tier is not paid twice.
        Assert.Equal(withTwo.Sheet.AttackBonus + 1, withThree.Sheet.AttackBonus);
        Assert.Equal(withTwo.Sheet.ArmourClass, withThree.Sheet.ArmourClass);

        var broken = await (await _alice.PostAsync($"/api/rpg/inventory/{boots}/unequip", null))
            .Content.ReadFromJsonAsync<EquipDto>();

        var after = broken!.Sheet.Sets.Single(s => s.Key == SetCatalog.Valewarden);
        Assert.Equal(2, after.Equipped);
        Assert.Contains(after.Tiers, t => t is { Pieces: 3, Active: false });
        Assert.Equal(withTwo.Sheet.AttackBonus, broken.Sheet.AttackBonus);
    }

    [Fact]
    public async Task Breaking_a_set_piece_down_takes_the_bonus_with_it()
    {
        await ChooseAsync(_alice, ClassCatalog.Ranger);

        var bow = await GiveAsync("auth0|alice", ItemCatalog.LongbowOfTheVale, Rarity.Common);
        (await _alice.PostAsync($"/api/rpg/inventory/{bow}/equip", null)).EnsureSuccessStatusCode();

        var wearing = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        Assert.Equal(2, wearing!.Sets.Single(s => s.Key == SetCatalog.Valewarden).Equipped);

        // Take the armour off first, because the forge refuses to break down what you are wearing.
        var armour = (await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory"))!
            .Single(i => i.ItemKey == ItemCatalog.LeatherArmour);

        (await _alice.PostAsync($"/api/rpg/inventory/{armour.Id}/unequip", null)).EnsureSuccessStatusCode();
        (await _alice.PostAsync($"/api/rpg/inventory/{armour.Id}/salvage", null)).EnsureSuccessStatusCode();

        var after = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        Assert.Equal(1, after!.Sets.Single(s => s.Key == SetCatalog.Valewarden).Equipped);
        Assert.Equal(wearing.ArmourClass - 2 - 1, after.ArmourClass);
    }

    // ------------------------------------------------------------- the shelf

    [Fact]
    public async Task A_whole_day_of_buying_and_breaking_pays_less_than_two_epic_words()
    {
        // The gold-to-essence route, walked rather than computed. The arithmetic version of
        // this lives in AffixAndSetTests and only multiplies constants, which is exactly how a
        // shop that resold the same offer forever passed it.
        await ChooseAsync(_alice);
        await GrantGoldAsync("auth0|alice", 1_000_000);

        var shop = await _alice.GetFromJsonAsync<ShopDto>("/api/rpg/shop");

        foreach (var offer in shop!.Offers)
        {
            (await _alice.PostAsync($"/api/rpg/shop/{offer.OfferId}/buy", null)).EnsureSuccessStatusCode();
        }

        // The shelf is empty now, and a million gold does not refill it.
        foreach (var offer in shop.Offers)
        {
            Assert.Equal(
                HttpStatusCode.Conflict,
                (await _alice.PostAsync($"/api/rpg/shop/{offer.OfferId}/buy", null)).StatusCode);
        }

        var bought = (await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory"))!
            .Where(i => !i.IsEquipped)
            .ToList();

        Assert.Equal(ShopService.OfferCount, bought.Count);

        foreach (var item in bought)
        {
            (await _alice.PostAsync($"/api/rpg/inventory/{item.Id}/salvage", null)).EnsureSuccessStatusCode();
        }

        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        // The bound AffixAndSetTests asserts, now met by the code rather than by the comment.
        var ceiling = ShopService.OfferCount * ForgeRules.EssenceFor(ShopService.MaxStockRarity, 0);

        Assert.True(sheet!.Essence <= ceiling, $"{sheet.Essence} essence from one shelf");
        Assert.True(sheet.Essence < 2 * ForgeRules.ImbueCost(Rarity.Epic));
    }

    // -------------------------------------------------------- the invariant

    [Fact]
    public async Task No_forge_route_can_move_experience()
    {
        // The guarantee the whole design rests on (DEC-012), asserted through the wire against
        // every route this phase added.
        await ChooseAsync(_alice, ClassCatalog.Ranger);
        await GrantEssenceAsync("auth0|alice", 1000);

        var before = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        var scrap = await GiveAsync(
            "auth0|alice", ItemCatalog.GreatAxe, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheFox);
        var work = await GiveAsync("auth0|alice", ItemCatalog.SilveredBlade, Rarity.Epic);
        var boots = await GiveAsync("auth0|alice", ItemCatalog.BootsOfSpeed, Rarity.Common);

        (await _alice.PostAsync($"/api/rpg/inventory/{scrap}/salvage", null)).EnsureSuccessStatusCode();
        (await _alice.PostAsync($"/api/rpg/inventory/{work}/imbue", null)).EnsureSuccessStatusCode();
        (await _alice.PostAsync($"/api/rpg/inventory/{work}/imbue", null)).EnsureSuccessStatusCode();
        (await _alice.PostAsync($"/api/rpg/inventory/{work}/reforge", null)).EnsureSuccessStatusCode();
        (await _alice.PostAsync($"/api/rpg/inventory/{boots}/equip", null)).EnsureSuccessStatusCode();
        (await _alice.PostAsync($"/api/rpg/inventory/{boots}/unequip", null)).EnsureSuccessStatusCode();

        await _alice.GetAsync("/api/rpg/sheet");
        await _alice.GetAsync("/api/rpg/inventory");

        var after = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.Equal(before!.TotalXp, after!.TotalXp);
        Assert.Equal(before.Level, after.Level);
    }

    // ---- wire shapes -------------------------------------------------------

    private sealed record CharacterDto(int Level, int TotalXp);
    private sealed record TierDto(int Pieces, string Description, bool Active);

    private sealed record SetDto(
        string Key, string Name, string Blurb, int Equipped, int Total, List<TierDto> Tiers);

    private sealed record SheetDto(
        int Level, int ArmourClass, int AttackBonus, int Gold, int Essence, List<SetDto> Sets);

    private sealed record ItemDto(
        Guid Id, string ItemKey, string Name, string Slot, string Rarity, bool IsEquipped,
        int ArmourBonus, int SellValue, string? Prefix, string? Suffix, string? SetName,
        int AffixSlots, int SalvageValue, int ImbueCost, int ReforgeCost);

    private sealed record OfferDto(string OfferId, string ItemKey, int Price, bool SoldOut);
    private sealed record ShopDto(List<OfferDto> Offers, DateTimeOffset RotatesAt, int Gold);

    private sealed record SalvageDto(int EssenceGained, int Essence);
    private sealed record CraftDto(ItemDto Item, int EssenceSpent, int Essence);
    private sealed record EquipDto(SheetDto Sheet, List<ItemDto> Inventory);
}
