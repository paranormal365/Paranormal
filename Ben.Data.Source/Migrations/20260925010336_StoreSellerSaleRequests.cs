using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreSellerSaleRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstOnSaleUtc",
                table: "StoreProducts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SellerAskPerUnit",
                table: "StoreProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoreProductSaleRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerAskingPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SellerNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductSaleRequests", x => x.Id);
                    table.CheckConstraint("CK_StoreProductSaleRequests_Ask", "[SellerAskingPrice] > 0");
                    table.ForeignKey(
                        name: "FK_StoreProductSaleRequests_AppUsers_DecidedByAppUserId",
                        column: x => x.DecidedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductSaleRequests_AppUsers_SellerAppUserId",
                        column: x => x.SellerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreProductSaleRequests_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductSaleRequests_DecidedByAppUserId",
                table: "StoreProductSaleRequests",
                column: "DecidedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductSaleRequests_ProductId",
                table: "StoreProductSaleRequests",
                column: "ProductId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductSaleRequests_SellerAppUserId",
                table: "StoreProductSaleRequests",
                column: "SellerAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductSaleRequests_Status_RequestedUtc",
                table: "StoreProductSaleRequests",
                columns: new[] { "Status", "RequestedUtc" });

            // An item on sale now, or one that has sold, has been on sale: the earliest paid order
            // that holds it says when, or failing that the day it was made. Only a draft that never
            // went on sale stays null — the one a seller may delete.
            migrationBuilder.Sql("""
                UPDATE p SET p.FirstOnSaleUtc = COALESCE((
                    SELECT MIN(o.PaidUtc) FROM StoreOrderItems i
                    JOIN StoreOrders o ON o.Id = i.OrderId
                    WHERE i.ProductId = p.Id AND o.PaidUtc IS NOT NULL), p.DateCreated)
                FROM StoreProducts p
                WHERE p.IsActive = 1 OR p.UnitsSold > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreProductSaleRequests");

            migrationBuilder.DropColumn(
                name: "FirstOnSaleUtc",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "SellerAskPerUnit",
                table: "StoreProducts");
        }
    }
}
