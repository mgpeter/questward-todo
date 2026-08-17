using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRpgLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Charisma",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<string>(
                name: "ClassKey",
                table: "characters",
                type: "varchar(40)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Constitution",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "CurrentHitPoints",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Dexterity",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "Gold",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HitPointsUpdatedAt",
                table: "characters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Intelligence",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "Stamina",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Strength",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "Wisdom",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.CreateTable(
                name: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonsterKey = table.Column<string>(type: "varchar(60)", nullable: false),
                    MonsterHitPoints = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Log = table.Column<string>(type: "jsonb", nullable: false),
                    GoldAwarded = table.Column<int>(type: "integer", nullable: false),
                    BlessingUsed = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encounters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_encounters_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemKey = table.Column<string>(type: "varchar(60)", nullable: false),
                    Rarity = table.Column<int>(type: "integer", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    IsEquipped = table.Column<bool>(type: "boolean", nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_items_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quest_progress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestKey = table.Column<string>(type: "varchar(60)", nullable: false),
                    Counters = table.Column<string>(type: "jsonb", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quest_progress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quest_progress_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_encounters_UserId",
                table: "encounters",
                column: "UserId",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_encounters_UserId_StartedAt",
                table: "encounters",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_UserId",
                table: "inventory_items",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_UserId_Slot",
                table: "inventory_items",
                columns: new[] { "UserId", "Slot" },
                unique: true,
                filter: "\"IsEquipped\"");

            migrationBuilder.CreateIndex(
                name: "IX_quest_progress_UserId_QuestKey",
                table: "quest_progress",
                columns: new[] { "UserId", "QuestKey" },
                unique: true);

            // Hand-added backfill. Existing characters would otherwise sit at zero hit
            // points, which reads as dead on arrival.
            //
            // HitPointsUpdatedAt is deliberately left NULL rather than computing max hit
            // points here: that formula depends on class, level and Constitution, and
            // duplicating it in SQL guarantees the two drift apart. The service treats a
            // NULL timestamp as "never initialised" and heals to full on first read.
            //
            // The stamina grant is a one-time welcome so the feature is reachable without
            // first completing a task. It is not a repeatable source, so the rule that
            // only real work produces stamina still holds (DEC-003).
            migrationBuilder.Sql(
                """
                UPDATE characters
                SET "Stamina" = 3
                WHERE "Stamina" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "encounters");

            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropTable(
                name: "quest_progress");

            migrationBuilder.DropColumn(
                name: "Charisma",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "ClassKey",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Constitution",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "CurrentHitPoints",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Dexterity",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Gold",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "HitPointsUpdatedAt",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Intelligence",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Stamina",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Strength",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Wisdom",
                table: "characters");
        }
    }
}
