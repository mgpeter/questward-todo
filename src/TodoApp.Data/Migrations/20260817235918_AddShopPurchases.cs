using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApp.Data.Migrations
{
    /// <summary>
    /// One table, no columns. The shelf is still computed from the user and the date (DEC-002);
    /// what was missing was the historical fact that an offer had been taken off it, without
    /// which the same offer id is buyable for as long as the gold lasts.
    /// </summary>
    /// <remarks>
    /// Purchases made before this ran were never recorded, so every user's shelf reads as
    /// untouched on the day of deployment. That is one extra day of stock at worst, and the
    /// alternative is inventing purchase rows for items nobody can prove were bought.
    /// </remarks>
    public partial class AddShopPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The scaffold also emitted an AddColumn for "xmin" on characters, and it was
            // removed by hand. xmin is a Postgres system column that every table already has;
            // adding it fails outright with "column name xmin conflicts with a system column
            // name". The model maps it as a concurrency token, which needs no DDL at all.
            migrationBuilder.CreateTable(
                name: "shop_purchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<string>(type: "varchar(80)", nullable: false),
                    PurchasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_purchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shop_purchases_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The cap itself. A unique index rather than a check in the shop service, because
            // application logic loses the race and the database does not, and losing this one
            // turns gold into essence at whatever rate the shelf happens to be priced at.
            migrationBuilder.CreateIndex(
                name: "IX_shop_purchases_UserId_OfferId",
                table: "shop_purchases",
                columns: new[] { "UserId", "OfferId" },
                unique: true);
        }

        /// <summary>
        /// Reversible, unlike its neighbours: dropping the table loses only the record of which
        /// offers were spent, and the shelf it caps rotates daily anyway.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_purchases");
        }
    }
}
