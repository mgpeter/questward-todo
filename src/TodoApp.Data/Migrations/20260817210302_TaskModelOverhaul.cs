using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class TaskModelOverhaul : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_IsCompleted_SortOrder",
                table: "tasks");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Recurrence",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // defaultValueSql, hand-added: Postgres cannot add a NOT NULL column to a table
            // that already has rows without one, so the scaffolded version fails on any
            // database with a single task in it.
            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                table: "tasks",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "XpEligibleFrom",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            // Hand-added, and the reason this migration is not purely mechanical. Status
            // scaffolds with default 0 (Todo), so dropping IsCompleted straight afterwards
            // would quietly reopen every task anyone has ever finished - their XP would
            // stay banked while the list claimed the work was never done.
            migrationBuilder.Sql(
                """
                UPDATE tasks SET "Status" = 2 WHERE "IsCompleted";
                """);

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "tasks");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_ParentId",
                table: "tasks",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_Tags",
                table: "tasks",
                column: "Tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_Status_SortOrder",
                table: "tasks",
                columns: new[] { "UserId", "Status", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_tasks_ParentId",
                table: "tasks",
                column: "ParentId",
                principalTable: "tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_tasks_ParentId",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_ParentId",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_Tags",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_Status_SortOrder",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Recurrence",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "XpEligibleFrom",
                table: "tasks");

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The mirror of the Up backfill. Rolling back loses the todo/in-progress
            // distinction, which the old schema simply could not hold, but it must not
            // lose the fact that a task was completed.
            migrationBuilder.Sql(
                """
                UPDATE tasks SET "IsCompleted" = true WHERE "Status" = 2;
                """);

            migrationBuilder.DropColumn(
                name: "Status",
                table: "tasks");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_IsCompleted_SortOrder",
                table: "tasks",
                columns: new[] { "UserId", "IsCompleted", "SortOrder" });
        }
    }
}
