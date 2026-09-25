using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreProductSaleCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSoldUtc",
                table: "StoreProducts",
                type: "datetime2",
                nullable: true);

            // The product's sold count was never written (only the variants' were), so it is the
            // sum of its variants'. The last sale is the newest paid order that holds the product.
            migrationBuilder.Sql("""
                UPDATE p SET p.UnitsSold = ISNULL((SELECT SUM(v.UnitsSold) FROM StoreProductVariants v WHERE v.ProductId = p.Id), 0)
                FROM StoreProducts p;

                UPDATE p SET p.LastSoldUtc = (
                    SELECT MAX(o.PaidUtc) FROM StoreOrderItems i
                    JOIN StoreOrders o ON o.Id = i.OrderId
                    WHERE i.ProductId = p.Id AND o.PaidUtc IS NOT NULL)
                FROM StoreProducts p;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSoldUtc",
                table: "StoreProducts");
        }
    }
}
