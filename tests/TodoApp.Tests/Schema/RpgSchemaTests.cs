using Microsoft.EntityFrameworkCore;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Schema;

/// <summary>
/// The RPG rules that must hold even under concurrency are enforced by partial unique
/// indexes rather than service code. These assert the indexes themselves.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class RpgSchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Only_one_item_can_be_equipped_per_slot()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        db.InventoryItems.Add(new InventoryItem
        {
            UserId = alice.Id, ItemKey = ItemCatalog.RustyLongsword,
            Slot = ItemSlot.Weapon, IsEquipped = true
        });
        await db.SaveChangesAsync();

        db.InventoryItems.Add(new InventoryItem
        {
            UserId = alice.Id, ItemKey = ItemCatalog.WornDagger,
            Slot = ItemSlot.Weapon, IsEquipped = true
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Unequipped_duplicates_in_a_slot_are_fine()
    {
        // The index is filtered on IsEquipped, so a backpack full of swords is allowed.
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        for (var i = 0; i < 5; i++)
        {
            db.InventoryItems.Add(new InventoryItem
            {
                UserId = alice.Id, ItemKey = ItemCatalog.RustyLongsword,
                Slot = ItemSlot.Weapon, IsEquipped = false
            });
        }

        await db.SaveChangesAsync();

        Assert.Equal(5, await db.InventoryItems.CountAsync(i => i.UserId == alice.Id));
    }

    [Fact]
    public async Task Two_users_can_each_equip_their_own_weapon()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");
        var bob = await postgres.CreateUserAsync("test|bob");

        await using var db = postgres.CreateContext();

        db.InventoryItems.Add(new InventoryItem
        {
            UserId = alice.Id, ItemKey = ItemCatalog.RustyLongsword,
            Slot = ItemSlot.Weapon, IsEquipped = true
        });
        db.InventoryItems.Add(new InventoryItem
        {
            UserId = bob.Id, ItemKey = ItemCatalog.WornDagger,
            Slot = ItemSlot.Weapon, IsEquipped = true
        });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.InventoryItems.CountAsync(i => i.IsEquipped));
    }

    [Fact]
    public async Task Only_one_encounter_can_be_active_at_a_time()
    {
        // Without this, two concurrent requests could each spend one stamina and open a
        // second fight, turning one unit of real work into two sets of loot.
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        db.Encounters.Add(new Encounter
        {
            UserId = alice.Id, MonsterKey = MonsterCatalog.Goblin,
            MonsterHitPoints = 10, Status = EncounterStatus.Active
        });
        await db.SaveChangesAsync();

        db.Encounters.Add(new Encounter
        {
            UserId = alice.Id, MonsterKey = MonsterCatalog.Skeleton,
            MonsterHitPoints = 16, Status = EncounterStatus.Active
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Finished_encounters_do_not_block_a_new_one()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        foreach (var status in new[] { EncounterStatus.Won, EncounterStatus.Lost, EncounterStatus.Fled })
        {
            db.Encounters.Add(new Encounter
            {
                UserId = alice.Id, MonsterKey = MonsterCatalog.Goblin,
                MonsterHitPoints = 0, Status = status
            });
        }

        db.Encounters.Add(new Encounter
        {
            UserId = alice.Id, MonsterKey = MonsterCatalog.Goblin,
            MonsterHitPoints = 10, Status = EncounterStatus.Active
        });

        await db.SaveChangesAsync();

        Assert.Equal(4, await db.Encounters.CountAsync(e => e.UserId == alice.Id));
    }

    [Fact]
    public async Task A_quest_has_one_progress_row_per_user()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        db.QuestProgress.Add(new QuestProgress { UserId = alice.Id, QuestKey = QuestCatalog.GoblinCull });
        await db.SaveChangesAsync();

        db.QuestProgress.Add(new QuestProgress { UserId = alice.Id, QuestKey = QuestCatalog.GoblinCull });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task An_offer_can_be_taken_off_the_shelf_only_once()
    {
        // The daily cap on the shop is this index. Two requests that both got past the shop's
        // own check must not both mint an item, because the forge turns the second into
        // essence and the shelf is the only priced route to any.
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        db.ShopPurchases.Add(new ShopPurchase
        {
            UserId = alice.Id, OfferId = "20260101-0-silvered-blade"
        });
        await db.SaveChangesAsync();

        db.ShopPurchases.Add(new ShopPurchase
        {
            UserId = alice.Id, OfferId = "20260101-0-silvered-blade"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_shoppers_can_each_buy_their_own_shelf()
    {
        // Offer ids are rolled per user, but they collide by construction on the day the same
        // item lands in the same slot on two shelves.
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");
        var bob = await postgres.CreateUserAsync("test|bob");

        await using var db = postgres.CreateContext();

        foreach (var id in new[] { alice.Id, bob.Id })
        {
            db.ShopPurchases.Add(new ShopPurchase { UserId = id, OfferId = "20260101-0-silvered-blade" });
        }

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.ShopPurchases.CountAsync());
    }

    [Fact]
    public async Task A_balance_written_from_a_stale_read_is_refused()
    {
        // Gold rather than essence on purpose: the token lives on the character row, so it
        // guards every balance on it. Without one, both writers succeed and the larger spend
        // is simply forgotten.
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var first = postgres.CreateContext();
        await using var second = postgres.CreateContext();

        var mine = await first.Characters.SingleAsync(c => c.UserId == alice.Id);
        var stale = await second.Characters.SingleAsync(c => c.UserId == alice.Id);

        mine.Gold = 100;
        await first.SaveChangesAsync();

        stale.Gold = 40;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var reader = postgres.CreateContext();
        Assert.Equal(100, (await reader.Characters.SingleAsync(c => c.UserId == alice.Id)).Gold);
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_whole_adventure()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");
        var bob = await postgres.CreateUserAsync("test|bob");

        await using (var seed = postgres.CreateContext())
        {
            foreach (var id in new[] { alice.Id, bob.Id })
            {
                seed.InventoryItems.Add(new InventoryItem
                {
                    UserId = id, ItemKey = ItemCatalog.WornDagger, Slot = ItemSlot.Weapon
                });
                seed.Encounters.Add(new Encounter
                {
                    UserId = id, MonsterKey = MonsterCatalog.Goblin,
                    MonsterHitPoints = 3, Status = EncounterStatus.Won
                });
                seed.QuestProgress.Add(new QuestProgress { UserId = id, QuestKey = QuestCatalog.FirstBlood });
            }

            await seed.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();

        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == alice.Id));
        await db.SaveChangesAsync();

        Assert.Empty(await db.InventoryItems.Where(i => i.UserId == alice.Id).ToListAsync());
        Assert.Empty(await db.Encounters.Where(e => e.UserId == alice.Id).ToListAsync());
        Assert.Empty(await db.QuestProgress.Where(q => q.UserId == alice.Id).ToListAsync());

        // Bob is untouched.
        Assert.Single(await db.InventoryItems.Where(i => i.UserId == bob.Id).ToListAsync());
        Assert.Single(await db.Encounters.Where(e => e.UserId == bob.Id).ToListAsync());
    }

    [Fact]
    public async Task Character_rpg_columns_round_trip()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using (var write = postgres.CreateContext())
        {
            var character = await write.Characters.SingleAsync(c => c.UserId == alice.Id);

            character.ClassKey = ClassCatalog.Ranger;
            character.AbilityScores = new AbilityScores(12, 17, 13, 10, 14, 10);
            character.CurrentHitPoints = 21;
            character.Stamina = 4;
            character.Gold = 250;

            await write.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();
        var reloaded = await db.Characters.SingleAsync(c => c.UserId == alice.Id);

        Assert.Equal(ClassCatalog.Ranger, reloaded.ClassKey);
        Assert.Equal(17, reloaded.AbilityScores.Dexterity);
        Assert.Equal(21, reloaded.CurrentHitPoints);
        Assert.Equal(4, reloaded.Stamina);
        Assert.Equal(250, reloaded.Gold);
    }
}
