using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreOrderTrackingToParcels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tracking lives on each package now (store sellers P7). Anything written on an order
            // since its package was made moves to that package before the order's columns go.
            migrationBuilder.Sql("""
                UPDATE x SET x.Carrier = o.Carrier, x.TrackingNumber = o.TrackingNumber, x.TrackingUrl = o.TrackingUrl
                FROM StoreOrderParcels x JOIN StoreOrders o ON o.Id = x.OrderId
                WHERE x.Number = 1 AND x.Carrier IS NULL AND o.Carrier IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "Carrier",
                table: "StoreOrders");

            migrationBuilder.DropColumn(
                name: "TrackingNumber",
                table: "StoreOrders");

            migrationBuilder.DropColumn(
                name: "TrackingUrl",
                table: "StoreOrders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Carrier",
                table: "StoreOrders",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingNumber",
                table: "StoreOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingUrl",
                table: "StoreOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
