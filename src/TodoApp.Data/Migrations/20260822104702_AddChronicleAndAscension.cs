using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChronicleAndAscension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AscendedAt",
                table: "characters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Ascensions",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "chronicle_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Era = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Facts = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chronicle_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chronicle_entries_encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_chronicle_entries_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chronicle_entries_EncounterId",
                table: "chronicle_entries",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_chronicle_entries_UserId_OccurredAt",
                table: "chronicle_entries",
                columns: new[] { "UserId", "OccurredAt" });

            // Seed the journal from the fights that already happened, so a database with history
            // does not open on an empty chronicle the day the feature ships. The same one-time
            // seed the bestiary did from the same table, and with the same caveats: it uses
            // gen_random_uuid() rather than uuidv7() because uuidv7() is a PostgreSQL 18 built-in
            // and the shared host runs 17.6, and on a fresh database it matches nothing at all.
            //
            // Only finished fights. An active encounter is not yet an entry in a journal of
            // things that happened, and it will write its own when it ends.
            //
            // Era is 0 for every seeded row: nobody has ascended before this migration exists.
            // Facts carry keys and numbers, never prose, because ChronicleNarrator does the words.
            migrationBuilder.Sql(
                """
                INSERT INTO chronicle_entries ("Id", "UserId", "Kind", "Era", "OccurredAt", "EncounterId", "Facts")
                SELECT
                    gen_random_uuid(),
                    e."UserId",
                    CASE e."Status" WHEN 1 THEN 0 WHEN 2 THEN 1 ELSE 2 END,
                    0,
                    COALESCE(e."EndedAt", e."StartedAt"),
                    e."Id",
                    jsonb_build_object(
                        'monsterKey', e."MonsterKey",
                        'rounds', e."Round"::text,
                        'gold', e."GoldAwarded"::text)
                FROM encounters e
                WHERE e."Status" <> 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chronicle_entries");

            migrationBuilder.DropColumn(
                name: "AscendedAt",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Ascensions",
                table: "characters");
        }
    }
}
