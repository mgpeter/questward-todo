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
        builder.ToTable("encounters");

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

        builder.Property(e => e.StartedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(e => e.EndedAt).HasColumnType("timestamp with time zone");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<DungeonRun>()
            .WithMany()
            .HasForeignKey(e => e.DungeonRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.UserId, e.StartedAt });

        // How deep a run has got is a count of its won rooms rather than a stored number
        // (DEC-002), so this is the index that count is read through. Composite and in this
        // order because the count is always "this run, won rooms only", and leading on the
        // foreign key also means EF does not add a second index for the relationship above.
        builder.HasIndex(e => new { e.DungeonRunId, e.Status });

        // One fight at a time. Without this, two concurrent requests could each spend one
        // stamina and open a second encounter, turning one unit of real work into two sets
        // of loot.
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
