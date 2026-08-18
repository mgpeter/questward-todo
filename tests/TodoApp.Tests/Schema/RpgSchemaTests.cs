using Microsoft.EntityFrameworkCore;
using TodoApp.Models;
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

    /// <summary>
    /// One row per consumable per rarity, enforced by the database rather than by the services.
    /// </summary>
    /// <remarks>
    /// The shop and the loot service both acquire items, and both used to insert directly. Two
    /// rows for the same potion would each carry their own count, so the bag would show the same
    /// item twice and spending from one would leave the other untouched. Application logic loses
    /// this race and the database does not, which is the same reasoning the equipped-slot index
    /// rests on.
    /// </remarks>
    [Fact]
    public async Task A_consumable_gets_one_row_per_rarity_and_no_more()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        db.InventoryItems.Add(new InventoryItem
        {
            UserId = alice.Id, ItemKey = ItemCatalog.DraughtOfMending,
            Slot = ItemSlot.Consumable, Rarity = Rarity.Common, Quantity = 6
        });
        await db.SaveChangesAsync();

        db.InventoryItems.Add(new InventoryItem
        {
            UserId = alice.Id, ItemKey = ItemCatalog.DraughtOfMending,
            Slot = ItemSlot.Consumable, Rarity = Rarity.Common, Quantity = 1
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_same_potion_at_another_rarity_is_a_different_item()
    {
        // The rarity is part of the key because it is part of what the item does: a Rare
        // Draught of Mending heals more than a Common one, so merging them would silently
        // upgrade or downgrade whichever stack lost.
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");
        var bob = await postgres.CreateUserAsync("test|bob");

        await using var db = postgres.CreateContext();

        foreach (var rarity in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare })
        {
            db.InventoryItems.Add(new InventoryItem
            {
                UserId = alice.Id, ItemKey = ItemCatalog.DraughtOfMending,
                Slot = ItemSlot.Consumable, Rarity = rarity, Quantity = 2
            });
        }

        // And the key is per user, or Alice holding a potion would stop Bob ever holding one.
        db.InventoryItems.Add(new InventoryItem
        {
            UserId = bob.Id, ItemKey = ItemCatalog.DraughtOfMending,
            Slot = ItemSlot.Consumable, Rarity = Rarity.Common, Quantity = 1
        });

        await db.SaveChangesAsync();

        Assert.Equal(4, await db.InventoryItems.CountAsync(i => i.Slot == ItemSlot.Consumable));
    }

    /// <summary>
    /// The stacking index is filtered on the slot, so nothing worn is touched by it.
    /// </summary>
    /// <remarks>
    /// Unfiltered it would forbid a backpack of five identical swords, which
    /// <see cref="Unequipped_duplicates_in_a_slot_are_fine"/> says is legal and which every
    /// existing bag relies on.
    /// </remarks>
    [Fact]
    public async Task Duplicate_gear_is_still_allowed_beside_the_stacking_index()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        for (var i = 0; i < 4; i++)
        {
            db.InventoryItems.Add(new InventoryItem
            {
                UserId = alice.Id, ItemKey = ItemCatalog.GreatAxe,
                Slot = ItemSlot.Weapon, Rarity = Rarity.Common
            });
        }

        await db.SaveChangesAsync();

        var rows = await db.InventoryItems
            .Where(i => i.ItemKey == ItemCatalog.GreatAxe)
            .ToListAsync();

        Assert.Equal(4, rows.Count);

        // And the default the migration wrote is one, so every existing row means one item.
        Assert.All(rows, r => Assert.Equal(1, r.Quantity));
    }

    [Fact]
    public async Task Encounter_phase_and_item_quantity_round_trip()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using (var write = postgres.CreateContext())
        {
            write.Encounters.Add(new Encounter
            {
                UserId = alice.Id, MonsterKey = MonsterCatalog.ElderDragon,
                MonsterHitPoints = 30, Status = EncounterStatus.Active, Phase = 2
            });

            write.InventoryItems.Add(new InventoryItem
            {
                UserId = alice.Id, ItemKey = ItemCatalog.VialOfSerpentsKiss,
                Slot = ItemSlot.Consumable, Rarity = Rarity.Rare, Quantity = 7
            });

            await write.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();

        var encounter = await db.Encounters.SingleAsync(e => e.UserId == alice.Id);
        var vials = await db.InventoryItems.SingleAsync(i => i.UserId == alice.Id);

        Assert.Equal(2, encounter.Phase);
        Assert.Equal(7, vials.Quantity);

        // The name of the phase is not stored anywhere. It comes back from the catalog, keyed
        // by the one integer that is (DEC-004).
        Assert.Equal("Last Fire", encounter.Monster!.PhaseDefinition(encounter.Phase)!.Name);
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
    public async Task Only_one_dungeon_run_can_be_active_at_a_time()
    {
        // The parallel of the encounter index, and there for the same reason. Two concurrent
        // POST /dungeons would otherwise each pass the service's check and open a run, and the
        // loser could never be finished because the one encounter slot belongs to the other.
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        db.DungeonRuns.Add(new DungeonRun
        {
            UserId = alice.Id, DungeonKey = DungeonCatalog.SunkenWarren,
            Status = DungeonRunStatus.Active
        });
        await db.SaveChangesAsync();

        db.DungeonRuns.Add(new DungeonRun
        {
            UserId = alice.Id, DungeonKey = DungeonCatalog.BarrowDeeps,
            Status = DungeonRunStatus.Active
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Finished_dungeon_runs_do_not_block_a_new_one()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        var finished = new[]
        {
            DungeonRunStatus.Cleared, DungeonRunStatus.Failed, DungeonRunStatus.Abandoned
        };

        foreach (var status in finished)
        {
            db.DungeonRuns.Add(new DungeonRun
            {
                UserId = alice.Id, DungeonKey = DungeonCatalog.SunkenWarren,
                Status = status, EndedAt = DateTimeOffset.UtcNow
            });
        }

        db.DungeonRuns.Add(new DungeonRun
        {
            UserId = alice.Id, DungeonKey = DungeonCatalog.SunkenWarren,
            Status = DungeonRunStatus.Active
        });

        await db.SaveChangesAsync();

        Assert.Equal(4, await db.DungeonRuns.CountAsync(r => r.UserId == alice.Id));
    }

    [Fact]
    public async Task Two_adventurers_can_each_be_in_their_own_dungeon()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");
        var bob = await postgres.CreateUserAsync("test|bob");

        await using var db = postgres.CreateContext();

        foreach (var id in new[] { alice.Id, bob.Id })
        {
            db.DungeonRuns.Add(new DungeonRun
            {
                UserId = id, DungeonKey = DungeonCatalog.SunkenWarren,
                Status = DungeonRunStatus.Active
            });
        }

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.DungeonRuns.CountAsync());
    }

    /// <summary>
    /// A room's fight is an ordinary encounter row, which is the ruling the whole feature rests
    /// on: IX_encounters_UserId still governs it, so a dungeon cannot open a second fight.
    /// </summary>
    [Fact]
    public async Task A_dungeon_room_is_still_governed_by_the_one_fight_index()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        var run = new DungeonRun
        {
            UserId = alice.Id, DungeonKey = DungeonCatalog.SunkenWarren,
            Status = DungeonRunStatus.Active
        };

        db.DungeonRuns.Add(run);
        db.Encounters.Add(new Encounter
        {
            UserId = alice.Id, MonsterKey = MonsterCatalog.GiantRat,
            MonsterHitPoints = 7, Status = EncounterStatus.Active, DungeonRunId = run.Id
        });
        await db.SaveChangesAsync();

        // A tavern fight beside the room, which the service refuses and the index refuses under it.
        db.Encounters.Add(new Encounter
        {
            UserId = alice.Id, MonsterKey = MonsterCatalog.Goblin,
            MonsterHitPoints = 10, Status = EncounterStatus.Active
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>
    /// One live contract per task, enforced by the database rather than by a service check.
    /// </summary>
    /// <remarks>
    /// The service asks whether a task already carries a contract before it writes one, and two
    /// concurrent accepts both pass that question. The loser leaves a second contract on the same
    /// task that one completion would discharge: two fights, two bounties, one piece of work.
    /// <para>
    /// The index is filtered on the two open states, so a contract that has been fought or torn up
    /// stops blocking with no flag anywhere to remember to clear.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Only_one_live_contract_can_stand_on_a_task()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        var task = new TodoTask { UserId = alice.Id, Title = "File the tax return" };

        db.Tasks.Add(task);
        db.HuntContracts.Add(Contract(alice.Id, task.Id, HuntContractStatus.Accepted));
        await db.SaveChangesAsync();

        db.HuntContracts.Add(Contract(alice.Id, task.Id, HuntContractStatus.Accepted));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();

        // Discharged is the other live state, and it is refused for the same reason.
        db.HuntContracts.Add(Contract(alice.Id, task.Id, HuntContractStatus.Discharged));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>A contract that is over stops blocking, which is what makes the next one possible.</summary>
    [Fact]
    public async Task A_closed_contract_does_not_block_the_next_one()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        var task = new TodoTask { UserId = alice.Id, Title = "Water the plants" };

        db.Tasks.Add(task);
        db.HuntContracts.Add(Contract(alice.Id, task.Id, HuntContractStatus.Fought));
        db.HuntContracts.Add(Contract(alice.Id, task.Id, HuntContractStatus.Abandoned));
        db.HuntContracts.Add(Contract(alice.Id, task.Id, HuntContractStatus.Accepted));

        await db.SaveChangesAsync();

        Assert.Equal(3, await db.HuntContracts.CountAsync(c => c.TaskId == task.Id));
    }

    /// <summary>
    /// A task tidied away nulls the link and leaves the contract standing.
    /// </summary>
    /// <remarks>
    /// SET NULL rather than cascade, and the difference is the whole point: a discharged contract
    /// is work that was already done, and DeleteTask runs ExecuteDeleteAsync, so this referential
    /// action is the only thing between "the user tidied a task away" and an earned fight
    /// vanishing with it. The endpoint sweeps the merely accepted ones to Abandoned itself,
    /// because a referential action cannot tell the two apart.
    /// </remarks>
    [Fact]
    public async Task Deleting_a_task_keeps_its_contracts_and_only_nulls_the_link()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        var task = new TodoTask { UserId = alice.Id, Title = "Finished first" };
        var contract = Contract(alice.Id, task.Id, HuntContractStatus.Discharged);

        db.Tasks.Add(task);
        db.HuntContracts.Add(contract);
        await db.SaveChangesAsync();

        await db.Tasks.Where(t => t.Id == task.Id).ExecuteDeleteAsync();

        await using var fresh = postgres.CreateContext();

        var survivor = await fresh.HuntContracts.SingleAsync(c => c.Id == contract.Id);

        Assert.Null(survivor.TaskId);
        Assert.Equal(HuntContractStatus.Discharged, survivor.Status);

        // Every number it was written for is still on the row, so it still derives its block.
        Assert.Equal(12, survivor.DaysOverdue);
        Assert.Equal(FactionCatalog.TheLedger, survivor.FactionKey);
        Assert.Equal("Finished first", survivor.TaskTitle);
        Assert.NotNull(survivor.Monster);
    }

    private static HuntContract Contract(Guid userId, Guid taskId, HuntContractStatus status) =>
        new()
        {
            UserId = userId,
            TaskId = taskId,
            TaskTitle = "Finished first",
            ArchetypeKey = HuntArchetypeCatalog.Bulwark,
            Level = 3,
            DaysOverdue = 12,
            Subtasks = 0,
            FactionKey = FactionCatalog.TheLedger,
            Status = status,
            ClosedAt = status is HuntContractStatus.Fought or HuntContractStatus.Abandoned
                ? DateTimeOffset.UtcNow
                : null
        };

    /// <summary>
    /// A contract's fight is an ordinary encounter row, which is the ruling the whole feature
    /// rests on: IX_encounters_UserId still governs it, and it was never rebuilt to learn about
    /// hunts.
    /// </summary>
    [Fact]
    public async Task A_hunt_is_still_governed_by_the_one_fight_index()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        var task = new TodoTask { UserId = alice.Id, Title = "File the tax return" };

        db.Tasks.Add(task);
        db.Encounters.Add(new Encounter
        {
            UserId = alice.Id,
            MonsterKey = HuntArchetypeCatalog.Bulwark,
            MonsterHitPoints = 33,
            Status = EncounterStatus.Active,
            TaskId = task.Id,
            HuntLevel = 3,
            HuntDaysOverdue = 12,
            HuntSubtasks = 0,
            HuntFactionKey = FactionCatalog.TheLedger
        });
        await db.SaveChangesAsync();

        // A tavern fight beside the contract, which the service refuses and the index refuses
        // under it. A hunt that had been made a second kind of fight would slip past this.
        db.Encounters.Add(new Encounter
        {
            UserId = alice.Id, MonsterKey = MonsterCatalog.Goblin,
            MonsterHitPoints = 10, Status = EncounterStatus.Active
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>
    /// The frozen inputs are all or nothing, enforced by a check constraint.
    /// </summary>
    /// <remarks>
    /// <see cref="Encounter.Monster"/> uses HuntLevel as its discriminator and coalesces the other
    /// two, so a half written row would not throw: it would quietly derive a stat block from
    /// defaulted zeros and look correctly tuned. There is no symptom to notice, which is why the
    /// database refuses the row rather than the code remembering to.
    /// </remarks>
    [Fact]
    public async Task A_half_written_contract_is_refused_by_the_database()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        db.Encounters.Add(new Encounter
        {
            UserId = alice.Id,
            MonsterKey = HuntArchetypeCatalog.Drudge,
            MonsterHitPoints = 5,
            Status = EncounterStatus.Active,
            HuntLevel = 2
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();

        // A fight that is not a contract carries none of them, which is what lets all five
        // columns be nullable and the migration need no backfill.
        db.Encounters.Add(new Encounter
        {
            UserId = alice.Id, MonsterKey = MonsterCatalog.Goblin,
            MonsterHitPoints = 10, Status = EncounterStatus.Active
        });

        await db.SaveChangesAsync();

        Assert.False((await db.Encounters.AsNoTracking().SingleAsync()).IsHunt);
    }

    /// <summary>
    /// Tidying a task away must never delete a fought battle, its gold or its log.
    /// </summary>
    /// <remarks>
    /// DeleteTask runs ExecuteDeleteAsync and bypasses the change tracker, so the referential
    /// action is the whole answer. SET NULL loses the attribution and keeps the fight; the four
    /// frozen scalars are untouched by it, so the stat block still derives and the row stays
    /// renderable and finishable. CASCADE would have taken the battle with the task, and RESTRICT
    /// would have turned tidying a list into a 500.
    /// </remarks>
    [Fact]
    public async Task Deleting_a_hunted_task_keeps_the_fight_and_only_nulls_the_link()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        var task = new TodoTask { UserId = alice.Id, Title = "File the tax return" };

        db.Tasks.Add(task);
        db.Encounters.Add(new Encounter
        {
            UserId = alice.Id,
            MonsterKey = HuntArchetypeCatalog.Dread,
            MonsterHitPoints = 0,
            Status = EncounterStatus.Won,
            GoldAwarded = 240,
            TaskId = task.Id,
            HuntLevel = 6,
            HuntDaysOverdue = 90,
            HuntSubtasks = 4,
            HuntFactionKey = FactionCatalog.TheLedger
        });
        await db.SaveChangesAsync();

        await db.Tasks.Where(t => t.Id == task.Id).ExecuteDeleteAsync();

        db.ChangeTracker.Clear();

        var survivor = await db.Encounters.AsNoTracking().SingleAsync();

        Assert.Null(survivor.TaskId);
        Assert.True(survivor.IsHunt);
        Assert.Equal(EncounterStatus.Won, survivor.Status);
        Assert.Equal(240, survivor.GoldAwarded);
        Assert.Equal(90, survivor.HuntDaysOverdue);
        Assert.Equal(FactionCatalog.TheLedger, survivor.HuntFactionKey);

        // Still derives, so the chronicle renders it and standing still counts it.
        Assert.Equal("Immemorial Dread", survivor.Monster!.Name);
    }

    [Fact]
    public async Task A_dungeon_run_round_trips_with_its_rolled_chain()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        var id = Guid.CreateVersion7();

        await using (var seed = postgres.CreateContext())
        {
            var run = new DungeonRun
            {
                Id = id, UserId = alice.Id, DungeonKey = DungeonCatalog.SunkenWarren,
                Status = DungeonRunStatus.Cleared, GoldAwarded = 60, EndedAt = DateTimeOffset.UtcNow
            };

            DungeonRuns.Write(run, [MonsterCatalog.GiantRat, MonsterCatalog.Goblin, MonsterCatalog.HedgeTroll]);

            seed.DungeonRuns.Add(run);
            await seed.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();

        var loaded = await db.DungeonRuns.SingleAsync(r => r.Id == id);

        Assert.Equal(DungeonRunStatus.Cleared, loaded.Status);
        Assert.Equal(60, loaded.GoldAwarded);
        Assert.Equal(
            [MonsterCatalog.GiantRat, MonsterCatalog.Goblin, MonsterCatalog.HedgeTroll],
            DungeonRuns.Read(loaded));
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

    /// <summary>
    /// One row per user per monster, enforced by the database rather than by the service.
    /// </summary>
    /// <remarks>
    /// Two starts against the same monster racing each other would both read no row and both
    /// insert one, and the counters would stop being counters. CombatService drops the losing
    /// chronicle write rather than the fight, which only works because the index is here to do
    /// the refusing.
    /// </remarks>
    [Fact]
    public async Task A_monster_has_one_chronicle_row_per_user()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        db.BestiaryEntries.Add(new BestiaryEntry
        {
            UserId = alice.Id, MonsterKey = MonsterCatalog.Goblin, Encounters = 1
        });
        await db.SaveChangesAsync();

        db.BestiaryEntries.Add(new BestiaryEntry
        {
            UserId = alice.Id, MonsterKey = MonsterCatalog.Goblin, Encounters = 1
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_users_can_each_have_met_the_same_monster()
    {
        // The index is on the pair. Scoped to the key alone, Alice meeting a goblin would
        // permanently prevent Bob from ever recording one.
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");
        var bob = await postgres.CreateUserAsync("test|bob");

        await using var db = postgres.CreateContext();

        db.BestiaryEntries.Add(new BestiaryEntry
        {
            UserId = alice.Id, MonsterKey = MonsterCatalog.Goblin, Encounters = 1
        });
        db.BestiaryEntries.Add(new BestiaryEntry
        {
            UserId = bob.Id, MonsterKey = MonsterCatalog.Goblin, Encounters = 1
        });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.BestiaryEntries.CountAsync(b => b.MonsterKey == MonsterCatalog.Goblin));
    }

    [Fact]
    public async Task Chronicle_counters_round_trip()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        var firstSeen = new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.Zero);
        var lastSeen = new DateTimeOffset(2026, 8, 14, 18, 5, 0, TimeSpan.Zero);

        await using (var write = postgres.CreateContext())
        {
            write.BestiaryEntries.Add(new BestiaryEntry
            {
                UserId = alice.Id,
                MonsterKey = MonsterCatalog.Skeleton,
                Encounters = 9,
                Kills = 4,
                GoldTaken = 137,
                BestRound = 2,
                FirstSeenAt = firstSeen,
                LastSeenAt = lastSeen
            });

            await write.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();
        var reloaded = await db.BestiaryEntries.SingleAsync(b => b.UserId == alice.Id);

        Assert.Equal(9, reloaded.Encounters);
        Assert.Equal(4, reloaded.Kills);
        Assert.Equal(137, reloaded.GoldTaken);
        Assert.Equal(2, reloaded.BestRound);
        Assert.Equal(firstSeen, reloaded.FirstSeenAt);
        Assert.Equal(lastSeen, reloaded.LastSeenAt);

        // Derived and deliberately unmapped, so they come back from the catalog rather than
        // from a column that could disagree with it.
        Assert.True(reloaded.IsSlain);
        Assert.Equal("Skeleton", reloaded.Definition!.Name);
    }

    /// <summary>
    /// A key retired from the catalog leaves the row readable. The chronicle is history, and
    /// history does not stop being true when the catalog moves on.
    /// </summary>
    [Fact]
    public async Task A_row_for_a_monster_no_longer_in_the_catalog_still_loads()
    {
        await postgres.ResetAsync();
        var alice = await postgres.CreateUserAsync("test|alice");

        await using var db = postgres.CreateContext();

        db.BestiaryEntries.Add(new BestiaryEntry
        {
            UserId = alice.Id, MonsterKey = "retired-monster", Encounters = 2, Kills = 1
        });
        await db.SaveChangesAsync();

        var reloaded = await db.BestiaryEntries.SingleAsync(b => b.MonsterKey == "retired-monster");

        Assert.Null(reloaded.Definition);
        Assert.True(reloaded.IsSlain);
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
                seed.BestiaryEntries.Add(new BestiaryEntry
                {
                    UserId = id, MonsterKey = MonsterCatalog.Goblin, Encounters = 1, Kills = 1
                });
                seed.DungeonRuns.Add(new DungeonRun
                {
                    UserId = id, DungeonKey = DungeonCatalog.SunkenWarren,
                    Status = DungeonRunStatus.Active
                });
            }

            await seed.SaveChangesAsync();
        }

        await using var db = postgres.CreateContext();

        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == alice.Id));
        await db.SaveChangesAsync();

        Assert.Empty(await db.InventoryItems.Where(i => i.UserId == alice.Id).ToListAsync());
        Assert.Empty(await db.Encounters.Where(e => e.UserId == alice.Id).ToListAsync());
        Assert.Empty(await db.QuestProgress.Where(q => q.UserId == alice.Id).ToListAsync());
        Assert.Empty(await db.BestiaryEntries.Where(b => b.UserId == alice.Id).ToListAsync());
        Assert.Empty(await db.DungeonRuns.Where(r => r.UserId == alice.Id).ToListAsync());

        // Bob is untouched.
        Assert.Single(await db.InventoryItems.Where(i => i.UserId == bob.Id).ToListAsync());
        Assert.Single(await db.Encounters.Where(e => e.UserId == bob.Id).ToListAsync());
        Assert.Single(await db.BestiaryEntries.Where(b => b.UserId == bob.Id).ToListAsync());
        Assert.Single(await db.DungeonRuns.Where(r => r.UserId == bob.Id).ToListAsync());
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
