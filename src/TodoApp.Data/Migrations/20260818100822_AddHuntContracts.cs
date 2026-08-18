using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <summary>
    /// One new table, <c>hunt_contracts</c>: a task written up as a promise, before there is any
    /// fight to have.
    /// </summary>
    /// <remarks>
    /// The table exists so that accepting a contract can be free. Before it, a contract WAS an
    /// encounter opened on the spot, which meant taking one cost a stamina and the creature could
    /// be killed while the task it stood for went on being ignored: the bounty on a neglected
    /// chore paid out for continuing to neglect it. DEC-013 makes a backlog a bounty and never a
    /// toll, so the promise and the fight are now two rows and three steps.
    /// <para>
    /// A new table takes no backfill and no default, which is why every column below can be NOT
    /// NULL where the model says so: there are no existing rows for a NOT NULL column to fail
    /// against. Nothing is added to <c>tasks</c> or to <c>encounters</c>, so TaskDto, the task
    /// mirrors in the endpoint tests and the complete/reopen ledger invariance test are all
    /// untouched by this schema.
    /// </para>
    /// <para>
    /// The partial unique index on TaskId is the one that matters. One live contract per task is
    /// otherwise a service-level check that two concurrent accepts both pass, and the loser leaves
    /// a second contract on the same task that one completion discharges: two fights, two
    /// bounties, one piece of work. Filtered on the two open states (0 Accepted, 1 Discharged)
    /// written as literals, because a migration must keep meaning what it meant when it ran.
    /// </para>
    /// <para>
    /// The task foreign key is ON DELETE SET NULL rather than cascade, matching the encounter's.
    /// A discharged contract is work that was already done, and deleting the task afterwards must
    /// not take back what doing it earned; DeleteTask sweeps the merely accepted ones to
    /// Abandoned itself, ahead of the delete, because a referential action cannot tell the two
    /// apart. Two cascade paths from users, to tasks and to contracts, are fine; Postgres is happy
    /// to have both, as the dungeon migration already noted.
    /// </para>
    /// <para>
    /// The scaffold was structurally correct and both directions are as it emitted them. Down is
    /// clean here in a way the previous hunt migration's was not: dropping a table the application
    /// no longer knows about strands nothing, because a contract holds no encounter slot and an
    /// unfought contract has cost the player nothing.
    /// </para>
    /// </remarks>
    public partial class AddHuntContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hunt_contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskTitle = table.Column<string>(type: "varchar(200)", nullable: false),
                    ArchetypeKey = table.Column<string>(type: "varchar(60)", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    DaysOverdue = table.Column<int>(type: "integer", nullable: false),
                    Subtasks = table.Column<int>(type: "integer", nullable: false),
                    FactionKey = table.Column<string>(type: "varchar(40)", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DischargedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hunt_contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_hunt_contracts_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_hunt_contracts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // One live contract per task, enforced by the database rather than by the service
            // check that races against itself. Filtered on Accepted and Discharged, so a fought
            // or torn up contract stops blocking with no flag to remember to clear.
            migrationBuilder.CreateIndex(
                name: "IX_hunt_contracts_TaskId",
                table: "hunt_contracts",
                column: "TaskId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            // Whether a task has already had its contract this window is COUNT(its contracts
            // since a moment) rather than a column on the task (DEC-002), so this is the index
            // that question runs on.
            migrationBuilder.CreateIndex(
                name: "IX_hunt_contracts_TaskId_AcceptedAt",
                table: "hunt_contracts",
                columns: new[] { "TaskId", "AcceptedAt" });

            // The board reads one hunter's live contracts on every open, in this order.
            migrationBuilder.CreateIndex(
                name: "IX_hunt_contracts_UserId_Status",
                table: "hunt_contracts",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hunt_contracts");
        }
    }
}
