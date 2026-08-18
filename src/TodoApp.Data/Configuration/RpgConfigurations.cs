using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoApp.Models;
using TodoApp.Models.Rpg;

namespace TodoApp.Data.Configuration;

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(i => i.UserId).HasColumnType("uuid").IsRequired();
        builder.Property(i => i.ItemKey).HasColumnType("varchar(60)").IsRequired();
        builder.Property(i => i.Rarity).HasColumnType("integer").IsRequired();
        builder.Property(i => i.Slot).HasColumnType("integer").IsRequired();
        builder.Property(i => i.IsEquipped).HasColumnType("boolean").IsRequired();

        // Nullable, so an item with no affix stores no affix rather than a sentinel word
        // that AffixCatalog.Find would then have to be taught to ignore.
        builder.Property(i => i.PrefixKey).HasColumnType("varchar(40)");
        builder.Property(i => i.SuffixKey).HasColumnType("varchar(40)");

        // Defaulted so the migration can add it to a populated table, which Postgres will not
        // do for a NOT NULL column without one. Every row that already exists is one item.
        builder.Property(i => i.Quantity).HasColumnType("integer").IsRequired().HasDefaultValue(1);

        builder.Property(i => i.AcquiredAt).HasColumnType("timestamp with time zone").IsRequired();

        // Everything derived from the three keys plus the rarity (DEC-002). Named here rather
        // than left to convention: EF ignores a get-only property today, but the day one of
        // these grows a setter it would map silently and the model would start expecting
        // columns no migration ever wrote.
        builder.Ignore(i => i.Definition);
        builder.Ignore(i => i.Set);
        builder.Ignore(i => i.DisplayName);
        builder.Ignore(i => i.AffixEffects);
        builder.Ignore(i => i.AbilityBonuses);
        builder.Ignore(i => i.ArmourBonus);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.UserId);

        // At most one equipped item per slot per user. A partial unique index rather than
        // a check in the service, for the same reason the badge index is one: application
        // logic loses the race, the database does not.
        builder.HasIndex(i => new { i.UserId, i.Slot })
            .IsUnique()
            .HasFilter("\"IsEquipped\"");

        // One row per consumable per rarity, so a bag of six potions is one row with a count
        // rather than six rows the shop and the forge each have to reason about separately.
        // Filtered rather than global because everything worn is one row per item by design: a
        // backpack of five identical swords is legal and Unequipped_duplicates_in_a_slot_are_fine
        // says so. Consumables carry no affix at any rarity (AffixRules.RollableFor returns zero
        // for the slot), so two of them can never differ by a word this key cannot see.
        builder.HasIndex(i => new { i.UserId, i.ItemKey, i.Rarity })
            .IsUnique()
            .HasFilter($"\"Slot\" = {(int)ItemSlot.Consumable}");
    }
}

public class ShopPurchaseConfiguration : IEntityTypeConfiguration<ShopPurchase>
{
    public void Configure(EntityTypeBuilder<ShopPurchase> builder)
    {
        builder.ToTable("shop_purchases");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(p => p.UserId).HasColumnType("uuid").IsRequired();

        // Wide enough for "yyyyMMdd-<slot>-<item key>" at the longest key the catalog allows.
        builder.Property(p => p.OfferId).HasColumnType("varchar(80)").IsRequired();

        builder.Property(p => p.PurchasedAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One purchase per offer per user, and the offer id carries its own date, so this is
        // also the daily cap. A unique index rather than a check in the shop, for the same
        // reason the equipped-slot index is one: application logic loses the race and the
        // database does not, and losing this particular race mints essence out of gold.
        builder.HasIndex(p => new { p.UserId, p.OfferId }).IsUnique();
    }
}

public class EncounterConfiguration : IEntityTypeConfiguration<Encounter>
{
    public void Configure(EntityTypeBuilder<Encounter> builder)
    {
        // The check constraint is the only one in the tree, and it is here because the four
        // frozen hunt inputs are all-or-nothing. Encounter.Monster reads HuntLevel as the
        // discriminator and coalesces the other two to zero, so a half-written row would not
        // throw: it would quietly derive a stat block from defaulted zeros and look like a
        // correctly tuned hunt. That failure has no symptom the application could notice, which
        // is exactly the kind the database should be refusing.
        builder.ToTable("encounters", t => t.HasCheckConstraint(
            "CK_encounters_hunt_inputs_together",
            "\"HuntLevel\" IS NULL OR (\"HuntDaysOverdue\" IS NOT NULL AND \"HuntSubtasks\" IS NOT NULL)"));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(e => e.UserId).HasColumnType("uuid").IsRequired();
        builder.Property(e => e.MonsterKey).HasColumnType("varchar(60)").IsRequired();
        builder.Property(e => e.MonsterHitPoints).HasColumnType("integer").IsRequired();
        builder.Property(e => e.Status).HasColumnType("integer").IsRequired();
        builder.Property(e => e.Round).HasColumnType("integer").IsRequired();
        builder.Property(e => e.Log).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.GoldAwarded).HasColumnType("integer").IsRequired();
        builder.Property(e => e.BlessingUsed).HasColumnType("boolean").IsRequired();
        builder.Property(e => e.AbilityUses).HasColumnType("jsonb").IsRequired().HasDefaultValue("{}");
        builder.Property(e => e.Effects).HasColumnType("jsonb").IsRequired().HasDefaultValue("[]");

        // The high-water mark of the boss phases this fight has entered. One integer, because the
        // phase's name is read back from the catalog by it (DEC-004). Its line and its entry
        // effects are not read back: the line is composed into Log when the phase is entered and
        // the effects are applied once onto Effects beside it, where their rounds are spent down.
        builder.Property(e => e.Phase).HasColumnType("integer").IsRequired().HasDefaultValue(0);

        // Nullable, which is what lets the migration add it to a populated encounters table with
        // no default and no backfill: every fight that already exists was taken at the tavern.
        builder.Property(e => e.DungeonRunId).HasColumnType("uuid");

        // The hunt block. Every one of them nullable, which is the same argument DungeonRunId
        // made and is what lets the migration add all five to a populated encounters table with
        // no default and no backfill: every fight that already exists was taken at the tavern or
        // in a dungeon, and null says exactly that. A NOT NULL column here would have needed a
        // sentinel task id pointing at nothing.
        builder.Property(e => e.TaskId).HasColumnType("uuid");

        // The four frozen inputs, and the complete set of them: DEC-002 says store the rolled or
        // historical fact, so what is here is what a hunt was written against, never anything
        // derived from it. Armour class, hit points, gold range, drop chance, loot table, phases
        // and the monster's name are all recomputed by HuntRules.StatBlock on every read, which
        // is what lets a retune reach a fight already in progress (DEC-004).
        builder.Property(e => e.HuntLevel).HasColumnType("integer");
        builder.Property(e => e.HuntDaysOverdue).HasColumnType("integer");
        builder.Property(e => e.HuntSubtasks).HasColumnType("integer");

        // The catalog key only, sized like every other catalog key column in the tree.
        builder.Property(e => e.HuntFactionKey).HasColumnType("varchar(40)");

        builder.Property(e => e.StartedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(e => e.EndedAt).HasColumnType("timestamp with time zone");

        // Everything a hunt is, beyond the five columns above, is computed. Named here rather
        // than left to convention for the reason DungeonRunConfiguration gives: EF ignores a
        // get-only property today, but the day one of these grows a setter it maps silently and
        // the model starts expecting a column no migration ever wrote. Monster is the one that
        // would hurt, being a whole record.
        builder.Ignore(e => e.Monster);
        builder.Ignore(e => e.IsHunt);
        builder.Ignore(e => e.IsOver);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<DungeonRun>()
            .WithMany()
            .HasForeignKey(e => e.DungeonRunId)
            .OnDelete(DeleteBehavior.Cascade);

        // SET NULL, and this is the one place the hunt link deliberately differs from the
        // dungeon link above. DeleteTask uses ExecuteDeleteAsync and bypasses the change tracker,
        // so this referential action is the only thing standing between "the user tidied a task
        // away" and "a fought battle, its gold and its log left the chronicle". Nulling the
        // column leaves the four frozen scalars intact, so Encounter.Monster still resolves and
        // the fight stays renderable and finishable. RESTRICT was rejected: it would turn
        // deleting a task while a fight was open into a 500.
        builder.HasOne<TodoTask>()
            .WithMany()
            .HasForeignKey(e => e.TaskId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.UserId, e.StartedAt });

        // How deep a run has got is a count of its won rooms rather than a stored number
        // (DEC-002), so this is the index that count is read through. Composite and in this
        // order because the count is always "this run, won rooms only", and leading on the
        // foreign key also means EF does not add a second index for the relationship above.
        builder.HasIndex(e => new { e.DungeonRunId, e.Status });

        // Whether a task already had its hunt this period is a question about the fights on the
        // table rather than a HuntedAt column on the task (DEC-002), so this is the index that
        // question is read through. Leading on the foreign key also means EF adds no second index
        // for the relationship above. StartedAt trails it because the question is always "this
        // task, since when".
        builder.HasIndex(e => new { e.TaskId, e.StartedAt });

        // Standing with a faction is COUNT(won hunts under that banner) rather than a stored
        // reputation number, which is the whole of the faction storage: a counter can disagree
        // with the fights that actually happened, a count cannot, and there is correspondingly
        // nothing to inflate, drift or migrate. This is the index it is counted through, in the
        // order the count filters: one user, one banner, won only.
        builder.HasIndex(e => new { e.UserId, e.HuntFactionKey, e.Status });

        // One fight at a time. Without this, two concurrent requests could each spend one
        // stamina and open a second encounter, turning one unit of real work into two sets
        // of loot. Untouched by the hunt work above, and deliberately so: a hunt is an ordinary
        // encounter row with Status = Active, so this governs it exactly as it governs a tavern
        // fight, and an AddColumn does not rebuild an index.
        builder.HasIndex(e => e.UserId)
            .IsUnique()
            .HasFilter($"\"Status\" = {(int)EncounterStatus.Active}");
    }
}

public class DungeonRunConfiguration : IEntityTypeConfiguration<DungeonRun>
{
    public void Configure(EntityTypeBuilder<DungeonRun> builder)
    {
        builder.ToTable("dungeon_runs");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(r => r.UserId).HasColumnType("uuid").IsRequired();
        builder.Property(r => r.DungeonKey).HasColumnType("varchar(60)").IsRequired();

        // The rolled chain of monster keys. jsonb for the same reason the combat log is: it is
        // written once, always read whole, and never queried by its contents.
        builder.Property(r => r.Rooms).HasColumnType("jsonb").IsRequired().HasDefaultValue("[]");

        builder.Property(r => r.Status).HasColumnType("integer").IsRequired();
        builder.Property(r => r.GoldAwarded).HasColumnType("integer").IsRequired();
        builder.Property(r => r.StartedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(r => r.EndedAt).HasColumnType("timestamp with time zone");

        // Everything else about a dungeon is read from the catalog by its key (DEC-004). Named
        // here for the same reason InventoryItem names its derived members: EF ignores a get-only
        // property today, but the day one grows a setter it would map silently and the model
        // would start expecting a column no migration ever wrote.
        builder.Ignore(r => r.Dungeon);
        builder.Ignore(r => r.IsOver);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.UserId, r.StartedAt });

        // One run at a time, the parallel of the encounter index and there for the same reason.
        // Two concurrent starts would otherwise each pass the service's AnyAsync check and open a
        // run, and the loser of that race would be a run the player can never finish because the
        // one encounter slot belongs to the other. The service check is the friendly path; this
        // is what actually wins the race.
        builder.HasIndex(r => r.UserId)
            .IsUnique()
            .HasFilter($"\"Status\" = {(int)DungeonRunStatus.Active}");
    }
}

public class BestiaryEntryConfiguration : IEntityTypeConfiguration<BestiaryEntry>
{
    public void Configure(EntityTypeBuilder<BestiaryEntry> builder)
    {
        builder.ToTable("bestiary_entries");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(b => b.UserId).HasColumnType("uuid").IsRequired();
        builder.Property(b => b.MonsterKey).HasColumnType("varchar(60)").IsRequired();
        builder.Property(b => b.Encounters).HasColumnType("integer").IsRequired();
        builder.Property(b => b.Kills).HasColumnType("integer").IsRequired();
        builder.Property(b => b.GoldTaken).HasColumnType("integer").IsRequired();
        builder.Property(b => b.BestRound).HasColumnType("integer").IsRequired();
        builder.Property(b => b.FirstSeenAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(b => b.LastSeenAt).HasColumnType("timestamp with time zone").IsRequired();

        // Read through MonsterCatalog on every request (DEC-004), so a retuned monster is
        // retuned everywhere at once. Named here for the same reason InventoryItem names its
        // derived members: EF ignores a get-only property today, but the day one grows a
        // setter it would map silently and expect a column no migration ever wrote.
        builder.Ignore(b => b.Definition);
        builder.Ignore(b => b.IsSlain);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per user per monster, the same reasoning as the QuestProgress index: two
        // concurrent starts on the same monster would otherwise each insert a row and split
        // one sighting count across both, with neither telling the truth afterwards.
        builder.HasIndex(b => new { b.UserId, b.MonsterKey }).IsUnique();
    }
}

public class QuestProgressConfiguration : IEntityTypeConfiguration<QuestProgress>
{
    public void Configure(EntityTypeBuilder<QuestProgress> builder)
    {
        builder.ToTable("quest_progress");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(q => q.UserId).HasColumnType("uuid").IsRequired();
        builder.Property(q => q.QuestKey).HasColumnType("varchar(60)").IsRequired();
        builder.Property(q => q.Counters).HasColumnType("jsonb").IsRequired();
        builder.Property(q => q.ClaimedAt).HasColumnType("timestamp with time zone");
        builder.Property(q => q.StartedAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per user per quest, so progress cannot be double-counted into two rows.
        builder.HasIndex(q => new { q.UserId, q.QuestKey }).IsUnique();
    }
}

public class HuntContractConfiguration : IEntityTypeConfiguration<HuntContract>
{
    public void Configure(EntityTypeBuilder<HuntContract> builder)
    {
        builder.ToTable("hunt_contracts");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(c => c.UserId).HasColumnType("uuid").IsRequired();

        // Nullable, because the foreign key below sets it null rather than taking the contract
        // down with the task. A discharged contract outlives the row it was written on for the
        // same reason a fought battle does: the work was done, and tidying the task away
        // afterwards must not take back what doing it earned.
        builder.Property(c => c.TaskId).HasColumnType("uuid");

        // The task's own words, sized like the title column it is copied from.
        builder.Property(c => c.TaskTitle).HasColumnType("varchar(200)").IsRequired();

        // Catalog keys only (DEC-004), sized like every other catalog key column in the tree.
        builder.Property(c => c.ArchetypeKey).HasColumnType("varchar(60)").IsRequired();
        builder.Property(c => c.FactionKey).HasColumnType("varchar(40)");

        // The three frozen numbers, and the complete set of them. Everything the stat block
        // answers (hit points, armour class, the purse, the drop chance, the phases) is
        // recomputed by HuntRules.StatBlock on every read, so a retune reaches a contract that
        // was accepted before it (DEC-002, DEC-004).
        builder.Property(c => c.Level).HasColumnType("integer").IsRequired();
        builder.Property(c => c.DaysOverdue).HasColumnType("integer").IsRequired();
        builder.Property(c => c.Subtasks).HasColumnType("integer").IsRequired();

        builder.Property(c => c.Status).HasColumnType("integer").IsRequired();
        builder.Property(c => c.AcceptedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(c => c.DischargedAt).HasColumnType("timestamp with time zone");
        builder.Property(c => c.ClosedAt).HasColumnType("timestamp with time zone");
        builder.Property(c => c.EncounterId).HasColumnType("uuid");

        // Named rather than left to convention, for the reason DungeonRunConfiguration gives:
        // EF ignores a get-only property today, but the day one grows a setter it maps silently
        // and the model starts expecting a column no migration ever wrote. Monster is the one
        // that would hurt, being a whole record.
        builder.Ignore(c => c.Monster);
        builder.Ignore(c => c.IsLive);
        builder.Ignore(c => c.MayBeFought);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // SET NULL, the same choice the encounter's task link makes and for the same reason:
        // DeleteTask uses ExecuteDeleteAsync and bypasses the change tracker, so this referential
        // action is the only thing that runs. Cascade was rejected because it would take a
        // discharged contract, which is work already done, away with the row.
        builder.HasOne<TodoTask>()
            .WithMany()
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.SetNull);

        // No foreign key to the encounter on purpose. The link points contract to fight, and a
        // constraint here would have to choose a referential action for a row the chronicle
        // keeps forever; the encounter carries its own frozen copies of the same facts, so
        // nothing downstream reads back through this.
        builder.HasIndex(c => new { c.UserId, c.Status });

        // Whether a task already carries a contract, and which one, is read through this. It
        // leads on the foreign key, so EF adds no second index for the relationship above, and
        // AcceptedAt trails it because the question is always "this task, since when".
        builder.HasIndex(c => new { c.TaskId, c.AcceptedAt });

        // One live contract per task. Without it two concurrent accepts each pass the service's
        // check and write a row, and the loser leaves a second contract on the same task that
        // one completion would discharge: two fights, two bounties, one piece of work.
        //
        // Filtered on the two open states rather than on a boolean column, so a fought or torn
        // up contract stops blocking without anything having to remember to clear a flag.
        builder.HasIndex(c => c.TaskId)
            .IsUnique()
            .HasFilter(
                $"\"Status\" IN ({(int)HuntContractStatus.Accepted}, "
                + $"{(int)HuntContractStatus.Discharged})");
    }
}
