using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreSellerEarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreSellerPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CutoffUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RecordedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VoidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSellerPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreSellerPayouts_AppUsers_RecordedByAppUserId",
                        column: x => x.RecordedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreSellerPayouts_AppUsers_SellerAppUserId",
                        column: x => x.SellerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreSellerPayouts_AppUsers_VoidedByAppUserId",
                        column: x => x.VoidedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoreSellerEarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Units = table.Column<int>(type: "int", nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParcelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PayoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSellerEarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreSellerEarnings_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreSellerEarnings_AppUsers_SellerAppUserId",
                        column: x => x.SellerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoreSellerEarnings_StoreSellerPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "StoreSellerPayouts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreSellerEarnings_CreatedByAppUserId",
                table: "StoreSellerEarnings",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreSellerEarnings_OrderItemId",
                table: "StoreSellerEarnings",
                column: "OrderItemId",
                unique: true,
                filter: "[Kind] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StoreSellerEarnings_ParcelId",
                table: "StoreSellerEarnings",
                column: "ParcelId",
                unique: true,
                filter: "[Kind] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_StoreSellerEarnings_PayoutId",
                table: "StoreSellerEarnings",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreSellerEarnings_RefundId_OrderItemId",
                table: "StoreSellerEarnings",
                columns: new[] { "RefundId", "OrderItemId" },
                unique: true,
                filter: "[Kind] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_StoreSellerEarnings_SellerAppUserId_PayoutId_OccurredUtc",
                table: "StoreSellerEarnings",
                columns: new[] { "SellerAppUserId", "PayoutId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreSellerPayouts_RecordedByAppUserId",
                table: "StoreSellerPayouts",
                column: "RecordedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreSellerPayouts_SellerAppUserId_PaidOnUtc",
                table: "StoreSellerPayouts",
                columns: new[] { "SellerAppUserId", "PaidOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreSellerPayouts_VoidedByAppUserId",
                table: "StoreSellerPayouts",
                column: "VoidedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreSellerEarnings");

            migrationBuilder.DropTable(
                name: "StoreSellerPayouts");
        }
    }
}
