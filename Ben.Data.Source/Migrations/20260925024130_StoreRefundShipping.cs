using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreRefundShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreRefundShipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParcelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreRefundShipping", x => x.Id);
                    table.CheckConstraint("CK_StoreRefundShipping_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_StoreRefundShipping_StoreOrderParcels_ParcelId",
                        column: x => x.ParcelId,
                        principalTable: "StoreOrderParcels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreRefundShipping_StoreRefunds_RefundId",
                        column: x => x.RefundId,
                        principalTable: "StoreRefunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreRefundShipping_ParcelId",
                table: "StoreRefundShipping",
                column: "ParcelId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRefundShipping_RefundId_ParcelId",
                table: "StoreRefundShipping",
                columns: new[] { "RefundId", "ParcelId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreRefundShipping");
        }
    }
}
