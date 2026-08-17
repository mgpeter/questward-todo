using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "achievement_unlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AchievementKey = table.Column<string>(type: "varchar(60)", nullable: false),
                    UnlockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_achievement_unlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "character",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "varchar(60)", nullable: false),
                    AvatarKey = table.Column<string>(type: "varchar(40)", nullable: false),
                    TotalXp = table.Column<int>(type: "integer", nullable: false),
                    TasksCompleted = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character", x => x.Id);
                    table.CheckConstraint("ck_character_singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    XpAwarded = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_achievement_unlocks_AchievementKey",
                table: "achievement_unlocks",
                column: "AchievementKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_CompletedAt",
                table: "tasks",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_IsCompleted_SortOrder",
                table: "tasks",
                columns: new[] { "IsCompleted", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "achievement_unlocks");

            migrationBuilder.DropTable(
                name: "character");

            migrationBuilder.DropTable(
                name: "tasks");
        }
    }
}
