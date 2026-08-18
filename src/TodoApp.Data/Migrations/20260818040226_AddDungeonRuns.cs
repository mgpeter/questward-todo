using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <summary>
    /// One table, one nullable column and three indexes: dungeon runs, and the pointer from a
    /// room's fight back to the run it belongs to.
    /// </summary>
    /// <remarks>
    /// The scaffold was structurally correct this time and the operations are as it emitted them,
    /// reordered only so the table is created before the column that will point at it. It was
    /// still checked against a live Postgres rather than trusted, because the one thing this
    /// migration must not do is disturb the fight that is already in progress.
    /// <para>
    /// <c>encounters.DungeonRunId</c> is nullable, which is what lets it be added to a populated
    /// table with no default and no backfill: every fight that already exists was taken at the
    /// tavern, and null says exactly that. A NOT NULL column here would have needed a sentinel
    /// run id that points at nothing.
    /// </para>
    /// <para>
    /// <c>IX_encounters_UserId</c>, the partial unique index that is the database-level
    /// enforcement of one fight at a time, is deliberately absent from this file. An AddColumn
    /// does not rebuild an index, so it survives untouched, and that is the whole reason a
    /// dungeon fight was made an ordinary encounter row rather than a second kind of fight.
    /// </para>
    /// </remarks>
    public partial class AddDungeonRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dungeon_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DungeonKey = table.Column<string>(type: "varchar(60)", nullable: false),
                    Rooms = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    GoldAwarded = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dungeon_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dungeon_runs_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Nullable and therefore addable to a populated table with no default. Null means the
            // fight was taken at the tavern, which is true of every row that exists today.
            migrationBuilder.AddColumn<Guid>(
                name: "DungeonRunId",
                table: "encounters",
                type: "uuid",
                nullable: true);

            // How deep a run has got is COUNT(won rooms) rather than a stored counter (DEC-002),
            // so this is the index that count runs on. Leading on the foreign key means it also
            // serves the relationship below and no second index is needed for it.
            migrationBuilder.CreateIndex(
                name: "IX_encounters_DungeonRunId_Status",
                table: "encounters",
                columns: new[] { "DungeonRunId", "Status" });

            // One run at a time, the parallel of IX_encounters_UserId and there for the same
            // reason. Two concurrent POST /dungeons would otherwise each pass the service's
            // AnyAsync check and open a run, and the loser could never be finished: the one
            // encounter slot would belong to the other. Application logic loses that race, the
            // database does not. The filter is a literal 0 because DungeonRunStatus.Active is
            // zero on purpose; renumbering the enum would silently invert this index.
            migrationBuilder.CreateIndex(
                name: "IX_dungeon_runs_UserId",
                table: "dungeon_runs",
                column: "UserId",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_dungeon_runs_UserId_StartedAt",
                table: "dungeon_runs",
                columns: new[] { "UserId", "StartedAt" });

            // Cascade, so a deleted run takes its rooms with it. The alternative, setting the
            // column back to null, would rewrite history to claim those fights were taken at the
            // tavern. Nothing deletes a run except a user being deleted, at which point both
            // cascade paths from users lead to the same place and Postgres is happy to have two.
            migrationBuilder.AddForeignKey(
                name: "FK_encounters_dungeon_runs_DungeonRunId",
                table: "encounters",
                column: "DungeonRunId",
                principalTable: "dungeon_runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <summary>
        /// Reverses the schema, and this time reverses the data with no loss worth naming.
        /// </summary>
        /// <remarks>
        /// Unusually for this project, Down is close to clean. Every dungeon run is discarded and
        /// every room's fight forgets which run it belonged to, but no fight, no gold and no item
        /// is destroyed: the encounters themselves are untouched and their logs still read
        /// correctly on their own. What is lost is only the grouping, and a run in progress
        /// becomes an ordinary fight the player can finish or flee.
        /// <para>
        /// Dropping the foreign key before the table is not optional. Postgres refuses to drop a
        /// table another table still references.
        /// </para>
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_encounters_dungeon_runs_DungeonRunId",
                table: "encounters");

            migrationBuilder.DropTable(
                name: "dungeon_runs");

            migrationBuilder.DropIndex(
                name: "IX_encounters_DungeonRunId_Status",
                table: "encounters");

            migrationBuilder.DropColumn(
                name: "DungeonRunId",
                table: "encounters");
        }
    }
}
