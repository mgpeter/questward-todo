using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <summary>
    /// Two columns and one index: the boss phase a fight has entered, and the count on a
    /// stackable item.
    /// </summary>
    /// <remarks>
    /// Both columns are added with an explicit default, because Postgres will not add a NOT NULL
    /// column to a populated table without one. The values are chosen so every row that already
    /// exists reads correctly with no backfill at all: a fight in progress has entered no phase,
    /// and every row in every bag today is exactly one item.
    /// </remarks>
    public partial class AddPhasesAndConsumables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "inventory_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Phase",
                table: "encounters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // One row per consumable per rarity. Deliberately written with no preceding dedupe,
            // which is the one thing that could make this migration fail on a populated
            // database, so here is why it cannot.
            //
            // The filter is "Slot" = 3, which is ItemSlot.Consumable. InventoryItem.Slot is only
            // ever written from ItemCatalog's definition of the item, at the four sites that
            // create a row, and until this change no entry in that catalog carried the value. So
            // no existing row can match the filter, the index is created over an empty set, and
            // there is nothing to merge. Verified against a live database as well as reasoned
            // about: SELECT COUNT(*) FROM inventory_items WHERE "Slot" = 3 returns zero.
            //
            // A merge written on the assumption that duplicates might exist would be the more
            // dangerous choice, not the safer one: it would destroy rows on the strength of
            // reasoning that has just been shown to be wrong. If the impossible has happened,
            // CREATE UNIQUE INDEX refuses in a transaction and the database is untouched, which
            // is the correct way for this to fail.
            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_UserId_ItemKey_Rarity",
                table: "inventory_items",
                columns: new[] { "UserId", "ItemKey", "Rarity" },
                unique: true,
                filter: "\"Slot\" = 3");
        }

        /// <summary>
        /// Reverses the schema. It does not reverse the data, and cannot.
        /// </summary>
        /// <remarks>
        /// Down is lossy and says so rather than letting someone find out. Dropping Quantity
        /// turns a stack of six potions into one row that now means one potion, and the other
        /// five are gone with the column; there is nowhere in the old schema to put them. The
        /// phase a fight had entered goes the same way, which on a fight still in progress means
        /// a boss that will announce its phase a second time on the next blow that crosses the
        /// threshold. A down migration on a populated database is a data decision, not a
        /// rollback.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inventory_items_UserId_ItemKey_Rarity",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "encounters");
        }
    }
}
