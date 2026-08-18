using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <summary>
    /// Three columns, no new table. Set membership is not among them: it is a pure function of
    /// ItemKey through SetCatalog, so a column would be the same fact twice and would go stale
    /// the day a set is re-composed, which is the edit DEC-004 exists to keep free.
    /// </summary>
    /// <remarks>
    /// Every value added here is a roll outcome or a running balance whose inputs are destroyed,
    /// the same category as the Rarity, Gold and Stamina columns beside them, so this migration
    /// adds no DEC-002 exposure.
    /// </remarks>
    public partial class AddItemAffixesAndSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Both affix columns are nullable, which is what makes this half of the migration
            // safe on a populated table: a nullable add needs no default, so every existing row
            // simply reads as an item with no affixes, which is exactly what it is.
            migrationBuilder.AddColumn<string>(
                name: "PrefixKey",
                table: "inventory_items",
                type: "varchar(40)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuffixKey",
                table: "inventory_items",
                type: "varchar(40)",
                nullable: true);

            // defaultValue: 0 is load-bearing rather than cosmetic. Postgres refuses a NOT NULL
            // add on a table that already has rows without one, and every deployed database has
            // a character row in it, so without this the migration fails on contact with real
            // data while passing on an empty test database.
            migrationBuilder.AddColumn<int>(
                name: "Essence",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <summary>
        /// Rolling this back destroys every affix ever rolled and every essence balance ever
        /// earned, and neither can be recomputed: salvage already deleted the items that paid
        /// for the essence. Stated here because a scaffolded Down looks reversible and this one
        /// is not.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrefixKey",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "SuffixKey",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "Essence",
                table: "characters");
        }
    }
}
