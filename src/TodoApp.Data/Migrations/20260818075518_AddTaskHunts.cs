using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <summary>
    /// Five nullable columns, two indexes, one foreign key and one check constraint, all on
    /// <c>encounters</c>: a fight that is a contract on a task, and the frozen facts it was
    /// written against.
    /// </summary>
    /// <remarks>
    /// The <c>tasks</c> table gets no column at all, and that is the most important property of
    /// this file. The one-hunt-per-window rule is read from <c>encounters.StartedAt</c> rather
    /// than a <c>TodoTask.HuntedAt</c> column (DEC-002), so TaskDto, the task mirrors in the
    /// endpoint tests and the complete/reopen ledger invariance test are all untouched by this
    /// schema, and ReopenAsync needs no new unwind.
    /// <para>
    /// Every column here is nullable, which is what lets all five be added to a populated
    /// encounters table with no default and no backfill: every fight that already exists was
    /// taken at the tavern or in a dungeon, and null says exactly that. This is the same argument
    /// the DungeonRunId migration made verbatim, and the reason a NOT NULL column here would have
    /// needed a sentinel task id pointing at nothing.
    /// </para>
    /// <para>
    /// <c>IX_encounters_UserId</c>, the partial unique index that is the database-level
    /// enforcement of one fight at a time, is deliberately absent from this file. An AddColumn
    /// does not rebuild an index, so it survives untouched, and that is the whole reason a hunt
    /// was made an ordinary encounter row rather than a second kind of fight.
    /// </para>
    /// <para>
    /// The scaffold was structurally correct and Up is as it emitted it. Down was not, and is
    /// hand-written below.
    /// </para>
    /// </remarks>
    public partial class AddTaskHunts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The four frozen inputs a hunt derives its whole stat block from, plus the link back
            // to the task the contract was taken on. Nullable and therefore addable to a
            // populated table with no default; null means the fight was not a hunt, which is true
            // of every row that exists today.
            migrationBuilder.AddColumn<int>(
                name: "HuntDaysOverdue",
                table: "encounters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HuntFactionKey",
                table: "encounters",
                type: "varchar(40)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HuntLevel",
                table: "encounters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HuntSubtasks",
                table: "encounters",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaskId",
                table: "encounters",
                type: "uuid",
                nullable: true);

            // Whether a task has already had its hunt this period is COUNT(its fights since a
            // moment) rather than a column on the task (DEC-002), so this is the index that
            // question runs on. Leading on the foreign key means it also serves the relationship
            // below and no second index is needed for it.
            migrationBuilder.CreateIndex(
                name: "IX_encounters_TaskId_StartedAt",
                table: "encounters",
                columns: new[] { "TaskId", "StartedAt" });

            // Standing with a faction is COUNT(won hunts under that banner) and nothing is
            // stored for it anywhere, which is the entirety of the faction persistence: no table,
            // no reputation counter, nothing that can drift away from the fights that actually
            // happened. This is the index that count runs on, in the order it filters.
            migrationBuilder.CreateIndex(
                name: "IX_encounters_UserId_HuntFactionKey_Status",
                table: "encounters",
                columns: new[] { "UserId", "HuntFactionKey", "Status" });

            // The frozen inputs are all-or-nothing. Encounter.Monster uses HuntLevel as the
            // discriminator and coalesces the other two, so a half-written row would not throw:
            // it would quietly derive a stat block from defaulted zeros and look correctly tuned.
            // Validated against the existing rows as it is added, which passes trivially because
            // every row that exists has HuntLevel null.
            migrationBuilder.AddCheckConstraint(
                name: "CK_encounters_hunt_inputs_together",
                table: "encounters",
                sql: "\"HuntLevel\" IS NULL OR (\"HuntDaysOverdue\" IS NOT NULL AND \"HuntSubtasks\" IS NOT NULL)");

            // SET NULL, not CASCADE, and the difference is the whole point. DeleteTask runs
            // ExecuteDeleteAsync and bypasses the change tracker, so this referential action is
            // the only thing standing between "the user tidied a task away" and "a fought battle,
            // its gold and its log left the chronicle". Nulling the column loses the attribution
            // and keeps the fight: the four frozen scalars are untouched by it, so the stat block
            // still derives and the row stays renderable and finishable. RESTRICT was rejected,
            // because it would turn deleting a task while a fight was open into a 500. Two
            // cascade paths from users, to tasks and to encounters, are fine; Postgres is happy
            // to have both, as the dungeon migration already noted.
            migrationBuilder.AddForeignKey(
                name: "FK_encounters_tasks_TaskId",
                table: "encounters",
                column: "TaskId",
                principalTable: "tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <summary>
        /// Reverses the schema, and unlike the dungeon migration this one is not clean.
        /// </summary>
        /// <remarks>
        /// Dropping the columns turns every hunt into an encounter whose MonsterKey no longer
        /// resolves to anything, because the archetype keys live in HuntArchetypeCatalog and
        /// MonsterCatalog has never heard of them. For a finished row that is survivable:
        /// EncounterDto falls back to <c>monster?.Name ?? encounter.MonsterKey</c> and the
        /// chronicle renders the raw key, which is ugly and honest.
        /// <para>
        /// For an <b>active</b> hunt it is not survivable. ResolveRoundAsync returns NotFound on
        /// a null monster, so the fight becomes unwinnable and unfleeable while still holding the
        /// one-fight-at-a-time slot through IX_encounters_UserId: the player would be locked out
        /// of combat entirely, with no route back except a database edit. So live hunts are
        /// closed before the columns go, and that statement is hand-added to the scaffold, which
        /// emitted the drops alone.
        /// </para>
        /// <para>
        /// What is lost is the archetype scaling on historical rows and the faction attribution
        /// behind standing. No gold, no item, no completion and no fight is destroyed.
        /// </para>
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fled rather than Lost, because losing costs the player something and this is the
            // schema's fault rather than theirs; a fight that never resolved is closer to walking
            // away from it. Status 3 is EncounterStatus.Fled and 0 is Active, written as literals
            // because a migration must keep meaning what it meant when it ran.
            //
            // Predicated on HuntLevel rather than TaskId deliberately. HuntLevel is what
            // Encounter.IsHunt reads and therefore exactly the set of rows whose monster stops
            // resolving; a hunt whose task was deleted has already had TaskId nulled by the
            // foreign key above and would have been missed by a TaskId test, leaving behind the
            // one row this whole statement exists to prevent.
            //
            // This also cannot collide with IX_encounters_UserId: it only ever moves rows out of
            // that partial index's filter, never into it.
            migrationBuilder.Sql(
                """
                UPDATE encounters
                SET "Status" = 3, "EndedAt" = now()
                WHERE "Status" = 0 AND "HuntLevel" IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_encounters_tasks_TaskId",
                table: "encounters");

            migrationBuilder.DropCheckConstraint(
                name: "CK_encounters_hunt_inputs_together",
                table: "encounters");

            migrationBuilder.DropIndex(
                name: "IX_encounters_TaskId_StartedAt",
                table: "encounters");

            migrationBuilder.DropIndex(
                name: "IX_encounters_UserId_HuntFactionKey_Status",
                table: "encounters");

            migrationBuilder.DropColumn(
                name: "HuntDaysOverdue",
                table: "encounters");

            migrationBuilder.DropColumn(
                name: "HuntFactionKey",
                table: "encounters");

            migrationBuilder.DropColumn(
                name: "HuntLevel",
                table: "encounters");

            migrationBuilder.DropColumn(
                name: "HuntSubtasks",
                table: "encounters");

            migrationBuilder.DropColumn(
                name: "TaskId",
                table: "encounters");
        }
    }
}
