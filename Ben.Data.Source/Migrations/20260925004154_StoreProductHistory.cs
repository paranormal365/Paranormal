using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreProductHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreProductChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Area = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ActorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorRole = table.Column<int>(type: "int", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProductChanges_AppUsers_ActorAppUserId",
                        column: x => x.ActorAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductChanges_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductChanges_ActorAppUserId",
                table: "StoreProductChanges",
                column: "ActorAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductChanges_ProductId_OccurredUtc",
                table: "StoreProductChanges",
                columns: new[] { "ProductId", "OccurredUtc" });

            // Every item already in the store opens its history with the line it would have had:
            // made, when and by whom. The maker is left blank if their account has since gone.
            migrationBuilder.Sql("""
                INSERT INTO StoreProductChanges (Id, ProductId, Area, Summary, ActorAppUserId, ActorRole, OccurredUtc)
                SELECT NEWID(), p.Id, 0, N'Created it.',
                       CASE WHEN EXISTS (SELECT 1 FROM AppUsers u WHERE u.Id = p.CreatedByAppUserId) THEN p.CreatedByAppUserId END,
                       0, p.DateCreated
                FROM StoreProducts p;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreProductChanges");
        }
    }
}
