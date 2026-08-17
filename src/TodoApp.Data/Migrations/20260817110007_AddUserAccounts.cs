using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited. Ownership is being introduced, and existing rows have no owner
            // to assign: EF scaffolded a Guid.Empty default for the new UserId columns,
            // which would then fail the foreign keys because no such user exists.
            //
            // Dropping the data is the deliberate decision recorded in the spec; the
            // project has never been released, so there is no deployed data at risk.
            // These must run before the NOT NULL columns are added.
            migrationBuilder.Sql("DELETE FROM tasks;");
            migrationBuilder.Sql("DELETE FROM achievement_unlocks;");
            migrationBuilder.Sql("DELETE FROM character;");

            migrationBuilder.DropIndex(
                name: "IX_tasks_CompletedAt",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_IsCompleted_SortOrder",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_achievement_unlocks_AchievementKey",
                table: "achievement_unlocks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_character",
                table: "character");

            migrationBuilder.DropCheckConstraint(
                name: "ck_character_singleton",
                table: "character");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "character");

            migrationBuilder.RenameTable(
                name: "character",
                newName: "characters");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "tasks",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "achievement_unlocks",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "characters",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_characters",
                table: "characters",
                column: "UserId");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Auth0Sub = table.Column<string>(type: "varchar(128)", nullable: false),
                    Email = table.Column<string>(type: "varchar(320)", nullable: true),
                    DisplayName = table.Column<string>(type: "varchar(200)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_CompletedAt",
                table: "tasks",
                columns: new[] { "UserId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_IsCompleted_SortOrder",
                table: "tasks",
                columns: new[] { "UserId", "IsCompleted", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_achievement_unlocks_UserId_AchievementKey",
                table: "achievement_unlocks",
                columns: new[] { "UserId", "AchievementKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Auth0Sub",
                table: "users",
                column: "Auth0Sub",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_achievement_unlocks_users_UserId",
                table: "achievement_unlocks",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_characters_users_UserId",
                table: "characters",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_users_UserId",
                table: "tasks",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_achievement_unlocks_users_UserId",
                table: "achievement_unlocks");

            migrationBuilder.DropForeignKey(
                name: "FK_characters_users_UserId",
                table: "characters");

            migrationBuilder.DropForeignKey(
                name: "FK_tasks_users_UserId",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_CompletedAt",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_IsCompleted_SortOrder",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_achievement_unlocks_UserId_AchievementKey",
                table: "achievement_unlocks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_characters",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "achievement_unlocks");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "characters");

            migrationBuilder.RenameTable(
                name: "characters",
                newName: "character");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "character",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_character",
                table: "character",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_CompletedAt",
                table: "tasks",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_IsCompleted_SortOrder",
                table: "tasks",
                columns: new[] { "IsCompleted", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_achievement_unlocks_AchievementKey",
                table: "achievement_unlocks",
                column: "AchievementKey",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_character_singleton",
                table: "character",
                sql: "\"Id\" = 1");
        }
    }
}
