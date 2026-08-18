using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <summary>
    /// One table, one row per user per monster, plus a one-time seed from the encounters
    /// already on disk so a player who has been fighting since Phase 3 opens a populated
    /// codex rather than an empty one.
    /// </summary>
    /// <remarks>
    /// There is deliberately no companion seed of "quest_progress". The seed changes what
    /// counts as a first sighting, so a backfilled row would have silently retired the
    /// discovery quests for exactly the players it was written for. That is safe only because
    /// QuestService derives discovery progress from these rows rather than from a counter it
    /// increments on the sighting; the rows below are the progress. Anything that reintroduces
    /// a stored discovery counter has to reintroduce a seed for it here as well.
    /// </remarks>
    public partial class AddBestiary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bestiary_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonsterKey = table.Column<string>(type: "varchar(60)", nullable: false),
                    Encounters = table.Column<int>(type: "integer", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    GoldTaken = table.Column<int>(type: "integer", nullable: false),
                    BestRound = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bestiary_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bestiary_entries_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // One row per user per monster. A unique index rather than a check in the service,
            // because two concurrent starts on the same monster would each insert a row and
            // split one sighting count across both, with neither row telling the truth after.
            migrationBuilder.CreateIndex(
                name: "IX_bestiary_entries_UserId_MonsterKey",
                table: "bestiary_entries",
                columns: new[] { "UserId", "MonsterKey" },
                unique: true);

            // Hand-written backfill. The scaffold emitted the table and the index only.
            migrationBuilder.Sql(
                """
                -- DEC-002 EXCEPTION, DELIBERATE. READ THIS BEFORE "SIMPLIFYING" THIS TABLE.
                --
                -- Encounters, Kills, GoldTaken and BestRound are all derivable from "encounters", and the
                -- backfill immediately below derives them with a GROUP BY. That is normally the whole
                -- argument for not having the columns at all, and it is the reason this note exists: from
                -- here it looks like a mistake, and it is not one.
                --
                -- The columns are stored anyway, for two reasons the derivation cannot serve:
                --
                --   1. The chronicle is prunable. Old encounter rows are expected to be deleted eventually,
                --      and a derived count would silently shrink when they went. The bestiary is a record of
                --      what happened, not a view over what is still on disk.
                --
                --   2. A sighting is not a win. Starting a fight and then losing it or fleeing still counts
                --      as having met the monster, and a monster that has never been killed has to be
                --      recordable at all. Deriving from won encounters alone would lose exactly the entries
                --      the bestiary exists to show.
                --
                -- So this backfill is a one-time seed from the best source available on the day it ran, not
                -- evidence that the columns are redundant. From the first sighting written after this
                -- migration, "encounters" and "bestiary_entries" are allowed to disagree, and
                -- "bestiary_entries" is the one that is right.
                INSERT INTO bestiary_entries (
                    "Id", "UserId", "MonsterKey", "Encounters", "Kills", "GoldTaken", "BestRound",
                    "FirstSeenAt", "LastSeenAt")
                SELECT
                    uuidv7(),
                    e."UserId",
                    e."MonsterKey",
                    (COUNT(*))::integer,
                    (COUNT(*) FILTER (WHERE e."Status" = 1))::integer,
                    (COALESCE(SUM(e."GoldAwarded") FILTER (WHERE e."Status" = 1), 0))::integer,
                    COALESCE(MIN(e."Round") FILTER (WHERE e."Status" = 1), 0),
                    MIN(e."StartedAt"),
                    MAX(e."StartedAt")
                FROM encounters e
                GROUP BY e."UserId", e."MonsterKey";
                """);
        }

        /// <summary>
        /// Reversible: the table is new, so dropping it puts the schema back exactly. What it
        /// loses is every sighting written after the seed, which by the argument above is the
        /// only copy of those. Down is for a failed deploy, not for routine use.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bestiary_entries");
        }
    }
}
