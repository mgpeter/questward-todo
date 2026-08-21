using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecurrenceSpawnsSuccessor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SpawnedTaskId",
                table: "tasks",
                type: "uuid",
                nullable: true);

            // Hand-added, and the reason this migration is not mechanical.
            //
            // Under the old model a repeating task was ONE row that stayed stored as Completed
            // and read as open again once XpEligibleFrom had passed. Dropping that column without
            // this would leave every such row reading plainly Completed, so every repeating task
            // a user currently has outstanding would disappear from their list and never return.
            //
            // So each one is given the successor it would have had. XpEligibleFrom is exactly the
            // moment the old model considered it due again, which makes it the honest due date
            // for the new row, and it is data this migration is about to destroy.
            //
            // The finished row is left completed, with its CompletedAt and XpAwarded intact. The
            // alternative, flipping it back to Todo, would have kept XpAwarded on a row that is
            // no longer completed and broken the ledger invariant that the character holds
            // exactly the XP its completed tasks record.
            migrationBuilder.Sql(
                """
                INSERT INTO tasks (
                    "Id", "UserId", "ParentId", "Title", "Notes", "Difficulty", "Priority",
                    "Tags", "DueDate", "Status", "CompletedAt", "StartedAt", "XpAwarded",
                    "StaminaAwarded", "Recurrence", "SortOrder", "CreatedAt", "UpdatedAt")
                SELECT
                    -- gen_random_uuid(), not uuidv7(): uuidv7() is a PostgreSQL 18 built-in and the
                    -- shared host (Asgard) runs 17.6. One cluster serves every tenant there, so the
                    -- major version is not this project's to choose - see the host's
                    -- docs/tenant-contract.md.
                    --
                    -- The app still generates v7 for every row it writes (Guid.CreateVersion7 on the
                    -- entity), so this affects ONLY rows created by this one-time backfill. Those get
                    -- a v4 and therefore lose the time-ordering that makes v7 worth having.
                    --
                    -- Which is close to free in practice: on a fresh database the SELECT below matches
                    -- nothing, so the backfill inserts zero rows and the function is needed only to
                    -- parse. It matters solely when migrating a database that already has history.
                    gen_random_uuid(), "UserId", NULL, "Title", "Notes", "Difficulty", "Priority",
                    "Tags", "XpEligibleFrom", 0, NULL, NULL, 0,
                    0, "Recurrence", "SortOrder", now(), now()
                FROM tasks
                WHERE "Status" = 2
                  AND "ParentId" IS NULL
                  AND "Recurrence" <> 0
                  AND "XpEligibleFrom" IS NOT NULL
                  AND "XpEligibleFrom" <= now();
                """);

            migrationBuilder.DropColumn(
                name: "XpEligibleFrom",
                table: "tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "XpEligibleFrom",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            // Rolling back cannot un-spawn the successors, because by then they may have been
            // edited, started or completed and are ordinary tasks. What it can do is restore the
            // gate on the rows that had one, so the old model does not immediately re-pay a
            // repeat that was completed moments ago.
            migrationBuilder.Sql(
                """
                UPDATE tasks
                SET "XpEligibleFrom" = CASE "Recurrence"
                    WHEN 1 THEN "CompletedAt" + interval '1 day'
                    WHEN 2 THEN "CompletedAt" + interval '7 days'
                    WHEN 3 THEN "CompletedAt" + interval '1 month'
                END
                WHERE "Status" = 2 AND "Recurrence" <> 0 AND "CompletedAt" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "SpawnedTaskId",
                table: "tasks");
        }
    }
}
