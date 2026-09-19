using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EventCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PriceAtPurchase = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PurchasedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiryWarningSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SpentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SpentOnHostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProviderCheckoutRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProviderPaymentRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventCredits", x => x.Id);
                    table.CheckConstraint("CK_EventCredits_OneOwner", "([OwnerOrganizationId] IS NOT NULL AND [OwnerAppUserId] IS NULL) OR ([OwnerOrganizationId] IS NULL AND [OwnerAppUserId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EventCredits_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventCredits_AppUsers_OwnerAppUserId",
                        column: x => x.OwnerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventCredits_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventCredits_HostedEvents_SpentOnHostedEventId",
                        column: x => x.SpentOnHostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EventCredits_Organizations_OwnerOrganizationId",
                        column: x => x.OwnerOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventCredits_CreatedByAppUserId",
                table: "EventCredits",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventCredits_OwnerAppUserId_SpentUtc_ExpiresUtc",
                table: "EventCredits",
                columns: new[] { "OwnerAppUserId", "SpentUtc", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventCredits_OwnerOrganizationId_SpentUtc_ExpiresUtc",
                table: "EventCredits",
                columns: new[] { "OwnerOrganizationId", "SpentUtc", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventCredits_ProviderPaymentRef",
                table: "EventCredits",
                column: "ProviderPaymentRef",
                filter: "[ProviderPaymentRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EventCredits_SpentOnHostedEventId",
                table: "EventCredits",
                column: "SpentOnHostedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventCredits_UpdatedByAppUserId",
                table: "EventCredits",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventCredits");
        }
    }
}
