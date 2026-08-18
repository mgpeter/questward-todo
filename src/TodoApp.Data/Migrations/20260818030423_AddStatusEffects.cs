using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <summary>
    /// Folds the bare MonsterDisadvantageRounds counter into a typed status effect array.
    /// </summary>
    /// <remarks>
    /// The scaffold emitted the drop before the add and no backfill between them, which would
    /// have thrown away the state of every fight in progress. Hand-corrected to add first,
    /// convert second, drop third.
    /// </remarks>
    public partial class AddStatusEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Explicit default because Postgres cannot add a NOT NULL column to a populated
            // table without one. Same pattern as AbilityUses in AddAbilitiesAndShop.
            migrationBuilder.AddColumn<string>(
                name: "Effects",
                table: "encounters",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            // A fight already in progress keeps its mechanic. That is the whole reason the
            // conversion happens before the drop rather than after it: a dropped column takes
            // its data with it, and the point of folding the counter into Weakened is that
            // nobody mid-fight notices the change.
            //
            // This is expected to match zero rows, and it is written anyway. The counter is set
            // at the end of the player's half and consumed by the monster's half of the same
            // ResolveRoundAsync call, so on this build it is never observably non-zero between
            // requests. "Expected zero" is not "provably zero" for a database someone else has
            // been running, and the cost of being wrong is a player's fight silently losing its
            // mechanic. A no-op backfill with no explanation reads as cargo cult, so here is the
            // reasoning instead.
            //
            // jsonb_build_object rather than string concatenation: a value cannot break out of
            // the literal, and Postgres validates the result as jsonb rather than trusting it.
            // The names and the numbers are exactly what a bare JsonSerializer.Serialize writes,
            // which is what StatusEffects.Write uses: PascalCase property names, enums as
            // numbers, Kind 0 for Weakened and Target 1 for Monster. Get that wrong and nothing
            // throws. StatusEffects.Read swallows JsonException by design, so a mis-cased blob
            // binds defaults, produces a Weakened of zero rounds and is pruned away in silence.
            migrationBuilder.Sql(
                """
                UPDATE encounters
                SET "Effects" = jsonb_build_array(
                        jsonb_build_object(
                            'Kind', 0,
                            'Target', 1,
                            'Rounds', "MonsterDisadvantageRounds",
                            'Magnitude', 0,
                            'Source', 'vicious-mockery'))
                WHERE "MonsterDisadvantageRounds" > 0;
                """);

            migrationBuilder.DropColumn(
                name: "MonsterDisadvantageRounds",
                table: "encounters");
        }

        /// <summary>
        /// Reverses the schema. It does not reverse the data, and cannot.
        /// </summary>
        /// <remarks>
        /// Down is lossy and says so rather than letting someone find out: every effect that is
        /// not a Weakened on the monster is discarded, because the old schema has nowhere to put
        /// it. A down migration on a populated database is a data decision, not a rollback.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MonsterDisadvantageRounds",
                table: "encounters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The first Weakened on the monster, and only that. Guarded on jsonb_typeof so a
            // blob that is somehow not an array fails the filter rather than the migration.
            migrationBuilder.Sql(
                """
                UPDATE encounters
                SET "MonsterDisadvantageRounds" = COALESCE((
                        SELECT (effect ->> 'Rounds')::int
                        FROM jsonb_array_elements("Effects") AS effect
                        WHERE (effect ->> 'Kind')::int = 0
                          AND (effect ->> 'Target')::int = 1
                        LIMIT 1), 0)
                WHERE jsonb_typeof("Effects") = 'array';
                """);

            migrationBuilder.DropColumn(
                name: "Effects",
                table: "encounters");
        }
    }
}
