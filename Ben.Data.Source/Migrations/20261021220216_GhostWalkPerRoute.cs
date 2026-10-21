using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class GhostWalkPerRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "GhostWalkSubscriptions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPriceAtPeriodStart",
                table: "GhostWalkSubscriptions",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "GhostWalkRouteChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GhostWalkSubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromQuantity = table.Column<int>(type: "int", nullable: false),
                    ToQuantity = table.Column<int>(type: "int", nullable: false),
                    ProratedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SettledUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SettledPaymentRef = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GhostWalkRouteChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GhostWalkRouteChanges_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GhostWalkRouteChanges_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GhostWalkRouteChanges_GhostWalkSubscriptions_GhostWalkSubscriptionId",
                        column: x => x.GhostWalkSubscriptionId,
                        principalTable: "GhostWalkSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GhostWalkRouteChanges_CreatedByAppUserId",
                table: "GhostWalkRouteChanges",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GhostWalkRouteChanges_GhostWalkSubscriptionId_SettledUtc",
                table: "GhostWalkRouteChanges",
                columns: new[] { "GhostWalkSubscriptionId", "SettledUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GhostWalkRouteChanges_UpdatedByAppUserId",
                table: "GhostWalkRouteChanges",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GhostWalkRouteChanges");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "GhostWalkSubscriptions");

            migrationBuilder.DropColumn(
                name: "UnitPriceAtPeriodStart",
                table: "GhostWalkSubscriptions");
        }
    }
}
