using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreEconomics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StripeFeeAmount",
                table: "StoreOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StripeNetAmount",
                table: "StoreOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostBasis",
                table: "StoreOrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitSellerAsk",
                table: "StoreOrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitSellerEarning",
                table: "StoreOrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitSiteMarkup",
                table: "StoreOrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Lines sold before costs were kept: no cost is known, so the store's margin on them is
            // their price. (Every earlier sale was the store's own stock.)
            migrationBuilder.Sql("UPDATE StoreOrderItems SET UnitSiteMarkup = UnitPrice WHERE UnitSiteMarkup = 0 AND UnitSellerEarning = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripeFeeAmount",
                table: "StoreOrders");

            migrationBuilder.DropColumn(
                name: "StripeNetAmount",
                table: "StoreOrders");

            migrationBuilder.DropColumn(
                name: "UnitCostBasis",
                table: "StoreOrderItems");

            migrationBuilder.DropColumn(
                name: "UnitSellerAsk",
                table: "StoreOrderItems");

            migrationBuilder.DropColumn(
                name: "UnitSellerEarning",
                table: "StoreOrderItems");

            migrationBuilder.DropColumn(
                name: "UnitSiteMarkup",
                table: "StoreOrderItems");
        }
    }
}
