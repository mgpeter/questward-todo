using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotStaminaAwarded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StaminaAwarded",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Hand-added backfill. Tasks completed before this column existed did grant
            // stamina, so leaving them at zero would mean reopening one refunds its XP and
            // silently keeps the stamina, which is the exact bug this column exists to fix.
            // Mirrors DifficultyExtensions.Stamina(): Easy 1, Medium 2, Hard 3, Epic 5.
            migrationBuilder.Sql(
                """
                UPDATE tasks
                SET "StaminaAwarded" = CASE "Difficulty"
                    WHEN 0 THEN 1
                    WHEN 1 THEN 2
                    WHEN 2 THEN 3
                    WHEN 3 THEN 5
                    ELSE 1
                END
                WHERE "IsCompleted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StaminaAwarded",
                table: "tasks");
        }
    }
}
