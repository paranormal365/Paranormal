using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreOrderParcels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParcelId",
                table: "StoreOrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParcelId",
                table: "StoreOrderEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoreOrderParcels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    SellerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SellerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ShippingAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingTaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingRefunded = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SellerShippingCredit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Carrier = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TrackingUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PackedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ShippedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOrderParcels", x => x.Id);
                    table.CheckConstraint("CK_StoreOrderParcels_Refund", "[ShippingRefunded] >= 0 AND [ShippingRefunded] <= [ShippingAmount] + [ShippingTaxAmount]");
                    table.ForeignKey(
                        name: "FK_StoreOrderParcels_AppUsers_SellerAppUserId",
                        column: x => x.SellerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreOrderParcels_StoreOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "StoreOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrderItems_ParcelId",
                table: "StoreOrderItems",
                column: "ParcelId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrderEvents_ParcelId",
                table: "StoreOrderEvents",
                column: "ParcelId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrderParcels_OrderId_Number",
                table: "StoreOrderParcels",
                columns: new[] { "OrderId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreOrderParcels_SellerAppUserId_Status",
                table: "StoreOrderParcels",
                columns: new[] { "SellerAppUserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_StoreOrderEvents_StoreOrderParcels_ParcelId",
                table: "StoreOrderEvents",
                column: "ParcelId",
                principalTable: "StoreOrderParcels",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StoreOrderItems_StoreOrderParcels_ParcelId",
                table: "StoreOrderItems",
                column: "ParcelId",
                principalTable: "StoreOrderParcels",
                principalColumn: "Id");

            // Every order placed before packages existed becomes one package: the site's own stock,
            // with the order's shipping, tax on shipping, carrier, tracking and times, and a status
            // read from the order's. Its lines join it.
            migrationBuilder.Sql("""
                INSERT INTO StoreOrderParcels (Id, OrderId, Number, SellerAppUserId, SellerName, Status,
                    ShippingAmount, ShippingTaxAmount, ShippingRefunded, SellerShippingCredit,
                    Carrier, TrackingNumber, TrackingUrl, PackedUtc, ShippedUtc, DeliveredUtc, CancelledUtc, DateCreated)
                SELECT NEWID(), o.Id, 1, NULL, NULL,
                    CASE o.Status WHEN 2 THEN 1 WHEN 3 THEN 2 WHEN 4 THEN 3 WHEN 5 THEN 4 ELSE
                        CASE WHEN o.DeliveredUtc IS NOT NULL THEN 3 WHEN o.ShippedUtc IS NOT NULL THEN 2 WHEN o.PackedUtc IS NOT NULL THEN 1 ELSE 0 END END,
                    o.ShippingAmount, o.ShippingTaxAmount, 0, 0,
                    o.Carrier, o.TrackingNumber, o.TrackingUrl, o.PackedUtc, o.ShippedUtc, o.DeliveredUtc, o.CancelledUtc, o.DateCreated
                FROM StoreOrders o
                WHERE NOT EXISTS (SELECT 1 FROM StoreOrderParcels x WHERE x.OrderId = o.Id);

                UPDATE i SET i.ParcelId = x.Id
                FROM StoreOrderItems i JOIN StoreOrderParcels x ON x.OrderId = i.OrderId AND x.Number = 1
                WHERE i.ParcelId IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoreOrderEvents_StoreOrderParcels_ParcelId",
                table: "StoreOrderEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_StoreOrderItems_StoreOrderParcels_ParcelId",
                table: "StoreOrderItems");

            migrationBuilder.DropTable(
                name: "StoreOrderParcels");

            migrationBuilder.DropIndex(
                name: "IX_StoreOrderItems_ParcelId",
                table: "StoreOrderItems");

            migrationBuilder.DropIndex(
                name: "IX_StoreOrderEvents_ParcelId",
                table: "StoreOrderEvents");

            migrationBuilder.DropColumn(
                name: "ParcelId",
                table: "StoreOrderItems");

            migrationBuilder.DropColumn(
                name: "ParcelId",
                table: "StoreOrderEvents");
        }
    }
}
