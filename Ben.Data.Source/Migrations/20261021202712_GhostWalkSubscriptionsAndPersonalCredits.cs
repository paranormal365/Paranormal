using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class GhostWalkSubscriptionsAndPersonalCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PayerAppUserId",
                table: "BillingLedgerEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GhostWalkSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PriceAtPeriodStart = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentPeriodStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "bit", nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ProviderSubscriptionRef = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProviderCustomerRef = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProviderPaymentMethodRef = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GhostWalkSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GhostWalkSubscriptions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GhostWalkSubscriptions_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GhostWalkSubscriptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_PayerAppUserId",
                table: "BillingLedgerEntries",
                column: "PayerAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GhostWalkSubscriptions_CreatedByAppUserId",
                table: "GhostWalkSubscriptions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GhostWalkSubscriptions_OrganizationId",
                table: "GhostWalkSubscriptions",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GhostWalkSubscriptions_Status_CurrentPeriodEnd",
                table: "GhostWalkSubscriptions",
                columns: new[] { "Status", "CurrentPeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_GhostWalkSubscriptions_UpdatedByAppUserId",
                table: "GhostWalkSubscriptions",
                column: "UpdatedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BillingLedgerEntries_AppUsers_PayerAppUserId",
                table: "BillingLedgerEntries",
                column: "PayerAppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BillingLedgerEntries_AppUsers_PayerAppUserId",
                table: "BillingLedgerEntries");

            migrationBuilder.DropTable(
                name: "GhostWalkSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_BillingLedgerEntries_PayerAppUserId",
                table: "BillingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "PayerAppUserId",
                table: "BillingLedgerEntries");
        }
    }
}
